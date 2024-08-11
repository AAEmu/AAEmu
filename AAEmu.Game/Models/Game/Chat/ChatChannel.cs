using System.Collections.Generic;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Chat;

public class ChatChannel
{
    public ChatType ChatType { get; set; }
    public short SubType { get; set; } // used for things like zone key for /shout
    public FactionsEnum Faction { get; set; }
    public List<Character> Members { get; set; }
    public long InternalId { get; set; }
    public string InternalName { get; set; }

    public ChatChannel()
    {
        ChatType = ChatType.White;
        SubType = 0;
        Faction = 0;
        Members = [];
        InternalId = 0;
        InternalName = string.Empty;
    }

    public bool JoinChannel(Character character)
    {
        if (character == null)
            return false;

        if (Members.Contains(character))
            return false;

        // character.SendMessage(ChatType.System, "ChatManager.JoinChannel {0} - {1} - {2}", chatType, internalId, internalName);
        Members.Add(character);
        character.SendPacket(new SCJoinedChatChannelPacket(ChatType, SubType, Faction));

        return true;
    }

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
    /// <param name="origin">Can be null or be the charater that is the origin of the message</param>
    /// <param name="msg">Text to send</param>
    /// <param name="ability"></param>
    /// <param name="languageType"></param>
    /// <returns>Number of members the message was sent to</returns>
    public int SendMessage(Character origin, string msg, int ability = 0, byte languageType = 0)
    {
        var res = 0;
        foreach (var m in Members)
        {
            m.SendPacket(new SCChatMessagePacket(ChatType, origin != null ? origin : m, msg, ability, languageType));
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
