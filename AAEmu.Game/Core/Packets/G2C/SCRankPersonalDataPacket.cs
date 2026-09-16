using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A character's own lines in the rankings: the ranking asked for, then one line per ranking key the
/// server can fill.
/// </summary>
/// <remarks>
/// Field order and widths are the client's own: a 64-bit ranking id, a 32-bit count, then each line as a
/// 32-bit key followed by the entry itself.
/// </remarks>
public class SCRankPersonalDataPacket(long type, IReadOnlyList<RankingEntryLine> entries)
    : GamePacket(SCOffsets.SCRankPersonalDataPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(type);
        stream.Write((uint)entries.Count);
        foreach (var line in entries)
        {
            stream.Write(line.Key);
            line.Entry.Write(stream);
        }

        return stream;
    }
}
