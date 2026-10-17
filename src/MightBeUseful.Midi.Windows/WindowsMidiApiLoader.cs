using System.Runtime.InteropServices;

namespace MightBeUseful.Midi.Windows;

/// <summary>Loads a consumer-provided app-local API or the optional shared installation.</summary>
internal static class WindowsMidiApiLoader
{
    internal const string ApiVersion = "0.99.88-preview.10";
    private static readonly object Gate = new();
    private static nint _module;
    private static string? _applicationDirectory;
    internal static string ApiDirectory => Environment.GetEnvironmentVariable("MBU_MIDI_API_DIRECTORY")
        ?? _applicationDirectory
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MightBeUseful", "Midi", ApiVersion, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());

    internal static void ConfigureApplicationDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (Gate)
        {
            if (_module != 0) { throw new InvalidOperationException("Configure the app-local MIDI API before opening MIDI ports."); }
            _applicationDirectory = Path.GetFullPath(directory);
        }
    }
    public static void LoadOptionalComponent()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)) { return; }
        lock (Gate)
        {
            if (_module != 0) { return; }
            string directory = ApiDirectory;
            string dll = Path.Combine(directory, "Windows.Devices.Midi2.dll");
            if (!File.Exists(dll))
            {
                throw new FileNotFoundException($"The optional Windows MIDI API is missing at {dll}. Run Initialize-SharedMidi.ps1 -EnableWindowsServices for this Windows user.", dll);
            }
            if (!File.Exists(Path.Combine(directory, "Windows.Devices.Midi2.pri"))) { throw new IOException("The optional Windows MIDI API resource file is missing. Run Initialize-SharedMidi.ps1 -EnableWindowsServices again."); }
            _module = NativeLibrary.Load(dll);
        }
    }
}
