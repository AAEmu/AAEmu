using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// skillId + fireAnim are pish/pisc-packed after SkillObject tail, not at packet start.
/// </summary>
public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;

    /// <summary>Effect delay in ms; wire uses (delayMs + 100) / 10 (legacy +100 ms base).</summary>
    public int EffectDelayMs { get; set; }

    /// <summary>Fire animation ID (pish/pisc second value).</summary>
    public uint FireAnimId { get; set; }

    public bool Flag0 { get; set; }

    /// <summary>Second bit of trailing flag byte.</summary>
    public bool Flag1 { get; set; }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
        : base(SCOffsets.SCSkillFiredPacket, 1)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        FireAnimId = skill.Template.FireAnim?.Id ?? 0;
    }

    /// <summary>Legacy alias — maps to EffectDelayMs.</summary>
    public short ComputedDelay
    {
        get => (short)EffectDelayMs;
        set => EffectDelayMs = value;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_tl);
        stream.Write(_caster);
        stream.Write(_target);
        stream.WriteSkillCastExtra(_skillObject);

        var delayInternal = EffectDelayMs + 100;
        var channelInternal = _skill.Template.ChannelingTime + 100;
        stream.WriteSkillMsec(delayInternal);
        stream.WriteSkillMsec(channelInternal);
        stream.WriteSkillCastTail();

        stream.WritePisc(_id, FireAnimId);

        var flag = (byte)((Flag0 ? 1 : 0) | (Flag1 ? 2 : 0));
        stream.Write(flag);
        return stream;
    }
}
