using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The raid-team registration queries against a database holding a soft-deleted character. Deleting a character
/// marks its row <c>deleted=1</c> and renames it, and the asset cleanup that follows does not touch
/// <c>siege_raid_team_members</c> - the registration outlives the character - so the query is the only thing
/// between a character who is gone and a place on a team.
/// </summary>
public class SiegeRaidTeamQueriesTests : IDisposable
{
    private const ushort SiegeZone = 1;
    private const ushort ZoneWithNothingLeft = 2;

    private const uint RegisteredNuia = 11;
    private const uint RegisteredNuiaSecond = 12;
    private const uint RegisteredHaranya = 13;
    private const uint SoftDeleted = 14;
    private const uint SoftDeletedAlone = 15;

    private const uint Nuia = 148;
    private const uint Haranya = 149;

    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SiegeRaidTeamQueriesTests()
    {
        _connection.Open();
        Execute("""
            CREATE TABLE characters (id INTEGER PRIMARY KEY, name TEXT NOT NULL, level INTEGER NOT NULL,
                ability1 INTEGER NOT NULL, ability2 INTEGER NOT NULL, ability3 INTEGER NOT NULL,
                heir_exp INTEGER NOT NULL, faction_id INTEGER NOT NULL, deleted INTEGER NOT NULL);
            CREATE TABLE siege_raid_team_members (zone_id INTEGER NOT NULL, character_id INTEGER NOT NULL,
                registered_at TEXT NOT NULL, PRIMARY KEY (zone_id, character_id));
            """);

        Character(RegisteredNuia, "Nuian", Nuia);
        Character(RegisteredNuiaSecond, "NuianSecond", Nuia);
        Character(RegisteredHaranya, "Haranyan", Haranya);
        // The delete flow keeps the row and marks it; the name it leaves behind starts with "!".
        Character(SoftDeleted, "!NuianThird", Nuia, deleted: true);
        Character(SoftDeletedAlone, "!LateNuian", Nuia, deleted: true);

        Register(SiegeZone, RegisteredNuia, "2026-01-01 00:00:01");
        Register(SiegeZone, RegisteredNuiaSecond, "2026-01-01 00:00:02");
        Register(SiegeZone, SoftDeleted, "2026-01-01 00:00:03");
        Register(SiegeZone, RegisteredHaranya, "2026-01-01 00:00:04");
        Register(ZoneWithNothingLeft, SoftDeletedAlone, "2026-01-01 00:00:05");
    }

    [Test]
    public async Task MemberList_LeavesOutASoftDeletedCharacter()
    {
        var members = ReadMembers(SiegeZone);

        await Assert.That(members.Select(member => member.CharacterId))
            .IsEquivalentTo(new[] { RegisteredNuia, RegisteredNuiaSecond, RegisteredHaranya });
        await Assert.That(members.Any(member => member.CharacterId == SoftDeleted)).IsFalse();
        // It is not listed under the name the delete left on it either.
        await Assert.That(members.Any(member => member.Name.StartsWith('!'))).IsFalse();
    }

    [Test]
    public async Task TeamRoster_DoesNotCountASoftDeletedMember()
    {
        var roster = ReadRoster(SiegeZone);

        await Assert.That(roster.Select(member => member.CharacterId))
            .IsEquivalentTo(new[] { RegisteredNuia, RegisteredNuiaSecond, RegisteredHaranya });

        var teams = SiegeRaidTeamRules.Group(roster, defenderFactionId: Nuia, isWaitWar: false);

        await Assert.That(teams.Count).IsEqualTo(2);
        await Assert.That(teams[0].FactionId).IsEqualTo(Nuia);
        // Two Nuia registrations, not three: the deleted one is not a member of the team.
        await Assert.That(teams[0].MemberCount).IsEqualTo(2);
        await Assert.That(teams[1].MemberCount).IsEqualTo(1);
    }

    [Test]
    public async Task AZoneGroupWhoseOnlyRegistrationIsDeletedHasNoTeams()
    {
        await Assert.That(ReadMembers(ZoneWithNothingLeft).Count).IsEqualTo(0);
        await Assert.That(ReadRoster(ZoneWithNothingLeft).Count).IsEqualTo(0);
        await Assert.That(SiegeRaidTeamRules.Group(ReadRoster(ZoneWithNothingLeft), Nuia, isWaitWar: false).Count)
            .IsEqualTo(0);
    }

    /// <summary>Runs the member-list statement the manager runs, against this database.</summary>
    private List<(uint CharacterId, string Name)> ReadMembers(ushort zoneId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SiegeRaidTeamQueries.MembersSql;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();

        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        var members = new List<(uint, string)>();
        while (reader.Read())
            members.Add((reader.GetUInt32("id"), reader.IsDBNull("name") ? string.Empty : reader.GetString("name")));

        return members;
    }

    /// <summary>Runs the roster statement the manager runs, against this database.</summary>
    private List<SiegeRaidTeamMember> ReadRoster(ushort zoneId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = SiegeRaidTeamQueries.RosterSql;
        command.Parameters.AddWithValue("@z", zoneId);
        command.Prepare();

        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        var roster = new List<SiegeRaidTeamMember>();
        while (reader.Read())
            roster.Add(new SiegeRaidTeamMember(reader.GetUInt32("character_id"), reader.GetUInt32("faction_id")));

        return roster;
    }

    private void Character(uint id, string name, uint factionId, bool deleted = false)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = """
            INSERT INTO characters (id, name, level, ability1, ability2, ability3, heir_exp, faction_id, deleted)
            VALUES (@id, @name, 55, 1, 2, 0, 0, @faction, @deleted)
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@faction", factionId);
        command.Parameters.AddWithValue("@deleted", deleted ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private void Register(ushort zoneId, uint characterId, string registeredAt)
    {
        using var command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO siege_raid_team_members (zone_id, character_id, registered_at) VALUES (@z, @c, @at)";
        command.Parameters.AddWithValue("@z", zoneId);
        command.Parameters.AddWithValue("@c", characterId);
        command.Parameters.AddWithValue("@at", registeredAt);
        command.ExecuteNonQuery();
    }

    private void Execute(string sql)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
