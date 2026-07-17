using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJoinedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId)
    : GamePacket(SCOffsets.SCJoinedChatChannelPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0 reads this as a single s64 "chat" field:
        //   bits 0..15  -> ChatType,
        //   bits 16..31 -> subType,
        //   bits 32..63 -> factionId.
        var chat = ((long)(short)type) | ((long)subType << 16) | ((long)(uint)factionId << 32);
        stream.Write(chat);
        stream.Write(""); // name
        return stream;
    }
}
