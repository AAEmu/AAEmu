using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffCreatedPacket(Buff buff) : GamePacket(SCOffsets.SCBuffCreatedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(buff.SkillCaster);        // skillCaster
        stream.Write(buff.Caster?.Id ?? 0);    // casterId
        stream.WriteBc(buff.Owner.ObjId);      // targetId
        stream.Write(buff.Index);              // buffId
        stream.Write(buff.Template.BuffId);    // t template buffId
        stream.Write(buff.Caster?.Level ?? 1); // l sourceLevel
        stream.Write((short)buff.AbLevel);     // a sourceAbLevel
        //TODO: Fix this applying CD to wrong skill
        if (buff.Skill is not null && buff.Skill.Template.ToggleBuffId.Equals(buff.Template.Id))
            stream.Write(buff.Skill.Template.Id); // s skillId
        else
            stream.Write(0);

        buff.WriteData(stream);

        return stream;
    }

    public override string Verbose()
    {
        return $" - {buff.Owner.DebugName()} <- {buff?.Template?.BuffId}";
    }
}
