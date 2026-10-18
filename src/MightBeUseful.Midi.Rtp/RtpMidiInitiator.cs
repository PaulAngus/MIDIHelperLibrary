using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
namespace MightBeUseful.Midi.Rtp;

/// <summary>Explicit Apple network-MIDI initiator. One peer, no discovery, reconnection or edit replay.</summary>
public sealed class RtpMidiInitiator : IAsyncDisposable
{
    public event Action<byte[]>? Received;
    public event Action<Exception>? Faulted;
    public event Action<uint, uint>? ShortReceived;
    private readonly UdpClient control, data;
    private string sessionName = "MIDI";
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly uint token = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4)), source = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
    private uint peer;
    private ushort sequence = (ushort)RandomNumberGenerator.GetInt32(65536);
    private Task[] workers = [];
    private Exception? failure;
    private bool disposed;
    private long offset, lastSync, pendingSync, partialSince;
    private RtpMidiCodec? codec;
    private static long Clock => (long)(Stopwatch.GetTimestamp() * (10000.0 / Stopwatch.Frequency));

    public static IPEndPoint ParseEndpoint(string endpoint)
    {
        string[] parts = endpoint.Split(':');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != AddressFamily.InterNetwork ||
            address.ToString() != parts[0] || (!parts[1].All(char.IsAsciiDigit) || !int.TryParse(parts[1], out int port)) || port is < 1 or > 65534 ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) || address.GetAddressBytes()[0] >= 224)
            throw new ArgumentException("Network endpoint must be a unicast IPv4 literal and control port 1–65534; MIDI uses port + 1.");
        return new(address, port);
    }
    private RtpMidiInitiator(IPEndPoint endpoint)
    {
        UdpClient? first = null, second = null;
        try
        {
            for (int attempt = 0; attempt < 64; attempt++)
            {
                int port = RandomNumberGenerator.GetInt32(20000, 60000);
                try { first = new UdpClient(new IPEndPoint(IPAddress.Any, port)); second = new UdpClient(new IPEndPoint(IPAddress.Any, port + 1)); break; }
                catch (SocketException) { first?.Dispose(); second?.Dispose(); first = second = null; }
            }
            if (first == null || second == null) throw new IOException("Cannot bind a consecutive network MIDI port pair.");
            control = first; data = second; data.Client.ReceiveBufferSize = 262144;
            control.Connect(endpoint); data.Connect(new IPEndPoint(endpoint.Address, endpoint.Port + 1));
        }
        catch { first?.Dispose(); second?.Dispose(); throw; }
    }
    public static async Task<RtpMidiInitiator> ConnectAsync(string endpoint, CancellationToken cancellationToken = default, string sessionName = "MIDI")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionName);
        if (Encoding.ASCII.GetByteCount(sessionName) > 7) throw new ArgumentException("Session name must fit seven ASCII bytes.", nameof(sessionName));
        cancellationToken.ThrowIfCancellationRequested();
        var transport = new RtpMidiInitiator(ParseEndpoint(endpoint)) { sessionName = sessionName };
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(TimeSpan.FromSeconds(12));
            transport.peer = await transport.Invite(transport.control, null, deadline.Token);
            await transport.Invite(transport.data, transport.peer, deadline.Token);
            transport.codec = new(transport.peer);
            transport.workers = [transport.ReceiveControl(), transport.ReceiveData(), transport.Synchronize()];
            await transport.ready.Task.WaitAsync(deadline.Token);
            transport.Check(); return transport;
        }
        catch { await transport.DisposeAsync(); throw; }
    }
    private byte[] SessionPacket(string command)
    {
        byte[] packet = new byte[24]; packet[0] = packet[1] = 255; packet[2] = (byte)command[0]; packet[3] = (byte)command[1];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), token);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(12), source);
        Encoding.ASCII.GetBytes(sessionName).CopyTo(packet, 16); return packet;
    }
    private async Task<uint> Invite(UdpClient socket, uint? expected, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            await socket.SendAsync(SessionPacket("IN"), ct);
            using var window = CancellationTokenSource.CreateLinkedTokenSource(ct); window.CancelAfter(1000);
            try
            {
                while (true)
                {
                    var bytes = (await socket.ReceiveAsync(window.Token)).Buffer;
                    if (bytes.Length < 16 || bytes.Length > 1024 || bytes[0] != 255 || bytes[1] != 255 ||
                        BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) != 2 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)) != token) continue;
                    if (bytes[2] == 'N' && bytes[3] == 'O') throw new IOException("Network MIDI invitation rejected.");
                    if (bytes[2] != 'O' || bytes[3] != 'K') continue;
                    uint identity = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12));
                    if (expected.HasValue && identity != expected) throw new IOException("Network MIDI peer identity changed during invitation.");
                    return identity;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
        throw new TimeoutException("Network MIDI invitation timed out.");
    }
    private void Check()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Volatile.Read(ref failure) is Exception error) throw new IOException("Network session failed; retain evidence and reconcile without replay.", error);
    }
    private void Fail(Exception error)
    {
        if (disposed || Interlocked.CompareExchange(ref failure, error, null) != null) return;
        ready.TrySetException(error); stop.Cancel(); Faulted?.Invoke(error);
    }
    private void CheckExit(byte[] bytes)
    {
        if (bytes.Length is >= 16 and <= 1024 && bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 'B' && bytes[3] == 'Y' &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) == 2 && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(8)) == token &&
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(12)) == peer)
            throw new IOException("Network MIDI peer ended the session; pending delivery is uncertain.");
    }
    private async Task ReceiveControl()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                byte[] bytes = (await control.ReceiveAsync(stop.Token)).Buffer; CheckExit(bytes);
                // Receiver feedback is transport evidence only. It never proves hardware execution or persistence.
                if (bytes.Length == 12 && bytes[0] == 255 && bytes[1] == 255 && bytes[2] == 'R' && bytes[3] == 'S' &&
                    BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) == peer) continue;
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }
    private async Task ReceiveData()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                byte[] bytes = (await data.ReceiveAsync(stop.Token)).Buffer; CheckExit(bytes);
                if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 255)
                {
                    if (bytes[2] == 'C' && bytes[3] == 'K') await SyncReply(bytes);
                    continue;
                }
                if (!ready.Task.IsCompletedSuccessfully) continue; // subscription starts only after clock synchronization
                var messages = codec!.Decode(bytes);
                if (codec.Incomplete) { if (partialSince == 0) partialSince = Clock; } else partialSince = 0;
                foreach (var message in messages)
                {
                    int delayTicks = unchecked((int)(message.Timestamp - (uint)(Clock + Interlocked.Read(ref offset))));
                    if (delayTicks > 20000) throw new IOException("Peer scheduled MIDI more than two seconds ahead.");
                    if (delayTicks > 0) await Task.Delay(TimeSpan.FromMilliseconds(delayTicks / 10.0), stop.Token);
                    if (message.Bytes[0] == 240) Received?.Invoke(message.Bytes);
                    else
                    {
                        uint packed = 0; for (int i = 0; i < message.Bytes.Length; i++) packed |= (uint)message.Bytes[i] << (8 * i);
                        ShortReceived?.Invoke(packed, message.Timestamp / 10);
                    }
                }
                // Apple-compatible feedback places the RTP sequence in the first 16 bits of its four-byte field.
                byte[] feedback = [255, 255, (byte)'R', (byte)'S', 0, 0, 0, 0, bytes[2], bytes[3], 0, 0];
                BinaryPrimitives.WriteUInt32BigEndian(feedback.AsSpan(4), source); await control.SendAsync(feedback, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }
    private async Task SyncReply(byte[] bytes)
    {
        if (bytes.Length != 36 || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) != peer || bytes[8] != 1) return;
        long sent = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(12));
        if (sent != Interlocked.Read(ref pendingSync)) return;
        long now = Clock, remote = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(20));
        Interlocked.Exchange(ref offset, remote - (sent + now) / 2); Interlocked.Exchange(ref lastSync, now);
        bytes[8] = 2; BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), source); BinaryPrimitives.WriteInt64BigEndian(bytes.AsSpan(28), now);
        await data.SendAsync(bytes, stop.Token); ready.TrySetResult();
    }
    private async Task Synchronize()
    {
        try
        {
            long started = Clock; int cycles = 0;
            while (!stop.IsCancellationRequested)
            {
                long now = Clock;
                if (now - Math.Max(started, Interlocked.Read(ref lastSync)) > 150000) throw new TimeoutException("Network MIDI clock heartbeat lost.");
                if (partialSince != 0 && now - partialSince > 100000) throw new TimeoutException("Incomplete network SysEx expired.");
                if (cycles++ % 5 == 0)
                {
                    byte[] sync = new byte[36]; sync[0] = sync[1] = 255; sync[2] = (byte)'C'; sync[3] = (byte)'K';
                    BinaryPrimitives.WriteUInt32BigEndian(sync.AsSpan(4), source); BinaryPrimitives.WriteInt64BigEndian(sync.AsSpan(12), now);
                    Interlocked.Exchange(ref pendingSync, now); await data.SendAsync(sync, stop.Token);
                }
                await Task.Delay(1000, stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (Exception error) { Fail(error); }
    }
    public async Task SendAsync(byte[] message, CancellationToken ct)
    {
        await sending.WaitAsync(ct);
        try
        {
            Check();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
            foreach (var packet in RtpMidiCodec.Encode(message, source, ref sequence, (uint)Clock))
            {
                await data.SendAsync(packet, deadline.Token);
                await Task.Delay(1, deadline.Token);
            }
        }
        catch (Exception error) { Fail(error); throw; }
        finally { sending.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        try { using var end = new CancellationTokenSource(250); await control.SendAsync(SessionPacket("BY"), end.Token); } catch (Exception) { }
        stop.Cancel(); control.Dispose(); data.Dispose();
        try { await Task.WhenAll(workers); } finally { stop.Dispose(); }
    }
}
