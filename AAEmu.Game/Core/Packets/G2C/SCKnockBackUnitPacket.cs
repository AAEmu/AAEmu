using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Knockback impulse. The three floats are an impulse vector the client applies to the unit's
/// physics (direction × magnitude, Z = lift) — not a landing position.
/// </summary>
public class SCKnockBackUnitPacket(uint objId, float impulseX, float impulseY, float impulseZ)
    : GamePacket(SCOffsets.SCKnockBackUnitPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(impulseX);
        stream.Write(impulseY);
        stream.Write(impulseZ);
        return stream;
    }
}
