using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNpcChatMessagePacket(
    ChatType chatType,
    Npc npc,
    Character character,
    byte kind,
    uint type,
    string message)
    : GamePacket(SCOffsets.SCNpcChatMessagePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        #region Int64_chat
        stream.Write((short)chatType);                     // ChatType -> ChatChannelNo
        stream.Write((short)(character?.Faction.Id ?? 0)); // chat, subType
        stream.Write((uint)(character?.Faction.Id ?? 0));  // chat, factionId
        #endregion Int64_chat

        stream.WriteBc(npc.ObjId);             // bc
        stream.Write(npc.Name);                // name
        stream.WriteBc(character?.ObjId ?? 0); // bc
        stream.Write(kind);                    // kind
        if (kind == 1)
            stream.Write(type);
        else
            stream.Write(message);

        return stream;
    }
}
