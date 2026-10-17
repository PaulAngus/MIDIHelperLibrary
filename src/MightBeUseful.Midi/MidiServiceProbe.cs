namespace MightBeUseful.Midi;

// Keep a timed-out native call alive for the next refresh instead of starting
// another potentially blocked native call. Completed failures remain retryable.
internal sealed class MidiServiceProbe(Func<(bool Service, bool Loopback)> probe, int attempts = 3, TimeSpan? retryDelay = null)
{
    private readonly object _gate = new();
    private Task<(bool Service, bool Loopback)>? _pending;

    internal (bool Service, bool Loopback) GetResult(TimeSpan timeout)
    {
        Task<(bool Service, bool Loopback)> pending;
        lock (_gate) { pending = _pending ??= Task.Run(ProbeAsync); }
        try { return pending.WaitAsync(timeout).GetAwaiter().GetResult(); }
        finally
        {
            lock (_gate)
            {
                if (pending.IsCompleted && ReferenceEquals(_pending, pending)) { _pending = null; }
            }
        }
    }

    private async Task<(bool Service, bool Loopback)> ProbeAsync()
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var result = probe();
                if (result is (true, true) || attempt >= attempts) { return result; }
            }
            catch when (attempt < attempts) { }
            await Task.Delay(retryDelay ?? TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
        }
    }
}
