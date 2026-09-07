using System.Reflection;

using AAEmu.Commons.Exceptions;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class DoodadGroupLoadingTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE doodad_groups (
                id INTEGER PRIMARY KEY,
                guard_on_field_time INTEGER NOT NULL,
                is_export INTEGER NOT NULL,
                removed_by_house INTEGER NOT NULL
            );
            INSERT INTO doodad_groups (id, guard_on_field_time, is_export, removed_by_house)
            VALUES (0, 0, 0, 0), (7, 3600, 1, 0), (8, 172800, 0, 1);

            CREATE TABLE doodad_almighties (
                id INTEGER PRIMARY KEY,
                group_id INTEGER NOT NULL,
                model_kind_id INTEGER NOT NULL DEFAULT 0,
                faction_id INTEGER NOT NULL DEFAULT 0,
                once_one_man INTEGER, once_one_interaction INTEGER, mgmt_spawn INTEGER,
                percent INTEGER, min_time INTEGER, max_time INTEGER,
                use_creator_faction INTEGER, force_tod_top_priority INTEGER, milestone_id INTEGER,
                use_target_decal INTEGER, use_target_silhouette INTEGER, use_target_highlight INTEGER,
                target_decal_size REAL, sim_radius INTEGER, collide_ship INTEGER, collide_vehicle INTEGER,
                climate_id INTEGER, save_indun INTEGER, force_up_action INTEGER,
                parentable INTEGER, childable INTEGER, growth_time INTEGER,
                despawn_on_collision INTEGER, no_collision INTEGER, restrict_zone_id INTEGER
            );
            INSERT INTO doodad_almighties (id, group_id) VALUES (100, 0), (101, 7), (102, 8);
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    [Arguments(100u, 0u, 0u, false, false)]
    [Arguments(101u, 7u, 3600u, true, false)]
    [Arguments(102u, 8u, 172800u, false, true)]
    public async Task LoadDoodadTemplates_ExistingGroups_PublishesTemplatesWithMetadata(
        uint templateId, uint groupId, uint guardTime, bool isExport, bool removedByHouse)
    {
        var manager = CreateManager();

        manager.LoadDoodadTemplates(Connection);

        var template = manager.GetTemplate(templateId);
        await Assert.That(template).IsNotNull();
        await Assert.That(template.GroupId).IsEqualTo(groupId);
        await Assert.That(template.Group).IsNotNull();
        await Assert.That(template.Group.Id).IsEqualTo(groupId);
        await Assert.That(template.Group.GuardOnFieldTime).IsEqualTo(guardTime);
        await Assert.That(template.Group.IsExport).IsEqualTo(isExport);
        await Assert.That(template.Group.RemovedByHouse).IsEqualTo(removedByHouse);
    }

    [Test]
    public async Task LoadDoodadTemplates_MissingGroup_RejectsTemplateWithIdentifiableError()
    {
        using var command = Connection.CreateCommand();
        command.CommandText = "UPDATE doodad_almighties SET group_id = 999 WHERE id = 101";
        command.ExecuteNonQuery();
        var manager = CreateManager();

        var error = Assert.Throws<GameException>(() => manager.LoadDoodadTemplates(Connection));

        await Assert.That(error.Message).Contains("999");
        await Assert.That(error.Message).Contains("101");
        await Assert.That(manager.GetTemplate(101)).IsNull();
    }

    private static DoodadManager CreateManager()
    {
        var manager = new DoodadManager(
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<IDoodadIdManager>().Object,
            Mock.Of<IItemManager>().Object,
            new Lazy<IHousingManager>(() => Mock.Of<IHousingManager>().Object),
            Mock.Of<ISusManager>().Object);
        // Earlier loading phases initialize these collections. No doodad functions are needed here.
        SetField(manager, "_templates", new Dictionary<uint, DoodadTemplate>());
        SetField(manager, "_allFuncGroups", new Dictionary<uint, DoodadFuncGroups>());
        return manager;
    }

    private static void SetField(DoodadManager manager, string name, object value)
    {
        var field = typeof(DoodadManager).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(DoodadManager).FullName, name);
        field.SetValue(manager, value);
    }
}
