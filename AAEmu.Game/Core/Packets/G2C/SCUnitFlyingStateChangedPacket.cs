using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitFlyingStateChangedPacket(uint objId, bool isFlying)
    : GamePacket(SCOffsets.SCUnitFlyingStateChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(isFlying);
        return stream;
    }
}
