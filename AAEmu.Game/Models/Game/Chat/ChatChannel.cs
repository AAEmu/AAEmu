using System.Collections.Generic;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Chat;

public class ChatChannel
{
    public ChatType chatType;
    public short subType; // used for things like zonekey for /shout
    public uint faction;
    public List<Character> members;
    public long internalId;
    public string internalName;

    public ChatChannel()
    {
        chatType = ChatType.White;
        subType = 0;
        faction = 0;
        members = new List<Character>();
        internalId = 0;
        internalName = string.Empty;
    }

    public bool JoinChannel(Character character)
    {
        if (character == null)
            return false;

        if (members.Contains(character))
            return false;

        // character.SendMessage(ChatType.System, "ChatManager.JoinChannel {0} - {1} - {2}", chatType, internalId, internalName);
        members.Add(character);
        character.SendPacket(new SCJoinedChatChannelPacket(chatType, subType, faction));

        return true;
    }

    public bool LeaveChannel(Character character)
    {
        if (character == null)
            return false;
        // character.SendMessage(ChatType.System, "ChatManager.LeaveChannel {0} - {1} - {2}", chatType, internalId, internalName);
        if (members.Remove(character))
        {
            character.SendPacket(new SCLeavedChatChannelPacket(chatType, subType, faction));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Sends a message to all members of the channel
    /// </summary>
    /// <param name="origin">Can be null or be the charater that is the origin of the message</param>
    /// <param name="msg">Text to send</param>
    /// <param name="ability"></param>
    /// <param name="languageType"></param>
    /// <returns>Number of members the message was sent to</returns>
    public int SendMessage(Character origin, string msg, int ability = 0, byte languageType = 0)
    {
        var res = 0;
        foreach (var m in members)
        {
            m.SendPacket(new SCChatMessagePacket(chatType, origin != null ? origin : m, msg, ability, languageType));
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
        foreach (var m in members)
        {
            m.SendPacket(packet);
            res++;
        }
        return res;
    }
}
