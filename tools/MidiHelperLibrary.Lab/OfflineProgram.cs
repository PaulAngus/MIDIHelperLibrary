using ApplicationTestLab;
if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This profile requires Windows.");
LabStage[] stages = [
    new("offline-functional-codecs-and-policy", "Functional", "bin/tests/MidiHelperLibrary.LabTests.exe", ["MightBeUseful.Midi.Tests", "{result}"], ReadTestResult: true),
    new("native-midi", "Native", UnavailableReason: "Native MIDI is excluded by the offline profile.", IncludeInAll: false),
    new("desktop", "Desktop", UnavailableReason: "Interactive desktop automation is excluded by the offline profile; any UI checks are headless.", IncludeInAll: false)
];
return await PortableLab.RunAsync(args, new LabApplication("midi-helper-library", "MIDIHelperLibrary", stages, ["Offline Windows profile: All covers only the audited Functional stages. No hardware, native MIDI, MIDI routing or desktop automation qualification is claimed.", "No native MIDI enumeration/opening, virtual MIDI ports, TCP MIDI or UDP MIDI sessions are exercised. The connected FM9 is not opened.", "In-memory codec, logic and injected-interface fixtures do not create operating-system or network MIDI endpoints.", "RtpPolicyTests: includes real UDP MIDI sessions", "RtpMidiDiagnosticPeerTests: creates UDP MIDI peers", "Native backend and hardware qualification"],
    new Dictionary<string, string?> {
        ["PRESET_SENDER_DATA_DIRECTORY"] = "{output}/profile",
        ["PRESET_SENDER_SCREENSHOT_DIR"] = "{output}/screenshots/main",
        ["PRESET_SENDER_CONFIG_SCREENSHOT_DIR"] = "{output}/screenshots/configuration",
        ["PRESET_SENDER_NATIVE_MIDI_TEST"] = null, ["PRESET_SENDER_VIRTUAL_DEVICE_NAME"] = null,
        ["PRESET_MAESTRO_HARDWARE"] = null, ["PRESET_MAESTRO_COMPATIBILITY"] = null,
        ["PRESET_MAESTRO_ALLOW_NAVIGATION"] = null, ["PRESET_MAESTRO_NAVIGATION"] = null,
        ["TMPDIR"] = "{output}/profile", ["TMP"] = "{output}/profile", ["TEMP"] = "{output}/profile"
    }));
