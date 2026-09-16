using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Rankings;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;
using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

namespace AAEmu.UnitTests.Game.Core.Managers;

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
                CREATE TABLE ranks (id INTEGER, name TEXT, rank_detail_id INTEGER, rank_kind_id INTEGER,
                                    tab_name TEXT, display_order INTEGER, permit_tie BOOLEAN, rank_reset_id INTEGER);
                INSERT INTO ranks VALUES (23, 'all gear', 23, 9, 'rank_tab_achievement', 1, {(permitTie ? 1 : 0)}, NULL);
                CREATE TABLE gear_rank_details (id INTEGER, min_score INTEGER);
                INSERT INTO gear_rank_details VALUES (23, 2500);
                CREATE TABLE item_rank_details (id INTEGER, min_level INTEGER, min_grade INTEGER);
                CREATE TABLE rank_resets (id INTEGER, reset_interval_id INTEGER, day_of_week_id INTEGER);
                """;
            command.ExecuteNonQuery();
        }

        var data = new RankingGameData();
        data.Load(connection);
        return new SingletonScope<RankingGameData>(data);
    }

    private sealed class InMemoryStore : IRankScoreStore
    {
        public List<RankScore> Rows { get; } = [];

        public void Save(MySqlConnection connection, MySqlTransaction transaction, IReadOnlyList<RankScore> scores)
        {
            Rows.AddRange(scores);
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
    }
}
