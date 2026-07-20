using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDominionTaxBalancedPacket(ushort id, int peaceTaxMoney, int peaceTaxAaPoint)
    : GamePacket(SCOffsets.SCDominionTaxBalancedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(peaceTaxMoney);
        stream.Write(peaceTaxAaPoint);
        return stream;
    }
}
