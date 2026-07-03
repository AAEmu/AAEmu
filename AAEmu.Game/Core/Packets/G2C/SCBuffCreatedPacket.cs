using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCBuffCreatedPacket(Buff buff) : GamePacket(SCOffsets.SCBuffCreatedPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(buff.SkillCaster);        // skillCaster
        stream.Write((ulong)(buff.Caster?.Id ?? 0)); // casterId (i64, dedicate serializer sub_39C46000 +152)
        stream.WriteBc(buff.Owner.ObjId);      // targetId
        stream.Write(buff.Index);              // buffId
        stream.Write(buff.Template.BuffId);          // t template buffId (u32)
        stream.Write((byte)(buff.Caster?.Level ?? 1)); // l sourceLevel (u8)
        stream.Write((short)buff.AbLevel);           // a sourceAbLevel (u16)
        //TODO: Fix this applying CD to wrong skill
        if (buff.Skill is not null && buff.Skill.Template.ToggleBuffId.Equals(buff.Template.Id))
            stream.Write(buff.Skill.Template.Id); // s skillId (u32)
        else
            stream.Write(0);

        stream.Write(1u); // stack (u32) — single application; dedicate serializer sub_3938F7F0 +160
        buff.WriteData(stream);

        return stream;
    }

    public override string Verbose()
    {
        return $" - {buff.Owner.DebugName()} <- {buff?.Template?.BuffId}";
    }
}
