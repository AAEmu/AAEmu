using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

#pragma warning disable IDE0052 // Remove unread private members

public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private uint _id;
    private ushort _tl;
    private SkillCaster _caster;
    private SkillCastTarget _target;
    private SkillObject _skillObject;
    private Skill _skill;

    private short _effectDelay = 37;
    private int _fireAnimId = 2;
    private bool _dist;

    public short ComputedDelay { get; set; }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
    }
    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject, short effectDelay = 37, int fireAnimId = 2, bool dist = true)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        _effectDelay = effectDelay;
        _fireAnimId = fireAnimId;
        _dist = dist;
    }

    public override PacketStream Write(PacketStream stream)
    {
        //stream.Write(_id);      // st - skill type  removed in 3.0.3.0
        stream.Write(_tl);       // sid - skill id

        stream.Write(_caster);      // SkillCaster
        stream.Write(_target);      // SkillCastTarget
        stream.Write(_skillObject); // SkillObject

        stream.Write((short)(ComputedDelay / 10 + 10)); // TODO  +10 It became visible flying arrows 
        stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));
        stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
        stream.WritePisc(_id, _skill.Template.FireAnimId); // added skill type here in 3.0.3.0
        stream.Write((byte)0); // flag

        return stream;
    }
}
