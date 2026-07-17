using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCDominionTaxRatePacket(ushort id, int taxRate) : GamePacket(SCOffsets.SCDominionTaxRatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(taxRate);
        return stream;
    }
}
