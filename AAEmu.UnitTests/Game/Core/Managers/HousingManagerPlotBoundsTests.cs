using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The housing plot lookup the furniture/backpack placement paths use. The plot is the
/// housing_sizes.garden_radius square rotated by the house yaw, so lookups must accept points the
/// old axis-aligned test rejected and vice versa, and a house whose content row is missing must
/// loudly claim nothing instead of silently falling back to a zero radius.
/// </summary>
public class HousingManagerPlotBoundsTests
{
    private const float Radius = 10f;
    private const float HouseX = 100f;
    private const float HouseY = 200f;

    [Test]
    public async Task GetHouseAtLocation_UnrotatedPlot_AcceptsInteriorRejectsOutside()
    {
        var manager = CreateManager();
        var world = CreateWorld();
        var house = CreateHouse(world, 1, HouseX, HouseY);
        SetHouses(manager, house);

        await Assert.That(manager.GetHouseAtLocation(world, 108f, 203f)).IsSameReferenceAs(house);
        await Assert.That(manager.GetHouseAtLocation(world, 111f, 200f)).IsNull();
    }

    [Test]
    public async Task GetHouseAtLocation_CardinalRotations_90_180_270_AcceptAndRejectConsistently()
    {
        var manager = CreateManager();
        var world = CreateWorld();
        var house = CreateHouse(world, 1, HouseX, HouseY);
        SetHouses(manager, house);

        house.Transform.Local.SetRotation(0f, 0f, MathF.PI / 2f);
        await Assert.That(manager.GetHouseAtLocation(world, 97f, 208f)).IsSameReferenceAs(house);
        await Assert.That(manager.GetHouseAtLocation(world, 89f, 200f)).IsNull();

        house.Transform.Local.SetRotation(0f, 0f, MathF.PI);
        await Assert.That(manager.GetHouseAtLocation(world, 92f, 197f)).IsSameReferenceAs(house);
        await Assert.That(manager.GetHouseAtLocation(world, 89f, 200f)).IsNull();

        house.Transform.Local.SetRotation(0f, 0f, 3f * MathF.PI / 2f);
        await Assert.That(manager.GetHouseAtLocation(world, 103f, 192f)).IsSameReferenceAs(house);
        await Assert.That(manager.GetHouseAtLocation(world, 100f, 189f)).IsNull();
    }

    [Test]
    public async Task GetHouseAtLocation_ArbitraryYaw_MatchesTheRotatedPlotNotTheAxisAlignedSquare()
    {
        var manager = CreateManager();
        var world = CreateWorld();
        var house = CreateHouse(world, 1, HouseX, HouseY);
        SetHouses(manager, house);

        house.Transform.Local.SetRotation(0f, 0f, MathF.PI / 4f);

        // Corner of the unrotated square: outside the 45-degree rotated plot.
        await Assert.That(manager.GetHouseAtLocation(world, 109f, 209f)).IsNull();
        // Diagonal reach past the axis-aligned square but inside the rotated plot.
        await Assert.That(manager.GetHouseAtLocation(world, 113f, 200f)).IsSameReferenceAs(house);
    }

    [Test]
    public async Task GetHouseAtLocation_MissingHousingContent_MatchesNothingInsteadOfFallingBack()
    {
        var manager = CreateManager();
        var world = CreateWorld();

        // No template at all, and a template whose housing_sizes row never resolved: both used to
        // degrade to radius 0 through `?? 0f`; now they log loudly and claim nothing.
        var noTemplate = new House { Id = 1 };
        noTemplate.Transform.Local.SetPosition(300f, 400f, 0f);
        SetParentWorld(noTemplate, world);

        var noSize = new House { Id = 2, Template = new HousingTemplate() };
        noSize.Transform.Local.SetPosition(500f, 600f, 0f);
        SetParentWorld(noSize, world);

        var healthy = CreateHouse(world, 3, HouseX, HouseY);
        SetHouses(manager, noTemplate, noSize, healthy);

        await Assert.That(manager.GetHouseAtLocation(world, 300f, 400f)).IsNull();
        await Assert.That(manager.GetHouseAtLocation(world, 500f, 600f)).IsNull();
        // The healthy neighbour is unaffected by its broken siblings.
        await Assert.That(manager.GetHouseAtLocation(world, 105f, 205f)).IsSameReferenceAs(healthy);
    }

    private static WorldInstance CreateWorld()
    {
        var worldTemplate = new WorldTemplate { Name = "rotated_plot_world" };
        return new WorldInstance(worldTemplate, 0, true, 21);
    }

    private static void SetHouses(HousingManager manager, params House[] houses)
    {
        var map = new Dictionary<uint, House>();
        foreach (var house in houses)
            map[house.Id] = house;
        SetPrivateField(manager, "_houses", map);
    }

    private static House CreateHouse(WorldInstance world, uint id, float x, float y)
    {
        var house = new House
        {
            Id = id,
            Template = new HousingTemplate
            {
                HousingSize = new HousingSize { Id = 2, GardenRadius = Radius }
            }
        };
        house.Transform.Local.SetPosition(x, y, 0f);
        SetParentWorld(house, world);
        return house;
    }

    private static void SetParentWorld(House house, WorldInstance world)
    {
        typeof(AAEmu.Game.Models.Game.World.GameObject).GetField("_parentWorld",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(house, world);
    }

    private static HousingManager CreateManager()
    {
        return new HousingManager(
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

    private static void SetPrivateField(object instance, string fieldName, object value)
    {
        instance.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(instance, value);
    }
}
