using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZUnitDamaged (0x030) — World → Zone hit mirror.
/// </summary>
public class WZUnitDamagedPacket(
    CastAction castAction,
    SkillCaster skillCaster,
    uint casterId,
    uint targetId,
    int damage,
    int absorbed,
    SkillHitType hitType = SkillHitType.MeleeHit,
    float am = 0f,
    byte holdableId = 0,
    int manaBurn = 0)
    : ZonePacket(WzOpcodes.UnitDamaged)
{
    protected override void WriteBody(PacketStream stream)
    {
        UnitDamagedWire.Write(
            stream,
            castAction,
            skillCaster,
            casterId,
            targetId,
            damage,
            absorbed,
            manaBurn,
            holdableId,
            hitType);
        stream.Write(am);
    }
}
