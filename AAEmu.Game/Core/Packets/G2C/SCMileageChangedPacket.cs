using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCMileageChangedPacket(uint objId, int mileage) : GamePacket(SCOffsets.SCMileageChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(mileage);
        return stream;
    }
}
