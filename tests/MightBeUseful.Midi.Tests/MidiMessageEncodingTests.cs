namespace MightBeUseful.Midi.Tests;

public class MidiMessageEncodingTests
{
    [Fact]
    public void CcAndPcUseOneBasedChannelsAndPreserveRawZeroBasedData()
    {
        for (int channel = 1; channel <= 16; channel++)
        foreach (int value in new[] { 0, 1, 127 })
        {
            Assert.Equal(new byte[] { (byte)(0xc0 + channel - 1), (byte)value },
                MidiMessageEncoding.GetShortMessageBytes(MidiMessageEncoding.ProgramChange(value, channel)));
            foreach (int controller in new[] { 0, 1, 127 })
                Assert.Equal(new byte[] { (byte)(0xb0 + channel - 1), (byte)controller, (byte)value },
                    MidiMessageEncoding.GetShortMessageBytes(MidiMessageEncoding.ControlChange(controller, value, channel)));
        }
    }
}
