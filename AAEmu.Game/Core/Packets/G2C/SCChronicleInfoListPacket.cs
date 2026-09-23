using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The saga group (chronicle info) records a character holds — pushed on login and on world entry;
/// the story tab builds its list from this. Sent whole when it fits: one packet, isFirst and
/// endList both set, count entries.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: u8 isFirst, u8 endList, u32 count, then per entry u32 type and
/// u8 status.
/// </remarks>
public class SCChronicleInfoListPacket(
    bool isFirst,
    bool endList,
    IReadOnlyList<(int Type, sbyte Status)> entries)
    : GamePacket(SCOffsets.SCChronicleInfoListPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isFirst);
        stream.Write(endList);
        stream.Write(entries.Count);
        foreach (var (type, status) in entries)
        {
            stream.Write(type);
            stream.Write(status);
        }

        return stream;
    }
}
