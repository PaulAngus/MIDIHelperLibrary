namespace MightBeUseful.Midi;

public static class MidiMessageEncoding
{
    // Callers range-check MIDI channels (1-16) and data values (0-127) before encoding.
    public static int ControlChange(int controller, int value, int channel) =>
        (0xb0 | (channel - 1)) | (controller << 8) | (value << 16);

    public static int ProgramChange(int program, int channel) =>
        (0xc0 | (channel - 1)) | (program << 8);

    public static byte[] GetShortMessageBytes(int message)
    {
        byte status = (byte)message;
        int length = status switch
        {
            >= 0x80 and <= 0xbf or >= 0xe0 and <= 0xef or 0xf2 => 3,
            >= 0xc0 and <= 0xdf or 0xf1 or 0xf3 => 2,
            0xf6 or >= 0xf8 => 1,
            _ => throw new ArgumentException("Expected a complete MIDI short message.", nameof(message))
        };
        return length switch
        {
            1 => [status],
            2 => [status, (byte)(message >> 8)],
            _ => [status, (byte)(message >> 8), (byte)(message >> 16)]
        };
    }
}
