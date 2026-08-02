using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// order — vel, angvel, impulse, angImpulse — held at +0x30, +0x48, +0x3C and +0x54. The shape is
/// </summary>
/// <remarks>
/// Wheeled vehicles are simulated by the driving client — it reports full state through
/// VehicleMoveType, where a ship reports only throttle and steering through ShipRequestMoveType and
/// is integrated server-side. A stunt skill therefore cannot be applied by moving the hull on the
/// server; the impulse has to be handed to the owning client, which is what this packet is for.
/// </remarks>
public class SCUnitImpulsePacket(
    uint targetUnitObjId,
    SkillCaster caster,
    float velX, float velY, float velZ,
    float angVelX, float angVelY, float angVelZ,
    float impulseX, float impulseY, float impulseZ,
    float angImpulseX, float angImpulseY, float angImpulseZ)
    : GamePacket(SCOffsets.SCUnitImpulsePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(targetUnitObjId);
        stream.Write(caster);

        stream.Write(velX);
        stream.Write(velY);
        stream.Write(velZ);

        stream.Write(angVelX);
        stream.Write(angVelY);
        stream.Write(angVelZ);

        stream.Write(impulseX);
        stream.Write(impulseY);
        stream.Write(impulseZ);

        stream.Write(angImpulseX);
        stream.Write(angImpulseY);
        stream.Write(angImpulseZ);

        return stream;
    }
}
