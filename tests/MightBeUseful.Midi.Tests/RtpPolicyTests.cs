using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MightBeUseful.Midi.Rtp;

namespace MightBeUseful.Midi.Tests;

public sealed class RtpPolicyTests
{
    private static byte[] Packet(ushort sequence, params byte[] command) => RtpMidiCodec.Packet(command, sequence, 100, 7);

    [Theory]
    [InlineData(RtpMidiReceivePolicy.Strict)]
    [InlineData(RtpMidiReceivePolicy.Stream)]
    public void SequenceWrapAndLossNeverReplay(RtpMidiReceivePolicy policy)
    {
        using var codec = new RtpMidiCodec(7, policy);
        Assert.Single(codec.Decode(Packet(65535, 0xc0, 1)));
        Assert.Single(codec.Decode(Packet(0, 0xc0, 2)));
        if (policy == RtpMidiReceivePolicy.Strict)
            Assert.Throws<IOException>(() => codec.Decode(Packet(0, 0xc0, 2)));
        else
        {
            Assert.Empty(codec.Decode(Packet(0, 0xc0, 2)));
            Assert.Empty(codec.Decode(Packet(65535, 0xc0, 1)));
        }
        Assert.Throws<IOException>(() => codec.Decode(Packet(2, 0xc0, 3)));
    }

    [Theory]
    [InlineData(RtpMidiReceivePolicy.Strict)]
    [InlineData(RtpMidiReceivePolicy.Stream)]
    public void CancellationAndReplacementRetainPolicy(RtpMidiReceivePolicy policy)
    {
        using var codec = new RtpMidiCodec(7, policy);
        Assert.Empty(codec.Decode(Packet(0, 0xf0, 1, 0xf0)));
        if (policy == RtpMidiReceivePolicy.Strict)
        {
            Assert.Throws<IOException>(() => codec.Decode(Packet(1, 0xf7, 0xf4)));
            Assert.Throws<InvalidDataException>(() => codec.Decode(Packet(2, 0xf0, 2, 0xf7)));
        }
        else
        {
            Assert.Empty(codec.Decode(Packet(1, 0xf7, 0xf4)));
            Assert.False(codec.Incomplete);
            Assert.Equal(new byte[] { 0xf0, 2, 0xf7 }, Assert.Single(codec.Decode(Packet(2, 0xf0, 2, 0xf7))).Bytes);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1002)]
    [InlineData(4096)]
    [InlineData(70000)]
    public void FrameLimitsAndWireHeadersRemainDistinct(int size)
    {
        byte[] frame = new byte[size]; frame[0] = 0xf0; frame[^1] = 0xf7;
        using var stream = new RtpMidiCodec(7, RtpMidiReceivePolicy.Stream);
        ushort sequence = 65530;
        var decoded = RtpMidiCodec.Encode(frame).SelectMany(c => stream.Decode(Packet(sequence++, c))).ToArray();
        Assert.Equal(frame, Assert.Single(decoded).Bytes);
        sequence = 65530;
        if (size > 4096) { Assert.Throws<ArgumentException>(() => RtpMidiCodec.Encode(frame, 7, ref sequence, 100)); return; }
        var packets = RtpMidiCodec.Encode(frame, 7, ref sequence, 100);
        using var strict = new RtpMidiCodec(7);
        Assert.Equal(frame, Assert.Single(packets.SelectMany(p => strict.Decode(p))).Bytes);
        Assert.All(packets, p => { Assert.Equal(0xe1, p[1]); Assert.NotEqual(0, p[12] & 128); });
        Assert.Equal((int)Math.Ceiling(Math.Max(1, size - 2) / 1000.0), packets.Length);
    }

    [Fact]
    public void JournalBoundaryAndTimestampPolicyIsExplicit()
    {
        var packet = Packet(0, 2, 0x90, 60, 100, 3, 61, 90);
        packet[12] |= 0x20;
        using var strict = new RtpMidiCodec(7);
        Assert.Equal(new uint[] { 102, 105 }, strict.Decode(packet).Select(m => m.Timestamp));
        using var invalid = new RtpMidiCodec(7);
        Assert.Throws<InvalidDataException>(() => invalid.Decode([.. packet, 0]));
        using var stream = new RtpMidiCodec(7, RtpMidiReceivePolicy.Stream);
        Assert.Equal(2, stream.Decode([.. packet, 0]).Count);
    }

    // Real UDP, synthetic peers only. No native MIDI assembly is referenced.
    [Fact]
    public async Task LoopbackFailureCreatesFreshSessionWithoutReplayingPartialMidi()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = deadline.Token;
        var (control, data, driverPort) = BindPair();
        using var controlOwner = control;
        using var dataOwner = data;
        var (unusedControl, unusedData, peerPort) = BindPair();
        unusedControl.Dispose(); unusedData.Dispose();
        await using var peer = new RtpMidiLoopbackPort("Synthetic", driverPort, peerPort: peerPort);
        var controlTarget = new IPEndPoint(IPAddress.Loopback, peerPort);
        var dataTarget = new IPEndPoint(IPAddress.Loopback, peerPort + 1);

        async Task<IMidiDuplexPort> Connect(uint token)
        {
            byte[] invite = new byte[16]; invite[0] = invite[1] = 255; invite[2] = (byte)'I'; invite[3] = (byte)'N';
            BinaryPrimitives.WriteUInt32BigEndian(invite.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32BigEndian(invite.AsSpan(8), token);
            BinaryPrimitives.WriteUInt32BigEndian(invite.AsSpan(12), 7);
            await control.SendAsync(invite, controlTarget, ct);
            await Receive(control, b => b.Length >= 16 && b[2] == 'O' && b[3] == 'K', ct);
            await data.SendAsync(invite, dataTarget, ct);
            await Receive(data, b => b.Length >= 16 && b[2] == 'O' && b[3] == 'K', ct);
            byte[] sync = new byte[36]; sync[0] = sync[1] = 255; sync[2] = (byte)'C'; sync[3] = (byte)'K'; sync[8] = 2;
            BinaryPrimitives.WriteUInt32BigEndian(sync.AsSpan(4), 7);
            for (int i = 0; i < 6; i++) await data.SendAsync(sync, dataTarget, ct);
            return await peer.WaitForConnectionAsync(ct);
        }
        var first = await Connect(10);
        await using var reader = first.ReadAllAsync(ct).GetAsyncEnumerator();
        await data.SendAsync(Packet(0, 0xf0, 1, 0xf0), dataTarget, ct);
        await data.SendAsync(Packet(2, 0xf7, 2, 0xf7), dataTarget, ct);
        await Assert.ThrowsAsync<IOException>(async () => await reader.MoveNextAsync());
        await Assert.ThrowsAsync<IOException>(async () => await first.SendAsync(new(MidiPacketFormat.Midi1, new byte[] { 0xc0, 9 }), ct));
        // The listener itself invites again; application code does not restart it.
        await Receive(control, b => b.Length >= 16 && b[2] == 'I' && b[3] == 'N', ct);
        var second = await Connect(11);
        Assert.NotSame(first, second);
        await using var next = second.ReadAllAsync(ct).GetAsyncEnumerator();
        await data.SendAsync(Packet(400, 0xf0, 3, 0xf7), dataTarget, ct);
        Assert.True(await next.MoveNextAsync());
        Assert.Equal(new byte[] { 0xf0, 3, 0xf7 }, next.Current.Data.ToArray());
        await second.SendAsync(new(MidiPacketFormat.Midi1, new byte[] { 0xc0, 4 }), ct);
        byte[] sent = await Receive(data, b => b[0] == 128, ct);
        uint sender = BinaryPrimitives.ReadUInt32BigEndian(sent.AsSpan(8));
        using var decoder = new RtpMidiCodec(sender, RtpMidiReceivePolicy.Stream);
        Assert.Equal(new byte[] { 0xc0, 4 }, Assert.Single(decoder.Decode(sent)).Bytes);
        await first.DisposeAsync(); await second.DisposeAsync();
    }

    private static async Task<byte[]> Receive(UdpClient socket, Func<byte[], bool> match, CancellationToken ct)
    {
        while (true) { var bytes = (await socket.ReceiveAsync(ct)).Buffer; if (match(bytes)) return bytes; }
    }
    private static (UdpClient, UdpClient, int) BindPair()
    {
        for (int i = 0; i < 100; i++)
        {
            int port = Random.Shared.Next(20000, 60000); UdpClient? first = null;
            try { first = new(new IPEndPoint(IPAddress.Loopback, port)); return (first, new(new IPEndPoint(IPAddress.Loopback, port + 1)), port); }
            catch (SocketException) { first?.Dispose(); }
        }
        throw new IOException("Unable to reserve synthetic UDP endpoints.");
    }
}
