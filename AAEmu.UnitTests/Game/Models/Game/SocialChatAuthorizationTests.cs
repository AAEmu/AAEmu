using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Expeditions;
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
}
