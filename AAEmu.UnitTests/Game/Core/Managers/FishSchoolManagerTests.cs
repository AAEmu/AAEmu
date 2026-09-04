using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;

namespace AAEmu.UnitTests.Game.Core.Managers;

[NotInParallel]
public class FishSchoolManagerTests
{
    [Test]
    public async Task GetAllFishSchools_SkipsDeletedAndForeignTemplates()
    {
        FishSchoolManager.Instance.Initialize();
        try
        {
            var world = NewWorld(11);
            var live = School(1, visible: true);
            var hidden = School(2, visible: false);
            var tree = new Doodad
            {
                ObjId = 3,
                Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.Deforestation },
                IsVisible = true
            };
            Attach(live, world);
            Attach(hidden, world);
            Attach(tree, world);
            world.AddObject(live);
            world.AddObject(hidden);
            world.AddObject(tree);

            FishSchoolManager.Instance.Track(live);
            FishSchoolManager.Instance.Track(hidden);
            FishSchoolManager.Instance.Track(tree);

            var listed = FishSchoolManager.Instance.GetAllFishSchools();
            await Assert.That(listed).Contains(live);
            await Assert.That(listed).DoesNotContain(hidden);
            await Assert.That(listed).DoesNotContain(tree);

            world.RemoveObject(live);
            listed = FishSchoolManager.Instance.GetAllFishSchools();
            await Assert.That(listed).DoesNotContain(live);

            world.Dispose();
        }
        finally
        {
            FishSchoolManager.Instance.Initialize();
        }
    }

    [Test]
    public async Task Untrack_DropsASchoolFromRadar()
    {
        FishSchoolManager.Instance.Initialize();
        try
        {
            var world = NewWorld(12);
            var school = School(4, visible: true);
            Attach(school, world);
            world.AddObject(school);
            FishSchoolManager.Instance.Track(school);

            await Assert.That(FishSchoolManager.Instance.GetAllFishSchools()).Contains(school);

            FishSchoolManager.Instance.Untrack(school);
            await Assert.That(FishSchoolManager.Instance.GetAllFishSchools()).DoesNotContain(school);

            world.Dispose();
        }
        finally
        {
            FishSchoolManager.Instance.Initialize();
        }
    }

    [Test]
    public async Task Load_ReplacesLeftoverPinsForThatWorld()
    {
        FishSchoolManager.Instance.Initialize();
        try
        {
            var world = NewWorld(13);
            var leftover = School(5, visible: true);
            var live = School(6, visible: true);
            Attach(leftover, world);
            Attach(live, world);
            world.AddObject(live);
            FishSchoolManager.Instance.Track(leftover);

            FishSchoolManager.Instance.Load(world);

            var listed = FishSchoolManager.Instance.GetAllFishSchools();
            await Assert.That(listed).Contains(live);
            await Assert.That(listed).DoesNotContain(leftover);

            world.Dispose();
        }
        finally
        {
            FishSchoolManager.Instance.Initialize();
        }
    }

    private static Doodad School(uint objId, bool visible) =>
        new()
        {
            ObjId = objId,
            Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.SportFishing },
            IsVisible = visible
        };

    private static void Attach(Doodad doodad, WorldInstance world)
    {
        typeof(GameObject)
            .GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(doodad, world);
    }

    private static WorldInstance NewWorld(uint id) =>
        new(new WorldTemplate
        {
            CellX = 1,
            CellY = 1,
            Cells = new WorldCell[0, 0],
            HousingZones = [],
            Id = 0,
            Name = "test_world",
            OceanLevel = 100f,
            SubZones = [],
            XmlWorld = new XmlWorld { Zones = [] },
            XmlWorldZones = [],
            ZoneKeyByRegions = new uint[1, 1],
            ZoneKeys = [0]
        }, 0, true, id);
}
