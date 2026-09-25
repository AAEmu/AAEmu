using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public sealed class SkillEquipSlotCatalogTests
{
    [Test]
    public async Task NoLinkSentinelComesFromTheCatalogRow()
    {
        using var connection = CreateDb(
            "CREATE TABLE enum_equip_slot (id INTEGER NOT NULL, name TEXT NOT NULL, category TEXT NULL);",
            "INSERT INTO enum_equip_slot VALUES (-7, 'invalid', NULL);",
            "INSERT INTO enum_equip_slot VALUES (5, 'hands', 'armor');");
        var catalog = new SkillEquipSlotCatalog();

        catalog.Load(connection);

        await Assert.That(catalog.NoLinkSlotId).IsEqualTo(-7);
        await Assert.That(catalog.Get(5).Name).IsEqualTo("hands");
        await Assert.That(catalog.Get(-7).IsNoLink).IsTrue();
    }

    [Test]
    public async Task MissingNoLinkCatalogRowFailsLoudly()
    {
        using var connection = CreateDb(
            "CREATE TABLE enum_equip_slot (id INTEGER NOT NULL, name TEXT NOT NULL, category TEXT NULL);",
            "INSERT INTO enum_equip_slot VALUES (5, 'hands', 'armor');");
        var catalog = new SkillEquipSlotCatalog();

        var exception = Assert.Throws<InvalidOperationException>(() => catalog.Load(connection));

        await Assert.That(exception.Message).Contains(SkillEquipSlotCatalog.NoLinkSlotName);
    }

    [Test]
    public async Task DuplicateCatalogNamesFailLoudly()
    {
        using var connection = CreateDb(
            "CREATE TABLE enum_equip_slot (id INTEGER NOT NULL, name TEXT NOT NULL, category TEXT NULL);",
            "INSERT INTO enum_equip_slot VALUES (-7, 'invalid', NULL);",
            "INSERT INTO enum_equip_slot VALUES (5, 'hands', 'armor');",
            "INSERT INTO enum_equip_slot VALUES (6, 'hands', 'armor');");
        var catalog = new SkillEquipSlotCatalog();

        Assert.Throws<InvalidDataException>(() => catalog.Load(connection));
    }

    [Test]
    public async Task UnknownSkillLinkFailsLoudly()
    {
        using var connection = CreateDb(
            "CREATE TABLE enum_equip_slot (id INTEGER NOT NULL, name TEXT NOT NULL, category TEXT NULL);",
            "INSERT INTO enum_equip_slot VALUES (-7, 'invalid', NULL);",
            "INSERT INTO enum_equip_slot VALUES (5, 'hands', 'armor');");
        var catalog = new SkillEquipSlotCatalog();
        catalog.Load(connection);
        var skills = new[]
        {
            new SkillTemplate { Id = 10, LinkEquipSlotId = -7 },
            new SkillTemplate { Id = 11, LinkEquipSlotId = 5 }
        };

        catalog.ValidateSkillLinks(skills);

        var unknown = new[] { new SkillTemplate { Id = 12, LinkEquipSlotId = 99 } };
        var exception = Assert.Throws<InvalidDataException>(() => catalog.ValidateSkillLinks(unknown));
        await Assert.That(exception.Message).Contains("99");
    }

    private static SqliteConnection CreateDb(params string[] statements)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        foreach (var statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return connection;
    }
}
