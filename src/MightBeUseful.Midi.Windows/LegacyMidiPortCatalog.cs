namespace MightBeUseful.Midi.Windows;

/// <summary>Names from the same legacy adapters used to open the ports (including untruncated WinRT input names).</summary>
public static class LegacyMidiPortCatalog
{
    public static IReadOnlyList<string> GetInputNames() => WindowsLegacyMidiBackend.GetInputDevices().Select(p => p.Name).ToArray();
    public static IReadOnlyList<string> GetOutputNames() => WindowsLegacyMidiBackend.GetOutputDevices().Select(p => p.Name).ToArray();
}
