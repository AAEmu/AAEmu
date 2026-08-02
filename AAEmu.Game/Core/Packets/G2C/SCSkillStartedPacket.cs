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
    HasBool = 8,
}

/// <summary>
/// </summary>
public class SCSkillStartedPacket(
    uint id,
    ushort tl,
    SkillCaster caster,
    SkillCastTarget target,
    Skill skill,
    SkillObject skillObject)
    : GamePacket(SCOffsets.SCSkillStartedPacket, 1)
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    public int RealCastTimeMs { get; set; }

    /// <summary>Base cast time in ms.</summary>
    public int BaseCastTimeMs { get; set; }

    public byte CastSynergy { get; set; }

    private ExtraDataFlags ExtraDataFlag { get; set; }
    private byte ExtraDataByte { get; set; }
    private ushort ExtraDataUShort { get; set; }
    private uint ExtraDataUInt { get; set; }
    private bool ExtraDataBool { get; set; } = true;

    /// <summary>Legacy helper — sets div10 fields via ms.</summary>
    public ushort RealCastTimeDiv10
    {
        get => (ushort)Math.Max(0, RealCastTimeMs / 10);
        set => RealCastTimeMs = value * 10;
    }

    public ushort BaseCastTimeDiv10
    {
        get => (ushort)Math.Max(0, BaseCastTimeMs / 10);
        set => BaseCastTimeMs = value * 10;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(id);
        stream.Write(tl);
        stream.Write(caster);
        stream.Write(target);
        stream.WriteSkillCastExtra(skillObject);
        stream.WriteSkillMsec(RealCastTimeMs);
        stream.WriteSkillMsec(BaseCastTimeMs);
        stream.Write(CastSynergy);

        var tailByte = ExtraDataFlag.HasFlag(ExtraDataFlags.HasByte) ? ExtraDataByte : (byte)0;
        var tailUShort = ExtraDataFlag.HasFlag(ExtraDataFlags.HasUShort) ? ExtraDataUShort : (ushort)0;
        var tailUInt = ExtraDataFlag.HasFlag(ExtraDataFlags.HasUInt) ? ExtraDataUInt : 0u;
        var tailBool = !ExtraDataFlag.HasFlag(ExtraDataFlags.HasBool) || ExtraDataBool;
        stream.WriteSkillCastTail(tailByte, tailUShort, tailUInt, tailBool);
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

    public SCSkillStartedPacket SetResultBool(bool val)
    {
        if (!val)
            ExtraDataFlag |= ExtraDataFlags.HasBool;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasBool;
        ExtraDataBool = val;
        return this;
    }

    public override string Verbose()
    {
        return $" - Id {id}, TlId {tl}, Caster {caster.ObjId}, Target {target.ObjId}, Skill {skill.Template.Id}";
    }
}
