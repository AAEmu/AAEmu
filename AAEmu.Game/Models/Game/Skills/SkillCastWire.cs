using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// </summary>
public static class SkillCastWire
{
    /// <summary>Write msec u16 on wire; internal value is stored in ms (wire = ms / 10).</summary>
    public static PacketStream WriteSkillMsec(this PacketStream stream, int internalMs)
    {
        var wire = (ushort)Math.Max(0, internalMs / 10);
        return stream.Write(wire);
    }

    public static PacketStream WriteSkillCastExtra(this PacketStream stream, SkillObject skillObject)
    {
        skillObject ??= new SkillObject();

        // ActiveAbilitySet (15) is CS-only: skillsaver slot index. Echoing it on SCSkillStarted /
        // SCSkillFired makes the client drop cast UX (no cast bar / cast anim) and can scramble
        // parsing of later SC packets in the same session. Slot is already stashed from CSStartSkill.
        var flag = skillObject.Flag == SkillObjectType.AbilitySet
            ? SkillObjectType.None
            : skillObject.Flag;

        stream.Write((byte)flag); // flag: type in low 6 bits; 0x40/0x80 unused for normal casts
        switch (flag)
        {
            case SkillObjectType.Unk1 when skillObject is SkillObjectUnk1 u1:
                stream.Write(u1.Type);
                stream.Write(u1.Id);
                stream.Write(Helpers.ConvertLongX(u1.X));
                stream.Write(Helpers.ConvertLongX(u1.Y));
                stream.Write(u1.Z);
                stream.Write(0); // indunZoneKey
                break;
            case SkillObjectType.Unk2 when skillObject is SkillObjectUnk2 u2:
                stream.Write(u2.Id);
                stream.Write(u2.Name ?? string.Empty);
                break;
            case SkillObjectType.Unk3 when skillObject is SkillObjectUnk3 u3:
                stream.Write(u3.Msg ?? string.Empty);
                break;
            case SkillObjectType.Unk4 when skillObject is SkillObjectUnk4 u4:
                stream.Write(Helpers.ConvertLongX(u4.X));
                stream.Write(Helpers.ConvertLongY(u4.Y));
                stream.Write(u4.Z);
                break;
            case SkillObjectType.Unk5 when skillObject is SkillObjectUnk5 u5:
                stream.Write(u5.Step);
                break;
            case SkillObjectType.Unk6 when skillObject is SkillObjectUnk6 u6:
                stream.Write(u6.Name ?? string.Empty);
                break;
            case SkillObjectType.ItemGradeEnchantingSupport when skillObject is SkillObjectItemGradeEnchantingSupport ig:
                stream.Write(ig.SupportItemId);
                stream.Write(ig.AutoUseAaPoint);
                break;
            // Synthesis echoes its material slots straight back. The flag byte above has already
            // announced type 8, so this body is not optional: leaving it out makes the client read the
            // rest of the cast packet - the cast times among them - as material ids, which costs the
            // cast bar and the animation with it.
            case SkillObjectType.ItemEvolvingMaterials when skillObject is SkillObjectItemEvolvingMaterials em:
                var materialIds = em.MaterialItemIds ?? [];
                stream.Write((ushort)(materialIds.Length * sizeof(ulong)));
                foreach (var materialId in materialIds)
                    stream.Write(materialId);
                stream.Write(em.AutoUseAaPoint);
                break;
            case SkillObjectType.ItemChangeMapping when skillObject is SkillObjectItemChangeMapping cm:
                stream.Write(cm.MappingId);
                break;
        }

        stream.Write((byte)0); // inputDirection
        return stream;
    }

    public static PacketStream WriteSkillCastTail(
        this PacketStream stream,
        byte extraByte = 0,
        ushort extraUShort = 0,
        uint extraUInt = 0,
        bool extraBool = true)
    {
        byte f = 0;
        if (extraByte != 0)
            f |= 1;
        if (extraUShort != 0)
            f |= 2;
        if (extraUInt != 0)
            f |= 4;
        if (!extraBool)
            f |= 8;

        stream.Write(f);
        if ((f & 1) != 0)
            stream.Write(extraByte);
        if ((f & 2) != 0)
            stream.Write(extraUShort);
        if ((f & 4) != 0)
            stream.Write(extraUInt);
        if ((f & 8) != 0)
            stream.Write(extraBool);
        return stream;
    }

    /// <summary>
    /// Must NOT use <see cref="SkillObject.Write"/> — that emits the CS SkillCastExtra type enum
    /// (nest interact sent Flag=12 → byte 0x0C → Zone expects u32+bool and TCP-dies).
    /// Empty cast → single <c>f=0</c>.
    /// </summary>
    public static PacketStream WriteWzSkillObject(this PacketStream stream, SkillObject skillObject = null)
    {
        // Until CS SkillCastExtra is mapped onto WZ c/e/p/d fields, always send empty.
        _ = skillObject;
        return stream.WriteSkillCastTail();
    }
}
