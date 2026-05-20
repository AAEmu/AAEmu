using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

#pragma warning disable IDE0052 // Remove unread private members

public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;

    private short _effectDelay = 37;
    private bool _dist;

    public short ComputedDelay { get; set; }

    /// <summary>Override animation ID. If &gt; 0, uses this instead of skill template FireAnim.</summary>
    public uint OverrideFireAnimId { get; set; }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject) : base(SCOffsets.SCSkillFiredPacket, 1)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
    }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject, short effectDelay = 37, int fireAnimId = 2, bool dist = true) : base(SCOffsets.SCSkillFiredPacket, 1)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        _effectDelay = effectDelay;
        OverrideFireAnimId = (uint)fireAnimId;
        _dist = dist;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        stream.Write(_tl);
        stream.Write(_caster);
        stream.Write(_target);
        stream.Write(_skillObject);

        stream.Write((short)(ComputedDelay / 10 + 10));
        stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));
        stream.Write((byte)0); // f

        // Use override anim if set, otherwise use skill template FireAnim
        if (OverrideFireAnimId > 0)
            stream.Write(OverrideFireAnimId);
        else
            stream.Write(_skill.Template.FireAnim?.Id ?? 0);

        stream.Write((byte)0); // flag

        return stream;
    }
}
