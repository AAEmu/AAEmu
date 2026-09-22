using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game;

public class SocialChatAuthorizationTests
{
    [Test]
    public async Task GuildChat_RequiresCurrentMemberAndRolePermission()
    {
        var character = new Character(new UnitCustomModelParams()) { Id = 7 };
        var expedition = new Expedition();
        character.Expedition = expedition;
        expedition.Members.Add(new ExpeditionMember { CharacterId = character.Id, Role = 2 });
        expedition.Policies.Add(new ExpeditionRolePolicy { Role = 2, Chat = false });

        await Assert.That(SocialChatAuthorization.CanSendGuildChat(expedition, character)).IsFalse();

        expedition.Policies[0].Chat = true;
        await Assert.That(SocialChatAuthorization.CanSendGuildChat(expedition, character)).IsTrue();

        expedition.Members.Clear();
        await Assert.That(SocialChatAuthorization.CanSendGuildChat(expedition, character)).IsFalse();
    }

    [Test]
    public async Task FactionScope_UsesRootFactionAndGroupsContentChildrenUnderTheirMother()
    {
        var playerNation = new SystemFaction
            { Id = (FactionsEnum)501, MotherId = FactionsEnum.Invalid, DiplomacyTarget = true };
        var racialChild = new SystemFaction
            { Id = FactionsEnum.Nuian, MotherId = FactionsEnum.NuiaAlliance, DiplomacyTarget = false };

        await Assert.That(SocialChatAuthorization.ResolveFactionChatId(playerNation)).IsEqualTo((FactionsEnum)501);
        await Assert.That(SocialChatAuthorization.ResolveFactionChatId(racialChild)).IsEqualTo(FactionsEnum.NuiaAlliance);
    }

    [Test]
    public async Task TrialChat_RequiresCurrentCaseAndASeatedRole()
    {
        var defendant = new Character(new UnitCustomModelParams()) { Id = 1 };
        var juror = new Character(new UnitCustomModelParams()) { Id = 2 };
        var invited = new Character(new UnitCustomModelParams()) { Id = 3 };
        var trial = new Trial { DefendantId = defendant.Id };
        trial.Jurors.Add(new TrialJuror { CharacterId = juror.Id });
        trial.Invited.Add(invited.Id);

        await Assert.That(SocialChatAuthorization.CanUseTrialChat(trial, defendant, true)).IsTrue();
        await Assert.That(SocialChatAuthorization.CanUseTrialChat(trial, juror, true)).IsTrue();
        await Assert.That(SocialChatAuthorization.CanUseTrialChat(trial, invited, true)).IsFalse();
        await Assert.That(SocialChatAuthorization.CanUseTrialChat(trial, defendant, false)).IsFalse();
    }

    private static Character CharacterWithFaction(FactionsEnum id) => new(new UnitCustomModelParams())
    {
        Faction = new SystemFaction { Id = id, MotherId = FactionsEnum.Invalid, DiplomacyTarget = true }
    };
}
