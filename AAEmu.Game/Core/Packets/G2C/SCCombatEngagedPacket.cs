using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One-id bodies cause client "not enough buffer for bc" / sc error cur=177.
/// </summary>
public class SCCombatEngagedPacket(uint objId, uint otherObjId) : GamePacket(SCOffsets.SCCombatEngagedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.WriteBc(otherObjId);
        return stream;
    }
}
