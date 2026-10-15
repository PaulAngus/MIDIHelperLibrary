using System.Threading.Channels;
using MightBeUseful.Midi;

namespace MightBeUseful.Midi.Windows;

internal static class WindowsUmpReceiveQueue
{
    // Native callbacks enqueue individual UMPs, not complete SysEx frames. A 1 MiB
    // SysEx7 needs about 175,000 packets. Bound raw UMP payload at 4 MiB (each
    // native packet is at most 16 bytes), while allowing a full supported frame
    // to arrive before its managed decoder gets a thread-pool timeslice.
    internal const int Capacity = 4 * MidiFrame.MaximumLength / 16;

    internal static Channel<MidiPacket> Create() => Channel.CreateBounded<MidiPacket>(
        new BoundedChannelOptions(Capacity) { SingleReader = true, SingleWriter = false });
}
