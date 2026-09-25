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

    [Test]
    public async Task LoadCraftMetadata_JoinsLinesCategoriesAndPacks()
    {
        using var connection = CreateMetadataDatabase();
        var manager = CreateManager();
        manager.LoadCrafts(connection);
        manager.LoadCraftMetadata(connection);
        manager.LoadCraftPackMembership(connection);

        await Assert.That(manager.GetCraftById(100).UseOnlyActability).IsTrue();
        await Assert.That(manager.GetCraftById(100).ProductPackId).IsEqualTo(777u);
        await Assert.That(manager.GetCraftById(100).CraftCCategoryId).IsEqualTo(30u);
        await Assert.That(manager.GetCraftById(100).CraftDCategoryId).IsEqualTo(40u);
        await Assert.That(manager.TryGetCraftLine(50, out var line)).IsTrue();
        await Assert.That(line.Name).IsEqualTo("Line");
        await Assert.That(line.Components.Select(component => component.CraftId)).IsEquivalentTo(new uint[] { 100 });
        await Assert.That(manager.GetCraftIdsForLine(50)).IsEquivalentTo(new uint[] { 100 });
        await Assert.That(manager.GetCraftIdsForCategory(CraftCategoryLevel.A, 10)).IsEquivalentTo(new uint[] { 100, 101 });
        await Assert.That(manager.GetCraftIdsForCategory(CraftCategoryLevel.C, 30)).IsEquivalentTo(new uint[] { 100, 101 });
        await Assert.That(manager.GetCraftIdsForCategory(CraftCategoryLevel.D, 40)).IsEquivalentTo(new uint[] { 100 });
        await Assert.That(manager.TryGetCraftCategory(CraftCategoryLevel.A, 10, out var categoryA)).IsTrue();
        await Assert.That(categoryA.ChildIds).Contains(20);
        await Assert.That(manager.TryGetCraftPack(5, out var pack)).IsTrue();
        await Assert.That(pack.CraftIds).IsEquivalentTo(new uint[] { 100 });
        await Assert.That(manager.GetUnresolvedProductPackIds()).IsEquivalentTo(new uint[] { 777 });
    }

    [Test]
    public async Task LoadCraftMetadata_RetainsMismatchedCAndDReferences()
    {
        using var connection = CreateMetadataDatabase();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO craft_c_categories VALUES (31, 'C2', 2, 20, 0); UPDATE craft_d_categories SET craft_c_category_id = 31 WHERE id = 40;";
            command.ExecuteNonQuery();
        }
        var manager = CreateManager();
        manager.LoadCrafts(connection);

        manager.LoadCraftMetadata(connection);

        var mismatches = manager.GetCraftCategoryMismatches();
        await Assert.That(mismatches).HasCount().EqualTo(1);
        var mismatch = mismatches.Single();
        await Assert.That(mismatch.CraftId).IsEqualTo(100u);
        await Assert.That(mismatch.CraftCCategoryId).IsEqualTo(30u);
        await Assert.That(mismatch.CraftDCategoryId).IsEqualTo(40u);
        await Assert.That(mismatch.DCategoryParentId).IsEqualTo(31u);
    }

    [Test]
    public void LoadCraftMetadata_InvalidCategoryParentFailsLoudly()
    {
        using var connection = CreateMetadataDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE craft_b_categories SET craft_a_category_id = 999 WHERE id = 20";
        command.ExecuteNonQuery();
        var manager = CreateManager(100);

        Assert.Throws<InvalidDataException>(() => manager.LoadCraftMetadata(connection));
    }

    [Test]
    public void LoadCraftMetadata_OrphanLineComponentFailsLoudly()
    {
        using var connection = CreateMetadataDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE craft_line_components SET craft_id = 999 WHERE id = 1";
        command.ExecuteNonQuery();
        var manager = CreateManager(100);

        Assert.Throws<InvalidDataException>(() => manager.LoadCraftMetadata(connection));
    }

    [Test]
    public async Task LoadCraftPackMembership_ReportsMissingCatalogPack()
    {
        using var connection = CreateMetadataDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO craft_pack_crafts VALUES (2, 6, 101)";
        command.ExecuteNonQuery();
        var manager = CreateManager(100, 101);
        manager.LoadCraftMetadata(connection);

        manager.LoadCraftPackMembership(connection);

        await Assert.That(manager.GetUnresolvedCraftPackIds()).IsEquivalentTo(new uint[] { 6 });
        await Assert.That(manager.IsCraftInPack(6, 101)).IsTrue();
    }

    private static SqliteConnection CreateMetadataDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE crafts (
                id INTEGER PRIMARY KEY,
                cast_delay INTEGER,
                skill_id INTEGER,
                wi_id INTEGER,
                milestone_id INTEGER,
                req_doodad_id INTEGER,
                actability_limit INTEGER,
                recommend_level INTEGER,
                visible_order INTEGER,
                orderable INTEGER,
                cost INTEGER,
                use_only_actability INTEGER,
                products_pack_id INTEGER,
                craft_c_category_id INTEGER,
                craft_d_category_id INTEGER
            );
            INSERT INTO crafts VALUES (100, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 777, 30, 40);
            INSERT INTO crafts VALUES (101, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 30, NULL);
            CREATE TABLE craft_a_categories (id INTEGER, name TEXT, ui_order INTEGER, btn_deco_key TEXT, child_file_path TEXT, represented_child_count INTEGER, visible INTEGER);
            INSERT INTO craft_a_categories VALUES (10, 'A', 1, 'a', 'a', 1, 1);
            CREATE TABLE craft_b_categories (id INTEGER, name TEXT, ui_order INTEGER, craft_a_category_id INTEGER, btn_deco_key TEXT, desc TEXT);
            INSERT INTO craft_b_categories VALUES (20, 'B', 1, 10, 'b', 'B description');
            CREATE TABLE craft_c_categories (id INTEGER, name TEXT, ui_order INTEGER, craft_b_category_id INTEGER, use_only_doodad INTEGER);
            INSERT INTO craft_c_categories VALUES (30, 'C', 1, 20, 0);
            CREATE TABLE craft_d_categories (id INTEGER, name TEXT, ui_order INTEGER, craft_c_category_id INTEGER, use_only_doodad INTEGER);
            INSERT INTO craft_d_categories VALUES (40, 'D', 1, 30, 0);
            CREATE TABLE craft_lines (id INTEGER, name TEXT, desc TEXT);
            INSERT INTO craft_lines VALUES (50, 'Line', 'Line description');
            CREATE TABLE craft_line_components (id INTEGER, craft_id INTEGER, craft_line_id INTEGER, rank INTEGER);
            INSERT INTO craft_line_components VALUES (1, 100, 50, 1);
            CREATE TABLE craft_packs (id INTEGER, name TEXT);
            INSERT INTO craft_packs VALUES (5, 'Pack');
            CREATE TABLE craft_pack_crafts (id INTEGER, craft_pack_id INTEGER, craft_id INTEGER);
            INSERT INTO craft_pack_crafts VALUES (1, 5, 100);
            """;
        command.ExecuteNonQuery();
        return connection;
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
