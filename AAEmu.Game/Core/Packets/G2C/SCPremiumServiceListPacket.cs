using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.CashShop;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPremiumServiceListPacket(bool isEnd, byte size, PremiumDetail detail, int exchangeRatio)
    : GamePacket(SCOffsets.SCPremiumServiceListPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(isEnd);
        stream.Write(size);
        stream.Write(detail);
        stream.Write(exchangeRatio);
        return stream;
    }
}
