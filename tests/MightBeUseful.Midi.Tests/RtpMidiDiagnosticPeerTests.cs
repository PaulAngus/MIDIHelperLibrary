using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using MightBeUseful.Midi.Rtp;

namespace MightBeUseful.Midi.Tests;

// Only synthetic UDP sockets on loopback. No native backend or port catalog is used.
public class RtpMidiDiagnosticPeerTests
{
    [Fact]
    public async Task DiagnosticHandshakeClockCountersFeedbackAndByeRemainCompatible()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        using var control = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var data = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var logs = Channel.CreateUnbounded<string>();
        int port = FreePair();
        var peer = new RtpMidiDiagnosticPeer(port, line => logs.Writer.TryWrite(line));
        var controlTarget = new IPEndPoint(IPAddress.Loopback, port);
        var dataTarget = new IPEndPoint(IPAddress.Loopback, port + 1);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => peer.SendMidiAsync([0xc0, 1], ct));
            uint sender = await Invite(control, controlTarget, ct);
            Assert.Equal(sender, await Invite(data, dataTarget, ct));
            Assert.False(peer.Connected.IsCompleted);
            for (int i = 0; i < 5; i++)
            {
                byte[] clock = Session("CK", 36); clock[8] = 0;
                BinaryPrimitives.WriteUInt64BigEndian(clock.AsSpan(12), 1234);
                await data.SendAsync(clock, dataTarget, ct);
                byte[] reply = (await data.ReceiveAsync(ct)).Buffer;
                Assert.Equal(1, reply[8]);
                Assert.Equal(sender, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(4)));
                Assert.Equal(1234UL, BinaryPrimitives.ReadUInt64BigEndian(reply.AsSpan(12)));
                Assert.NotEqual(0UL, BinaryPrimitives.ReadUInt64BigEndian(reply.AsSpan(20)));
                clock[8] = 2; await data.SendAsync(clock, dataTarget, ct);
            }
            // Feedback is a barrier: all earlier clock packets on this socket were processed.
            await SendAndAck(65535, [0xc0, 127]);
            Assert.Equal(new byte[] { 0xc0, 127 }, await peer.Messages.ReadAsync(ct));
            Assert.False(peer.Connected.IsCompleted);
            byte[] lastClock = Session("CK", 36); lastClock[8] = 2;
            await data.SendAsync(lastClock, dataTarget, ct);
            await peer.Connected.WaitAsync(ct);

            using var intruder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            await intruder.SendAsync(RtpMidiCodec.Packet([0xc0, 9], 40, 0, 7), dataTarget, ct);
            await SendAndAck(0, [0xbf, 127, 0]); // Sequence wrap; alien endpoint ignored.
            Assert.Equal(new byte[] { 0xbf, 127, 0 }, await peer.Messages.ReadAsync(ct));
            await SendAndAck(1, [0xf0, 1, 0xf0]);
            Assert.True(peer.PendingSysExBytes > 0);
            await SendAndAck(2, [0xf7, 0xf4]);
            Assert.Equal(0, peer.PendingSysExBytes); // Stream cancellation discards partial SysEx.
            await SendAndAck(3, [0xf0, 2, 0xf7]);
            Assert.Equal(new byte[] { 0xf0, 2, 0xf7 }, await peer.Messages.ReadAsync(ct));

            byte[] bye = Session("BY", 16);
            await data.SendAsync(bye, dataTarget, ct);
            await SendAndAck(4, [0xc0, 3]); // Remote BY is diagnostic output, not session recovery.
            Assert.Equal(new byte[] { 0xc0, 3 }, await peer.Messages.ReadAsync(ct));
            Assert.True(peer.Connected.IsCompletedSuccessfully);
            Assert.False(peer.Messages.Completion.IsCompleted);
            Assert.Equal(6, peer.ReceivedPackets);
            Assert.Equal(0, peer.SequenceGaps);
            while (!((await logs.Reader.ReadAsync(ct)).Contains("session disconnected"))) { }

            using var decoder = new RtpMidiCodec(sender, RtpMidiReceivePolicy.Stream);
            await peer.SendMidiAsync([0xcf, 127], ct);
            byte[] sent = (await data.ReceiveAsync(ct)).Buffer;
            Assert.Equal(0, BinaryPrimitives.ReadUInt16BigEndian(sent.AsSpan(2)));
            Assert.Equal(new byte[] { 0xcf, 127 }, Assert.Single(decoder.Decode(sent)).Bytes);
            byte[] large = new byte[70003]; large[0] = 0xf0; large[^1] = 0xf7;
            data.Client.ReceiveBufferSize = 524288;
            await peer.SendMidiAsync(large, ct);
            var messages = new List<RtpMidiMessage>();
            for (int i = 1; i <= 71; i++)
            {
                sent = (await data.ReceiveAsync(ct)).Buffer;
                Assert.Equal(i, BinaryPrimitives.ReadUInt16BigEndian(sent.AsSpan(2)));
                messages.AddRange(decoder.Decode(sent));
            }
            Assert.Equal(large, Assert.Single(messages).Bytes);
            Assert.Equal(72, peer.SentPackets);

            async Task SendAndAck(ushort sequence, byte[] command)
            {
                await data.SendAsync(RtpMidiCodec.Packet(command, sequence, 100, 7), dataTarget, ct);
                var feedback = await control.ReceiveAsync(ct);
                byte[] expected = Session("RS", 12);
                BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(4), sender);
                BinaryPrimitives.WriteUInt16BigEndian(expected.AsSpan(8), sequence);
                Assert.Equal(expected, feedback.Buffer);
                Assert.Equal(controlTarget, feedback.RemoteEndPoint);
            }
        }
        finally { await peer.DisposeAsync(); }
        byte[] closed = (await control.ReceiveAsync(ct)).Buffer;
        Assert.Equal("BY", Encoding.ASCII.GetString(closed, 2, 2));
        Assert.Equal(2U, BinaryPrimitives.ReadUInt32BigEndian(closed.AsSpan(4)));
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32BigEndian(closed.AsSpan(8)));
        await peer.Messages.Completion.WaitAsync(ct);
        // Disposal releases both sockets; no listener remains to reconnect.
        using var reboundControl = new UdpClient(controlTarget);
        using var reboundData = new UdpClient(dataTarget);
    }

    [Theory]
    [InlineData(9)] // Duplicate, unlike the production Stream policy.
    [InlineData(8)] // Reordered.
    [InlineData(11)] // Forward gap.
    public async Task PacketDiscontinuityFaultsDiagnosticStreamWithoutRecovery(ushort next)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        using var control = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var data = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        int port = FreePair();
        await using var peer = new RtpMidiDiagnosticPeer(port);
        var target = new IPEndPoint(IPAddress.Loopback, port + 1);
        await Invite(control, new(IPAddress.Loopback, port), ct);
        await Invite(data, target, ct);
        await data.SendAsync(RtpMidiCodec.Packet([0xc0, 1], 9, 100, 7), target, ct);
        Assert.Equal(new byte[] { 0xc0, 1 }, await peer.Messages.ReadAsync(ct));
        await data.SendAsync(RtpMidiCodec.Packet([0xc0, 2], next, 100, 7), target, ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => peer.Messages.Completion.WaitAsync(ct));
        await Assert.ThrowsAsync<InvalidDataException>(() => peer.Connected.WaitAsync(ct));
        Assert.Equal(1, peer.ReceivedPackets);
        Assert.Equal(1, peer.SequenceGaps);
    }

    [Fact]
    public async Task FailedDataBindDoesNotLeakControlSocket()
    {
        int port = FreePair();
        using (var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, port + 1)))
            Assert.Throws<SocketException>(() => new RtpMidiDiagnosticPeer(port));
        await using var peer = new RtpMidiDiagnosticPeer(port);
    }

    static async Task<uint> Invite(UdpClient socket, IPEndPoint target, CancellationToken ct)
    {
        byte[] invitation = Session("IN", 16);
        BinaryPrimitives.WriteUInt32BigEndian(invitation.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(invitation.AsSpan(8), 123);
        BinaryPrimitives.WriteUInt32BigEndian(invitation.AsSpan(12), 7);
        await socket.SendAsync(invitation, target, ct);
        byte[] reply = (await socket.ReceiveAsync(ct)).Buffer;
        Assert.Equal(30, reply.Length);
        byte[] header = invitation[..12]; header[2] = (byte)'O'; header[3] = (byte)'K';
        Assert.Equal(header, reply[..12]);
        Assert.Equal("Loopback Test\0", Encoding.UTF8.GetString(reply, 16, 14));
        return BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(12));
    }

    static byte[] Session(string command, int length)
    {
        byte[] packet = new byte[length]; packet[0] = packet[1] = 255;
        packet[2] = (byte)command[0]; packet[3] = (byte)command[1]; return packet;
    }

    static int FreePair()
    {
        for (int i = 0; i < 100; i++)
        {
            int port = Random.Shared.Next(20000, 60000);
            try
            {
                using var control = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                using var data = new UdpClient(new IPEndPoint(IPAddress.Loopback, port + 1));
                return port;
            }
            catch (SocketException) { }
        }
        throw new IOException("Unable to reserve synthetic UDP endpoints.");
    }
}
