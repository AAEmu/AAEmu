using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class DoodadCofferPermissionTests
{
    private const uint OwnerId = 51;
    private const uint OtherId = 52;

    [Test]
    public async Task ContentPublicPermission_AllowsAnyCharacter()
    {
        var actor = Character(OtherId);

        await Assert.That(DoodadManager.CanUseCofferPermission(actor, Coffer(OwnerId),
            DoodadFuncPermission.Public)).IsTrue();
    }

    [Test]
    public async Task ContentOwnerPermission_AllowsOnlyTheOwner()
    {
        var coffer = Coffer(OwnerId);

        await Assert.That(DoodadManager.CanUseCofferPermission(Character(OwnerId), coffer,
            DoodadFuncPermission.Owner)).IsTrue();
        await Assert.That(DoodadManager.CanUseCofferPermission(Character(OtherId), coffer,
            DoodadFuncPermission.Owner)).IsFalse();
    }

    [Test]
    public async Task NonHousingPermissions_AreFailClosed()
    {
        var actor = Character(OwnerId);
        var coffer = Coffer(OwnerId);
        DoodadFuncPermission[] unsupported =
        [
            DoodadFuncPermission.Account,
            DoodadFuncPermission.Family,
            DoodadFuncPermission.Expedition,
            DoodadFuncPermission.Party,
            DoodadFuncPermission.PartyOwner,
            (DoodadFuncPermission)byte.MaxValue
        ];

        foreach (var permission in unsupported)
            await Assert.That(DoodadManager.CanUseCofferPermission(actor, coffer, permission)).IsFalse();
    }

    private static Character Character(uint id) => new(new UnitCustomModelParams()) { Id = id };

    private static Doodad Coffer(uint ownerId) => new() { OwnerId = ownerId };
}
