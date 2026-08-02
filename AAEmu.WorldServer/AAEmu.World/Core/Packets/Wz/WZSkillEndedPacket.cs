using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.World.Core.Packets.Wz;

/// <summary>
/// WZSkillEnded (0x02D) — completes a World-authored Zone skill timeline.
/// </summary>
public class WZSkillEndedPacket(ushort timelineId, SkillCaster caster)
    : ZonePacket(WzOpcodes.SkillEnded)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(timelineId);
        stream.Write(caster);
    }
}
