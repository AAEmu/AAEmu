using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
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

                // The window reads the holder's character id out of the third identity slot — the one a
                // ranker's appearance is asked for by, and the one its name cache is queried with.
                Type = (long)place.Score.HolderId,
                PrivacyStatus = 0,
                Ranking = place.Position
            });
        }

        return rows;
    }

    /// <summary>
    /// What a character is worth on a board, or null when the board does not measure them: below the
    /// board's floor, or wearing nothing the board counts.
    /// </summary>
    public static long? ScoreFor(Character character, RankDefinition board, RankGate gate)
    {
        if (character == null)
            return null;

        if (board.DetailType == RankingGameData.GearScoreDetailType)
        {
            var score = character.GearScore;
            return score >= gate.MinScore ? score : null;
        }

        if (board.DetailType != RankingGameData.ItemDetailType)
            return null;

        var slots = RankingRules.ItemBoardSlots(board.Id);
        if (slots == null)
            return null;

        long? best = null;
        foreach (var item in character.Inventory?.Equipment?.Items ?? [])
        {
            if (item is not EquipItem equip || equip.Template is not WeaponTemplate weapon)
                continue;

            var holdable = ItemManager.Instance.GetHoldable(weapon.HoldableTemplate?.Id ?? 0);
            if (holdable == null || !slots.Contains((byte)holdable.SlotTypeId))
                continue;

            if (!RankingRules.ItemCounts(equip, gate))
                continue;

            var score = (long)Math.Round(GearScoreCalculator.EvaluateItem(equip));
            if (best == null || score > best.Value)
                best = score;
        }

        return best;
    }
}
