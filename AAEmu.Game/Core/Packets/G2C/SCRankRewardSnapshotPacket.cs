using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One board as the season left it: the lines its places ended on, which is what the window shows while
/// it is in its pre-season mode.
/// </summary>
/// <remarks>
/// The body is the board's own — the requested type and division, a line count and the lines, a tier count
/// and the tiers, then the board's id — and it stops there: unlike the board's own answer it carries no
/// time, because the window reads its season's off date itself.
/// </remarks>
public class SCRankRewardSnapshotPacket(
    uint type,
    uint divisionId,
    IReadOnlyList<RankingOrderedEntry> entries,
    long boardId)
    : GamePacket(SCOffsets.SCRankRewardSnapshotPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        SCRankSnapshotPacket.WriteBoard(stream, type, divisionId, entries, boardId);
        return stream;
    }
}
