using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCChargeMoneyPaidPacket(long mailId) : GamePacket(SCOffsets.SCChargeMoneyPaidPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mailId);
        return stream;
    }
}
