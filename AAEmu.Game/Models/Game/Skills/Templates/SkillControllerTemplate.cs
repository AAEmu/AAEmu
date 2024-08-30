using System;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class SkillControllerTemplate : EffectTemplate
{
    public uint KindId { get; set; }
    public int[] Value { get; set; }
    public byte ActiveWeaponId { get; set; }
    public uint EndSkillId { get; set; }
    public override bool OnActionTime { get; }
    public uint EndAnimId { get; set; }
    public uint StartAnimId { get; set; }
    public string StrValue1 { get; set; }
    public uint TransitionAnim1Id { get; set; }
    public uint TransitionAnim2Id { get; set; }

    public SkillControllerTemplate()
    {
        Value = new int[15];
    }

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj,
        EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
    {
        Logger.Debug("SkillControllerTemplate");
    }
}
