using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Justice;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Chat;

public static class SocialChatAuthorization
{
    public static FactionsEnum ResolveFactionChatId(SystemFaction faction)
    {
        if (faction == null)
            return FactionsEnum.Invalid;

        return faction.MotherId == FactionsEnum.Invalid ? faction.Id : faction.MotherId;
    }

    public static bool CanSendFactionChat(ChatChannel channel, Character character) =>
        channel != null && character?.Faction != null && channel.ChatType == ChatType.Ally &&
        channel.Faction == ResolveFactionChatId(character.Faction) && channel.Contains(character);

    public static bool CanReceiveFactionChat(ChatChannel channel, Character character) =>
        character is { IsOnline: true } && CanSendFactionChat(channel, character);

    public static bool CanUseTrialChat(Trial trial, Character character, bool isCurrentCourtTrial) =>
        trial != null && character != null && isCurrentCourtTrial &&
        (trial.DefendantId == character.Id || trial.FindJuror(character.Id) != null);

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
