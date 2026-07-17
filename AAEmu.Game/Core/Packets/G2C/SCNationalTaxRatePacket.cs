using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNationalTaxRatePacket(ushort id, int taxRate, int prevTaxRate, DateTime changedTime)
    : GamePacket(SCOffsets.SCNationalTaxRatePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(taxRate);
        stream.Write(prevTaxRate);
        stream.Write(changedTime);
        return stream;
    }
}
