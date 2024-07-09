using System;
using System.Collections.Generic;
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

public class SCSkillStartedPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly Skill _skill;
    private readonly SkillObject _skillObject;

    public ushort RealCastTimeDiv10 { get; set; }
    public ushort BaseCastTimeDiv10 { get; set; }
    public byte CastSynergy { get; set; }
    private ExtraDataFlags ExtraDataFlag { get; set; }
    private byte ExtraDataByte { get; set; }
    private ushort ExtraDataUShort { get; set; }
    private uint ExtraDataUInt { get; set; }

    public SCSkillStartedPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
        : base(SCOffsets.SCSkillStartedPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        stream.Write(_tl);
        stream.Write(_caster);
        stream.Write(_target);
        stream.Write(_skillObject);

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
        return $" - Id {_id}, TlId {_tl}, Caster {_caster.ObjId}, Target {_target.ObjId}, Skill {_skill.Template.Id}";
    }
}
