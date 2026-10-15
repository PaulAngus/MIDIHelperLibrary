using NAudio;
using NAudio.Midi;
using MightBeUseful.Midi;

namespace MightBeUseful.Midi.Windows;

public static class WindowsLegacyMidiBackend
{
    public static IReadOnlyList<(string Id, string Name)> GetInputDevices() =>
        Task.Run(async () => await WinRTMidiIn.GetDevicesAsync()).GetAwaiter().GetResult()
            .Select(device => (device.Id, device.Name)).ToArray();

    public static IMidiInputPort OpenInput(string id) =>
        new WindowsInput(Task.Run(async () => await WinRTMidiIn.CreateAsync(id)).GetAwaiter().GetResult());

    public static IReadOnlyList<(int Index, string Name)> GetOutputDevices() =>
        GetOutputPorts(MidiOut.NumberOfDevices, index => MidiOut.DeviceInfo(index).ProductName);

    public static IMidiOutputPort OpenOutput(int index) => new SerializedMidiOutputPort(new WindowsOutput(new MidiOut(index)));

    public static IReadOnlyList<(int Index, string Name)> GetOutputPorts(int deviceCount, Func<int, string> getName)
    {
        var ports = new List<(int Index, string Name)>();
        for (int i = 0; i < deviceCount; i++)
        {
            try { ports.Add((i, getName(i))); }
            catch (MmException) { } // A stale driver entry must not hide healthy ports.
        }
        return ports;
    }

    private sealed class WindowsInput : IMidiInputPort
    {
        private readonly IMidiInput _input;
        private bool _disposed;
        public WindowsInput(IMidiInput input)
        {
            _input = input;
            _input.MessageReceived += Receive;
            _input.SysexMessageReceived += ReceiveSysEx;
        }
        public event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
        public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived;
        public event EventHandler<string>? ErrorReceived;
        private void Receive(object? sender, MidiInMessageEventArgs e)
        {
            try { if (!_disposed) { MessageReceived?.Invoke(this, new(e.RawMessage)); } }
            catch (Exception error) { ReportError(error); }
        }
        private void ReceiveSysEx(object? sender, MidiInSysexMessageEventArgs e)
        {
            try { if (!_disposed) { SysexMessageReceived?.Invoke(this, new(e.SysexBytes.ToArray())); } }
            catch (Exception error) { ReportError(error); }
        }
        private void ReportError(Exception error)
        {
            try { ErrorReceived?.Invoke(this, error.Message); }
            catch { /* A diagnostic subscriber cannot unwind through WinRT. */ }
        }
        public void Start() => _input.Start();
        public void Stop() => _input.Stop();
        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            _input.MessageReceived -= Receive;
            _input.SysexMessageReceived -= ReceiveSysEx;
            _input.Dispose();
        }
    }

    private sealed class WindowsOutput(IMidiOutput output) : IMidiOutputPort
    {
        public void Send(int message) => output.Send(message);
        public void SendBuffer(byte[] frame) => output.SendBuffer(frame);
        public void Dispose() => output.Dispose();
    }
}
