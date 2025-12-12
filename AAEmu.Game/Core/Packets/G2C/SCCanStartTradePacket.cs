using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCanStartTradePacket(uint objId) : GamePacket(SCOffsets.SCCanStartTradePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId); // uint
        return stream;
    }
}
