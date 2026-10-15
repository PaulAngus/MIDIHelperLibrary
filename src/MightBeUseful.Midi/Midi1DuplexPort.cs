using System.Threading.Channels;

namespace MightBeUseful.Midi;

/// <summary>Composes the same MIDI 1.0 input/output adapters used by the controller applications.</summary>
public sealed class Midi1DuplexPort : IMidiDuplexPort
{
    private readonly IMidiInputPort _input;
    private readonly IMidiOutputPort _output;
    private readonly object _sendGate = new();
    private readonly Channel<MidiPacket> _messages = Channel.CreateBounded<MidiPacket>(1024);
    private bool _disposed;

    public Midi1DuplexPort(IMidiInputPort input, IMidiOutputPort output, string inputName, string outputName)
    {
        _input = input;
        _output = output;
        InputName = inputName;
        OutputName = outputName;
        _input.MessageReceived += Receive;
        _input.SysexMessageReceived += ReceiveSysEx;
        _input.ErrorReceived += ReceiveError;
        try { _input.Start(); }
        catch { DisposeAsync().GetAwaiter().GetResult(); throw; }
    }

    public string InputName { get; }
    public string OutputName { get; }
    public IAsyncEnumerable<MidiPacket> ReadAllAsync(CancellationToken cancellationToken) => _messages.Reader.ReadAllAsync(cancellationToken);

    public ValueTask SendAsync(MidiPacket packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (packet.Format != MidiPacketFormat.Midi1) { throw new NotSupportedException("This endpoint accepts MIDI 1.0 bytes; use the shared UMP compatibility adapter."); }
        packet.Validate();
        lock (_sendGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (packet.Data.Span[0] == 0xf0) { _output.SendBuffer(packet.Data.ToArray()); }
            else { _output.Send(Midi1ShortMessageCodec.Encode(packet.Data.Span)); }
        }
        return ValueTask.CompletedTask;
    }

    private void Receive(object? sender, MidiShortMessageEventArgs e)
    {
        try { Enqueue(Midi1ShortMessageCodec.Decode(e.RawMessage)); }
        catch (Exception error) { _messages.Writer.TryComplete(error); }
    }
    private void ReceiveSysEx(object? sender, MidiSysExMessageEventArgs e) => Enqueue(e.SysexBytes.ToArray());
    private void ReceiveError(object? sender, string error) => _messages.Writer.TryComplete(new IOException(error));
    private void Enqueue(byte[] data)
    {
        if (!_disposed && !_messages.Writer.TryWrite(new MidiPacket(MidiPacketFormat.Midi1, data)))
        {
            _messages.Writer.TryComplete(new IOException("MIDI receive queue overflow; the session cannot continue losslessly."));
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_sendGate)
        {
            if (_disposed) { return ValueTask.CompletedTask; }
            _disposed = true;
        }
        _input.MessageReceived -= Receive;
        _input.SysexMessageReceived -= ReceiveSysEx;
        _input.ErrorReceived -= ReceiveError;
        try { _input.Stop(); }
        finally
        {
            try { _input.Dispose(); }
            finally
            {
                try { lock (_sendGate) { _output.Dispose(); } }
                finally { _messages.Writer.TryComplete(); }
            }
        }
        return ValueTask.CompletedTask;
    }
}
