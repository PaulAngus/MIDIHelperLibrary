namespace MightBeUseful.Midi;

// Only complete MIDI 1.0 messages cross this boundary. OS libraries own packet parsing,
// SysEx reassembly and native lifetimes; the manager owns routing and serialization.
public interface IMidiInputPort : IDisposable
{
    event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
    event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived;
    event EventHandler<string>? ErrorReceived;
    void Start();
    void Stop();
}

public interface IMidiOutputPort : IDisposable
{
    void Send(int message);
    void SendBuffer(byte[] frame);
}

public sealed class MidiShortMessageEventArgs(int rawMessage) : EventArgs
{
    public int RawMessage { get; } = rawMessage;
}

public sealed class MidiSysExMessageEventArgs(byte[] sysexBytes) : EventArgs
{
    public byte[] SysexBytes { get; } = sysexBytes;
}
