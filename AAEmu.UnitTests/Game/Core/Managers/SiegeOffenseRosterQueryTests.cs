using AAEmu.Game.Core.Managers;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The offense roster statement behind the siege_offense_hq_user target relation: the attackers of one zone
/// group, and nobody else. A soft-deleted character keeps its registration row (the delete flow marks and
/// renames the character and never touches siege_raid_team_members), so the join has to drop it.
/// </summary>
public class SiegeOffenseRosterQueryTests : IDisposable
{
    private const ushort SiegeZone = 1;
    private const ushort OtherZone = 2;

    private const uint Attacker = 11;
    private const uint SecondAttacker = 12;
    private const uint Defender = 13;
    private const uint DeletedAttacker = 14;
    private const uint AttackerElsewhere = 15;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SiegeOffenseRosterQueryTests()
    {
        _connection.Open();
        Execute("""
            CREATE TABLE characters (id INTEGER PRIMARY KEY, name TEXT NOT NULL, deleted INTEGER NOT NULL);
            CREATE TABLE siege_raid_team_members (zone_id INTEGER NOT NULL, character_id INTEGER NOT NULL,
                is_offense INTEGER NOT NULL DEFAULT 0, registered_at TEXT NOT NULL,
                PRIMARY KEY (zone_id, character_id));
            """);

        Character(Attacker, "Attacker");
        Character(SecondAttacker, "SecondAttacker");
        Character(Defender, "Defender");
        Character(DeletedAttacker, "!DeletedAttacker", deleted: true);
        Character(AttackerElsewhere, "AttackerElsewhere");

        Register(SiegeZone, Attacker, isOffense: true);
        Register(SiegeZone, SecondAttacker, isOffense: true);
        Register(SiegeZone, Defender, isOffense: false);
        Register(SiegeZone, DeletedAttacker, isOffense: true);
        Register(OtherZone, AttackerElsewhere, isOffense: true);
    }

    [Test]
    public async Task OffenseRoster_IsTheLiveAttackersOfTheZoneGroup()
    {
        var roster = ReadOffenseRoster(SiegeZone);

        await Assert.That(roster).IsEquivalentTo(new[] { Attacker, SecondAttacker });
        await Assert.That(roster.Contains(Defender)).IsFalse();
        await Assert.That(roster.Contains(DeletedAttacker)).IsFalse();
        await Assert.That(roster.Contains(AttackerElsewhere)).IsFalse();
    }

    [Test]
    public async Task AZoneGroupWithNoRegistrations_HasAnEmptyRoster()
    {
        await Assert.That(ReadOffenseRoster(3).Count).IsEqualTo(0);
    }

    /// <summary>Runs the statement the manager runs, against this database.</summary>
    private HashSet<uint> ReadOffenseRoster(ushort zoneId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SiegeRaidTeamQueries.OffenseRosterSql;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();

        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        var roster = new HashSet<uint>();
        while (reader.Read())
            roster.Add(reader.GetUInt32("character_id"));

        return roster;
    }

    private void Character(uint id, string name, bool deleted = false) =>
        Execute("INSERT INTO characters (id, name, deleted) VALUES (@id, @name, @deleted)",
            ("@id", id), ("@name", name), ("@deleted", deleted ? 1 : 0));

    private void Register(ushort zoneId, uint characterId, bool isOffense) =>
        Execute("INSERT INTO siege_raid_team_members (zone_id, character_id, is_offense, registered_at) VALUES (@z, @c, @o, '2026-01-01 00:00:00')",
            ("@z", zoneId), ("@c", characterId), ("@o", isOffense ? 1 : 0));

    private void Execute(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
