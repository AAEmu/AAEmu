using AAEmu.Game.Core.Managers.UnitManagers;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public sealed class CharacterNationDeletionStoreTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public CharacterNationDeletionStoreTests()
    {
        _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE characters (
                id INTEGER PRIMARY KEY, deleted INTEGER, faction_id INTEGER, faction_name TEXT,
                leadership_point INTEGER, leadership_period_point INTEGER, accumulated_leadership_point INTEGER,
                daily_leadership_point INTEGER, last_daily_leadership_point_time TEXT,
                mobilization_order_today_count INTEGER, mobilization_order_total_count INTEGER,
                last_mobilization_order_time TEXT, last_mobilization_accept_time TEXT,
                last_mobilization_not_recv_time TEXT
            );
            CREATE TABLE character_hero_bonus_progress (character_id INTEGER);
            CREATE TABLE hero_bonus_claims (character_id INTEGER);
            CREATE TABLE hero_dominion_point_gives (character_id INTEGER);
            CREATE TABLE hero_candidates (character_id INTEGER);
            CREATE TABLE hero_votes (voter_character_id INTEGER, candidate_character_id INTEGER);
            CREATE TABLE faction_relation_counts (character_id INTEGER, other_id INTEGER);
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task StageRemovesStoredNationAndLeadershipEvenWhenRuntimeStateWasStale()
    {
        Seed(42, factionId: 166);
        using var transaction = _connection.BeginTransaction();

        CharacterNationDeletionStore.Stage(_connection, transaction, 42);
        transaction.Commit();

        using var character = _connection.CreateCommand();
        character.CommandText = "SELECT faction_id, faction_name, leadership_point, leadership_period_point, accumulated_leadership_point, daily_leadership_point, mobilization_order_total_count FROM characters WHERE id=42";
        using var reader = character.ExecuteReader();
        await Assert.That(reader.Read()).IsTrue();
        for (var index = 0; index < reader.FieldCount; index++)
            await Assert.That(index == 1 ? reader.GetString(index) : reader.GetInt32(index).ToString())
                .IsEqualTo(index == 1 ? string.Empty : "0");
        reader.Close();

        await Assert.That(CountTransientRelations(42)).IsEqualTo(0);
        await Assert.That(CountHistoricalHeroRows(42)).IsEqualTo(5);
        await Assert.That(CountHistoricalHeroRows(99)).IsEqualTo(6);
    }

    [Test]
    public async Task RollbackRestoresNationLeadershipAndEveryRelatedRow()
    {
        Seed(42, factionId: 166);
        using (var transaction = _connection.BeginTransaction())
        {
            CharacterNationDeletionStore.Stage(_connection, transaction, 42);
            transaction.Rollback();
        }

        using var character = _connection.CreateCommand();
        character.CommandText = "SELECT faction_id, leadership_point FROM characters WHERE id=42";
        using var reader = character.ExecuteReader();
        await Assert.That(reader.Read()).IsTrue();
        await Assert.That(reader.GetInt32(0)).IsEqualTo(166);
        await Assert.That(reader.GetInt32(1)).IsEqualTo(700);
        reader.Close();
        await Assert.That(CountTransientRelations(42)).IsEqualTo(2);
        await Assert.That(CountHistoricalHeroRows(42)).IsEqualTo(5);
    }

    private void Seed(uint characterId, uint factionId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO characters VALUES ({characterId},0,{factionId},'stored nation',700,600,500,400,
                '2026-09-22 00:00:00',3,9,'2026-09-22 00:00:00','2026-09-22 00:00:00','2026-09-22 00:00:00');
            INSERT INTO character_hero_bonus_progress VALUES ({characterId});
            INSERT INTO hero_bonus_claims VALUES ({characterId});
            INSERT INTO hero_dominion_point_gives VALUES ({characterId});
            INSERT INTO hero_candidates VALUES ({characterId});
            INSERT INTO hero_votes VALUES ({characterId},99);
            INSERT INTO faction_relation_counts VALUES ({characterId},99);
            INSERT INTO faction_relation_counts VALUES (99,{characterId});
            """;
        command.ExecuteNonQuery();

        if (characterId == 42)
            Seed(99, factionId: 1);
    }

    private int CountTransientRelations(uint characterId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM faction_relation_counts WHERE character_id={characterId} OR other_id={characterId}";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private int CountHistoricalHeroRows(uint characterId)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"""
            SELECT (SELECT COUNT(*) FROM character_hero_bonus_progress WHERE character_id={characterId}) +
                   (SELECT COUNT(*) FROM hero_bonus_claims WHERE character_id={characterId}) +
                   (SELECT COUNT(*) FROM hero_dominion_point_gives WHERE character_id={characterId}) +
                   (SELECT COUNT(*) FROM hero_candidates WHERE character_id={characterId}) +
                   (SELECT COUNT(*) FROM hero_votes WHERE voter_character_id={characterId} OR candidate_character_id={characterId})
            """;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose() => _connection.Dispose();
}
