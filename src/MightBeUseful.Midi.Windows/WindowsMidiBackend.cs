using System.Runtime.CompilerServices;
using MightBeUseful.Midi;

namespace MightBeUseful.Midi.Windows;

/// <summary>The single current/legacy selection and endpoint-opening policy used by all applications.</summary>
public static class WindowsMidiBackend
{
    private const string CurrentPrefix = "services:";
    private static Lazy<WindowsMidiCapabilities> _current = new(Detect);
    private static readonly MidiServiceProbe ServiceProbe = new(ProbeModernCore);
    private static readonly object OutputCatalogGate = new();
    private static readonly Dictionary<int, string> CurrentOutputIds = [];
    private static readonly Dictionary<string, int> CurrentOutputTokens = new(StringComparer.Ordinal);
    private static int _nextOutputToken = -1;
    public static WindowsMidiCapabilities Capabilities => _current.Value;
    /// <summary>The selected native API directory, for diagnostics.</summary>
    public static string ApiDirectory => WindowsMidiApiLoader.ApiDirectory;
    /// <summary>Opt into a consumer's app-local API before the first capability probe.</summary>
    public static void ConfigureAppLocalApiDirectory(string directory)
    {
        WindowsMidiApiLoader.ConfigureApplicationDirectory(directory);
        Interlocked.Exchange(ref _current, new Lazy<WindowsMidiCapabilities>(Detect));
    }
    public static WindowsMidiCapabilities RefreshCapabilities()
    {
        Interlocked.Exchange(ref _current, new Lazy<WindowsMidiCapabilities>(Detect));
        return Capabilities;
    }
    public static IReadOnlyList<(string Id, string Name)> GetInputDevices()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) && Capabilities.UseMidiServices)
        {
            return PhysicalMidiEndpointCatalog.GetDevices().Select(p => (CurrentPrefix + p.Id, p.Name)).ToArray();
        }
        RequireLegacyAllowed();
        return WindowsLegacyMidiBackend.GetInputDevices();
    }
    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) && Capabilities.UseMidiServices)
        {
            var devices = PhysicalMidiEndpointCatalog.GetDevices();
            lock (OutputCatalogGate)
            {
                return devices.Select(p =>
                {
                    if (!CurrentOutputTokens.TryGetValue(p.Id, out int token))
                    {
                        token = _nextOutputToken--;
                        CurrentOutputTokens.Add(p.Id, token);
                        CurrentOutputIds.Add(token, p.Id);
                    }
                    return (token, p.Name);
                }).ToArray();
            }
        }
        RequireLegacyAllowed();
        return WindowsLegacyMidiBackend.GetOutputDevices();
    }
    public static IMidiInputPort OpenInput(string id)
    {
        if (id.StartsWith(CurrentPrefix, StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) || !Capabilities.UseMidiServices)
            {
                throw new IOException("The selected Windows MIDI Services endpoint is unavailable. Refresh and select its legacy input explicitly.");
            }
            return new ServicesMidiInputPort(PhysicalMidiEndpointDuplexPort.OpenById(id[CurrentPrefix.Length..]));
        }
        RequireLegacyAllowed();
        return WindowsLegacyMidiBackend.OpenInput(id);
    }
    public static IMidiOutputPort OpenOutput(int index)
    {
        if (index < 0)
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) || !Capabilities.UseMidiServices)
            {
                throw new IOException("The selected Windows MIDI Services output is unavailable. Refresh and select its legacy output explicitly.");
            }
            string id;
            lock (OutputCatalogGate)
            {
                if (!CurrentOutputIds.TryGetValue(index, out string? found)) { throw new IOException("The selected MIDI output is stale. Refresh MIDI ports."); }
                id = found;
            }
            return new SerializedMidiOutputPort(new ServicesMidiOutputPort(PhysicalMidiEndpointDuplexPort.OpenById(id, receive: false)));
        }
        RequireLegacyAllowed();
        return WindowsLegacyMidiBackend.OpenOutput(index);
    }
    public static IReadOnlyList<string> GetEndpointNames() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) && Capabilities.UseMidiServices
            ? PhysicalMidiEndpointCatalog.GetNames()
            : WindowsLegacyMidiBackend.GetInputDevices().Select(p => p.Name).ToArray();
    public static IMidiDuplexPort OpenPhysical(string endpoint, string input, string output)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) && Capabilities.UseMidiServices)
        {
            return PhysicalMidiEndpointDuplexPort.Open(endpoint);
        }
        RequireLegacyAllowed();
        return OpenLegacyPair(input, output);
    }
    public static IMidiDuplexPort OpenClient(string interfaceName, string input, string output)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) && Capabilities.CanCreateVirtualPorts)
        {
            return WindowsMidiServicesDuplexPort.Open(interfaceName);
        }
        RequireLegacyAllowed();
        ValidateClientPorts(input, output);
        return OpenLegacyPair(input, output);
    }
    public static IMidiOutputPort OpenVirtualOutput(string name)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200) || !Capabilities.CanCreateVirtualPorts)
        {
            throw new PlatformNotSupportedException($"Built-in virtual MIDI is unavailable. {Capabilities.Description} Select an existing hardware or loopback output.");
        }
        return new SerializedMidiOutputPort(new ServicesMidiOutputPort(WindowsMidiServicesDuplexPort.Open(name, receive: false)));
    }
    private static IMidiDuplexPort OpenLegacyPair(string inputName, string outputName)
    {
        var inputs = WindowsLegacyMidiBackend.GetInputDevices().Where(p => p.Name.Equals(inputName, StringComparison.OrdinalIgnoreCase)).ToArray();
        var outputs = WindowsLegacyMidiBackend.GetOutputDevices().Where(p => p.Name.Equals(outputName, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (inputs.Length != 1 || outputs.Length != 1) { throw new IOException("Select unique, available MIDI input and output names."); }
        var output = WindowsLegacyMidiBackend.OpenOutput(outputs[0].Index);
        IMidiInputPort input;
        try { input = WindowsLegacyMidiBackend.OpenInput(inputs[0].Id); }
        catch { output.Dispose(); throw; }
        return new Midi1CompatiblePort(new Midi1DuplexPort(input, output, inputName, outputName));
    }
    public static void ValidateClientPorts(string input, string output)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException("Select the client MIDI input and output. On legacy Windows, create two virtual cables (for example in loopMIDI) first.");
        }
        if (input.Equals(output, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Use two different virtual cables: one from the MIDI app and one to the MIDI app. Using one loopback cable for both directions creates feedback.");
        }
    }
    public static void Print(TextWriter writer)
    {
        writer.WriteLine(Capabilities.Description);
        foreach (var p in GetInputDevices()) { writer.WriteLine($"  Input: {p.Name}"); }
        foreach (var p in GetOutputDevices()) { writer.WriteLine($"  Output: {p.Name}"); }
    }
    private static MidiBackendPreference Preference => MidiBackendPolicy.ParsePreference(
        Environment.GetEnvironmentVariable(MidiBackendPolicy.EnvironmentVariable)
        ?? (string.Equals(Environment.GetEnvironmentVariable("MIDI_FORWARDER_MIDI_API"), "winmm", StringComparison.OrdinalIgnoreCase) ? "legacy" : null));
    private static void RequireLegacyAllowed()
    {
        if (Preference == MidiBackendPreference.Current)
        {
            throw new PlatformNotSupportedException($"The explicitly selected current MIDI backend cannot perform this operation. {Capabilities.Description}");
        }
    }
    private static WindowsMidiCapabilities Detect() => MidiBackendPolicy.SelectWindows(Environment.OSVersion.Version, ProbeModern, Preference, ReadMachineMode());

    private static WindowsMidiApiMode ReadMachineMode()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)) { return WindowsMidiApiMode.Full; }
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Drivers32");
            return key?.GetValue("UseLegacyMidi") switch
            {
                null or 0 => WindowsMidiApiMode.Full,
                1 => WindowsMidiApiMode.Legacy,
                2 => WindowsMidiApiMode.Hybrid,
                _ => WindowsMidiApiMode.Unknown,
            };
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return WindowsMidiApiMode.Unknown;
        }
    }
    internal static WindowsMidiCapabilities Select(Version osVersion, Func<(bool Service, bool Loopback)> probe) => MidiBackendPolicy.SelectWindows(osVersion, probe);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool Service, bool Loopback) ProbeModern()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)) { return (false, false); }
        return ServiceProbe.GetResult(TimeSpan.FromSeconds(10));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (bool Service, bool Loopback) ProbeModernCore()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)) { return (false, false); }
        WindowsMidiApiLoader.LoadOptionalComponent();
        bool service = global::Windows.Devices.Midi2.MidiApi.EnsureServiceAvailable();
        return (service, service && global::Windows.Devices.Midi2.Transports.Loopback.MidiLoopbackManager.IsTransportAvailable);
    }
}
