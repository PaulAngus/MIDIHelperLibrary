using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MightBeUseful.Midi.Windows;

/// <summary>App-private native closure; no installer, registry writes or global DLL search.</summary>
internal static class BundledWindowsMidiRuntime
{
    private static readonly string[] Files = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? ["vcruntime140.dll", "msvcp140.dll", "Windows.Devices.Midi2.dll", "Windows.Devices.Midi2.pri"]
        : ["vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll", "Windows.Devices.Midi2.dll", "Windows.Devices.Midi2.pri"];
    private static readonly Lazy<(string Directory, byte[][] Contents)> Payload = new(ReadPayload);
    private static readonly Lazy<string> Prepared = new(ExtractAndLoadRuntime);

    internal static string DirectoryPath => Payload.Value.Directory;

    internal static string Prepare() => Prepared.Value;

    private static (string, byte[][]) ReadPayload()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException("The bundled Windows MIDI API requires x64 or arm64.")
        };
        byte[][] contents = Files.Select(name =>
        {
            using Stream stream = typeof(BundledWindowsMidiRuntime).Assembly.GetManifestResourceStream(
                $"MightBeUseful.Midi.Native.{architecture}.{name}")
                ?? throw new IOException($"The application is missing its bundled MIDI dependency: {architecture}/{name}.");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (byte[] bytes in contents) { hash.AppendData(bytes); }
        string root = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MightBeUseful", "Midi");
        return (Path.Combine(root, "private-runtime", architecture, Convert.ToHexString(hash.GetHashAndReset())), contents);
    }

    private static string ExtractAndLoadRuntime()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200))
        {
            throw new PlatformNotSupportedException("The current MIDI API is only loaded on Windows 11 25H2 or later.");
        }
        var (directory, contents) = Payload.Value;
        Directory.CreateDirectory(directory);
        for (int i = 0; i < Files.Length; i++)
        {
            string destination = Path.Combine(directory, Files[i]);
            byte[] expected = SHA256.HashData(contents[i]);
            if (Matches(destination, expected)) { continue; }
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, contents[i]);
                try { File.Move(temporary, destination, overwrite: true); }
                catch (IOException) when (Matches(destination, expected)) { }
                catch (UnauthorizedAccessException) when (Matches(destination, expected)) { }
            }
            finally { if (File.Exists(temporary)) { File.Delete(temporary); } }
        }
        // Retain these modules for process lifetime. Load the dependency chain in order,
        // resolving only beside the verified file or from the OS system directory.
        foreach (string name in Files.Take(Files.Length - 2))
        {
            NativeLibrary.Load(Path.Combine(directory, name), typeof(BundledWindowsMidiRuntime).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
        }
        return directory;
    }

    private static bool Matches(string path, byte[] expected)
    {
        if (!File.Exists(path)) { return false; }
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream).AsSpan().SequenceEqual(expected);
    }
}
