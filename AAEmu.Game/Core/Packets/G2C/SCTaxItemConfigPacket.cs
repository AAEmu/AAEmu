using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// single u64 `convertRatioToAAPoint` (vtbl+120).
public class SCTaxItemConfigPacket(ulong convertRatioToAAPoint) : GamePacket(SCOffsets.SCTaxItemConfigPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(convertRatioToAAPoint);
        return stream;
    }
}
