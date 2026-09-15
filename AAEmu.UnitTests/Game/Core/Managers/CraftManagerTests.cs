using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class CraftManagerTests
{
    [Test]
    public async Task LoadCraftPackMembership_PreservesExactRelationships()
    {
        using var connection = CreateDatabase();
        var manager = CreateManager(6211, 7777);

        manager.LoadCraftPackMembership(connection);

        await Assert.That(manager.IsCraftInPack(74, 6211)).IsTrue();
        await Assert.That(manager.IsCraftInPack(214, 7777)).IsTrue();
        await Assert.That(manager.IsCraftInPack(214, 6211)).IsFalse();
        await Assert.That(manager.GetCraftIdsForPack(74)).IsEquivalentTo(new uint[] { 6211 });
    }

    [Test]
    public async Task TryGetCraft_UnknownIdDoesNotThrow()
    {
        var manager = CreateManager(6211);

        var found = manager.TryGetCraft(999999, out var craft);

        await Assert.That(found).IsFalse();
        await Assert.That(craft).IsNull();
    }

    [Test]
    public async Task LoadCraftPackMembership_CollapsesDuplicateRelationships()
    {
        using var connection = CreateDatabase();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO craft_pack_crafts VALUES (74, 6211);";
            command.ExecuteNonQuery();
        }
        var manager = CreateManager(6211, 7777);

        manager.LoadCraftPackMembership(connection);

        await Assert.That(manager.GetCraftIdsForPack(74)).IsEquivalentTo(new uint[] { 6211 });
    }

    [Test]
    public async Task LoadCraftPackMembership_SkipsMissingCraftAndLoadsValidMemberships()
    {
        using var connection = CreateDatabase();
        var manager = CreateManager(6211);

        manager.LoadCraftPackMembership(connection);

        await Assert.That(manager.IsCraftInPack(74, 6211)).IsTrue();
        await Assert.That(manager.IsCraftInPack(214, 7777)).IsFalse();
        await Assert.That(manager.GetCraftIdsForPack(214)).IsEmpty();
    }

    private static CraftManager CreateManager(params uint[] craftIds)
    {
        var manager = new CraftManager();
        SetPrivateField(
            manager,
            "_crafts",
            craftIds.ToDictionary(id => id, id => new Craft { Id = id }));
        return manager;
    }

    private static SqliteConnection CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE craft_pack_crafts (craft_pack_id INTEGER NOT NULL, craft_id INTEGER NOT NULL);
            INSERT INTO craft_pack_crafts VALUES (15, 5515);
            INSERT INTO craft_pack_crafts VALUES (74, 6211);
            INSERT INTO craft_pack_crafts VALUES (214, 7777);
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static void SetPrivateField<T>(object target, string fieldName, T value)
    {
        target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    }
}
