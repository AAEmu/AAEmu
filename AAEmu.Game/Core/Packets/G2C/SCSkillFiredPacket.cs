using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;

    public short ComputedDelay { get; set; }

    /// <summary>
    /// The fire animation ID sent to the client.
    /// Default = skill template's FireAnim ID. Callers can override for weapon-based
    /// auto-attack animation (Skill.GetWeaponAttackAnimId) or NPC anim cycling.
    /// </summary>
    public uint FireAnimId { get; set; }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject) : base(SCOffsets.SCSkillFiredPacket, 1)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        FireAnimId = skill.Template.FireAnim?.Id ?? 0;
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
        stream.Write(FireAnimId);
        stream.Write((byte)0); // flag

        return stream;
    }
}
