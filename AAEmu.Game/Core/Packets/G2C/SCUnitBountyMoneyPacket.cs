using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitBountyMoneyPacket(uint objId, long moneyAmount) : GamePacket(SCOffsets.SCUnitBountyMoneyPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(moneyAmount);
        return stream;
    }
}
