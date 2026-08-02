using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZUnitHealed (0x031) — World → Zone heal mirror.
/// </summary>
public class WZUnitHealedPacket(
    CastAction castAction,
    SkillCaster skillCaster,
    uint targetId,
    HealType healType,
    HealHitType healHitType,
    long value,
    uint unitInCharge,
    bool critical = false)
    : ZonePacket(WzOpcodes.UnitHealed)
{
    protected override void WriteBody(PacketStream stream)
    {
        UnitHealedWire.Write(
            stream,
            castAction,
            skillCaster,
            targetId,
            healType,
            healHitType,
            value,
            critical: critical);
        stream.WriteBc(unitInCharge);
    }
}
