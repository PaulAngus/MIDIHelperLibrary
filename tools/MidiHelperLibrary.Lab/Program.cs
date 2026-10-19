using ApplicationTestLab;

string suffix = OperatingSystem.IsWindows() ? ".exe" : "";
var stages = new List<LabStage>();
stages.Add(new("functional-mightbeuseful-midi-tests", "Functional", "bin/tests/MidiHelperLibrary.LabTests" + suffix, ["MightBeUseful.Midi.Tests", "{result}"]));
stages.Add(new("native-midi", "Native", UnavailableReason: "Native Windows/CoreMIDI backend loopback qualification is not implemented in this portable adapter."));
stages.Add(new("desktop", "Desktop", UnavailableReason: "This library/CLI has no desktop UI suite.", IncludeInAll: false));
try
{
    return await PortableLab.RunAsync(args, new LabApplication("midi-helper-library", "MIDIHelperLibrary", stages.ToArray(), ["Functional uses the existing offline .NET tests. Physical hardware is not exercised.", "Native Windows/CoreMIDI backend loopback qualification is not implemented in this portable adapter."], new Dictionary<string, string?> { ["PRESET_MAESTRO_HARDWARE"] = null, ["PRESET_MAESTRO_COMPATIBILITY"] = null, ["PRESET_MAESTRO_NAVIGATION"] = null, ["PRESET_MAESTRO_SCREENSHOT_DIR"] = "{output}/screenshots", ["MIDI_FORWARDER_UI_SNAPSHOTS"] = "{output}/screenshots" }));
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
