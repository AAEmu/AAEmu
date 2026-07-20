using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionLowestPricePacket(uint itemTemplateId, byte itemGrade, int moneyAmount)
    : GamePacket(SCOffsets.SCAuctionLowestPricePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(itemTemplateId);
        stream.Write(itemGrade);
        stream.Write(moneyAmount);

        return stream;
    }
}
