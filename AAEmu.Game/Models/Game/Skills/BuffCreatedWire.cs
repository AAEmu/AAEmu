using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Shared body for SCBuffCreated (0x0EB) and WZBuffCreated (0x03E).
/// Dedicate: SkillCaster + i64 castId + bc target + u32 buffIndex + BuffData.
/// </summary>
public static class BuffCreatedWire
{
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
