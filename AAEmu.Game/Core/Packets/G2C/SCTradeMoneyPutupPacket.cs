using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// </remarks>
public class SCTradeMoneyPutupPacket(long moneyAmount) : GamePacket(SCOffsets.SCTradeMoneyPutupPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(moneyAmount);
        return stream;
    }
}
