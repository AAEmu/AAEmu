using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Rankings;
using AAEmu.Game.Models.Tasks.RankTask;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Scores the ranking boards and keeps them: what a holder is worth, in which window, and where that puts
/// them. A board shows every holder on the server rather than the ones in world, so the figures are written
/// to the database and read back when the window asks.
/// </summary>
/// <remarks>
/// The boards are refreshed on their own hourly tick — the cadence the window itself states ("Refreshed
/// every 1 h") — while a character's running totals are written with the character, so nothing earned
/// between two ticks is lost.
/// </remarks>
public class RankScoreManager(IRankScoreStore store, ITaskManager taskManager) : Singleton<RankScoreManager>, IInitializable
{
    /// <summary>How often every board is rebuilt, matching the cadence the window shows.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(1);

    /// <summary>How long after a start the first rebuild runs, so a restarted server has boards.</summary>
    public static readonly TimeSpan FirstRefreshDelay = TimeSpan.FromMinutes(1);

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Initialize()
    {
        taskManager.Schedule(new RankRefreshTask(), FirstRefreshDelay, RefreshInterval);
        Logger.Info("Rankings: boards refresh every {0}", RefreshInterval);
    }

    /// <summary>
    /// Rebuilds every board: a period board from the running totals everyone has stored, a board over a
    /// figure held right now from the characters in world, whose equipment is what it is read from.
    /// </summary>
    public int Refresh(IReadOnlyList<Character> charactersInWorld)
    {
        using var connection = MySQL.CreateConnection();
        using var transaction = connection.BeginTransaction();
        var written = Refresh(charactersInWorld, connection, transaction);
        transaction.Commit();
        Logger.Info("Rankings: refreshed {0} board line(s)", written);
        return written;
    }

    /// <summary>The same rebuild on a caller's connection and transaction.</summary>
    public int Refresh(IReadOnlyList<Character> charactersInWorld, MySqlConnection connection, MySqlTransaction transaction)
    {
        var now = DateTime.UtcNow;
        var written = 0;

        foreach (var board in RankingGameData.Instance.Ranks)
        {
            if (RankingGameData.Instance.HolderKindOf(board) != RankHolderKind.Character)
                continue;

            var period = RankingGameData.Instance.PeriodFor(board, now).StartUtc;
            var rows = new List<RankScore>();

            if (RankingGameData.Instance.GamePointCounterOf(board) is { } counter)
            {
                // Everyone with a total in this window is on the board, whether they are in world or not.
                rows.AddRange(store.ReadGamePointBoard(board.Id, counter.Kind, counter.Method, period));
            }
            else
            {
                var gate = RankingGameData.Instance.GateFor(board.Id);
                foreach (var character in charactersInWorld ?? [])
                {
                    var value = CharacterScore(character, board, gate);
                    if (value == null)
                        continue;

                    rows.Add(new RankScore
                    {
                        RankId = board.Id,
                        HolderKind = RankHolderKind.Character,
                        HolderId = character.Id,
                        AccountId = character.AccountId,
                        WorldId = (byte)AppConfiguration.Instance.Id,
                        Value = value.Value,
                        BareValue = 0,
                        PeriodStartUtc = period,
                        UpdatedAtUtc = now
                    });
                }
            }

            if (rows.Count == 0)
                continue;

            store.Save(connection, transaction, rows);
            written += rows.Count;
        }

        return written;
    }

    /// <summary>
    /// Writes what a character gained or spent since the last write. The boards themselves are rebuilt on
    /// their own tick; this only keeps the running totals from being lost between two of them.
    /// </summary>
    public int SaveCharacter(MySqlConnection connection, MySqlTransaction transaction, Character character)
    {
        if (character == null || !character.RankGamePointTotals.HasPending)
            return 0;

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

        var now = DateTime.UtcNow;
        var holder = new RankScore
        {
            HolderKind = RankHolderKind.Character,
            HolderId = character.Id,
            AccountId = character.AccountId,
            WorldId = (byte)AppConfiguration.Instance.Id
        };

        // Every window keeps its own totals, so what is waiting is filed under each of them.
        var pending = character.RankGamePointTotals.Pending;
        foreach (var window in counters
                     .Select(entry => RankingGameData.Instance.PeriodFor(entry.Board, now).StartUtc)
                     .Distinct())
        {
            store.AddGamePointTotals(connection, transaction, holder, window, pending, now);
        }

        character.RankGamePointTotals.Clear();
        return pending.Count;
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
}
