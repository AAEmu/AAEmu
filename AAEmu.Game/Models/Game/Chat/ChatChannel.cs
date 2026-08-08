using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Chat;

public class ChatChannel
{
    /// <summary>
    /// Chat channel type
    /// </summary>
    public ChatType ChatType { get; init; } = ChatType.White;

    /// <summary>
    /// Extra channel info (like zone, partyId, etc...)
    /// </summary>
    public short SubType { get; init; }

    /// <summary>
    /// Faction Id for this channel if needed
    /// </summary>
    public FactionsEnum Faction { get; init; } = 0;

    /// <summary>
    /// Current members in this channel
    /// </summary>
    public List<Character> Members { get; set; } = [];

    /// <summary>
    /// Internal Id
    /// </summary>
    public long InternalId { get; init; }

    /// <summary>
    /// Internal name of this channel
    /// </summary>
    public string InternalName { get; set; } = string.Empty;

    /// <summary>
    /// Add a character to a channel
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public bool JoinChannel(Character character)
    {
        if (character == null)
            return false;

        if (Members.Contains(character))
            return false;

        // character.SendMessage(ChatType.System, "ChatManager.JoinChannel {0} - {1} - {2}", chatType, internalId, internalName);
        Members.Add(character);
        character.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction, AnnouncedName));

        return true;
    }

    /// <summary>Only user channels are addressed by name; the fixed ones are identified by type alone.</summary>
    private string AnnouncedName => ChatType is ChatType.User or ChatType.Csm ? InternalName : string.Empty;

    /// <summary>
    /// Re-announces this channel to a character that is already a member.
    /// </summary>
    /// <remarks>
    /// Membership and the client's knowledge of a channel are not the same thing. The zone channel in
    /// particular is joined from Character.OnZoneChange while the login position is applied, long
    /// before the client can take a chat packet - so by the time CSNotifyInGame runs, JoinChannel sees
    /// an existing member and stays silent, and the client never learns the channel exists. Shout,
    /// Trade and LFG all ride that one channel, which is exactly the set that stayed dead.
    /// </remarks>
    public void AnnounceTo(Character character)
    {
        character?.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction, AnnouncedName));
    }

    /// <summary>
    /// Removes a character from the channel
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public bool LeaveChannel(Character character)
    {
        if (character == null)
            return false;

        // character.SendMessage(ChatType.System, "ChatManager.LeaveChannel {0} - {1} - {2}", chatType, internalId, internalName);
        if (Members.Remove(character))
        {
            character.SendPacket(new SCLeavedChatChannelPacket(ChatType, SubType, Faction));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sends a message to all members of the channel
    /// </summary>
    /// <param name="origin">Can be null or be the character that is the origin of the message</param>
    /// <param name="msg">Text to send</param>
    /// <param name="ability"></param>
    /// <param name="languageType"></param>
    /// <returns>Number of members the message was sent to</returns>
    /// <summary>
    /// Sends a message whose ChatType differs from the channel's own.
    /// </summary>
    /// <remarks>
    /// Trade, LFG and Shout all ride the zone channel, and Raid/RaidLeader share the raid one, so the
    /// message type and the channel type are not the same thing. The channel's SubType and Faction
    /// still have to travel with it - they are what lets the client match the message to a channel it
    /// joined - which is why these callers cannot simply build the packet themselves.
    /// </remarks>
    public int SendMessage(Character origin, ChatType type, string msg, int ability = 0, byte languageType = 0)
    {
        var res = 0;
        foreach (var m in Members)
        {
            m.SendPacket(new SCChatMessagePacket(type, origin ?? m, msg, ability, languageType, SubType, Faction));
            res++;
        }
        return res;
    }

    public int SendMessage(Character origin, string msg, int ability = 0, byte languageType = 0)
    {
        var res = 0;
        foreach (var m in Members)
        {
            m.SendPacket(new SCChatMessagePacket(ChatType, origin ?? m, msg, ability, languageType, SubType, Faction));
            res++;
        }
        return res;
    }

    /// <summary>
    /// Sends a GamePacket to all members of the chat channel
    /// </summary>
    /// <param name="packet">Packet to send</param>
    /// <returns>Number of members the packet was sent to</returns>
    public int SendPacket(GamePacket packet)
    {
        var res = 0;
        foreach (var m in Members)
        {
            m.SendPacket(packet);
            res++;
        }
        return res;
    }
}
