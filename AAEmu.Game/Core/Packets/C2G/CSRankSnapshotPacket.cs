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
    /// A board's lines: every character the World has a value for, best first, with the place each one
    /// holds. A board the tables gate keeps only the values that reach it.
    /// </summary>
    private static List<RankingOrderedEntry> RankedRows(RankDefinition board)
    {
        var gate = RankingGameData.Instance.GateFor(board.Id);
        var scored = new List<(Character Character, long Score)>();

        // The value of a board is read off the characters in the world, which are the ones whose gear the
        // World is holding. A board over values it has not measured stays empty rather than made up.
        foreach (var candidate in WorldManager.Instance.GetAllCharacters() ?? [])
        {
            var score = ScoreFor(candidate, board, gate);
            if (score != null)
                scored.Add((candidate, score.Value));
        }

        scored.Sort((left, right) => right.Score.CompareTo(left.Score));

        var rows = new List<RankingOrderedEntry>(scored.Count);
        for (var i = 0; i < scored.Count; i++)
        {
            var (holder, score) = scored[i];
            rows.Add(new RankingOrderedEntry
            {
                V1 = score,
                V2 = 0,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                // The world a holder is on is the server the client knows them by (its world list is
                // 1-based); the transform's own world id is 0 for the main continent and names nothing.
                WorldId = (byte)AppConfiguration.Instance.Id,
                Id = holder.Id,
                AccountId = holder.AccountId,

                // The window reads the holder's character id out of the third identity slot — the one a
                // ranker's appearance is asked for by, and the one its name cache is queried with. It is
                // written here as well as in Id because the client takes the holder from this slot: a line
                // sent without it makes the window ask about a character that does not exist.
                Type = holder.Id,
                PrivacyStatus = (byte)holder.PrivacyStatus,
                Ranking = (uint)(i + 1)
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
