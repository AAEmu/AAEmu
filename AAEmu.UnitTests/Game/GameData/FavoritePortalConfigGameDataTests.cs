using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

[NotInParallel]
public class FavoritePortalConfigGameDataTests
{
    [Test]
    public async Task Load_ResolvesIdBackedEnumConfigWithSeparateKind()
    {
        using var connection = CreateDatabase();
        InsertEnum(connection, 44, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        InsertContent(connection, id: 44, kind: 22, value: 6);
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            data.Load(connection);
            data.PostLoad();

            await Assert.That(FavoritePortalCapacityRules.GetBaseLimit()).IsEqualTo(6);
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    [Test]
    public void Load_MissingEnumConfigRowFailsDuringStartupLoad()
    {
        using var connection = CreateDatabase();
        InsertEnum(connection, 44, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            Assert.Throws<InvalidOperationException>(() => data.Load(connection));
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    [Test]
    public void Load_DuplicateNameAcrossConfigIdsFailsLoudly()
    {
        using var connection = CreateDatabase();
        InsertEnum(connection, 44, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        InsertEnum(connection, 45, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        InsertContent(connection, id: 44, kind: 22, value: 6);
        InsertContent(connection, id: 45, kind: 23, value: 7);
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            data.Load(connection);

            Assert.Throws<InvalidOperationException>(() => data.PostLoad());
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    [Test]
    public void Load_NegativeDefaultFailsDuringPostLoad()
    {
        using var connection = CreateDatabase();
        InsertEnum(connection, 44, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        InsertContent(connection, id: 44, kind: 22, value: -1);
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            data.Load(connection);

            Assert.Throws<InvalidOperationException>(() => data.PostLoad());
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    [Test]
    public void Load_NullKindFailsDuringStartupLoad()
    {
        using var connection = CreateDatabase();
        InsertEnum(connection, 44, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        InsertContent(connection, id: 44, kind: null, value: 6);
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            Assert.Throws<InvalidOperationException>(() => data.Load(connection));
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    [Test]
    public void Load_NullValueFailsDuringStartupLoad()
    {
        using var connection = CreateDatabase();
        InsertEnum(connection, 44, FavoritePortalConfigGameData.DefaultFavoritePortalLimit);
        InsertContent(connection, id: 44, kind: 22, value: null);
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            Assert.Throws<InvalidOperationException>(() => data.Load(connection));
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    [Test]
    public void Load_MissingEnumTableFailsDuringStartupLoad()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var data = FavoritePortalConfigGameData.Instance;
        try
        {
            Assert.Throws<SqliteException>(() => data.Load(connection));
        }
        finally
        {
            data.RemoveForTest();
        }
    }

    private static SqliteConnection CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        Execute(connection,
            "CREATE TABLE enum_content_configs (id INTEGER NOT NULL, name TEXT NOT NULL)");
        Execute(connection,
            "CREATE TABLE content_configs (id INTEGER NOT NULL, kind_id INTEGER NULL, value INTEGER NULL)");
        return connection;
    }

    private static void InsertEnum(SqliteConnection connection, int id, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO enum_content_configs(id, name) VALUES (@id, @name)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@name", name);
        command.ExecuteNonQuery();
    }

    private static void InsertContent(SqliteConnection connection, int id, int? kind, int? value)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO content_configs(id, kind_id, value) VALUES (@id, @kind, @value)";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@kind", (object?)kind ?? DBNull.Value);
        command.Parameters.AddWithValue("@value", (object?)value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
