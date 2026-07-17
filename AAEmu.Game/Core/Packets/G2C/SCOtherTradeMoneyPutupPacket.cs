using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCOtherTradeMoneyPutupPacket(int moneyAmount) : GamePacket(SCOffsets.SCOtherTradeMoneyPutupPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(moneyAmount);
        return stream;
    }
}
