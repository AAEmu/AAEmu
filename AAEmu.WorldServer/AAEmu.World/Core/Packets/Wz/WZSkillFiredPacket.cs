using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZSkillFired (0x02C) — World → Zone fire/hit intake.
///   st/type u32 skillId @+16, sid u16 tl @+20,
/// </summary>
public class WZSkillFiredPacket(
    uint skillId,
    ushort tl,
    SkillCaster caster,
    SkillCastTarget target,
    SkillObject skillObject,
    bool flag = false)
    : ZonePacket(WzOpcodes.SkillFired)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(skillId);
        stream.Write(tl);
        stream.Write(caster);
        stream.Write(target);
        stream.WriteWzSkillObject(skillObject);
        stream.Write(flag); // field "b"
    }
}
