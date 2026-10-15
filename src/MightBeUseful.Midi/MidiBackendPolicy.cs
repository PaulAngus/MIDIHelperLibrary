namespace MightBeUseful.Midi;

public enum MidiBackendPreference { Automatic, Current, Legacy }

public enum WindowsMidiApiMode { Full, Legacy, Hybrid, Unknown }

public sealed record WindowsMidiCapabilities(bool UseMidiServices, bool CanCreateVirtualPorts, string Description);

/// <summary>Pure capability policy, shared by every Windows consumer and testable without native APIs.</summary>
public static class MidiBackendPolicy
{
    public const string EnvironmentVariable = "MBU_MIDI_BACKEND";

    public static MidiBackendPreference ParsePreference(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" or "automatic" => MidiBackendPreference.Automatic,
        "current" or "services" => MidiBackendPreference.Current,
        "legacy" or "winmm" => MidiBackendPreference.Legacy,
        _ => throw new ArgumentException($"Invalid {EnvironmentVariable} value '{value}'. Use auto, current or legacy.", nameof(value)),
    };

    public static WindowsMidiCapabilities SelectWindows(Version osVersion, Func<(bool Service, bool Loopback)> probe,
        MidiBackendPreference preference = MidiBackendPreference.Automatic, WindowsMidiApiMode machineMode = WindowsMidiApiMode.Full)
    {
        ArgumentNullException.ThrowIfNull(osVersion);
        ArgumentNullException.ThrowIfNull(probe);
        if (preference == MidiBackendPreference.Legacy)
        {
            return new(false, false, "Legacy MIDI 1.0 (selected by MBU_MIDI_BACKEND; WinRT input / WinMM output).");
        }
        if (osVersion < new Version(10, 0, 26200))
        {
            return new(false, false, "Legacy MIDI 1.0 (Windows MIDI Services API requires Windows 11 25H2 or later).");
        }
        if (machineMode is WindowsMidiApiMode.Legacy or WindowsMidiApiMode.Unknown)
        {
            return new(false, false, machineMode == WindowsMidiApiMode.Legacy
                ? "Legacy MIDI 1.0 (Windows is configured for Legacy API mode; the current service was not probed)."
                : "Legacy MIDI 1.0 (Windows API mode could not be determined; the current service was not probed).");
        }
        if (machineMode == WindowsMidiApiMode.Hybrid && preference == MidiBackendPreference.Automatic)
        {
            return new(false, false, "Legacy MIDI 1.0 (Windows is in Hybrid API mode; choose MBU_MIDI_BACKEND=current explicitly for devices on the current driver).");
        }
        try
        {
            (bool service, bool loopback) = probe();
            return service
                ? new(true, loopback, loopback
                    ? "Windows MIDI Services (virtual loopback available)."
                    : "Windows MIDI Services for hardware; legacy MIDI 1.0 for client ports (loopback unavailable).")
                : new(false, false, "Legacy MIDI 1.0 (Windows MIDI Services is unavailable).");
        }
        catch (Exception error)
        {
            return new(false, false, $"Legacy MIDI 1.0 (Windows MIDI Services could not initialize: {error.Message}).");
        }
    }
}
