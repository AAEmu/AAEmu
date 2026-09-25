using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj.Details;

using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.GameData;

[NotInParallel]
public sealed class DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalogTests
{
    private const uint SyntheticDescriptorId = 101;
    private const uint SyntheticItemId = 202;
    private const int SyntheticDelay = 7;
    private const float SyntheticOffsetX = 1.25f;
    private const float SyntheticOffsetZ = -2.5f;
    private const float SyntheticAngle = 30f;

    [Test]
    public async Task LoadReadsTypedContentFields()
    {
        using var connection = CreateConnection();
        InsertRow(connection, SyntheticDescriptorId, SyntheticItemId, SyntheticDelay,
            SyntheticOffsetX, SyntheticOffsetZ, SyntheticAngle);

        var catalog = DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog.Load(connection);

        await Assert.That(catalog.Count).IsEqualTo(1);
        await Assert.That(catalog.TryGet(SyntheticDescriptorId, out var descriptor)).IsTrue();
        await Assert.That(descriptor).IsNotNull();
        await Assert.That(descriptor.Id).IsEqualTo(SyntheticDescriptorId);
        await Assert.That(descriptor.ItemId).IsEqualTo(SyntheticItemId);
        await Assert.That(descriptor.Delay).IsEqualTo(SyntheticDelay);
        await Assert.That(descriptor.OffsetX).IsEqualTo(SyntheticOffsetX);
        await Assert.That(descriptor.OffsetZ).IsEqualTo(SyntheticOffsetZ);
        await Assert.That(descriptor.Angle).IsEqualTo(SyntheticAngle);
    }

    [Test]
    public async Task LoadRejectsNullContentFields()
    {
        using var connection = CreateConnection();
        InsertRow(connection, SyntheticDescriptorId, null, SyntheticDelay,
            SyntheticOffsetX, SyntheticOffsetZ, SyntheticAngle);

        await Assert.That(() => DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadRejectsNegativeDelay()
    {
        using var connection = CreateConnection();
        InsertRow(connection, SyntheticDescriptorId, SyntheticItemId, -1,
            SyntheticOffsetX, SyntheticOffsetZ, SyntheticAngle);

        await Assert.That(() => DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadRejectsDuplicateDescriptorIds()
    {
        using var connection = CreateConnection();
        InsertRow(connection, SyntheticDescriptorId, SyntheticItemId, SyntheticDelay,
            SyntheticOffsetX, SyntheticOffsetZ, SyntheticAngle);
        InsertRow(connection, SyntheticDescriptorId, SyntheticItemId + 1, SyntheticDelay,
            SyntheticOffsetX, SyntheticOffsetZ, SyntheticAngle);

        await Assert.That(() => DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog.Load(connection))
            .Throws<InvalidDataException>();
    }

    [Test]
    public async Task LoadRejectsMissingContentTable()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await Assert.That(() => DoodadFuncSpawnSlaveAfterGetItemDescriptorCatalog.Load(connection))
            .Throws<InvalidDataException>();
    }

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE doodad_func_spawn_slave_after_get_items (
                id INTEGER NOT NULL,
                item_id INTEGER,
                delay INTEGER,
                offset_x REAL,
                offset_z REAL,
                angle REAL
            );
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static void InsertRow(
        SqliteConnection connection,
        uint id,
        uint? itemId,
        int delay,
        float offsetX,
        float offsetZ,
        float angle)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO doodad_func_spawn_slave_after_get_items
                (id, item_id, delay, offset_x, offset_z, angle)
            VALUES
                (@id, @itemId, @delay, @offsetX, @offsetZ, @angle);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@itemId", itemId is null ? DBNull.Value : itemId.Value);
        command.Parameters.AddWithValue("@delay", delay);
        command.Parameters.AddWithValue("@offsetX", offsetX);
        command.Parameters.AddWithValue("@offsetZ", offsetZ);
        command.Parameters.AddWithValue("@angle", angle);
        command.ExecuteNonQuery();
    }
}
