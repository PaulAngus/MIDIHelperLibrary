

namespace MightBeUseful.Midi;

/// <summary>Converts MIDI 1.0 UMP from a modern peer before sending to a legacy port.</summary>
public sealed class Midi1CompatiblePort(IMidiDuplexPort port) : IMidiDuplexPort
{
    private readonly SysEx7Assembler[] _sysEx = Enumerable.Range(0, 16).Select(_ => new SysEx7Assembler()).ToArray();

    public string InputName => port.InputName;
    public string OutputName => port.OutputName;

    public IAsyncEnumerable<MidiPacket> ReadAllAsync(CancellationToken cancellationToken) => port.ReadAllAsync(cancellationToken);

    public ValueTask SendAsync(MidiPacket packet, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (packet.Format == MidiPacketFormat.Midi1) { return port.SendAsync(packet, cancellationToken); }

        uint[] words = packet.ToUmpWords();
        uint type = words[0] >> 28;
        if (type == 0) { return ValueTask.CompletedTask; } // UMP utility messages have no MIDI 1.0 equivalent.
        if (type is not (1 or 2 or 3))
        {
            throw new NotSupportedException("This endpoint supports MIDI 1.0 messages and SysEx7 only. Configure the remote device to use MIDI 1.0.");
        }

        int group = (int)((words[0] >> 24) & 0xf);
        byte[]? message = UmpMidi1Codec.Decode(words, _sysEx[group]);
        return message is null ? ValueTask.CompletedTask
            : port.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, message), cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        try { await port.DisposeAsync().ConfigureAwait(false); }
        finally { foreach (SysEx7Assembler assembler in _sysEx) { assembler.Dispose(); } }
    }
}
