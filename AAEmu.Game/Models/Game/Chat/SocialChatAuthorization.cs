using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.Game.Models.Game.Chat;

public static class SocialChatAuthorization
{
    /// <summary>Caller must hold <see cref="Expedition.SyncRoot"/> across this check and send.</summary>
    public static bool CanSendGuildChat(Expedition expedition, Character character)
    {
        if (expedition == null || character == null || expedition.isDisbanded ||
            !ReferenceEquals(character.Expedition, expedition))
            return false;
        var member = expedition.GetMember(character);
        return member != null && expedition.GetPolicyByRole(member.Role)?.Chat == true;
    }
}
