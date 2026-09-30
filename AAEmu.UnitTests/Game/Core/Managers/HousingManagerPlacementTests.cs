using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The placement checks Build runs before charging for a new house. The client chooses the spot, so
/// without these a house could be planted on top of another one or outside every housing area.
/// </summary>
public sealed class HousingManagerPlacementTests : IDisposable
{
    private const uint ZoneKey = 179;

    // Scarecrow Garden (housing_sizes 2, radius 4) and a small cottage (housing_sizes 6, radius 7.5).
    private static readonly HousingTemplate Garden = Template(267, sizeId: 2, radius: 4f);
    private static readonly HousingTemplate Cottage = Template(175, sizeId: 6, radius: 7.5f);

    private readonly WorldInstance _world = World(31);
    private readonly WorldInstance _otherWorld = World(32);
    private readonly HousingManager _manager = CreateManager();

    public void Dispose()
    {
        _world.Dispose();
        _otherWorld.Dispose();
    }

    [Test]
    public async Task SpotOnAnExistingHouse_IsRefused()
    {
        Houses(House(_world, 1, Garden, 15352f, 14640f));

        var error = _manager.CheckPlacement(_world, ZoneKey, Garden, 15352f, 14640f, 0f, false);

        await Assert.That(error).IsEqualTo(ErrorMessageType.HouseCannotLocateOverlapHouse);
    }

    [Test]
    public async Task PlotReachingIntoANeighbour_IsRefused()
    {
        // Garden spans 15348..15356; a cottage at 15342 would reach 15349.5.
        Houses(House(_world, 1, Garden, 15352f, 14640f));

        var error = _manager.CheckPlacement(_world, ZoneKey, Cottage, 15342f, 14640f, 0f, false);

        await Assert.That(error).IsEqualTo(ErrorMessageType.HouseCannotLocateOverlapHouse);
    }

    [Test]
    public async Task NeighbourSharingAnEdge_IsAccepted()
    {
        // Garden spans 15348..15356, the second garden 15340..15348.
        Houses(House(_world, 1, Garden, 15352f, 14640f));

        var error = _manager.CheckPlacement(_world, ZoneKey, Garden, 15344f, 14640f, 0f, false);

        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task RotatedPlotWhoseCornerReachesANeighbour_IsRefused()
    {
        // Axis-aligned the two gardens would be 1 m apart. Turned 45 degrees, the new one reaches
        // 4 * sqrt(2) = 5.66 m from its centre and crosses into the neighbour.
        Houses(House(_world, 1, Garden, 15352f, 14640f));

        var aligned = _manager.CheckPlacement(_world, ZoneKey, Garden, 15343f, 14640f, 0f, false);
        var turned = _manager.CheckPlacement(_world, ZoneKey, Garden, 15343f, 14640f, MathF.PI / 4f, false);

        await Assert.That(aligned).IsNull();
        await Assert.That(turned).IsEqualTo(ErrorMessageType.HouseCannotLocateOverlapHouse);
    }

    [Test]
    public async Task HouseInAnotherWorldInstance_DoesNotBlock()
    {
        Houses(House(_otherWorld, 1, Garden, 15352f, 14640f));

        var error = _manager.CheckPlacement(_world, ZoneKey, Garden, 15352f, 14640f, 0f, false);

        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task ZoneWithHousingAreas_RefusesASpotOutsideThem()
    {
        Houses();
        _world.Template.HousingZones[ZoneKey] = [Square(15330f, 14620f, 15370f, 14660f)];

        var inside = _manager.CheckPlacement(_world, ZoneKey, Garden, 15352f, 14640f, 0f, false);
        var outside = _manager.CheckPlacement(_world, ZoneKey, Garden, 15400f, 14640f, 0f, false);

        await Assert.That(inside).IsNull();
        await Assert.That(outside).IsEqualTo(ErrorMessageType.HouseCannotLocateInvalidArea);
    }

    [Test]
    public async Task ZoneWithoutLoadedHousingAreas_IsNotJudgedOnArea()
    {
        Houses();

        var error = _manager.CheckPlacement(_world, ZoneKey, Garden, 15400f, 14640f, 0f, false);

        await Assert.That(error).IsNull();
    }

    [Test]
    public async Task TerritoryDesign_IsLeftToTheClaimRules()
    {
        Houses(House(_world, 1, Garden, 15352f, 14640f));
        _world.Template.HousingZones[ZoneKey] = [Square(0f, 0f, 10f, 10f)];

        var error = _manager.CheckPlacement(_world, ZoneKey, Garden, 15352f, 14640f, 0f, true);

        await Assert.That(error).IsNull();
    }

    private void Houses(params House[] houses)
    {
        var map = houses.ToDictionary(house => house.Id);
        typeof(HousingManager).GetField("_houses", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_manager, map);
    }

    private static HousingTemplate Template(uint id, uint sizeId, float radius) =>
        new() { Id = id, HousingSize = new HousingSize { Id = sizeId, GardenRadius = radius } };

    private static WorldInstance World(uint instanceId) =>
        new(new WorldTemplate { Name = "placement_world_" + instanceId }, 0, true, instanceId);

    private static House House(WorldInstance world, uint id, HousingTemplate template, float x, float y)
    {
        var house = new House { Id = id, Template = template };
        house.Transform.Local.SetPosition(x, y, 170f);
        typeof(GameObject).GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(house, world);
        return house;
    }

    private static Area Square(float minX, float minY, float maxX, float maxY) => new()
    {
        Name = "LevelDesignShape_test",
        _points =
        [
            new Point(minX, minY, 0f),
            new Point(maxX, minY, 0f),
            new Point(maxX, maxY, 0f),
            new Point(minX, maxY, 0f),
        ],
    };

    private static HousingManager CreateManager() => new(
        Mock.Of<IObjectIdManager>().Object,
        Mock.Of<IFactionManager>().Object,
        Mock.Of<ILocalizationManager>().Object,
        Mock.Of<IWorldManager>().Object,
        Mock.Of<ITaskManager>().Object,
        Mock.Of<ISkillManager>().Object,
        Mock.Of<IHousingIdManager>().Object,
        Mock.Of<IHousingTldManager>().Object,
        Mock.Of<IItemManager>().Object,
        Mock.Of<IMailManager>().Object,
        Mock.Of<INameManager>().Object,
        Mock.Of<IZoneManager>().Object,
        Mock.Of<IDoodadManager>().Object,
        Mock.Of<IUccManager>().Object,
        Mock.Of<IButlerManager>().Object,
        Mock.Of<IDominionManager>().Object,
        Mock.Of<IGuildDominionManager>().Object);
}
