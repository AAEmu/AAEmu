using System.Reflection;

using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Xml;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj;

public class FishSchoolLookupTests
{
    [Test]
    public async Task IsSchool_Group65_IsTrue()
    {
        var school = new Doodad { Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.SportFishing } };
        var tree = new Doodad { Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.Deforestation } };
        await Assert.That(FishSchoolLookup.IsSchool(school)).IsTrue();
        await Assert.That(FishSchoolLookup.IsSchool(tree)).IsFalse();
        await Assert.That(FishSchoolLookup.IsSchool(null)).IsFalse();
    }

    [Test]
    public async Task IsPresent_RequiresALiveSchoolInItsWorld()
    {
        var school = new Doodad
        {
            Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.SportFishing },
            IsVisible = true
        };
        var tree = new Doodad
        {
            Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.Deforestation },
            IsVisible = true
        };

        await Assert.That(FishSchoolLookup.IsPresent(school)).IsFalse();
        await Assert.That(FishSchoolLookup.IsPresent(tree)).IsFalse();
        await Assert.That(FishSchoolLookup.IsPresent(null)).IsFalse();

        school.IsVisible = false;
        await Assert.That(FishSchoolLookup.IsPresent(school)).IsFalse();
    }

    [Test]
    public async Task IsPresent_TrueOnlyWhileTheWorldStillOwnsTheDoodad()
    {
        var world = new WorldInstance(new WorldTemplate
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
        }, 0, true, 14);

        var school = new Doodad
        {
            ObjId = 7,
            Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.SportFishing },
            IsVisible = true
        };
        typeof(GameObject)
            .GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(school, world);

        await Assert.That(FishSchoolLookup.IsPresent(school)).IsFalse();

        world.AddObject(school);
        await Assert.That(FishSchoolLookup.IsPresent(school)).IsTrue();

        world.RemoveObject(school);
        await Assert.That(FishSchoolLookup.IsPresent(school)).IsFalse();

        world.Dispose();
    }

    [Test]
    public async Task ReadActiveSpawnerId_IdlePhase_IsZero()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.SportFishing }
        };
        doodad.CurrentPhaseFuncs.Add(new DoodadPhaseFunc { FuncId = 1, FuncType = "DoodadFuncTimer" });

        var id = FishSchoolLookup.ReadActiveSpawnerId(doodad, (_, type) =>
            type == "DoodadFuncFishSchool" ? new DoodadFuncFishSchool { NpcSpawnerId = 17535 } : null);

        await Assert.That(id).IsEqualTo(0u);
    }

    [Test]
    public async Task ReadActiveSpawnerId_ChummedPhase_ReturnsSpawner()
    {
        var doodad = new Doodad
        {
            Template = new DoodadTemplate { GroupId = (uint)DoodadGroupId.SportFishing }
        };
        doodad.CurrentPhaseFuncs.Add(new DoodadPhaseFunc { FuncId = 29, FuncType = "DoodadFuncFishSchool" });

        var id = FishSchoolLookup.ReadActiveSpawnerId(doodad, (_, type) =>
            type == "DoodadFuncFishSchool" ? new DoodadFuncFishSchool { NpcSpawnerId = 17535 } : null);

        await Assert.That(id).IsEqualTo(17535u);
    }

    [Test]
    public async Task ResolveNearestSpawnerId_PicksClosestChummedSchool()
    {
        var schools = new (float X, float Y, uint SpawnerId)[]
        {
            (0f, 0f, 0),       // idle, ignored
            (40f, 0f, 17534),  // farther chummed
            (10f, 0f, 17535),  // nearer chummed
        };

        var id = FishSchoolLookup.ResolveNearestSpawnerId(schools, originX: 0f, originY: 0f, rangeMeters: 50f);
        await Assert.That(id).IsEqualTo(17535u);
    }

    [Test]
    public async Task ResolveNearestSpawnerId_OutOfRange_IsZero()
    {
        var schools = new (float X, float Y, uint SpawnerId)[] { (100f, 0f, 17535) };
        var id = FishSchoolLookup.ResolveNearestSpawnerId(schools, 0f, 0f, 50f);
        await Assert.That(id).IsEqualTo(0u);
    }

    [Test]
    public async Task SelectWeighted_RespectsWeights()
    {
        var npcs = new List<NpcSpawnerNpc>
        {
            new() { MemberId = 1, Weight = 1f },
            new() { MemberId = 2, Weight = 3f },
        };

        await Assert.That(FishSchoolLookup.SelectWeighted(npcs, 0.0)?.MemberId).IsEqualTo(1u);
        await Assert.That(FishSchoolLookup.SelectWeighted(npcs, 0.24)?.MemberId).IsEqualTo(1u);
        await Assert.That(FishSchoolLookup.SelectWeighted(npcs, 0.25)?.MemberId).IsEqualTo(2u);
        await Assert.That(FishSchoolLookup.SelectWeighted(npcs, 0.99)?.MemberId).IsEqualTo(2u);
    }

    [Test]
    public async Task SelectWeighted_Empty_IsNull()
    {
        await Assert.That(FishSchoolLookup.SelectWeighted([], 0.5)).IsNull();
    }
}
