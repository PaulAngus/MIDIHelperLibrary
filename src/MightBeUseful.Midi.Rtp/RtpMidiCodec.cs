using System.Buffers.Binary;
namespace MightBeUseful.Midi.Rtp;

/// <summary>Strict: fail on discontinuity/cancellation, 4 KiB frames and exact journal bounds.
/// Stream: ignore duplicate/late packets, discard cancelled frames, allow 1 MiB frames.
/// Both fail on forward loss; neither repairs or replays MIDI.</summary>
public enum RtpMidiReceivePolicy { Strict, Stream }

public sealed record RtpMidiMessage(uint Timestamp, byte[] Bytes);

/// <summary>RFC 6295 command sections, restricted to Apple's RTP header profile. No loss reconstruction.</summary>
public sealed class RtpMidiCodec(uint source, RtpMidiReceivePolicy policy = RtpMidiReceivePolicy.Strict) : IDisposable
{
    private bool Strict => policy == RtpMidiReceivePolicy.Strict;
    private int MaximumLength => Strict ? 4096 : MidiFrame.MaximumLength;
    private ushort? previous;
    private readonly List<byte> partial = [];
    public bool Incomplete => partial.Count != 0;
    public int BufferedSysExBytes => partial.Count;
    public IReadOnlyList<RtpMidiMessage> Decode(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < 13 || packet.Length > 65507 || packet[0] != 0x80 || (packet[1] & 127) != 0x61 ||
            BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(8)) != source) throw new InvalidDataException("Invalid RTP-MIDI header/source.");
        ushort sequence = BinaryPrimitives.ReadUInt16BigEndian(packet.Slice(2));
        if (!Strict && previous.HasValue)
        {
            ushort distance = unchecked((ushort)(sequence - previous.Value));
            if (distance == 0 || distance > 0x8000) return [];
        }
        if (previous.HasValue && sequence != unchecked((ushort)(previous.Value + 1)))
            throw new IOException("RTP sequence discontinuity: loss, duplicate or reordering; no replay or journal repair of device edits.");
        previous = sequence;
        uint timestamp = BinaryPrimitives.ReadUInt32BigEndian(packet.Slice(4));
        int flags = packet[12], position = 13, length = flags & 15;
        if ((flags & 128) != 0)
        {
            if (position >= packet.Length) throw new InvalidDataException("Truncated RTP-MIDI length.");
            length = length << 8 | packet[position++];
        }
        int end = position + length;
        if (end > packet.Length || (Strict && ((flags & 64) == 0 ? end != packet.Length : packet.Length - end < 3)))
            throw new InvalidDataException("Invalid RTP-MIDI command/journal boundary.");
        var messages = new List<RtpMidiMessage>(); int running = 0; bool delta = (flags & 32) != 0;
        while (position < end)
        {
            if (delta)
            {
                uint ticks = 0; int count = 0; byte part;
                do
                {
                    if (position >= end || count++ == 4) throw new InvalidDataException("Invalid RTP delta time.");
                    part = packet[position++]; ticks = ticks << 7 | (uint)(part & 127);
                } while ((part & 128) != 0);
                timestamp = unchecked(timestamp + ticks);
                if (position == end) break;
            }
            delta = true;
            int status = packet[position];
            if (status >= 128) position++;
            else if (running != 0) status = running;
            else throw new InvalidDataException("Missing RTP MIDI running status.");
            if (status < 240) running = status;
            else if (status < 248) running = 0;
            if (status is 240 or 247)
            {
                if (Strict && status == 240 && partial.Count != 0 || status == 247 && partial.Count == 0)
                    throw new InvalidDataException("Unexpected SysEx segment boundary.");
                if (status == 240) { partial.Clear(); partial.Add(240); }
                int start = position;
                while (position < end && packet[position] < 128)
                {
                    if (partial.Count >= MaximumLength - 1) throw new InvalidDataException("Network SysEx exceeds the configured frame limit.");
                    partial.Add(packet[position++]);
                }
                if (position == end) throw new InvalidDataException("Unterminated SysEx segment.");
                byte tail = packet[position++];
                if (!Strict && tail == 244) { partial.Clear(); continue; }
                if (tail == 244 && status == 247 && position - start == 1)
                    throw new IOException("Peer cancelled an incomplete SysEx message.");
                if (tail == 240)
                {
                    if (Strict && position - start == 1) throw new InvalidDataException("Empty nonfinal SysEx segment.");
                }
                else if (tail is 247 or 245)
                {
                    partial.Add(247); messages.Add(new(timestamp, partial.ToArray())); partial.Clear();
                }
                else throw new InvalidDataException("Invalid SysEx segment terminator.");
                continue;
            }
            if (Strict && partial.Count != 0 && status < 248) throw new InvalidDataException("Non-realtime command interrupts segmented SysEx.");
            int data = status < 240 ? (status & 240) is 192 or 208 ? 1 : 2 : status switch
            { 241 or 243 => 1, 242 => 2, 246 or 248 or 250 or 251 or 252 or 254 or 255 => 0, _ => throw new InvalidDataException("Unsupported MIDI status in RTP packet.") };
            if (position + data > end || packet.Slice(position, data).ContainsAnyInRange((byte)128, (byte)255))
                throw new InvalidDataException("Truncated or invalid RTP MIDI command.");
            messages.Add(new(timestamp, [(byte)status, .. packet.Slice(position, data)])); position += data;
        }
        return messages;
    }
    public static byte[][] Encode(byte[] message, uint source, ref ushort sequence, uint timestamp)
    {
        if (message.Length == 0 || message.Length > 4096) throw new ArgumentException("MIDI message length out of bound.");
        List<byte[]> commands = [];
        if (message[0] == 240)
        {
            if (message.Length < 2 || message[^1] != 247 || message.AsSpan(1, message.Length - 2).ContainsAnyInRange((byte)128, (byte)255))
                throw new ArgumentException("Expected one complete SysEx frame.");
            for (int offset = 1; offset < message.Length - 1; offset += 1000)
            {
                int count = Math.Min(1000, message.Length - 1 - offset);
                commands.Add([(byte)(offset == 1 ? 240 : 247), .. message.AsSpan(offset, count), (byte)(offset + count == message.Length - 1 ? 247 : 240)]);
            }
            if (commands.Count == 0) commands.Add([240, 247]);
        }
        else
        {
            int expected = (message[0] & 240) switch { 176 => 3, 192 => 2, _ => 0 };
            if (expected != message.Length || message.Skip(1).Any(b => b >= 128)) throw new ArgumentException("Only typed CC/PC short sends are supported.");
            commands.Add(message.ToArray());
        }
        List<byte[]> packets = [];
        foreach (var command in commands)
        {
            packets.Add(Packet(command, sequence++, timestamp, source, strictHeader: true));
        }
        return packets.ToArray();
    }

    public static IReadOnlyList<byte[]> Encode(ReadOnlySpan<byte> message)
    {
        MidiFrame.Validate(message);
        var commands = new List<byte[]>();
        int position = 0;
        byte runningStatus = 0;
        while (position < message.Length)
        {
            byte status = message[position] >= 128 ? message[position++] : runningStatus;
            if (status == 0xf0)
            {
                var fragment = new List<byte>(1002) { 0xf0 };
                bool ended = false;
                while (position < message.Length)
                {
                    byte value = message[position++];
                    if (value == 0xf7)
                    {
                        fragment.Add(0xf7); commands.Add(fragment.ToArray()); ended = true; break;
                    }
                    if (value >= 0xf8 && value is not (0xf9 or 0xfd))
                    {
                        if (fragment.Count > 1)
                        {
                            fragment.Add(0xf0); commands.Add(fragment.ToArray()); fragment = [0xf7];
                        }
                        commands.Add([value]);
                    }
                    else
                    {
                        if (value >= 128) { throw new InvalidDataException("Invalid MIDI SysEx data."); }
                        fragment.Add(value);
                        if (fragment.Count == 1001)
                        {
                            fragment.Add(0xf0); commands.Add(fragment.ToArray()); fragment = [0xf7];
                        }
                    }
                }
                if (!ended) { throw new InvalidDataException("Incomplete MIDI SysEx frame."); }
                runningStatus = 0;
            }
            else if (status == 0xf7)
            {
                runningStatus = 0; // Unpaired EOX has no RTP encoding or MIDI payload.
            }
            else
            {
                int bytes = Midi1ShortMessageCodec.GetLength(status);
                if (bytes == 0 || status is 0xf9 or 0xfd || bytes - 1 > message.Length - position)
                {
                    throw new InvalidDataException("Unsupported or incomplete MIDI short message.");
                }
                byte[] command = new byte[bytes]; command[0] = status;
                for (int index = 1; index < bytes; index++)
                {
                    byte value = message[position++];
                    if (value >= 128) { throw new InvalidDataException("Invalid MIDI data byte."); }
                    command[index] = value;
                }
                commands.Add(command);
                if (status < 0xf0) { runningStatus = status; }
                else if (status < 0xf8) { runningStatus = 0; }
            }
        }
        return commands;
    }

    public static byte[] Packet(ReadOnlySpan<byte> command, ushort sequence, uint timestamp, uint sender, bool strictHeader = false)
    {
        if (command.Length > 4095) throw new ArgumentOutOfRangeException(nameof(command));
        int header = strictHeader || command.Length > 15 ? 2 : 1;
        byte[] packet = new byte[12 + header + command.Length];
        packet[0] = 0x80; packet[1] = strictHeader ? (byte)0xe1 : (byte)0x61;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(8), sender);
        if (header == 1) { packet[12] = checked((byte)command.Length); }
        else { packet[12] = (byte)(0x80 | (command.Length >> 8)); packet[13] = (byte)command.Length; }
        command.CopyTo(packet.AsSpan(12 + header));
        return packet;
    }


    public void Dispose() => partial.Clear();
}
