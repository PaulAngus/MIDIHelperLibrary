using MightBeUseful.Midi;
using MightBeUseful.Midi.Windows;
using TestLab.Fm9;
bool configured = false;
return await Fm9ReadOnly.Run(args, selection =>
{
    if (!configured)
    {
        Environment.SetEnvironmentVariable("MBU_MIDI_API_DIRECTORY", null);
        Environment.SetEnvironmentVariable(MidiBackendPolicy.EnvironmentVariable, null);
        Environment.SetEnvironmentVariable("MIDI_FORWARDER_MIDI_API", null);
        WindowsMidiBackend.ConfigureAppLocalApiDirectory(Path.Combine(AppContext.BaseDirectory, "midi-api"));
            configured = true;
    }
    var inputs = WindowsMidiBackend.GetInputDevices().Where(p => p.Name == selection.EndpointName).ToArray();
    var outputs = WindowsMidiBackend.GetOutputDevices().Where(p => p.Name == selection.EndpointName).ToArray();
    if (inputs.Length != 1 || outputs.Length != 1 || !inputs[0].Id.StartsWith("services:", StringComparison.Ordinal))
    {
        throw new IOException("Select the unique existing FM9 hardware endpoint using Windows MIDI Services. No fallback.");
    }
    var output = WindowsMidiBackend.OpenOutput(outputs[0].Index);
    IMidiInputPort input;
    try { input = WindowsMidiBackend.OpenInput(inputs[0].Id); }
    catch { output.Dispose(); throw; }
    return new Fm9SharedTransport(new Midi1DuplexPort(input, output, selection.EndpointName, selection.EndpointName));
}, "midi-helper-library: production Windows MIDI Services transport boundary only");
