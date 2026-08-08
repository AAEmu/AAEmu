using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Shared body for SCBuffCreated (0x0EB) and WZBuffCreated (0x03E).
/// Dedicate: SkillCaster + i64 castId + bc target + u32 buffIndex + BuffData.
/// </summary>
public static class BuffCreatedWire
{
    /// <summary>Validates unit identifiers before relaying a buff to the zone.</summary>
    public static bool IsZoneSafe(Buff buff, out string reason)
    {
        if (!ObjectIdManager.IsZoneUnitId(buff.Owner?.ObjId ?? 0))
        {
            reason = $"owner ObjId {buff.Owner?.ObjId ?? 0} is not a zone unit id";
            return false;
        }

        var caster = buff.SkillCaster;
        var casterIsUnit = caster?.Type is SkillCasterType.Unit or SkillCasterType.Item or SkillCasterType.Mount;
        if (casterIsUnit && !ObjectIdManager.IsZoneUnitId(caster.ObjId))
        {
            reason = $"caster type {caster.Type} ObjId {caster.ObjId} is not a zone unit id";
            return false;
        }

        reason = null;
        return true;
    }

    public static void Write(PacketStream stream, Buff buff)
    {
        stream.Write(buff.SkillCaster);
        stream.Write((ulong)(buff.Caster?.Id ?? 0));
        stream.WriteBc(buff.Owner.ObjId);
        stream.Write(buff.Index);
        stream.Write(buff.Template.BuffId);
        stream.Write((byte)(buff.Caster?.Level ?? 1));
        stream.Write((short)buff.AbLevel);
        if (buff.Skill is not null && buff.Skill.Template.ToggleBuffId.Equals(buff.Template.Id))
            stream.Write(buff.Skill.Template.Id);
        else
            stream.Write(0);
        stream.Write(1u); // stack
        buff.WriteData(stream);
    }
}
