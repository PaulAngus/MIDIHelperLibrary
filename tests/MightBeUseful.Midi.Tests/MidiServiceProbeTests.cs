namespace MightBeUseful.Midi.Tests;

public sealed class MidiServiceProbeTests
{
    [Fact]
    public void TransientExceptionsAndUnavailableTransportRecoverDuringStartup()
    {
        int calls = 0;
        var probe = new MidiServiceProbe(() => ++calls switch
        {
            1 => throw new IOException("Service is starting"),
            2 => (true, false),
            _ => (true, true)
        }, retryDelay: TimeSpan.Zero);
        Assert.Equal((true, true), probe.GetResult(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void CompletedFailureCanRecoverOnRefresh()
    {
        bool available = false;
        var probe = new MidiServiceProbe(() => (available, available), retryDelay: TimeSpan.Zero);
        Assert.Equal((false, false), probe.GetResult(TimeSpan.FromSeconds(5)));
        available = true;
        Assert.Equal((true, true), probe.GetResult(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void PersistentExceptionIsBoundedAndCanRecoverOnRefresh()
    {
        int calls = 0;
        bool available = false;
        var probe = new MidiServiceProbe(() =>
        {
            calls++;
            return available ? (true, true) : throw new IOException("Service unavailable");
        }, retryDelay: TimeSpan.Zero);
        Assert.Throws<IOException>(() => probe.GetResult(TimeSpan.FromSeconds(5)));
        Assert.Equal(3, calls);
        available = true;
        Assert.Equal((true, true), probe.GetResult(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task TimedOutProbeIsReusedAndLateSuccessIsObserved()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var probe = new MidiServiceProbe(() =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            release.Wait();
            return (true, true);
        });
        try
        {
            Assert.Throws<TimeoutException>(() => probe.GetResult(TimeSpan.Zero));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Throws<TimeoutException>(() => probe.GetResult(TimeSpan.Zero));
            release.Set();
            Assert.Equal((true, true), probe.GetResult(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, calls);
        }
        finally { release.Set(); }
    }
}
