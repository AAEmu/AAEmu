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

    /// <summary>Faction chat is authorized by the channel the character is in, not by its faction id.</summary>
    /// <remarks>
    /// A character is put into a faction channel only by ChatManager.SyncFactionChannel - at login and
    /// on a permanent allegiance change - and taken out of one only by it or by LeaveAllChannels. No
    /// client packet reaches JoinChannel, so membership is the server's own record that the character
    /// was synced into that channel; a client cannot claim a membership it was never given.
    /// <para>
    /// Re-resolving the channel from Character.Faction instead would break every temporary faction
    /// change: duels, the buffs that carry a faction_id, battlefield teams and GM /setfaction move the
    /// faction without moving the channel, so the message would go to a channel the client never
    /// joined and be dropped, while the real faction channel no longer matched the sender and filtered
    /// out everything they were sent.
    /// </para>
    /// </remarks>
    public static bool CanSendFactionChat(ChatChannel channel, Character character) =>
        channel != null && character != null && channel.ChatType == ChatType.Ally && channel.Contains(character);

    /// <summary>The recipients of a send are the channel's members, so membership is the same check.</summary>
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

    /// <summary>One-to-one (direct) chat: both ends present and neither side hostile to the other.</summary>
    /// <remarks>
    /// The diplomacy rule is the one the whisper path has always used - the two characters have to
    /// share a mother faction - and it is shared here so the whisper command and the native
    /// one-to-one packet family cannot drift apart. It fails closed: a character whose faction is
    /// not resolved yet, a self-send and an unknown peer are all "no".
    /// </remarks>
    public static bool CanSendDirectChat(Character sender, Character receiver) =>
        sender != null && receiver != null &&
        sender.Id != receiver.Id &&
        sender.IsOnline && receiver.IsOnline &&
        sender.Faction != null && receiver.Faction != null &&
        sender.Faction.MotherId == receiver.Faction.MotherId &&
        !EitherHasBlocked(sender, receiver);

    /// <summary>True when either character's block list names the other.</summary>
    public static bool EitherHasBlocked(Character first, Character second) =>
        first?.Blocked?.Contains(second?.Id ?? 0) == true ||
        second?.Blocked?.Contains(first?.Id ?? 0) == true;
}
