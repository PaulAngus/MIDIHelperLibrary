namespace MightBeUseful.Midi;

/// <summary>Serializes writes and disposal; a command batch cannot interleave with controller thru.</summary>
public sealed class SerializedMidiOutputPort(IMidiOutputPort output) : IMidiOutputPort
{
    private readonly object _gate = new();
    private bool _disposed;
    public void Send(int message)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); output.Send(message); }
    }
    public void SendBuffer(byte[] frame)
    {
        lock (_gate) { ObjectDisposedException.ThrowIf(_disposed, this); output.SendBuffer(frame); }
    }
    public void SendBatch(ReadOnlySpan<int> messages)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            foreach (int message in messages) { output.Send(message); }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) { return; }
            _disposed = true;
            output.Dispose();
        }
    }
}
