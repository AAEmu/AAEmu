using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZImpulseUnit (0x034) — hands a skill's physical impulse to the zone.
/// then four vec3f in call order — vel, angvel, impulse, angImpulse — matching the twelve
/// impulse_effects columns.
/// </summary>
public class WZImpulseUnitPacket(
    uint targetUnitId,
    SkillCaster caster,
    float velX, float velY, float velZ,
    float angVelX, float angVelY, float angVelZ,
    float impulseX, float impulseY, float impulseZ,
    float angImpulseX, float angImpulseY, float angImpulseZ)
    : ZonePacket(WzOpcodes.ImpulseUnit)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.WriteBc(targetUnitId);
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
    }
}
