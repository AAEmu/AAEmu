using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests;

/// <summary>
/// Base class for tests that require a real SQLite database connection
/// </summary>
public abstract class SqliteTestBase : IDisposable
{
    protected SqliteConnection Connection { get; }
    protected string ConnectionString { get; }

    protected SqliteTestBase()
    {
        // Use in-memory database for tests
        ConnectionString = "Data Source=:memory:";
        Connection = new SqliteConnection(ConnectionString);
        Connection.Open();

        // Create test schema
        CreateTestSchema();
    }

    /// <summary>
    /// Creates the minimal test schema required for game data tests
    /// </summary>
    protected virtual void CreateTestSchema()
    {
        // Create np_skills table
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS np_skills (
                    id INTEGER PRIMARY KEY,
                    owner_id INTEGER NOT NULL,
                    owner_type TEXT NOT NULL,
                    skill_id INTEGER NOT NULL,
                    skill_use_condition_id INTEGER NOT NULL,
                    skill_use_param1 REAL,
                    skill_use_param2 REAL
                )";
            command.ExecuteNonQuery();
        }

        // Create np_passive_buffs table
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS np_passive_buffs (
                    id INTEGER PRIMARY KEY,
                    owner_id INTEGER NOT NULL,
                    owner_type TEXT NOT NULL,
                    passive_buff_id INTEGER NOT NULL
                )";
            command.ExecuteNonQuery();
        }

        // Create npc_spawners table
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS npc_spawners (
                    id INTEGER PRIMARY KEY,
                    npc_spawner_category_id INTEGER NOT NULL,
                    name TEXT NOT NULL,
                    comment TEXT,
                    maxPopulation INTEGER NOT NULL,
                    startTime REAL,
                    endTime REAL,
                    destroyTime REAL,
                    spawn_delay_min REAL,
                    activation_state INTEGER DEFAULT 1,
                    save_indun INTEGER DEFAULT 1,
                    min_population INTEGER NOT NULL,
                    spawn_count INTEGER NOT NULL,
                    respawn_time INTEGER NOT NULL,
                    npc_id INTEGER NOT NULL,
                    npc_grade_id INTEGER,
                    position_x REAL NOT NULL,
                    position_y REAL NOT NULL,
                    position_z REAL NOT NULL,
                    rotation REAL,
                    world_id INTEGER NOT NULL,
                    owner_id INTEGER,
                    owner_type TEXT,
                    group_id INTEGER,
                    faction_id INTEGER,
                    ai_id INTEGER
                )";
            command.ExecuteNonQuery();
        }

        // Create item_buffs table
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS item_buffs (
                    id INTEGER PRIMARY KEY,
                    item_id INTEGER NOT NULL,
                    grade INTEGER NOT NULL,
                    buff_id INTEGER NOT NULL
                )";
            command.ExecuteNonQuery();
        }

        // Create buff_effects table
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS buff_effects (
                    id INTEGER PRIMARY KEY,
                    buff_id INTEGER NOT NULL,
                    effect_id INTEGER NOT NULL
                )";
            command.ExecuteNonQuery();
        }

        // Create achievement_groups table
        using (var command = Connection.CreateCommand())
        {
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS achievement_groups (
                    id INTEGER PRIMARY KEY,
                    name TEXT NOT NULL,
                    parent_id INTEGER
                )";
            command.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Inserts test data for NPCs
    /// </summary>
    protected void InsertTestNpcSkill(uint ownerId, uint skillId, uint conditionId = 1)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO np_skills (owner_id, owner_type, skill_id, skill_use_condition_id)
            VALUES (@ownerId, 'npc', @skillId, @conditionId)";
        command.Parameters.AddWithValue("@ownerId", ownerId);
        command.Parameters.AddWithValue("@skillId", skillId);
        command.Parameters.AddWithValue("@conditionId", conditionId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Inserts test data for item buffs
    /// </summary>
    protected void InsertTestItemBuff(uint itemId, int grade, int buffId)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO item_buffs (item_id, grade, buff_id)
            VALUES (@itemId, @grade, @buffId)";
        command.Parameters.AddWithValue("@itemId", itemId);
        command.Parameters.AddWithValue("@grade", grade);
        command.Parameters.AddWithValue("@buffId", buffId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Disposes managed resources
    /// </summary>
    /// <param name="disposing">True if called from Dispose, false if called from finalizer</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Connection?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
