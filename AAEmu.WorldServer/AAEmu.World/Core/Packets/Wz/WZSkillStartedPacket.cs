using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZSkillStarted (0x02B) — World → Zone cast intake.
/// </summary>
public class WZSkillStartedPacket(
    uint skillId,
    ushort tl,
    SkillCaster caster,
    SkillCastTarget target,
    uint ct,
    SkillObject skillObject)
    : ZonePacket(WzOpcodes.SkillStarted)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(skillId);
        stream.Write(tl);
        stream.Write(caster);
        stream.Write(target);
        stream.Write(ct);
        stream.WriteWzSkillObject(skillObject);
    }
}
