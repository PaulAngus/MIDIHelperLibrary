using MightBeUseful.Midi.Rtp;

namespace MightBeUseful.Midi.CoreMidi;

/// <summary>Native endpoint validation around the platform-neutral loopback peer.</summary>
public sealed class CoreMidiNetworkPort : IAsyncDisposable
{
    public const int PeerPort = RtpMidiLoopbackPort.PeerPort;
    private readonly RtpMidiLoopbackPort peer;
    public CoreMidiNetworkPort(string endpointName, int driverPort = 5004, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        if (driverPort is < 1 or > 65534 || driverPort is PeerPort - 1 or PeerPort or PeerPort + 1)
            throw new ArgumentOutOfRangeException(nameof(driverPort));
        if (!CoreMidiPortCatalog.GetNames().Contains(endpointName, StringComparer.Ordinal))
            throw new IOException($"Enable '{endpointName}' in Audio MIDI Setup's network settings, then connect again.");
        peer = new(endpointName, driverPort, log, sessionName: "MIDI Forwarder");
    }
    public Task<IMidiDuplexPort> WaitForConnectionAsync(CancellationToken cancellationToken) => peer.WaitForConnectionAsync(cancellationToken);
    public ValueTask DisposeAsync() => peer.DisposeAsync();
}
