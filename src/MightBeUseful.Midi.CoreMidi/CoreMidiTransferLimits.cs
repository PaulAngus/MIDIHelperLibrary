namespace MightBeUseful.Midi.CoreMidi;

public static class CoreMidiTransferLimits
{
    public const int StandardMaximumSysExTransmitBytes = 60_000;

    public static void ValidateStandardSysEx(int length)
    {
        if (length > StandardMaximumSysExTransmitBytes)
        {
            throw new NotSupportedException($"The standard CoreMIDI adapter supports SysEx sends up to {StandardMaximumSysExTransmitBytes} bytes. Use the bulk-relay profile for larger transfers.");
        }
    }
}
