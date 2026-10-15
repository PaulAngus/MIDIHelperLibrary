namespace MightBeUseful.Midi.Tests;

public sealed class MidiBackendPolicyTests
{
    [Theory]
    [InlineData(WindowsMidiApiMode.Legacy, MidiBackendPreference.Automatic)]
    [InlineData(WindowsMidiApiMode.Legacy, MidiBackendPreference.Current)]
    [InlineData(WindowsMidiApiMode.Hybrid, MidiBackendPreference.Automatic)]
    [InlineData(WindowsMidiApiMode.Unknown, MidiBackendPreference.Automatic)]
    public void MachineCompatibilityModesDoNotStartTheCurrentService(WindowsMidiApiMode mode, MidiBackendPreference preference)
    {
        var result = MidiBackendPolicy.SelectWindows(new Version(10, 0, 26300), () => throw new Exception("Must not probe"), preference, mode);
        Assert.False(result.UseMidiServices);
        Assert.False(result.CanCreateVirtualPorts);
    }

    [Fact]
    public void HybridModeAllowsAnExplicitCurrentDriverSelection()
    {
        int probes = 0;
        var result = MidiBackendPolicy.SelectWindows(new Version(10, 0, 26300), () => { probes++; return (true, true); }, MidiBackendPreference.Current, WindowsMidiApiMode.Hybrid);
        Assert.True(result.UseMidiServices);
        Assert.Equal(1, probes);
    }
    [Theory]
    [InlineData(17763)]
    [InlineData(19045)]
    [InlineData(22631)]
    [InlineData(26100)]
    public void EarlierWindowsNeverActivatesCurrentApi(int build)
    {
        var result = MidiBackendPolicy.SelectWindows(new Version(10, 0, build), () => throw new Exception("Must not probe"));
        Assert.False(result.UseMidiServices);
        Assert.False(result.CanCreateVirtualPorts);
        Assert.Contains("requires Windows 11", result.Description, StringComparison.Ordinal);
    }
    [Fact]
    public void ForcedLegacyOnCurrentWindowsNeverActivatesCurrentApi()
    {
        var result = MidiBackendPolicy.SelectWindows(new Version(10, 0, 26300), () => throw new Exception("Must not probe"), MidiBackendPreference.Legacy);
        Assert.False(result.UseMidiServices);
        Assert.Contains("selected by", result.Description, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, false, false)]
    public void PhysicalAndVirtualCapabilitiesAreIndependent(bool service, bool loopback, bool expectedService, bool expectedVirtual)
    {
        var result = MidiBackendPolicy.SelectWindows(new Version(10, 0, 26300), () => (service, loopback));
        Assert.Equal(expectedService, result.UseMidiServices);
        Assert.Equal(expectedVirtual, result.CanCreateVirtualPorts);
    }
    [Fact]
    public void MissingOptionalApiHasAnActionableFallbackReason()
    {
        var result = MidiBackendPolicy.SelectWindows(new Version(10, 0, 26300), () => throw new DllNotFoundException("optional component absent"));
        Assert.False(result.UseMidiServices);
        Assert.Contains("optional component absent", result.Description, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData(null, MidiBackendPreference.Automatic)]
    [InlineData("AUTO", MidiBackendPreference.Automatic)]
    [InlineData("current", MidiBackendPreference.Current)]
    [InlineData("legacy", MidiBackendPreference.Legacy)]
    [InlineData("winmm", MidiBackendPreference.Legacy)]
    public void SharedPreferenceSyntax(string? text, MidiBackendPreference expected) => Assert.Equal(expected, MidiBackendPolicy.ParsePreference(text));
    [Fact]
    public void InvalidPreferenceIsNotSilentlyIgnored() => Assert.Throws<ArgumentException>(() => MidiBackendPolicy.ParsePreference("currnet"));
}
