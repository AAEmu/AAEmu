using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

#pragma warning disable IDE0052 // Remove unread private members

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
    public byte ExtraData { get; set; }
    public byte ExtraDataByte { get; set; }
    public ushort ExtraDataUShort { get; set; }
    public uint ExtraDataUInt { get; set; }

    public SCSkillStartedPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
        : base(SCOffsets.SCSkillStartedPacket, 1)
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
        stream.Write(ExtraData); // f
        switch (ExtraData)
        {
            case 1:
                stream.Write(ExtraDataByte);
                break;
            case 2:
                stream.Write(ExtraDataUShort);
                break;
            case 4:
                stream.Write(ExtraDataUInt);
                break;
        }
        return stream;
    }

    // TODO block with f flag
    /*
          a2->Reader->ReadByte("f", (unsigned __int8 *)&v5, 0);
          if ( v5 & 1 )
            a2->Reader->ReadByte("c", (unsigned __int8 *)v2, 0);
          if ( v5 & 2 )
            a2->Reader->ReadUInt16((struc_1 *)a2, "e", v3, 0);
          if ( v5 & 4 )
            a2->Reader->ReadUInt32("p", v4, 0);
     */

    public override string Verbose()
    {
        return $" - Id {_id}, TlId {_tl}, Caster {_caster.ObjId}, Target {_target.ObjId}, Skill {_skill.Template.Id}";
    }
}
