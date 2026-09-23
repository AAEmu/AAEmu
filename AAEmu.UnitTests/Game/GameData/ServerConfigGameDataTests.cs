using AAEmu.Game.GameData;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// <c>server_configs</c> rows are groups of logic ids, not peer servers. An unnamed
/// departure therefore has no destination, however many groups the table holds.
/// </summary>
[NotInParallel]
public class ServerConfigGameDataTests
{
    private static SqliteConnection Open(params (int Id, string LogicIds, string Name)[] rows)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "CREATE TABLE server_configs (id INTEGER PRIMARY KEY, logic_ids TEXT, server_open_date TEXT, server_name TEXT)";
            command.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO server_configs (id, logic_ids, server_open_date, server_name) VALUES ($id, $logic, $opened, $name)";
            command.Parameters.AddWithValue("$id", row.Id);
            command.Parameters.AddWithValue("$logic", row.LogicIds);
            command.Parameters.AddWithValue("$opened", "0");
            command.Parameters.AddWithValue("$name", row.Name);
            command.ExecuteNonQuery();
        }

        return connection;
    }

    [Test]
    public async Task ShippedGroups_DoNotNameAPeerServer()
    {
        using var connection = Open((1, "1,2,3", "group-a"), (2, "4,5,6", "group-b"));
        ServerConfigGameData.Instance.Load(connection);

        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(1)).IsNull();
        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(4)).IsNull();
    }

    [Test]
    public async Task OneGroup_IsStillNotADestination()
    {
        using var connection = Open((1, "1,2,3", "group-a"));
        ServerConfigGameData.Instance.Load(connection);

        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(1)).IsNull();
    }

    [Test]
    public async Task EmptyTable_IsNotADestination()
    {
        using var connection = Open();
        ServerConfigGameData.Instance.Load(connection);

        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(1)).IsNull();
    }
}
