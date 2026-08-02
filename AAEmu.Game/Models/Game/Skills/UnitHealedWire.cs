using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// </summary>
public static class UnitHealedWire
{
    public static PacketStream Write(
        PacketStream stream,
        CastAction castAction,
        SkillCaster skillCaster,
        uint targetId,
        HealType healType,
        HealHitType healHitType,
        long value,
        long overflow = 0,
        bool critical = false,
        uint elementHeal = 0,
        bool showElementEffect = false,
        uint elementType = 0,
        byte result = 1)
    {
        stream.Write(castAction);
        stream.Write(skillCaster);
        stream.WriteBc(targetId);
        stream.Write((byte)healType);
        stream.Write((byte)healHitType);
        stream.Write(value); // a (i64)
        stream.Write(overflow); // o (i64)
        stream.Write(critical); // c
        stream.Write(elementHeal);
        stream.Write(showElementEffect);
        stream.Write(elementType); // elementType.type (optional group always present on write)
        stream.Write(result);
        return stream;
    }
}
