using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Rankings;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// These tests install a seeded <see cref="RankingGameData"/> as the shared singleton, so they run on their
/// own: a reader in another test would otherwise see a board table mid-swap.
/// </summary>
[NotInParallel]
public class RankScoreManagerTests
{
    [Test]
    public async Task ReadBoard_HandsOutThePlacesTheStoredValuesEarn()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: true);
        var board = RankingGameData.Instance.GetBoard(23);

        store.Rows.AddRange([
            Row(23, 900, holder: 5),
            Row(23, 900, holder: 6),
            Row(23, 400, holder: 7)
        ]);

        var places = manager.ReadBoard(board, 100);

        await Assert.That(places.Count).IsEqualTo(3);
        await Assert.That(places[0].Position).IsEqualTo(1u);
        await Assert.That(places[1].Position).IsEqualTo(1u); // the board permits ties
        await Assert.That(places[2].Position).IsEqualTo(3u);
        await Assert.That(places[2].Score.HolderId).IsEqualTo(7UL);
    }

    [Test]
    public async Task ReadBoard_ReadsOnlyTheWindowTheBoardIsIn()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var board = RankingGameData.Instance.GetBoard(23);

        var current = RankingGameData.Instance.PeriodFor(board, DateTime.UtcNow).StartUtc;
        store.Rows.Add(Row(23, 900, holder: 5, period: current));
        store.Rows.Add(Row(23, 5000, holder: 9, period: current.AddDays(-7))); // a window that has closed

        var places = manager.ReadBoard(board, 100);

        await Assert.That(places.Count).IsEqualTo(1);
        await Assert.That(places[0].Score.HolderId).IsEqualTo(5UL);
    }

    [Test]
    public async Task PersonalLines_CarryTheHoldersOwnStoredValues()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        var period = RankingGameData.Instance.PeriodFor(RankingGameData.Instance.GetBoard(23), DateTime.UtcNow).StartUtc;
        store.Rows.Add(Row(23, 9412, holder: 5, period: period, bare: 9412));
        store.Rows.Add(Row(23, 9999, holder: 6, period: period));

        var lines = manager.PersonalLines(character);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Key).IsEqualTo(23u);
        await Assert.That(lines[0].Entry.V1).IsEqualTo(9412L);
        await Assert.That(lines[0].Entry.V2).IsEqualTo(9412L);
        await Assert.That(lines[0].Entry.Id).IsEqualTo(5UL);
    }

    [Test]
    public async Task PersonalLines_SendTheStonePointsAsTheGapBetweenTheTwoValues()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        var period = RankingGameData.Instance.PeriodFor(RankingGameData.Instance.GetBoard(23), DateTime.UtcNow).StartUtc;
        store.Rows.Add(Row(23, 9776, holder: 5, period: period, bare: 9774));

        var lines = manager.PersonalLines(character);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Entry.V1).IsEqualTo(9776L);
        await Assert.That(lines[0].Entry.V2).IsEqualTo(9774L);
    }

    [Test]
    public async Task PersonalLines_LeaveOutBoardsTheHolderHasNoValueOn()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        await Assert.That(manager.PersonalLines(character)).IsEmpty();
    }

    private static RankScore Row(uint board, long value, ulong holder, DateTime? period = null, long bare = 0)
    {
        return new RankScore
        {
            RankId = board,
            HolderKind = RankHolderKind.Character,
            HolderId = holder,
            AccountId = 3,
            WorldId = 1,
            Value = value,
            BareValue = bare,
            PeriodStartUtc = period ?? RankingGameData.Instance.PeriodFor(
                RankingGameData.Instance.GetBoard(board), DateTime.UtcNow).StartUtc,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static SingletonScope<RankingGameData> SeededBoard(bool permitTie, bool withExpeditionBoard = false,
        bool withFishingBoards = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE rank_details (id INTEGER, actual_type TEXT);
                INSERT INTO rank_details VALUES (23, 'GearRankDetail');
                INSERT INTO rank_details VALUES (28, 'ExpeditionGearScoreRankDetail');
                INSERT INTO rank_details VALUES (42, 'GamePointRankDetail');
                CREATE TABLE ranks (id INTEGER, name TEXT, rank_detail_id INTEGER, rank_kind_id INTEGER,
                                    tab_name TEXT, display_order INTEGER, permit_tie BOOLEAN, rank_reset_id INTEGER);
                INSERT INTO ranks VALUES (23, 'all gear', 23, 9, 'rank_tab_achievement', 1, {(permitTie ? 1 : 0)}, NULL);
                {(withExpeditionBoard ? "INSERT INTO ranks VALUES (28, 'guild level', 28, 12, 'rank_tab_expedition', 30, 1, NULL);" : string.Empty)}
                INSERT INTO ranks VALUES (42, 'honor earned', 42, 17, 'rank_tab_get_resource', 81, {(permitTie ? 1 : 0)}, 42);
                INSERT INTO ranks VALUES (45, 'honor spent', 45, 17, 'rank_tab_use_resource', 91, {(permitTie ? 1 : 0)}, 42);
                INSERT INTO ranks VALUES (47, 'labor spent', 47, 17, 'rank_tab_use_resource', 93, {(permitTie ? 1 : 0)}, 42);
                CREATE TABLE gear_rank_details (id INTEGER, min_score INTEGER);
                INSERT INTO gear_rank_details VALUES (23, 2500);
                CREATE TABLE item_rank_details (id INTEGER, min_level INTEGER, min_grade INTEGER);
                CREATE TABLE game_point_rank_details (id INTEGER, game_point_kind INTEGER, game_point_method INTEGER);
                INSERT INTO game_point_rank_details VALUES (42, 1, 0);
                INSERT INTO game_point_rank_details VALUES (45, 1, 1);
                INSERT INTO game_point_rank_details VALUES (47, 3, 1);
                CREATE TABLE rank_resets (id INTEGER, reset_interval_id INTEGER, day_of_week_id INTEGER);
                INSERT INTO rank_resets VALUES (42, 2, 8);
                {(withFishingBoards ? """
                INSERT INTO ranks VALUES (20, 'biggest catch', 20, 4, 'rank_tab_fish', 51, 0, 20);
                INSERT INTO ranks VALUES (21, 'catch in total', 21, 3, 'rank_tab_fish', 52, 0, 21);
                INSERT INTO rank_resets VALUES (20, 1, 7);
                INSERT INTO rank_resets VALUES (21, 1, 7);
                """ : string.Empty)}
                CREATE TABLE rank_tiers (id INTEGER, rank_id INTEGER, is_local BOOLEAN, scope_from INTEGER, scope_to INTEGER,
                                         reward_item_id INTEGER, reward_item_count INTEGER, reward_item_grade_id INTEGER,
                                         currency_id INTEGER, currency_amount INTEGER);
                INSERT INTO rank_tiers VALUES (1, 42, 0, 1, 1, 0, 0, 0, 0, 0);
                """;
            command.ExecuteNonQuery();
        }

        var data = new RankingGameData();
        data.Load(connection);
        return new SingletonScope<RankingGameData>(data);
    }

    [Test]
    public async Task Refresh_PutsWhatWasGainedOnTheBoardThatRanksIt()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 700);
        manager.SaveCharacter(null, null, character);
        await Assert.That(character.RankGamePointTotals.HasPending).IsFalse(); // written, not pending

        manager.Refresh([], null, null);

        var board = RankingGameData.Instance.GetBoard(42);
        var lines = manager.ReadBoard(board, 100);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Score.Value).IsEqualTo(700L);
    }

    [Test]
    public async Task SaveCharacter_AddsToTheRunningTotalOfTheWindow()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 700);
        manager.SaveCharacter(null, null, character);

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 300);
        manager.SaveCharacter(null, null, character);

        manager.Refresh([], null, null);

        var lines = manager.ReadBoard(RankingGameData.Instance.GetBoard(42), 100);
        await Assert.That(lines[0].Score.Value).IsEqualTo(1000L);
    }

    [Test]
    public async Task Refresh_LeavesABoardAloneWhenNothingWasCountedForIt()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);

        manager.Refresh([], null, null);

        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(42), 100)).IsEmpty();
    }

    [Test]
    public async Task Refresh_GivesEachBoardTheCounterTheTableNames()
    {
        // game_point_rank_details: 42 (kind 1, gained), 45 (kind 1, spent), 47 (kind 3, spent).
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 900);
        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Spent, 200);
        character.RankGamePointTotals.Add(RankGamePoints.Labor, RankGamePoints.Spent, 70);

        manager.SaveCharacter(null, null, character);
        manager.Refresh([], null, null);

        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(42), 100)[0].Score.Value).IsEqualTo(900L);
        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(45), 100)[0].Score.Value).IsEqualTo(200L);
        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(47), 100)[0].Score.Value).IsEqualTo(70L);
    }

    [Test]
    public async Task SaveCharacter_WithoutAnythingWaiting_WritesNothing()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        await Assert.That(manager.SaveCharacter(null, null, character)).IsEqualTo(0);
        await Assert.That(store.Operations).IsEmpty();
    }

    [Test]
    public async Task PayEndedWindows_RecordsTheWindowItPaid()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);

        manager.PayEndedWindows(DateTime.UtcNow, null, null);

        var board = RankingGameData.Instance.GetBoard(42);
        var previous = RankPayouts.Previous(board.ResetIntervalId, board.ResetDayOfWeekId, RankingGameData.Instance.PeriodFor(board, DateTime.UtcNow));
        await Assert.That(store.HasPayout(board.Id, previous.StartUtc)).IsTrue();
    }

    [Test]
    public async Task PayEndedWindows_PaysAWindowOnceAndLeavesItAloneAfterwards()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false);
        var board = RankingGameData.Instance.GetBoard(42);
        var previous = RankPayouts.Previous(board.ResetIntervalId, board.ResetDayOfWeekId, RankingGameData.Instance.PeriodFor(board, DateTime.UtcNow)).StartUtc;

        // a standing in the window that has closed, and a board whose tiers carry no reward, so the pass
        // exercises the bookkeeping rather than the mail it hands out
        store.Rows.Add(Row(42, 900, holder: 5, period: previous));

        manager.PayEndedWindows(DateTime.UtcNow, null, null);
        manager.PayEndedWindows(DateTime.UtcNow, null, null);

        await Assert.That(store.PayoutMarks.Count(pair => pair.RankId == 42 && pair.Period == previous)).IsEqualTo(1);
    }

    [Test]
    public async Task RefreshExpeditionBoards_ShowsTheLevelAndTheEquipmentPointsOfEveryMember()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: true, withExpeditionBoard: true);

        // two members: one only known from the character gear board, one standing in world
        store.Rows.Add(Row(23, 4000, holder: 11));
        var inWorld = new Character(new UnitCustomModelParams()) { Id = 12 };
        var expedition = new Expedition
        {
            Id = (FactionsEnum)77,
            Level = 5,
            Members = [new ExpeditionMember { CharacterId = 11 }, new ExpeditionMember { CharacterId = 12 }]
        };

        manager.RefreshExpeditionBoards(DateTime.UtcNow, null, null, [inWorld], [expedition]);

        var board = RankingGameData.Instance.GetBoard(28);
        var lines = manager.ReadBoard(board, 100);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Score.HolderKind).IsEqualTo(RankHolderKind.Expedition);
        await Assert.That(lines[0].Score.HolderId).IsEqualTo(77UL);
        await Assert.That(lines[0].Score.Value).IsEqualTo(5L);   // the guild's level

        // the member in world is counted at what they wear now, the one who is not at what is kept
        await Assert.That(lines[0].Score.BareValue).IsEqualTo(4000L + inWorld.GearScore);

        var subData = RankingSubData.FromBytes(lines[0].Score.SubData);
        await Assert.That(subData).IsNotNull();
        await Assert.That(subData.Counts[0]).IsEqualTo(2);             // the members it counted
    }

    [Test]
    public async Task RefreshExpeditionBoards_LeavesABoardNothingProducesAlone()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false, withExpeditionBoard: true);

        // the guild level board is the only expedition board with a source, so nothing is written for a
        // server whose guilds are all empty
        manager.RefreshExpeditionBoards(DateTime.UtcNow, null, null, [], []);

        await Assert.That(store.Rows).IsEmpty();
    }

    [Test]
    public async Task RecordCatch_ShowsTheLongestCatchAndWhatTheWindowCaughtInTotal()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false, withFishingBoards: true);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        // a long, light fish, then a shorter, heavier one; the window reads the figures in thousandths
        var first = new BigFish { Length = 249.4f, Weight = 448.2f };
        RankScoreManager.RecordCatch(character, first);
        await Assert.That(character.RankRecords.HasPending).IsTrue();
        manager.SaveCharacter(null, null, character);
        await Assert.That(character.RankRecords.HasPending).IsFalse(); // written, not pending

        var second = new BigFish { Length = 198f, Weight = 700.4f };
        RankScoreManager.RecordCatch(character, second);
        manager.SaveCharacter(null, null, character);

        manager.Refresh([], null, null);

        var longest = manager.ReadBoard(RankingGameData.Instance.GetBoard(20), 100);
        await Assert.That(longest.Count).IsEqualTo(1);
        await Assert.That(longest[0].Score.Value).IsEqualTo(249400L);   // 249.4 cm, the longest of the window

        var total = manager.ReadBoard(RankingGameData.Instance.GetBoard(21), 100);
        await Assert.That(total.Count).IsEqualTo(1);
        await Assert.That(total[0].Score.Value).IsEqualTo(1148600L);    // 448.2 + 700.4 kg together
    }

    [Test]
    public async Task RecordCatch_LeavesTheBoardsAloneWhenNothingWasCaught()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store, Mock.Of<ITaskManager>().Object);
        using var data = SeededBoard(permitTie: false, withFishingBoards: true);

        manager.SaveCharacter(null, null, new Character(new UnitCustomModelParams()) { Id = 5 });
        manager.Refresh([], null, null);

        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(20), 100)).IsEmpty();
        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(21), 100)).IsEmpty();
    }

    private sealed class InMemoryStore : IRankScoreStore
    {
        public List<RankScore> Rows { get; } = [];

        /// <summary>What was asked of the store, in order, so a test can pin the order of a read and a write.</summary>
        public List<string> Operations { get; } = [];

        /// <summary>The windows the manager has recorded as paid.</summary>
        public List<(uint RankId, DateTime Period)> PayoutMarks { get; } = [];

        private readonly Dictionary<(ulong Character, int Kind, int Method, DateTime Period), long> _totals = [];

        private readonly Dictionary<(ulong Character, int Kind, int Method, DateTime Period), (uint AccountId, byte WorldId)> _holders = [];

        /// <summary>What the store holds of each character's records, keyed as the table is.</summary>
        private readonly Dictionary<(ulong Character, RankRecordKind Kind, DateTime Period), (long Value, DateTime RecordedAt)> _records = [];

        public void Save(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<RankScore> scores)
        {
            foreach (var score in scores)
            {
                Rows.RemoveAll(row => row.RankId == score.RankId
                                      && row.HolderKind == score.HolderKind
                                      && row.HolderId == score.HolderId
                                      && row.PeriodStartUtc == score.PeriodStartUtc);
                Rows.Add(score);
            }
        }

        public List<RankScore> ReadBoard(uint rankId, DateTime periodStartUtc, int limit)
        {
            return Rows
                .Where(row => row.RankId == rankId && row.PeriodStartUtc == periodStartUtc)
                .OrderByDescending(row => row.Value)
                .ThenBy(row => row.HolderId)
                .Take(limit)
                .ToList();
        }

        public RankScore ReadHolder(uint rankId, DateTime periodStartUtc, RankHolderKind kind, ulong holderId)
        {
            return Rows.FirstOrDefault(row => row.RankId == rankId
                                              && row.PeriodStartUtc == periodStartUtc
                                              && row.HolderKind == kind
                                              && row.HolderId == holderId);
        }

        public Dictionary<ulong, long> ReadValues(uint rankId, DateTime periodStartUtc, IReadOnlyCollection<ulong> holderIds)
        {
            Operations.Add("read-values");
            return Rows
                .Where(row => row.RankId == rankId
                              && row.PeriodStartUtc == periodStartUtc
                              && row.HolderKind == RankHolderKind.Character
                              && holderIds.Contains(row.HolderId))
                .ToDictionary(row => row.HolderId, row => row.Value);
        }

        public void AddGamePointTotals(MySqlConnection connection, MySqlTransaction transaction, RankScore holder,
            DateTime periodStartUtc, IReadOnlyDictionary<(int Kind, int Method), long> totals, DateTime updatedAtUtc)
        {
            Operations.Add("add-totals");
            foreach (var ((kind, method), amount) in totals)
            {
                var key = (holder.HolderId, kind, method, periodStartUtc);
                _totals[key] = _totals.TryGetValue(key, out var current) ? current + amount : amount;
                _holders[key] = (holder.AccountId, holder.WorldId);
            }
        }

        public long ReadGamePointTotal(ulong characterId, int kind, int method, DateTime periodStartUtc)
        {
            Operations.Add("read-total");
            return _totals.TryGetValue((characterId, kind, method, periodStartUtc), out var total) ? total : 0;
        }

        public List<RankScore> ReadGamePointBoard(uint rankId, int kind, int method, DateTime periodStartUtc)
        {
            var rows = new List<RankScore>();
            foreach (var ((characterId, rowKind, rowMethod, period), total) in _totals)
            {
                if (rowKind != kind || rowMethod != method || period != periodStartUtc || total <= 0)
                    continue;

                var (accountId, worldId) = _holders[(characterId, rowKind, rowMethod, period)];
                rows.Add(new RankScore
                {
                    RankId = rankId,
                    HolderKind = RankHolderKind.Character,
                    HolderId = characterId,
                    AccountId = accountId,
                    WorldId = worldId,
                    Value = total,
                    BareValue = 0,
                    PeriodStartUtc = periodStartUtc,
                    UpdatedAtUtc = DateTime.UtcNow
                });
            }

            return rows;
        }

        public void AddRecords(MySqlConnection connection, MySqlTransaction transaction, RankScore holder,
            DateTime periodStartUtc, IReadOnlyList<RankRecordEvent> records, DateTime updatedAtUtc)
        {
            Operations.Add("add-records");
            foreach (var record in records)
            {
                var key = (holder.HolderId, record.Kind, periodStartUtc);
                _records.TryGetValue(key, out var current);

                _records[key] = RankRecordRules.AggregateOf(record.Kind) == RankRecordAggregate.Best
                    ? record.Value > current.Value ? (record.Value, record.RecordedAtUtc) : current
                    : (current.Value + record.Value, record.RecordedAtUtc);

                _holders[(holder.HolderId, 0, 0, periodStartUtc)] = (holder.AccountId, holder.WorldId);
            }
        }

        public List<RankScore> ReadRecordBoard(uint rankId, RankRecordKind kind, DateTime periodStartUtc)
        {
            Operations.Add("read-records");
            var rows = new List<RankScore>();
            foreach (var ((characterId, rowKind, period), (value, recordedAt)) in _records)
            {
                if (rowKind != kind || period != periodStartUtc || value <= 0)
                    continue;

                var (accountId, worldId) = _holders[(characterId, 0, 0, period)];
                rows.Add(new RankScore
                {
                    RankId = rankId,
                    HolderKind = RankHolderKind.Character,
                    HolderId = characterId,
                    AccountId = accountId,
                    WorldId = worldId,
                    Value = value,
                    BareValue = 0,
                    PeriodStartUtc = periodStartUtc,
                    UpdatedAtUtc = recordedAt
                });
            }

            return rows;
        }

        public bool HasPayout(uint rankId, DateTime periodStartUtc)
        {
            return PayoutMarks.Contains((rankId, periodStartUtc));
        }

        public void MarkPayout(uint rankId, DateTime periodStartUtc, DateTime paidAtUtc)
        {
            PayoutMarks.Add((rankId, periodStartUtc));
        }
    }
}
