using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCJoinedChatChannelPacket(ChatType type, short subType, FactionsEnum factionId)
    : GamePacket(SCOffsets.SCJoinedChatChannelPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((short)type);
        stream.Write(subType);
        stream.Write((uint)factionId);
        // -------------
        stream.Write(""); // name
        return stream;
    }
}
