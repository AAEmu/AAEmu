using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Rankings;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Asks for one board of the rankings: the lines it holds and the tiers its places are grouped into.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: the board asked for, its division, and the time of the snapshot
/// the client already holds.
/// </remarks>
public class CSRankSnapshotPacket() : GamePacket(CSOffsets.CSRankSnapshotPacket, 1)
{
    public int TypeValue { get; private set; }
    public int DivisionId { get; private set; }
    public long CurSnapTime { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadInt32();
        DivisionId = stream.ReadInt32();
        CurSnapTime = stream.ReadInt64();
    }

    public override void Execute()
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        var boardId = (uint)TypeValue;
        var board = RankingGameData.Instance.GetBoard(boardId);
        var rows = board == null ? [] : RankedRows(board);

        character.SendPacket(new SCRankSnapshotPacket(
            boardId,
            (uint)DivisionId,
            rows,
            boardId,
            (ulong)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    }

    /// <summary>
    /// A board's lines, read back from where the boards are kept: every holder on the server, best first,
    /// each with the place it holds. A board the World has no value for stays empty rather than made up.
    /// </summary>
    private static List<RankingOrderedEntry> RankedRows(RankDefinition board)
    {
        var rows = new List<RankingOrderedEntry>();
        foreach (var place in RankScoreManager.Instance.ReadBoard(board, SCRankSnapshotPacket.MaxEntries))
        {
            rows.Add(new RankingOrderedEntry
            {
                V1 = place.Score.Value,
                V2 = place.Score.BareValue,
                Timestamp = new DateTimeOffset(place.Score.UpdatedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                WorldId = place.Score.WorldId,
                Id = place.Score.HolderId,
                AccountId = place.Score.AccountId,

                // The window reads the holder out of the third identity slot — the one a ranker's appearance
                // is asked for by, and the one its name cache is queried with. An expedition line is named
                // from the expedition id, which is the same holder id.
                Type = (long)place.Score.HolderId,
                PrivacyStatus = 0,
                SubData = RankingSubData.FromBytes(place.Score.SubData),
                Ranking = place.Position
            });
        }

        return rows;
    }
}
