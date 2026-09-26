using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

public sealed class SkillContentFieldAuditTests
{
    [Test]
    public async Task ReaderMapsAllFourContentFieldsWithoutDefaults()
    {
        using var connection = CreateDb(
            "CREATE TABLE skills (id INTEGER NOT NULL, valid_height_edge_to_edge BOOLEAN NULL, " +
            "link_equip_slot_id INTEGER NULL, auto_fire BOOLEAN NULL, sensitive_operation BOOLEAN NULL);",
            "INSERT INTO skills VALUES (7, 't', 15, 't', 'f');");
        var template = new SkillTemplate();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM skills";
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            await Assert.That(reader.Read()).IsTrue();
            SkillContentFieldReader.Apply(reader, template);
        }

        await Assert.That(template.ValidHeightEdgeToEdge).IsTrue();
        await Assert.That(template.LinkEquipSlotId).IsEqualTo(15);
        await Assert.That(template.AutoFire).IsTrue();
        await Assert.That(template.SensitiveOperation).IsFalse();
    }

    [Test]
    public async Task ReaderRejectsNullContentValues()
    {
        using var connection = CreateDb(
            "CREATE TABLE skills (id INTEGER NOT NULL, valid_height_edge_to_edge BOOLEAN NULL, " +
            "link_equip_slot_id INTEGER NULL, auto_fire BOOLEAN NULL, sensitive_operation BOOLEAN NULL);",
            "INSERT INTO skills VALUES (7, 't', 15, 't', NULL);");
        var template = new SkillTemplate();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM skills";
        using var sqliteReader = command.ExecuteReader();
        using var reader = new SQLiteWrapperReader(sqliteReader);
        await Assert.That(reader.Read()).IsTrue();
        var exception = Assert.Throws<InvalidDataException>(() => SkillContentFieldReader.Apply(reader, template));
        await Assert.That(exception.Message).Contains("sensitive_operation");
    }

    [Test]
    public async Task AuditCountsFlagsAndResolvesCatalogLinksDeterministically()
    {
        using var connection = CreateDb(
            "CREATE TABLE enum_equip_slot (id INTEGER NOT NULL, name TEXT NOT NULL, category TEXT NULL);",
            "INSERT INTO enum_equip_slot VALUES (-7, 'invalid', NULL);",
            "INSERT INTO enum_equip_slot VALUES (5, 'hands', 'armor');");
        var catalog = new SkillEquipSlotCatalog();
        catalog.Load(connection);
        var skills = new[]
        {
            new SkillTemplate { Id = 30, LinkEquipSlotId = 5, AutoFire = true },
            new SkillTemplate { Id = 10, LinkEquipSlotId = -7, SensitiveOperation = true },
            new SkillTemplate { Id = 20, LinkEquipSlotId = -7, ValidHeightEdgeToEdge = true },
            new SkillTemplate { Id = 40, LinkEquipSlotId = 99 }
        };

        var report = SkillContentFieldAudit.Build(skills, catalog);

        await Assert.That(report.TotalSkills).IsEqualTo(4);
        await Assert.That(report.AutoFireCount).IsEqualTo(1);
        await Assert.That(report.SensitiveOperationCount).IsEqualTo(1);
        await Assert.That(report.ValidHeightEdgeToEdgeCount).IsEqualTo(1);
        await Assert.That(report.LinkedSkillCount).IsEqualTo(1);
        await Assert.That(report.NoLinkSkillCount).IsEqualTo(2);
        await Assert.That(report.UnknownLinkSkillIds).IsEquivalentTo(new uint[] { 40 });
        await Assert.That(report.Links[0].SkillId).IsEqualTo(30u);
        await Assert.That(report.Links[0].EquipSlotName).IsEqualTo("hands");
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
