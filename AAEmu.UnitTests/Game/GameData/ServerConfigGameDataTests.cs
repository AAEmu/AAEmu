using AAEmu.Game.GameData;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

/// <summary>
/// The departure target is the other <c>server_configs</c> row. Anything other than exactly one
/// peer is not a destination.
/// </summary>
[NotInParallel]
public class ServerConfigGameDataTests
{
    private static SqliteConnection Open(params int[] ids)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE server_configs (id INTEGER PRIMARY KEY)";
            command.ExecuteNonQuery();
        }

        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO server_configs (id) VALUES ($id)";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        return connection;
    }

    [Test]
    public async Task OneOtherServer_IsTheDepartureTarget()
    {
        using var connection = Open(1, 2);
        ServerConfigGameData.Instance.Load(connection);

        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(1)).IsEqualTo("2");
        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(2)).IsEqualTo("1");
    }

    [Test]
    public async Task MoreThanOnePeer_IsNotADestination()
    {
        using var connection = Open(1, 2, 3);
        ServerConfigGameData.Instance.Load(connection);

        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(1)).IsNull();
    }

    [Test]
    public async Task NoPeer_IsNotADestination()
    {
        using var connection = Open(1);
        ServerConfigGameData.Instance.Load(connection);

        await Assert.That(ServerConfigGameData.Instance.ResolvePeerKey(1)).IsNull();
    }
}
