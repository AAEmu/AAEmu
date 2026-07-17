using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.Game.Core.Packets.G2C;

[Flags]
public enum ExtraDataFlags
{
    HasByte = 1,
    HasUShort = 2,
    HasUInt = 4,
}

public class SCSkillStartedPacket(
    uint id,
    ushort tl,
    SkillCaster caster,
    SkillCastTarget target,
    Skill skill,
    SkillObject skillObject)
    : GamePacket(SCOffsets.SCSkillStartedPacket, 5)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public ushort RealCastTimeDiv10 { get; set; }
    public ushort BaseCastTimeDiv10 { get; set; }
    public byte CastSynergy { get; set; }
    private ExtraDataFlags ExtraDataFlag { get; set; }
    private byte ExtraDataByte { get; set; }
    private ushort ExtraDataUShort { get; set; }
    private uint ExtraDataUInt { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(tl);
        stream.Write(caster);
        stream.Write(target);
        stream.Write(skillObject);

        stream.Write(RealCastTimeDiv10);
        stream.Write(BaseCastTimeDiv10);
        stream.Write(CastSynergy); // castSynergy // (short)0
        stream.Write((byte)ExtraDataFlag); // f
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasByte))
            stream.Write(ExtraDataByte);
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasUShort))
            stream.Write(ExtraDataUShort);
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasUInt))
            stream.Write(ExtraDataUInt);
        return stream;
    }

    public SCSkillStartedPacket SetSkillResult(SkillResult skillResult)
    {
        if (skillResult != SkillResult.Success)
            ExtraDataFlag |= ExtraDataFlags.HasByte;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasByte;
        ExtraDataByte = (byte)skillResult;

        return this;
    }

    public SCSkillStartedPacket SetResultUShort(ushort val)
    {
        if (val != 0)
            ExtraDataFlag |= ExtraDataFlags.HasUShort;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasUShort;
        ExtraDataUShort = val;

        return this;
    }

    public SCSkillStartedPacket SetResultUInt(uint val)
    {
        if (val != 0)
            ExtraDataFlag |= ExtraDataFlags.HasUInt;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasUInt;
        ExtraDataUInt = val;

        return this;
    }

    public override string Verbose()
    {
        return $" - Id {id}, TlId {tl}, Caster {caster.ObjId}, Target {target.ObjId}, Skill {skill.Template.Id}";
    }
}
