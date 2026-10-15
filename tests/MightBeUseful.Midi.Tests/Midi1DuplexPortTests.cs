namespace MightBeUseful.Midi.Tests;

public sealed class Midi1DuplexPortTests
{
    [Fact]
    public async Task ReceivesCompleteMessagesAndCopiesNativeSysExMemory()
    {
        var input = new Input();
        var output = new Output();
        await using var port = new Midi1DuplexPort(input, output, "In", "Out");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var reader = port.ReadAllAsync(timeout.Token).GetAsyncEnumerator();
        input.Short(0xf8);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(new byte[] { 0xf8 }, reader.Current.Data.ToArray());
        byte[] frame = [0xf0, 0x7d, 1, 0xf7];
        input.SysEx(frame);
        frame[2] = 9;
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(new byte[] { 0xf0, 0x7d, 1, 0xf7 }, reader.Current.Data.ToArray());
        await port.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, new byte[] { 0xc5, 42 }), timeout.Token);
        Assert.Equal(0xc5 | (42 << 8), Assert.Single(output.ShortMessages));
    }
    [Fact]
    public async Task InputFailureFaultsTheStreamAndDisposalRejectsFurtherSends()
    {
        var input = new Input(); var output = new Output();
        var port = new Midi1DuplexPort(input, output, "In", "Out");
        await using var reader = port.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        input.Error("Device unplugged");
        await Assert.ThrowsAsync<IOException>(async () => await reader.MoveNextAsync());
        await port.DisposeAsync();
        Assert.True(input.Disposed); Assert.True(output.Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await port.SendAsync(new MidiPacket(MidiPacketFormat.Midi1, new byte[] { 0xc0, 1 }), CancellationToken.None));
    }
    [Fact]
    public async Task OverflowIsVisibleInsteadOfDroppingMessagesSilently()
    {
        var input = new Input();
        await using var port = new Midi1DuplexPort(input, new Output(), "In", "Out");
        for (int i = 0; i < 1025; i++) { input.Short(0xf8); }
        await using var reader = port.ReadAllAsync(CancellationToken.None).GetAsyncEnumerator();
        int count = 0;
        var error = await Assert.ThrowsAsync<IOException>(async () => { while (await reader.MoveNextAsync()) { count++; } });
        Assert.Equal(1024, count); Assert.Contains("overflow", error.Message, StringComparison.Ordinal);
    }
    [Fact]
    public void FailedInputStartupReleasesBothNativeHandles()
    {
        var input = new Input { FailStart = true }; var output = new Output();
        Assert.Throws<IOException>(() => new Midi1DuplexPort(input, output, "In", "Out"));
        Assert.True(input.Disposed); Assert.True(output.Disposed);
    }
    private sealed class Input : IMidiInputPort
    {
        public bool Disposed { get; private set; }
        public bool FailStart { get; init; }
        public event EventHandler<MidiShortMessageEventArgs>? MessageReceived;
        public event EventHandler<MidiSysExMessageEventArgs>? SysexMessageReceived;
        public event EventHandler<string>? ErrorReceived;
        public void Start() { if (FailStart) throw new IOException("Input failed"); }
        public void Stop() { }
        public void Dispose() => Disposed = true;
        public void Short(int value) => MessageReceived?.Invoke(this, new(value));
        public void SysEx(byte[] value) => SysexMessageReceived?.Invoke(this, new(value));
        public void Error(string value) => ErrorReceived?.Invoke(this, value);
    }
    private sealed class Output : IMidiOutputPort
    {
        public bool Disposed { get; private set; }
        public List<int> ShortMessages { get; } = [];
        public void Send(int value) => ShortMessages.Add(value);
        public void SendBuffer(byte[] frame) { }
        public void Dispose() => Disposed = true;
    }
}
