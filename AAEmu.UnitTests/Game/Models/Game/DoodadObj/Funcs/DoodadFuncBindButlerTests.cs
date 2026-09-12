using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBindButlerTests
{
    [Test]
    public async Task ResolveHouse_AcceptsOnlyHousingLinkedDoodadInCharactersWorldInstance()
    {
        var world = new WorldInstance(new WorldTemplate(), 0, true, 10);
        var character = new Character(new UnitCustomModelParams());
        var house = new House { Id = 20, ObjId = 30 };
        var doodad = new Doodad
        {
            OwnerType = DoodadOwnerType.Housing,
            OwnerDbId = house.Id,
            ParentObjId = house.ObjId,
            ParentObj = house
        };
        SetWorld(character, world);
        SetWorld(house, world);
        SetWorld(doodad, world);

        var result = DoodadFuncBindButler.ResolveHouse(character, doodad, _ => house);

        await Assert.That(result).IsSameReferenceAs(house);
    }

    [Test]
    public async Task ResolveHouse_RejectsMatchingDatabaseIdWithoutHousingParentLink()
    {
        var world = new WorldInstance(new WorldTemplate(), 0, true, 10);
        var character = new Character(new UnitCustomModelParams());
        var house = new House { Id = 20, ObjId = 30 };
        var doodad = new Doodad
        {
            OwnerType = DoodadOwnerType.Character,
            OwnerDbId = house.Id,
            ParentObjId = house.ObjId
        };
        SetWorld(character, world);
        SetWorld(house, world);
        SetWorld(doodad, world);

        var result = DoodadFuncBindButler.ResolveHouse(character, doodad, _ => house);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ResolveHouse_RejectsParentHouseThatIsNoLongerRegistered()
    {
        var world = new WorldInstance(new WorldTemplate(), 0, true, 10);
        var character = new Character(new UnitCustomModelParams());
        var house = new House { Id = 20, ObjId = 30 };
        var doodad = new Doodad
        {
            OwnerType = DoodadOwnerType.Housing,
            OwnerDbId = house.Id,
            ParentObjId = house.ObjId,
            ParentObj = house
        };
        SetWorld(character, world);
        SetWorld(house, world);
        SetWorld(doodad, world);

        var result = DoodadFuncBindButler.ResolveHouse(character, doodad, _ => null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ResolveHouse_RejectsBinderFromAnotherWorldInstance()
    {
        var template = new WorldTemplate();
        var characterWorld = new WorldInstance(template, 0, true, 10);
        var houseWorld = new WorldInstance(template, 0, true, 11);
        var character = new Character(new UnitCustomModelParams());
        var house = new House { Id = 20, ObjId = 30 };
        var doodad = new Doodad
        {
            OwnerType = DoodadOwnerType.Housing,
            OwnerDbId = house.Id,
            ParentObjId = house.ObjId,
            ParentObj = house
        };
        SetWorld(character, characterWorld);
        SetWorld(house, houseWorld);
        SetWorld(doodad, houseWorld);

        var result = DoodadFuncBindButler.ResolveHouse(character, doodad, _ => house);

        await Assert.That(result).IsNull();
    }

    private static void SetWorld(GameObject gameObject, WorldInstance world) =>
        typeof(GameObject).GetField("_parentWorld",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(gameObject, world);
}
