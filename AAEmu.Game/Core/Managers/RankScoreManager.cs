using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Rankings;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Scores the ranking boards and keeps them: what a holder is worth, in which window, and where that puts
/// them. A board shows every holder on the server rather than the ones in world, so the figures are written
/// as characters are saved and read back when the window asks.
/// </summary>
public class RankScoreManager(IRankScoreStore store) : Singleton<RankScoreManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// Writes every board's value for one character, in the window each board is in.
    /// </summary>
    /// <returns>How many boards the character was worth a line on.</returns>
    public int SaveCharacter(MySqlConnection connection, MySqlTransaction transaction, Character character)
    {
        if (character == null)
            return 0;

        var now = DateTime.UtcNow;
        var scores = new List<RankScore>();

        foreach (var board in RankingGameData.Instance.Ranks)
        {
            if (RankingGameData.Instance.HolderKindOf(board) != RankHolderKind.Character)
                continue;

            var value = CharacterScore(character, board, RankingGameData.Instance.GateFor(board.Id));
            if (value == null)
                continue;

            scores.Add(new RankScore
            {
                RankId = board.Id,
                HolderKind = RankHolderKind.Character,
                HolderId = character.Id,
                AccountId = character.AccountId,
                WorldId = (byte)AppConfiguration.Instance.Id,
                Value = value.Value,
                BareValue = 0,
                PeriodStartUtc = RankingGameData.Instance.PeriodFor(board, now).StartUtc,
                UpdatedAtUtc = now
            });
        }

        store.Save(connection, transaction, scores);
        return scores.Count + SavePeriodTotals(connection, transaction, character, now);
    }

    /// <summary>
    /// Writes what the character gained or spent since the last write into the running totals of each
    /// board's window, and then puts those totals on the boards that rank them.
    /// </summary>
    private int SavePeriodTotals(MySqlConnection connection, MySqlTransaction transaction, Character character, DateTime now)
    {
        var counters = new List<(RankDefinition Board, int Kind, int Method)>();
        foreach (var board in RankingGameData.Instance.Ranks)
        {
            if (RankingGameData.Instance.HolderKindOf(board) != RankHolderKind.Character)
                continue;

            if (RankingGameData.Instance.GamePointCounterOf(board) is not { } counter)
                continue;

            counters.Add((board, counter.Kind, counter.Method));
        }

        if (counters.Count == 0)
            return 0;

        // What is waiting has to be added to what is already stored, and the stored figure has to be read
        // before the write: a read through another connection cannot see this transaction's rows yet.
        var pending = character.RankGamePointTotals.Pending;
        var scores = new List<RankScore>();
        foreach (var (board, kind, method) in counters)
        {
            var period = RankingGameData.Instance.PeriodFor(board, now).StartUtc;
            var stored = store.ReadGamePointTotal(character.Id, kind, method, period);
            var delta = pending.TryGetValue((kind, method), out var waiting) ? waiting : 0;
            var total = stored + delta;
            if (total <= 0)
                continue;

            scores.Add(new RankScore
            {
                RankId = board.Id,
                HolderKind = RankHolderKind.Character,
                HolderId = character.Id,
                AccountId = character.AccountId,
                WorldId = (byte)AppConfiguration.Instance.Id,
                Value = total,
                BareValue = 0,
                PeriodStartUtc = period,
                UpdatedAtUtc = now
            });
        }

        if (pending.Count > 0)
        {
            // Every window keeps its own totals, so what is waiting is filed under each of them.
            foreach (var window in counters
                         .Select(entry => RankingGameData.Instance.PeriodFor(entry.Board, now).StartUtc)
                         .Distinct())
            {
                store.AddGamePointTotals(connection, transaction, character.Id, window, pending, now);
            }

            character.RankGamePointTotals.Clear();
        }

        store.Save(connection, transaction, scores);
        return scores.Count;
    }

    /// <summary>The lines of one board, best first, each with the place it holds.</summary>
    public List<RankPlace> ReadBoard(RankDefinition board, int limit)
    {
        var period = RankingGameData.Instance.PeriodFor(board, DateTime.UtcNow);
        var scores = store.ReadBoard(board.Id, period.StartUtc, limit);
        return RankScoreboard.Place(scores, board.PermitTie);
    }

    /// <summary>
    /// One holder's own line on every board they stand on, which is what the window's personal answer
    /// carries. A board the holder has no value on is left out rather than sent as a zero.
    /// </summary>
    public List<RankingEntryLine> PersonalLines(Character character)
    {
        var lines = new List<RankingEntryLine>();
        if (character == null)
            return lines;

        var now = DateTime.UtcNow;
        foreach (var board in RankingGameData.Instance.Ranks)
        {
            var kind = RankingGameData.Instance.HolderKindOf(board);
            var holderId = HolderIdOf(board, kind, character);
            if (holderId == null)
                continue;

            var period = RankingGameData.Instance.PeriodFor(board, now);
            var score = store.ReadHolder(board.Id, period.StartUtc, kind, holderId.Value);
            if (score == null)
                continue;

            lines.Add(new RankingEntryLine(board.Id, new RankingEntry
            {
                V1 = score.Value,
                V2 = score.BareValue,
                Timestamp = new DateTimeOffset(score.UpdatedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds(),
                WorldId = score.WorldId,
                Id = character.Id,
                AccountId = character.AccountId,
                Type = character.Id,
                PrivacyStatus = (byte)character.PrivacyStatus
            }));
        }

        return lines;
    }

    /// <summary>
    /// What a character is worth on a board, or null when the board does not measure them: below the
    /// board's floor, or wearing nothing the board counts.
    /// </summary>
    public static long? CharacterScore(Character character, RankDefinition board, RankGate gate)
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

    /// <summary>Who holds a board's lines: the character, or the expedition they belong to.</summary>
    private static ulong? HolderIdOf(RankDefinition board, RankHolderKind kind, Character character)
    {
        // An expedition is a faction in this tree, so its id is the faction's.
        return kind == RankHolderKind.Expedition
            ? character.Expedition == null ? null : (ulong)character.Expedition.Id
            : character.Id;
    }

    /// <summary>Scores the characters in world, which is what a board is refreshed from between saves.</summary>
    public int SaveCharactersInWorld()
    {
        var saved = 0;
        using var connection = AAEmu.Commons.Utils.DB.MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var character in WorldManager.Instance.GetAllCharacters() ?? [])
            saved += SaveCharacter(connection, transaction, character);

        transaction.Commit();
        Logger.Info("Rankings: refreshed {0} board value(s) from the characters in world", saved);
        return saved;
    }
}
