namespace MightBeUseful.Midi.Tests;

public sealed class Midi1PacketDecoderTests
{
    [Theory]
    [InlineData(0x40903c00u)]
    [InlineData(0x50000000u)]
    public void UnsupportedMidi2AndSysEx8AreExplicitAndPlatformNeutral(uint word)
    {
        using var decoder = new Midi1PacketDecoder();
        var error = Assert.Throws<IOException>(() => decoder.Decode(MidiPacket.FromUmpWords([word, 0, 0, 0])));
        Assert.Contains("MIDI 1.0 endpoint", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("macOS", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Windows", error.Message, StringComparison.Ordinal);
    }
}
