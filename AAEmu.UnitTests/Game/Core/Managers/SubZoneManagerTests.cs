using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using Jitter2.LinearMath;

namespace AAEmu.UnitTests.Game.Core.Managers;

public class SubZoneManagerTests
{
    [Test]
    public void Load_CallsGetWorlds()
    {
        var mockWorld = Mock.Of<IWorldManager>();
        mockWorld.GetAllWorldTemplates().Returns([]);
        var manager = new SubZoneManager(mockWorld.Object, Mock.Of<IZoneManager>().Object);
        manager.Load();

        mockWorld.GetAllWorldTemplates().WasCalled(Times.Once);
    }

    [Test]
    public async Task GetHousingZoneByPosition_DifferentZoneKeyAndId_UsesDatabaseId()
    {
        var template = new WorldTemplate();
        template.HousingZones[2] = [Square(7)];
        // A different zone's row ID may equal this zone's key. Never select that area.
        template.HousingZones[133] = [Square(99)];
        var result = FindHousingAreas(template, new Zone { Id = 2, ZoneKey = 133 }, 5, 5);
        await Assert.That(result.SequenceEqual(new uint[] { 7 })).IsTrue();
    }

    [Test]
    public async Task GetHousingZoneByPosition_EqualZoneKeyAndId_PreservesAreaResult()
    {
        var template = new WorldTemplate();
        template.HousingZones[133] = [Square(7)];
        var result = FindHousingAreas(template, new Zone { Id = 133, ZoneKey = 133 }, 5, 5);
        await Assert.That(result.SequenceEqual(new uint[] { 7 })).IsTrue();
    }

    [Test]
    public async Task GetHousingZoneByPosition_MissingZone_ReturnsEmpty()
    {
        var result = FindHousingAreas(new WorldTemplate(), null, 5, 5);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetHousingZoneByPosition_MissingHousingAreas_ReturnsEmpty()
    {
        var result = FindHousingAreas(new WorldTemplate(), new Zone { Id = 2, ZoneKey = 133 }, 5, 5);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    [Test]
    public async Task GetHousingZoneByPosition_OutsidePolygon_ReturnsEmpty()
    {
        var template = new WorldTemplate();
        template.HousingZones[2] = [Square(7)];
        var result = FindHousingAreas(template, new Zone { Id = 2, ZoneKey = 133 }, 15, 15);
        await Assert.That(result.Count).IsEqualTo(0);
    }

    private static Area Square(uint id)
    {
        return new Area
        {
            Id = id,
            Points = [new JVector(0, 0, 0), new JVector(10, 0, 0), new JVector(10, 10, 0), new JVector(0, 10, 0)]
        };
    }

    private static List<uint> FindHousingAreas(WorldTemplate template, Zone zone, float x, float y)
    {
        var mockWorld = Mock.Of<IWorldManager>();
        mockWorld.GetZoneId(template, x, y).Returns(133u);
        var mockZone = Mock.Of<IZoneManager>();
        mockZone.GetZoneByKey(133u).Returns(zone);
        var manager = new SubZoneManager(mockWorld.Object, mockZone.Object);
        var world = new WorldInstance(template, 0, true, 0);
        GC.SuppressFinalize(world);
        return manager.GetHousingZoneByPosition(world, x, y);
    }
}
