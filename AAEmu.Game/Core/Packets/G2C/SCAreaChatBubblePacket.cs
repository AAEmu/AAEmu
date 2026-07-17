using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAreaChatBubblePacket(bool enter, uint unitObjId, uint type)
    : GamePacket(SCOffsets.SCAreaChatBubblePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(enter);        // enter
        stream.WriteBc(unitObjId); // ObjId
        stream.Write(type);       // type

        return stream;
    }
}
