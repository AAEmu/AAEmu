using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Chat;

public class ChatChannel
{
    private readonly object _membersLock = new();

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

    public Character[] GetMembersSnapshot()
    {
        lock (_membersLock)
        {
            return Members.ToArray();
        }
    }

    public bool TryRemoveIfEmpty(Func<bool> removeChannel)
    {
        lock (_membersLock)
        {
            if (Members.Count > 0)
                return false;

            return removeChannel();
        }
    }

    /// <summary>
    /// Internal Id
    /// </summary>
    public uint InternalId { get; init; }

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

        lock (_membersLock)
        {
            if (Members.Contains(character))
                return false;

            Members.Add(character);
        }

        // character.SendMessage(ChatType.System, "ChatManager.JoinChannel {0} - {1} - {2}", chatType, internalId, internalName);
        character.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction));

        return true;
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
        var removed = false;
        lock (_membersLock)
        {
            removed = Members.Remove(character);
        }

        if (removed)
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
    public int SendMessage(Character origin, string msg, int ability = 0, byte languageType = 0)
    {
        var res = 0;
        var members = GetMembersSnapshot();

        foreach (var m in members)
        {
            m.SendPacket(new SCChatMessagePacket(ChatType, origin ?? m, msg, ability, languageType));
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
        var members = GetMembersSnapshot();

        foreach (var m in members)
        {
            m.SendPacket(packet);
            res++;
        }
        return res;
    }
}
