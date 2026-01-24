using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Login.Core.Network.Login;

public class LoginProtocolHandler : ILoginProtocolHandler
{
    public bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out ushort packetType,
        [NotNullWhen(true)] out PacketStream? packet)
    {
        const int PacketTypeSize = sizeof(ushort);

        // Start processing the buffer and parse packets.
        var reader = new SequenceReader<byte>(buffer);

        while (!reader.End)
        {
            // Check if there's enough data for reading the length of a packet.
            if (!reader.TryReadLittleEndian(out ushort packetLength))
            {
                // If there's not enough data to even read the length, just break out and wait for more data.
                break;
            }

            // TODO: Could enforce maximum packet size here (if smaller than ushort.MaxValue).

            // Check if we have enough data to read the full packet (packetLength includes packet type + packet data).
            if (reader.Remaining < packetLength)
            {
                // Not enough data, wait for more data to be received.
                break;
            }

            if (!reader.TryReadLittleEndian(out packetType))
            {
                break;
            }

            var dataLength = packetLength - PacketTypeSize; // Subtract the size of the packet type.
            // TODO: Improve this constructor as it makes an unnecessary copy of the data.
            packet = new PacketStream(reader.UnreadSequence.Slice(0, dataLength));
            reader.Advance(dataLength);

            // Move the buffer forward by the length of the full packet we just processed.
            buffer = buffer.Slice(reader.Position); // Slice the buffer to exclude the processed packet.

            return true;
        }

        packetType = 0;
        packet = null;
        return false;
    }
}
