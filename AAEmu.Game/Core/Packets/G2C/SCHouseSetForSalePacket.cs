using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseSetForSalePacket(
    ushort tl,
    uint moneyAmount,
    uint sellToPlayerId,
    string sellToName,
    string houseName)
    : GamePacket(SCOffsets.SCHouseSetForSalePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(moneyAmount);
        stream.Write(sellToPlayerId);
        stream.Write(sellToName);
        stream.Write(houseName);
        return stream;
    }
}
