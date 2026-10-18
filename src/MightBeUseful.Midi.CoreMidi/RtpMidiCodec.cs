namespace MightBeUseful.Midi.CoreMidi;

internal sealed class RtpMidiCodec : IDisposable
{
    private Rtp.RtpMidiCodec? codec;
    private uint? source;
    internal IReadOnlyList<byte[]> Decode(ReadOnlySpan<byte> packet, uint sender)
    {
        if (source.HasValue && source != sender) throw new InvalidDataException("RTP peer changed.");
        source = sender;
        codec ??= new(sender, Rtp.RtpMidiReceivePolicy.Stream);
        return codec.Decode(packet).Select(m => m.Bytes).ToArray();
    }
    internal static IReadOnlyList<byte[]> Encode(ReadOnlySpan<byte> message) => Rtp.RtpMidiCodec.Encode(message);
    internal static byte[] Packet(ReadOnlySpan<byte> command, ushort sequence, uint timestamp, uint sender) =>
        Rtp.RtpMidiCodec.Packet(command, sequence, timestamp, sender);
    public void Dispose() => codec?.Dispose();
}
