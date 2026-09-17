using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A character's own lines in the rankings: the character they are about, then one line per board the
/// server can fill.
/// </summary>
/// <remarks>
/// Field order and widths are the client's own: a 64-bit holder id, a 32-bit count, then each line as a
/// 32-bit board key followed by the entry itself. The holder id is the character the lines are for — the
/// client takes an answer only when that id is the character it is playing, and files every line it
/// carries under the line's own key, so all of a character's boards travel in one answer.
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
