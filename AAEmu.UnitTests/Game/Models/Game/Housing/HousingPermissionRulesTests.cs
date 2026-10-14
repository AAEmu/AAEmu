using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

public sealed class HousingPermissionRulesTests
{
    private const uint OwnerId = 11;
    private const uint AccountId = 22;
    private const uint FamilyId = 33;
    private const uint GuildId = 44;

    [Test]
    [Arguments(HousingPermission.Private)]
    [Arguments(HousingPermission.Guild)]
    [Arguments(HousingPermission.Public)]
    [Arguments(HousingPermission.Family)]
    public async Task DefinedContentPermissions_AreAccepted(HousingPermission permission)
    {
        await Assert.That(HousingPermissionRules.IsDefined(permission)).IsTrue();
    }

    [Test]
    public async Task UnknownPermission_IsFailClosed()
    {
        await Assert.That(HousingPermissionRules.IsDefined((HousingPermission)byte.MaxValue)).IsFalse();
    }

    [Test]
    public async Task PublicPermission_AllowsAnyCharacter()
    {
        var actor = Character(OwnerId + 1, AccountId + 1);

        await Assert.That(HousingPermissionRules.CanAccess(actor, OwnerId, HousingPermission.Public)).IsTrue();
    }

    [Test]
    public async Task PrivateOwnerOnly_RejectsOtherCharacters()
    {
        var owner = Character(OwnerId, AccountId);
        var other = Character(OwnerId + 1, AccountId + 1);

        await Assert.That(HousingPermissionRules.CanAccess(owner, OwnerId, HousingPermission.Private, true)).IsTrue();
        await Assert.That(HousingPermissionRules.CanAccess(other, OwnerId, HousingPermission.Private, true)).IsFalse();
    }

    [Test]
    public async Task FamilyAndGuildSelection_RequiresTheMatchingRelationship()
    {
        var actor = Character(OwnerId + 1, AccountId + 1);

        await Assert.That(HousingPermissionRules.CanSelect(actor, HousingPermission.Family)).IsFalse();
        await Assert.That(HousingPermissionRules.CanSelect(actor, HousingPermission.Guild)).IsFalse();

        actor.Family = FamilyId;
        actor.Expedition = new Expedition { Id = (FactionsEnum)GuildId };

        await Assert.That(HousingPermissionRules.CanSelect(actor, HousingPermission.Family)).IsTrue();
        await Assert.That(HousingPermissionRules.CanSelect(actor, HousingPermission.Guild)).IsTrue();
    }

    [Test]
    public async Task GuildAccess_PrefersExplicitResidenceGuildId()
    {
        // Without a live ExpeditionManager, guildIdForAccess is the only way Guild permission can pass.
        var member = Character(OwnerId + 1, AccountId + 1);
        member.Expedition = new Expedition { Id = (FactionsEnum)77 };
        await Assert.That(HousingPermissionRules.CanAccess(
            member, OwnerId, HousingPermission.Guild, guildIdForAccess: (FactionsEnum)77)).IsTrue();
        await Assert.That(HousingPermissionRules.CanAccess(
            member, OwnerId, HousingPermission.Guild, guildIdForAccess: (FactionsEnum)88)).IsFalse();
    }

    private static Character Character(uint id, uint accountId) =>
        new(new UnitCustomModelParams()) { Id = id, AccountId = accountId };
}
