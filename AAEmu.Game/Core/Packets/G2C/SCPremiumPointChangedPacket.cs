using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPremiumPointChangedPacket(uint objId, int point) : GamePacket(SCOffsets.SCPremiumPointChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(point);
        return stream;
    }
}
