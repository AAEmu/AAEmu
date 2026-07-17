using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCombatEngagedPacket(uint objId) : GamePacket(SCOffsets.SCCombatEngagedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        return stream;
    }
}
