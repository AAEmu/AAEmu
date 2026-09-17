using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for one board as its season left it, which is what the window shows in its pre-season mode.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: the board asked for, its division, and the reward time the client
/// already holds. The answer carries the board's own body, over the window that closed most recently —
/// the same window the payout settles — so the lines a season ended on can still be read. A board whose
/// window never closes has no season behind it and is answered empty.
/// </remarks>
public class CSRankRewardSnapshotPacket() : GamePacket(CSOffsets.CSRankRewardSnapshotPacket, 1)
{
    public int TypeValue { get; private set; }
    public int DivisionId { get; private set; }
    public long CurRewardTime { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        DivisionId = stream.ReadInt32();
        CurRewardTime = stream.ReadInt64();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var boardId = (uint)TypeValue;
        var board = RankingGameData.Instance.GetBoard(boardId);
        var lines = new List<RankingOrderedEntry>();

        if (board != null &&
            RankPayouts.SeasonOf(board, RankingGameData.Instance.PeriodFor(board, DateTime.UtcNow)) is { } season)
        {
            lines = RankScoreManager.Instance.ReadBoard(board, season.StartUtc, SCRankSnapshotPacket.MaxEntries)
                .Select(CSRankSnapshotPacket.Row)
                .ToList();
        }

        character.SendPacket(new SCRankRewardSnapshotPacket(boardId, (uint)DivisionId, lines, boardId));
    }
}
