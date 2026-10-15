using System.Runtime.InteropServices;

namespace MightBeUseful.Midi.Windows;

/// <summary>The preview native API is optional and is never embedded in legacy-compatible app packages.</summary>
internal static class WindowsMidiApiLoader
{
    internal const string ApiVersion = "0.99.88-preview.10";
    private static readonly object Gate = new();
    private static nint _module;
    public static void LoadOptionalComponent()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)) { return; }
        lock (Gate)
        {
            if (_module != 0) { return; }
            string directory = Environment.GetEnvironmentVariable("MBU_MIDI_API_DIRECTORY")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MightBeUseful", "Midi", ApiVersion, RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
            string dll = Path.Combine(directory, "Windows.Devices.Midi2.dll");
            if (File.Exists(dll))
            {
                if (!File.Exists(Path.Combine(directory, "Windows.Devices.Midi2.pri"))) { throw new IOException("The optional Windows MIDI API resource file is missing. Run Initialize-SharedMidi.ps1 -EnableWindowsServices again."); }
                _module = NativeLibrary.Load(dll);
            }
        }
    }
}
