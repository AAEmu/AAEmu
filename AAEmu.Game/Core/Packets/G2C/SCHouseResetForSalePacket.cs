using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseResetForSalePacket(ushort tl, string houseName) : GamePacket(SCOffsets.SCHouseResetForSalePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(houseName);
        return stream;
    }
}
