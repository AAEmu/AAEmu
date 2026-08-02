using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// </summary>
public static class UnitDamagedWire
{
    /// <summary>
    /// Write UnitDamaged without debug block (flag bit4 clear).
    /// </summary>
    public static PacketStream Write(
        PacketStream stream,
        CastAction castAction,
        SkillCaster skillCaster,
        uint casterId,
        uint targetId,
        int damage,
        int absorbed,
        int manaBurn,
        byte holdableId,
        SkillHitType hitType,
        byte crimeState = 0,
        uint elementDamage = 0,
        bool showElementEffect = false,
        uint elementType = 0,
        byte flag = 1,
        byte result = 1)
    {
        stream.Write(castAction);
        stream.Write(skillCaster);
        stream.WriteBc(casterId);
        stream.WriteBc(targetId);
        stream.Write(crimeState);

        // First pisc group: count=2 (damage, absorbed). Second: count=3 (0, 0, manaBurn).
        stream.WritePisc((uint)Math.Max(0, damage), (uint)Math.Max(0, absorbed));
        stream.WritePisc(0u, 0u, (uint)Math.Max(0, manaBurn));

        stream.Write(holdableId); // hol
        stream.Write(elementDamage);
        stream.Write(showElementEffect);
        // elementType optional group: binary Begin(true) is a no-op scope → always write type u32
        stream.Write(elementType);

        // de packing (read path): low5=hitType, bits5-7=field76, bits8-11=field84
        // Prior working floaters used 0x120 | hitType (field76=1, field84=1).
        stream.Write((ushort)(0x120 | ((ushort)hitType & 0x1F)));

        // flag bit4 (0x10) = debug block — keep clear
        stream.Write((byte)(flag & 0xEF));
        stream.Write(result);
        return stream;
    }
}
