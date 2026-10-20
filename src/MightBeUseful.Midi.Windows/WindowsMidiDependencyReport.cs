using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MightBeUseful.Midi.Windows;

/// <summary>Dependency-only diagnostic: does not enumerate, create, open or send MIDI ports.</summary>
public static class WindowsMidiDependencyReport
{
    public static int Write(TextWriter writer)
    {
        bool eligible = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200);
        string? error = null;
        if (eligible)
        {
            try { WindowsMidiApiLoader.LoadOptionalComponent(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                DllNotFoundException or BadImageFormatException or System.Security.SecurityException)
            {
                error = ex.Message;
            }
        }
        using var process = Process.GetCurrentProcess();
        var modules = process.Modules.Cast<ProcessModule>()
            .Where(m => m.ModuleName.StartsWith("vcruntime", StringComparison.OrdinalIgnoreCase)
                || m.ModuleName.StartsWith("msvcp140", StringComparison.OrdinalIgnoreCase)
                || m.ModuleName.Equals("Windows.Devices.Midi2.dll", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { name = m.ModuleName, path = m.FileName }).ToArray();
        writer.WriteLine(JsonSerializer.Serialize(new
        {
            os = Environment.OSVersion.VersionString,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            framework = RuntimeInformation.FrameworkDescription,
            eligible,
            apiDirectory = eligible ? WindowsMidiApiLoader.ApiDirectory : null,
            runtimeDirectory = eligible ? BundledWindowsMidiRuntime.DirectoryPath : null,
            modules,
            error
        }));
        return error is null ? 0 : 1;
    }
}
