using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitOfflinePacket(uint objId, bool isOffline) : GamePacket(SCOffsets.SCUnitOfflinePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(isOffline);
        return stream;
    }
}
