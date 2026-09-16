using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Rankings;
using AAEmu.Game.Models.Game.Units;
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
        var manager = new RankScoreManager(store);
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
        var manager = new RankScoreManager(store);
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
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        var period = RankingGameData.Instance.PeriodFor(RankingGameData.Instance.GetBoard(23), DateTime.UtcNow).StartUtc;
        store.Rows.Add(Row(23, 9412, holder: 5, period: period));
        store.Rows.Add(Row(23, 9999, holder: 6, period: period));

        var lines = manager.PersonalLines(character);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Key).IsEqualTo(23u);
        await Assert.That(lines[0].Entry.V1).IsEqualTo(9412L);
        await Assert.That(lines[0].Entry.Id).IsEqualTo(5UL);
    }

    [Test]
    public async Task PersonalLines_LeaveOutBoardsTheHolderHasNoValueOn()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        await Assert.That(manager.PersonalLines(character)).IsEmpty();
    }

    private static RankScore Row(uint board, long value, ulong holder, DateTime? period = null)
    {
        return new RankScore
        {
            RankId = board,
            HolderKind = RankHolderKind.Character,
            HolderId = holder,
            AccountId = 3,
            WorldId = 1,
            Value = value,
            BareValue = 0,
            PeriodStartUtc = period ?? RankingGameData.Instance.PeriodFor(
                RankingGameData.Instance.GetBoard(board), DateTime.UtcNow).StartUtc,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    private static SingletonScope<RankingGameData> SeededBoard(bool permitTie)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE rank_details (id INTEGER, actual_type TEXT);
                INSERT INTO rank_details VALUES (23, 'GearRankDetail');
                INSERT INTO rank_details VALUES (42, 'GamePointRankDetail');
                CREATE TABLE ranks (id INTEGER, name TEXT, rank_detail_id INTEGER, rank_kind_id INTEGER,
                                    tab_name TEXT, display_order INTEGER, permit_tie BOOLEAN, rank_reset_id INTEGER);
                INSERT INTO ranks VALUES (23, 'all gear', 23, 9, 'rank_tab_achievement', 1, {(permitTie ? 1 : 0)}, NULL);
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
                """;
            command.ExecuteNonQuery();
        }

        var data = new RankingGameData();
        data.Load(connection);
        return new SingletonScope<RankingGameData>(data);
    }

    [Test]
    public async Task SaveCharacter_PutsWhatWasGainedOnTheBoardThatRanksIt()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 700);

        manager.SaveCharacter(null, null, character);

        var board = RankingGameData.Instance.GetBoard(42);
        var lines = manager.ReadBoard(board, 100);
        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0].Score.Value).IsEqualTo(700L);
        await Assert.That(character.RankGamePointTotals.HasPending).IsFalse(); // written, not pending
    }

    [Test]
    public async Task SaveCharacter_AddsToTheRunningTotalOfTheWindow()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 700);
        manager.SaveCharacter(null, null, character);

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 300);
        manager.SaveCharacter(null, null, character);

        var lines = manager.ReadBoard(RankingGameData.Instance.GetBoard(42), 100);
        await Assert.That(lines[0].Score.Value).IsEqualTo(1000L);
    }

    [Test]
    public async Task SaveCharacter_ReadsTheStoredTotalBeforeWritingTheNewOne()
    {
        // The write goes into the caller's transaction, which a read through the store's own connection
        // cannot see — so the board's figure has to be the stored total plus what is waiting, and the read
        // has to come first (live 2026-09-17: reading after the write left the period boards empty).
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 700);
        manager.SaveCharacter(null, null, character);

        await Assert.That(store.Operations.IndexOf("read-total")).IsLessThan(store.Operations.IndexOf("add-totals"));
    }

    [Test]
    public async Task SaveCharacter_LeavesABoardAloneWhenNothingWasCountedForIt()
    {
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        manager.SaveCharacter(null, null, character);

        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(42), 100)).IsEmpty();
    }

    [Test]
    public async Task SaveCharacter_GivesEachBoardTheCounterTheTableNames()
    {
        // game_point_rank_details: 42 (kind 1, gained), 45 (kind 1, spent), 47 (kind 3, spent).
        var store = new InMemoryStore();
        var manager = new RankScoreManager(store);
        using var data = SeededBoard(permitTie: false);
        var character = new Character(new UnitCustomModelParams()) { Id = 5 };

        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Gained, 900);
        character.RankGamePointTotals.Add(RankGamePoints.Honor, RankGamePoints.Spent, 200);
        character.RankGamePointTotals.Add(RankGamePoints.Labor, RankGamePoints.Spent, 70);

        manager.SaveCharacter(null, null, character);

        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(42), 100)[0].Score.Value).IsEqualTo(900L);
        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(45), 100)[0].Score.Value).IsEqualTo(200L);
        await Assert.That(manager.ReadBoard(RankingGameData.Instance.GetBoard(47), 100)[0].Score.Value).IsEqualTo(70L);
    }

    private sealed class InMemoryStore : IRankScoreStore
    {
        public List<RankScore> Rows { get; } = [];

        /// <summary>What was asked of the store, in order, so a test can pin the order of a read and a write.</summary>
        public List<string> Operations { get; } = [];

        private readonly Dictionary<(ulong Character, int Kind, int Method, DateTime Period), long> _totals = [];

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

        public void AddGamePointTotals(MySqlConnection connection, MySqlTransaction transaction, ulong characterId,
            DateTime periodStartUtc, IReadOnlyDictionary<(int Kind, int Method), long> totals, DateTime updatedAtUtc)
        {
            Operations.Add("add-totals");
            foreach (var ((kind, method), amount) in totals)
            {
                var key = (characterId, kind, method, periodStartUtc);
                _totals[key] = _totals.TryGetValue(key, out var current) ? current + amount : amount;
            }
        }

        public long ReadGamePointTotal(ulong characterId, int kind, int method, DateTime periodStartUtc)
        {
            Operations.Add("read-total");
            return _totals.TryGetValue((characterId, kind, method, periodStartUtc), out var total) ? total : 0;
        }
    }
}
