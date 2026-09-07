using AAEmu.Game.Models.Game.CommonFarm.Static;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Utils;

internal sealed class CommonFarmTestData : IDisposable
{
    public SqliteConnection Connection { get; } = new("Data Source=:memory:");

    public CommonFarmTestData()
    {
        Connection.Open();
        Execute("""
            CREATE TABLE common_farms (
                id INTEGER PRIMARY KEY,
                farm_group_id INTEGER NOT NULL,
                guard_time INTEGER NOT NULL,
                comments TEXT NOT NULL
            );
            CREATE TABLE farm_groups (
                id INTEGER PRIMARY KEY,
                count INTEGER NOT NULL
            );
            CREATE TABLE farm_group_doodads (
                id INTEGER PRIMARY KEY,
                farm_group_id INTEGER NOT NULL,
                doodad_id INTEGER NOT NULL,
                item_id INTEGER NOT NULL
            );
            """);
    }

    public void InsertFarm(uint id, FarmGroupKind kind, uint guardTime, string comments)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = """
            INSERT INTO common_farms (id, farm_group_id, guard_time, comments)
            VALUES (@id, @kind, @guardTime, @comments)
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@kind", (uint)kind);
        command.Parameters.AddWithValue("@guardTime", guardTime);
        command.Parameters.AddWithValue("@comments", comments);
        command.ExecuteNonQuery();
    }

    public void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}
