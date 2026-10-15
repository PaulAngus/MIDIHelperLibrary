using System.Runtime.CompilerServices;
using MightBeUseful.Midi;



namespace MightBeUseful.Midi.Windows;

/// <summary>WinMM can split a SysEx message across multiple native input buffers.</summary>
internal sealed class WinMmSysExAssembler : IDisposable
{
    private readonly MemoryStream _buffer = new();

    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> bytes)
    {
        var messages = new List<byte[]>();
        foreach (byte value in bytes)
        {
            if (value >= 0xf8)
            {
                messages.Add([value]);
                continue;
            }

            if (value == 0xf0) { _buffer.SetLength(0); }
            else if (_buffer.Length == 0)
            {
                throw new InvalidDataException("Received a WinMM SysEx continuation without a start byte.");
            }
            else if (value >= 0x80 && value != 0xf7)
            {
                _buffer.SetLength(0);
                throw new InvalidDataException("Unexpected status byte in WinMM SysEx input.");
            }

            if (_buffer.Length >= MidiFrame.MaximumLength)
            {
                _buffer.SetLength(0);
                throw new InvalidDataException("WinMM SysEx input exceeds the MIDI frame size limit.");
            }
            _buffer.WriteByte(value);
            if (value == 0xf7)
            {
                messages.Add(_buffer.ToArray());
                _buffer.SetLength(0);
            }
        }
        return messages;
    }

    public void Dispose() => _buffer.Dispose();
}
