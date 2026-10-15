using System.Threading.Channels;
using MightBeUseful.Midi;
using MightBeUseful.Midi.Windows;

Console.WriteLine(WindowsMidiBackend.Capabilities.Description);
if (!args.Contains("--self-test", StringComparer.Ordinal)) { return 0; }
if (!WindowsMidiBackend.Capabilities.CanCreateVirtualPorts) { Console.Error.WriteLine("Current virtual MIDI is unavailable."); return 2; }
try
{
    await Task.Run(CheckAsync).WaitAsync(TimeSpan.FromSeconds(40));
    Console.WriteLine("PASS: shared current/legacy PC, clock, SysEx, multi-client delivery, hidden relay and endpoint cleanup.");
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }

static async Task CheckAsync()
{
    if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26200)) { throw new PlatformNotSupportedException(); }
    string name = "MBU Check " + Guid.NewGuid().ToString("N")[..8];
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    string? id = null;
    await using (var relay = WindowsMidiServicesDuplexPort.Open(name))
    {
        for (int i = 0; i < 50; i++)
        {
            var devices = WindowsLegacyMidiBackend.GetInputDevices();
            id = devices.FirstOrDefault(p => p.Name == name).Id;
            if (id is not null) { break; }
            await Task.Delay(100, timeout.Token);
        }
        if (id is null) { throw new IOException("Public legacy input did not appear."); }
        if (WindowsLegacyMidiBackend.GetInputDevices().Any(p => p.Name == name + " (Relay)")) { throw new IOException("Private relay leaked into legacy inputs."); }
        using var a = WindowsLegacyMidiBackend.OpenInput(id);
        using var b = WindowsLegacyMidiBackend.OpenInput(id);
        string currentId = "services:" + PhysicalMidiEndpointCatalog.GetDevices().Single(p => p.Name == name).Id;
        using var current = WindowsMidiBackend.OpenInput(currentId);
        var inboxes = new[] { Channel.CreateUnbounded<byte[]>(), Channel.CreateUnbounded<byte[]>(), Channel.CreateUnbounded<byte[]>() };
        IMidiInputPort[] inputs = [a, b, current];
        for (int i = 0; i < inputs.Length; i++)
        {
            var queue = inboxes[i];
            inputs[i].MessageReceived += (_, e) => queue.Writer.TryWrite(Midi1ShortMessageCodec.Decode(e.RawMessage));
            inputs[i].SysexMessageReceived += (_, e) => queue.Writer.TryWrite(e.SysexBytes);
            inputs[i].ErrorReceived += (_, e) => queue.Writer.TryComplete(new IOException(e));
            inputs[i].Start();
        }
        byte[][] messages = [[0xc5,42],[0xf8],[0xf0,0x7d,1,2,3,4,5,6,7,0xf7],
            [0xf0,0x7d,..Enumerable.Range(0, 60000).Select(i => (byte)(i%128)),0xf7]];
        foreach (byte[] message in messages)
        {
            await relay.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, message), timeout.Token);
            foreach (var queue in inboxes)
            {
                var received = await queue.Reader.ReadAsync(timeout.Token);
                if (!message.AsSpan().SequenceEqual(received)) { throw new IOException($"MIDI bytes differ for {message.Length}-byte message."); }
            }
        }
        for (int channel = 1; channel <= 16; channel++)
        {
            var bytes = MidiMessageEncoding.GetShortMessageBytes(MidiMessageEncoding.ProgramChange(127,channel));
            await relay.SendAsync(new MidiPacket(MidiPacketFormat.Midi1,bytes),timeout.Token);
            foreach (var queue in inboxes)
            {
                byte[] received = await queue.Reader.ReadAsync(timeout.Token);
                if (!bytes.AsSpan().SequenceEqual(received)) { throw new IOException("MIDI channel mismatch."); }
            }
        }
        // Send back through the same legacy output used by all three applications.
        var output = WindowsLegacyMidiBackend.GetOutputDevices().Single(p => p.Name == name);
        using var legacyOutput = WindowsLegacyMidiBackend.OpenOutput(output.Index);
        await using var receivedUmp = relay.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        legacyOutput.Send(MidiMessageEncoding.ProgramChange(5,1));
        if (!await receivedUmp.MoveNextAsync()) { throw new IOException("No legacy-to-current response."); }
        using var decoder = new Midi1PacketDecoder();
        if (!decoder.Decode(receivedUmp.Current)!.AsSpan().SequenceEqual(new byte[]{0xc0,5})) { throw new IOException("Legacy-to-current bytes differ."); }
    }
    for (int i=0;i<50;i++)
    {
        if (!WindowsLegacyMidiBackend.GetInputDevices().Any(p=>p.Id==id)) { return; }
        await Task.Delay(100,timeout.Token);
    }
    throw new IOException("Virtual endpoint did not disappear on dispose.");
}
