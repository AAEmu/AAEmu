using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Sieges;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The store's own statements, run against an in-memory database: the same text the server runs, so the
/// settlement's three writes are checked as a group rather than as a copy of the SQL.
/// </summary>
public class SiegeScoreStoreTests : IDisposable
{
    private const ushort ZoneGroup = 33;
    private static readonly DateTime CycleStart = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SettledAt = new(2026, 9, 4, 23, 0, 0, DateTimeKind.Utc);

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"siege-score-{Guid.NewGuid():N}.db");
    private readonly SqliteConnection _setup = new();
    private readonly MySqlSiegeScoreStore _store;

    public SiegeScoreStoreTests()
    {
        _setup.ConnectionString = $"Data Source={_dbPath}";
        _setup.Open();
        using var command = _setup.CreateCommand();
        command.CommandText = """
            CREATE TABLE siege_scores (
                zone_id INTEGER PRIMARY KEY,
                outlaw_point INTEGER NOT NULL DEFAULT 0,
                defense_point INTEGER NOT NULL DEFAULT 0,
                offense_point INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE siege_settlements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                zone_id INTEGER NOT NULL,
                cycle_week_start TEXT NOT NULL,
                settled_at TEXT NOT NULL,
                outlaw_point INTEGER NOT NULL DEFAULT 0,
                defense_point INTEGER NOT NULL DEFAULT 0,
                offense_point INTEGER NOT NULL DEFAULT 0,
                outcome INTEGER NOT NULL,
                defender_faction_id INTEGER NOT NULL,
                winner_faction_id INTEGER NOT NULL DEFAULT 0,
                reason TEXT NOT NULL DEFAULT '');
            CREATE UNIQUE INDEX uq_siege_settlements_zone_cycle ON siege_settlements (zone_id, cycle_week_start);
            CREATE TABLE dominions (
                zone_id INTEGER PRIMARY KEY,
                expedition_id INTEGER NOT NULL DEFAULT 0,
                faction_id INTEGER NOT NULL DEFAULT 0,
                last_siege_end_time TEXT,
                reign_start_time TEXT);
            """;
        command.ExecuteNonQuery();

        // The store opens and closes a connection per call, exactly as it does against the server, so the
        // fixture hands it a fresh one rather than sharing the connection the assertions use.
        _store = new MySqlSiegeScoreStore(() =>
        {
            var connection = new SqliteConnection($"Data Source={_dbPath}");
            connection.Open();
            return connection;
        });
    }

    public void Dispose()
    {
        _setup.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [Test]
    public async Task Read_OfAZoneGroupThatWasNeverScoredIsZero()
    {
        var state = _store.Read(ZoneGroup);

        await Assert.That(state.ZoneGroupId).IsEqualTo(ZoneGroup);
        await Assert.That(state.OutlawPoint).IsEqualTo(0u);
        await Assert.That(state.DefensePoint).IsEqualTo(0u);
        await Assert.That(state.OffensePoint).IsEqualTo(0u);
    }

    [Test]
    public async Task Add_FirstAwardCreatesTheRowAndLaterAwardsAccumulate()
    {
        await Assert.That(_store.Add(ZoneGroup, SiegeScoreSide.Offense, 40).OffensePoint).IsEqualTo(40u);
        var state = _store.Add(ZoneGroup, SiegeScoreSide.Offense, 25);

        await Assert.That(state.OffensePoint).IsEqualTo(65u);
        await Assert.That(state.DefensePoint).IsEqualTo(0u);
        await Assert.That(state.OutlawPoint).IsEqualTo(0u);
        await Assert.That(CountScoreRows()).IsEqualTo(1);
    }

    [Test]
    public async Task Add_KeepsEachSidesCounterToItself()
    {
        _store.Add(ZoneGroup, SiegeScoreSide.Outlaw, 7);
        _store.Add(ZoneGroup, SiegeScoreSide.Defense, 11);
        var state = _store.Add(ZoneGroup, SiegeScoreSide.Offense, 13);

        await Assert.That(state.OutlawPoint).IsEqualTo(7u);
        await Assert.That(state.DefensePoint).IsEqualTo(11u);
        await Assert.That(state.OffensePoint).IsEqualTo(13u);
    }

    [Test]
    public async Task Add_SaturatesAtTheCounterMaximumInsteadOfWrapping()
    {
        await Assert.That(_store.Add(ZoneGroup, SiegeScoreSide.Outlaw, uint.MaxValue - 5).OutlawPoint)
            .IsEqualTo(uint.MaxValue - 5);
        var state = _store.Add(ZoneGroup, SiegeScoreSide.Outlaw, 100);

        await Assert.That(state.OutlawPoint).IsEqualTo(uint.MaxValue);
    }

    [Test]
    public async Task Reset_ZeroesTheCountersAndKeepsTheRow()
    {
        _store.Add(ZoneGroup, SiegeScoreSide.Offense, 90);
        _store.Add(ZoneGroup, SiegeScoreSide.Outlaw, 12);

        _store.Reset(ZoneGroup);

        var state = _store.Read(ZoneGroup);
        await Assert.That(state.OffensePoint).IsEqualTo(0u);
        await Assert.That(state.OutlawPoint).IsEqualTo(0u);
        await Assert.That(CountScoreRows()).IsEqualTo(1);
    }

    [Test]
    public async Task Reset_OfAZoneGroupWithNoScoreCreatesTheZeroRow()
    {
        _store.Reset(ZoneGroup);

        await Assert.That(CountScoreRows()).IsEqualTo(1);
        await Assert.That(_store.Read(ZoneGroup).DefensePoint).IsEqualTo(0u);
    }

    [Test]
    public async Task Settle_RecordsTheOutcomeZeroesTheScoreAndHandsTheDominionToTheWinner()
    {
        SeedDominion(expeditionId: 4242, factionId: 148);
        _store.Add(ZoneGroup, SiegeScoreSide.Offense, 100);

        var settled = _store.Settle(Record(SiegeOutcome.OffenseBrokeThrough, defender: 148, winner: 149));

        await Assert.That(settled.Outcome).IsEqualTo(SiegeOutcome.OffenseBrokeThrough);
        await Assert.That(settled.WinnerFactionId).IsEqualTo(149u);
        await Assert.That(_store.Read(ZoneGroup).OffensePoint).IsEqualTo(0u);
        await Assert.That(ScoreOnRecord("offense_point")).IsEqualTo(100u);

        var row = ReadDominion();
        await Assert.That(row.FactionId).IsEqualTo(149u);
        // A settled dominion belongs to a nation, not to the guild that declared it.
        await Assert.That(row.ExpeditionId).IsEqualTo(0u);
        await Assert.That(row.ReignStartTime).IsEqualTo(SettledAt);
        await Assert.That(row.LastSiegeEndTime).IsEqualTo(SettledAt);
    }

    [Test]
    public async Task Settle_StampsTheReignWithoutChangingTheOwnerWhenTheDefenceHeld()
    {
        SeedDominion(expeditionId: 0, factionId: 148);
        _store.Add(ZoneGroup, SiegeScoreSide.Offense, 40);

        var settled = _store.Settle(Record(SiegeOutcome.DefenseHeld, defender: 148, winner: 0));

        await Assert.That(settled.Outcome).IsEqualTo(SiegeOutcome.DefenseHeld);
        await Assert.That(settled.WinnerFactionId).IsEqualTo(0u);
        var row = ReadDominion();
        await Assert.That(row.FactionId).IsEqualTo(148u);
        await Assert.That(row.ReignStartTime).IsEqualTo(SettledAt);
    }

    [Test]
    public async Task Settle_TwiceInTheSameCycleReturnsTheStoredOutcomeAndChangesNothing()
    {
        // A World that restarts between the commit and the phase update settles the same cycle again. The
        // outcome already on record is the one that comes back, so the second attempt cannot decide again from
        // counters that have since been zeroed.
        SeedDominion(expeditionId: 0, factionId: 148);
        _store.Add(ZoneGroup, SiegeScoreSide.Offense, 100);
        _store.Settle(Record(SiegeOutcome.OffenseBrokeThrough, defender: 148, winner: 149));

        var again = _store.Settle(Record(SiegeOutcome.DefenseHeld, defender: 148, winner: 0));

        await Assert.That(again.Outcome).IsEqualTo(SiegeOutcome.OffenseBrokeThrough);
        await Assert.That(again.WinnerFactionId).IsEqualTo(149u);
        await Assert.That(CountSettlementRows()).IsEqualTo(1);
        await Assert.That(ReadDominion().FactionId).IsEqualTo(149u);
    }

    [Test]
    public async Task Settle_IsIdempotentForTheDominionAndTheScoreWhenReapplied()
    {
        SeedDominion(expeditionId: 0, factionId: 148);
        _store.Add(ZoneGroup, SiegeScoreSide.Outlaw, 100);
        var record = Record(SiegeOutcome.OutlawBrokeThrough, defender: 148, winner: 114);

        _store.Settle(record);
        var again = _store.Settle(record);

        await Assert.That(again.SettledAtUtc).IsEqualTo(SettledAt);
        await Assert.That(ReadDominion().FactionId).IsEqualTo(114u);
        await Assert.That(_store.Read(ZoneGroup).OutlawPoint).IsEqualTo(0u);
    }

    [Test]
    public async Task Settle_RollsBackEverythingWhenTheDominionRowIsMissing()
    {
        // No dominions row: the outcome must not be left recorded against a dominion that does not exist.
        _store.Add(ZoneGroup, SiegeScoreSide.Offense, 100);

        var ex = Assert.Throws<InvalidOperationException>(
            () => _store.Settle(Record(SiegeOutcome.OffenseBrokeThrough, defender: 148, winner: 149)));

        await Assert.That(ex.Message).Contains("no dominions row");
        await Assert.That(CountSettlementRows()).IsEqualTo(0);
        await Assert.That(_store.Read(ZoneGroup).OffensePoint).IsEqualTo(100u);
    }

    [Test]
    public async Task Settle_RefusesAZoneGroupWithNoDominionToSettle()
    {
        _store.Add(ZoneGroup, SiegeScoreSide.Offense, 100);

        var ex = Assert.Throws<InvalidOperationException>(
            () => _store.Settle(Record(SiegeOutcome.OffenseBrokeThrough, defender: 148, winner: 149)));

        await Assert.That(ex.Message).Contains("no dominions row");
    }

    private SiegeSettlementRecord Record(SiegeOutcome outcome, uint defender, uint winner) => new()
    {
        ZoneGroupId = ZoneGroup,
        CycleWeekStart = CycleStart,
        SettledAtUtc = SettledAt,
        Score = _store.Read(ZoneGroup),
        Outcome = outcome,
        DefenderFactionId = defender,
        WinnerFactionId = winner,
        Reason = "test",
    };

    private void SeedDominion(uint expeditionId, uint factionId)
    {
        using var command = _setup.CreateCommand();
        command.CommandText =
            "INSERT INTO dominions (zone_id, expedition_id, faction_id) VALUES (@z, @e, @f)";
        command.Parameters.AddWithValue("@z", ZoneGroup);
        command.Parameters.AddWithValue("@e", expeditionId);
        command.Parameters.AddWithValue("@f", factionId);
        command.ExecuteNonQuery();
    }

    private (uint ExpeditionId, uint FactionId, DateTime ReignStartTime, DateTime LastSiegeEndTime) ReadDominion()
    {
        using var command = _setup.CreateCommand();
        command.CommandText = "SELECT expedition_id, faction_id, last_siege_end_time, reign_start_time FROM dominions";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (Convert.ToUInt32(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)),
            Convert.ToDateTime(reader.GetValue(2)), Convert.ToDateTime(reader.GetValue(3)));
    }

    private long ScoreOnRecord(string column)
    {
        using var command = _setup.CreateCommand();
        command.CommandText = $"SELECT {column} FROM siege_settlements";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private long CountScoreRows() => Scalar("SELECT COUNT(*) FROM siege_scores");

    private long CountSettlementRows() => Scalar("SELECT COUNT(*) FROM siege_settlements");

    private long Scalar(string sql)
    {
        using var command = _setup.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
