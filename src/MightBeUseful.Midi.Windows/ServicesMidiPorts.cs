using MightBeUseful.Midi;

namespace MightBeUseful.Midi.Windows;

internal sealed class ServicesMidiOutputPort(IMidiDuplexPort port) : IMidiOutputPort
{
    public void Send(int message) => port.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, Midi1ShortMessageCodec.Decode(message)), CancellationToken.None).GetAwaiter().GetResult();
    public void SendBuffer(byte[] frame) => port.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, frame), CancellationToken.None).GetAwaiter().GetResult();
    public void Dispose() => port.DisposeAsync().GetAwaiter().GetResult();
}

internal sealed class ServicesMidiInputPort(IMidiDuplexPort port) : IMidiInputPort
{
    private readonly object _gate = new();
    private CancellationTokenSource? _listening;
    private Task? _pump;
    private bool _disposed;
    public event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
    public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived;
    public event EventHandler<string>? ErrorReceived;
    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_listening is not null) { return; }
            _listening = new CancellationTokenSource();
            var token = _listening.Token;
            _pump = Task.Run(() => PumpAsync(token));
        }
    }
    private async Task PumpAsync(CancellationToken token)
    {
        using var decoder = new Midi1PacketDecoder();
        try
        {
            await foreach (var packet in port.ReadAllAsync(token).ConfigureAwait(false))
            {
                var bytes = decoder.Decode(packet);
                if (bytes is null) { continue; }
                token.ThrowIfCancellationRequested();
                if (bytes[0] == 0xf0) { SysexMessageReceived?.Invoke(this, new(bytes)); }
                else { MessageReceived?.Invoke(this, new(Midi1ShortMessageCodec.Encode(bytes))); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            try { ErrorReceived?.Invoke(this, (error.InnerException ?? error).Message); }
            catch { }
        }
    }
    public void Stop()
    {
        lock (_gate)
        {
            var listening = _listening;
            var pump = _pump;
            _listening = null;
            _pump = null;
            listening?.Cancel();
            if (pump is not null)
            {
                _ = pump.ContinueWith(_ => listening?.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            else { listening?.Dispose(); }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
        }
        Stop();
        port.DisposeAsync().GetAwaiter().GetResult();
    }
}
