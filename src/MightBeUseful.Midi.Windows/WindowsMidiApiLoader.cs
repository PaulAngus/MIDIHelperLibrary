using System.Runtime.InteropServices;

namespace MightBeUseful.Midi.Windows;

/// <summary>Loads the bundled API, or an explicitly configured diagnostic override.</summary>
internal static class WindowsMidiApiLoader
{
    internal const string ApiVersion = "0.99.88-preview.10";
    private static readonly object Gate = new();
    private static nint _module;
    private static string? _applicationDirectory;
    internal static string ApiDirectory => Environment.GetEnvironmentVariable("MBU_MIDI_API_DIRECTORY")
        ?? _applicationDirectory
        ?? (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)
            ? BundledWindowsMidiRuntime.DirectoryPath : string.Empty);

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
            BundledWindowsMidiRuntime.Prepare();
            string directory = ApiDirectory;
            string dll = Path.Combine(directory, "Windows.Devices.Midi2.dll");
            if (!File.Exists(dll))
            {
                throw new FileNotFoundException($"The Windows MIDI API is missing at {dll}. Restore the complete application or remove an invalid MBU_MIDI_API_DIRECTORY override.", dll);
            }
            if (!File.Exists(Path.Combine(directory, "Windows.Devices.Midi2.pri"))) { throw new IOException("The Windows MIDI API resource file is missing. Restore the complete application or remove an invalid MBU_MIDI_API_DIRECTORY override."); }
            _module = NativeLibrary.Load(dll, typeof(WindowsMidiApiLoader).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
        }
    }
}
