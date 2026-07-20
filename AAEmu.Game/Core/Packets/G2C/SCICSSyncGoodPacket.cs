using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSSyncGoodPacket(int cashShopId, int remainCount) : GamePacket(SCOffsets.SCICSSyncGoodPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(cashShopId);
        stream.Write(remainCount);
        return stream;
    }
}
