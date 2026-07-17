using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUpdatePremiumPointPacket(int point, byte oldPg, byte pg)
    : GamePacket(SCOffsets.SCUpdatePremiumPointPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(point);
        stream.Write(oldPg);
        stream.Write(pg);
        return stream;
    }
}
