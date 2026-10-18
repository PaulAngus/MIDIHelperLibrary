using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

namespace MightBeUseful.Midi.Rtp;

/// <summary>
/// Loopback-only diagnostic responder, with strict packet continuity and measurement counters.
/// This is not the reconnecting production relay: invitations are accepted, six completed clock
/// exchanges signal readiness, and receive failures complete the message stream with an error.
/// A peer BY is logged only. There are no invitation retries, heartbeat recovery or MIDI replay.
/// </summary>
public sealed class RtpMidiDiagnosticPeer : IAsyncDisposable
{
    readonly UdpClient control;
    readonly UdpClient data;
    readonly Action<string>? log;
    readonly CancellationTokenSource stop = new();
    readonly Channel<byte[]> messages = Channel.CreateUnbounded<byte[]>();
    readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly uint ssrc = (uint)Random.Shared.Next(1, int.MaxValue);
    RtpMidiCodec? codec;
    uint remoteSender;
    readonly Task controlPump, dataPump;
    IPEndPoint? remoteControl, remoteData;
    ushort sequence;
    ushort? receivedSequence;
    int syncExchanges;
    public int SentPackets { get; private set; }
    public int ReceivedPackets { get; private set; }
    public int SequenceGaps { get; private set; }
    public long PendingSysExBytes => codec?.BufferedSysExBytes ?? 0;
    public ChannelReader<byte[]> Messages => messages.Reader;
    public Task Connected => connected.Task;

    /// <param name="controlPort">Local loopback control port; the data port is controlPort + 1.</param>
    /// <param name="log">Optional diagnostic output. No native MIDI discovery is performed.</param>
    public RtpMidiDiagnosticPeer(int controlPort = 55006, Action<string>? log = null)
    {
        if (controlPort is < 1 or > 65534) throw new ArgumentOutOfRangeException(nameof(controlPort));
        this.log = log;
        control = new(new IPEndPoint(IPAddress.Loopback, controlPort));
        try { data = new(new IPEndPoint(IPAddress.Loopback, controlPort + 1)); }
        catch { control.Dispose(); stop.Dispose(); throw; }
        log?.Invoke($"Default RTP receive buffer: {data.Client.ReceiveBufferSize} bytes");
        data.Client.ReceiveBufferSize = 262144;
        log?.Invoke($"RTP test receive buffer: {data.Client.ReceiveBufferSize} bytes");
        controlPump = PumpAsync(control, false);
        dataPump = PumpAsync(data, true);
        log?.Invoke($"RTP diagnostic peer ready on loopback UDP {controlPort}/{controlPort + 1}");
    }

    static ulong Clock() => (ulong)(Stopwatch.GetTimestamp() * 10000.0 / Stopwatch.Frequency);

    async Task PumpAsync(UdpClient socket, bool midiPort)
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                UdpReceiveResult received = await socket.ReceiveAsync(stop.Token);
                byte[] packet = received.Buffer;
                if (!IPAddress.IsLoopback(received.RemoteEndPoint.Address)) continue;
                if (packet.Length >= 4 && packet[0] == 255 && packet[1] == 255)
                {
                    string command = Encoding.ASCII.GetString(packet, 2, 2);
                    if (command == "IN" && packet.Length >= 16)
                    {
                        if (midiPort) { remoteData = received.RemoteEndPoint; remoteSender = BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(12)); }
                        else remoteControl = received.RemoteEndPoint;
                        byte[] reply = new byte[16 + 14];
                        packet.AsSpan(0, 12).CopyTo(reply);
                        reply[2] = (byte)'O'; reply[3] = (byte)'K';
                        BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(12), ssrc);
                        Encoding.UTF8.GetBytes("Loopback Test\0").CopyTo(reply, 16);
                        await socket.SendAsync(reply, received.RemoteEndPoint, stop.Token);
                        log?.Invoke($"Accepted Apple invitation on {(midiPort ? "data" : "control")} port from {received.RemoteEndPoint}");
                    }
                    else if (command == "CK" && packet.Length >= 36)
                    {
                        log?.Invoke($"Clock count={packet[8]}, t1={BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(12))}, t2={BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(20))}, t3={BinaryPrimitives.ReadUInt64BigEndian(packet.AsSpan(28))}, ours={Clock()}");
                        if (packet[8] == 0)
                        {
                            byte[] reply = packet.ToArray();
                            BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(4), ssrc);
                            reply[8] = 1;
                            BinaryPrimitives.WriteUInt64BigEndian(reply.AsSpan(20), Clock());
                            await socket.SendAsync(reply, received.RemoteEndPoint, stop.Token);
                        }
                        else if (packet[8] == 2)
                        {
                            // This driver sends six initial sync exchanges at 1.5s intervals.
                            // Begin the throughput test after clock calibration, not just invitation acceptance.
                            if (++syncExchanges >= 6) connected.TrySetResult();
                        }
                    }
                    else if (command == "BY")
                    {
                        log?.Invoke("Apple MIDI session disconnected");
                    }
                    continue;
                }
                if (ReceivedPackets < 2) log?.Invoke($"Received datagram on {(midiPort ? "data" : "control")} from {received.RemoteEndPoint}, head {Convert.ToHexString(packet.AsSpan(0, Math.Min(packet.Length, 32)))}");
                if (!midiPort || remoteData is null || !received.RemoteEndPoint.Equals(remoteData)) continue;
                ParseMidi(packet);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error)
        {
            log?.Invoke("RTP receive failure: " + error);
            connected.TrySetException(error);
            messages.Writer.TryComplete(error);
        }
    }

    void ParseMidi(byte[] packet)
    {
        if (packet.Length < 13 || packet[0] != 0x80 || (packet[1] & 0x7f) != 0x61)
            throw new InvalidDataException("Unexpected RTP header");
        ushort gotSequence = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2));
        if (receivedSequence is ushort previous && gotSequence != unchecked((ushort)(previous + 1)))
        {
            SequenceGaps++;
            throw new InvalidDataException($"RTP sequence gap: {previous} -> {gotSequence}");
        }
        receivedSequence = gotSequence;
        ReceivedPackets++;
        codec ??= new(remoteSender, RtpMidiReceivePolicy.Stream);
        foreach (var message in codec.Decode(packet)) messages.Writer.TryWrite(message.Bytes);
        // ACK the received sequence so Apple's guard journals remain small.
        if (remoteControl is not null)
        {
            byte[] feedback = new byte[12]; feedback[0] = feedback[1] = 255;
            feedback[2] = (byte)'R'; feedback[3] = (byte)'S';
            BinaryPrimitives.WriteUInt32BigEndian(feedback.AsSpan(4), ssrc);
            BinaryPrimitives.WriteUInt16BigEndian(feedback.AsSpan(8), gotSequence);
            control.Send(feedback, remoteControl);
        }
    }

    public async Task SendMidiAsync(byte[] message, CancellationToken cancellationToken)
    {
        if (remoteData is null) throw new InvalidOperationException("RTP session is not connected");
        foreach (var command in RtpMidiCodec.Encode(message))
            await SendCommandAsync(command, cancellationToken);
    }

    async Task SendCommandAsync(byte[] command, CancellationToken cancellationToken)
    {
        byte[] packet = RtpMidiCodec.Packet(command, sequence++, (uint)Clock(), ssrc);
        await data.SendAsync(packet, remoteData!, cancellationToken);
        SentPackets++;
    }

    public async ValueTask DisposeAsync()
    {
        if (remoteControl is not null)
        {
            byte[] bye = new byte[16]; bye[0] = bye[1] = 255; bye[2] = (byte)'B'; bye[3] = (byte)'Y';
            BinaryPrimitives.WriteUInt32BigEndian(bye.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32BigEndian(bye.AsSpan(12), ssrc);
            await control.SendAsync(bye, remoteControl);
        }
        await stop.CancelAsync();
        await Task.WhenAll(controlPump, dataPump);
        messages.Writer.TryComplete();
        codec?.Dispose(); control.Dispose(); data.Dispose(); stop.Dispose();
    }
}
