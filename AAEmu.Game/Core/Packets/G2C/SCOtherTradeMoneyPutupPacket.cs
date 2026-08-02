using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCOtherTradeMoneyPutupPacket(long moneyAmount) : GamePacket(SCOffsets.SCOtherTradeMoneyPutupPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(moneyAmount);
        return stream;
    }
}
