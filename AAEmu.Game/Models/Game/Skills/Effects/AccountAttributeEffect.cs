using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class AccountAttributeEffect : EffectTemplate
{
    public uint KindId { get; set; }
    public bool BindWorld { get; set; }
    public bool IsAdd { get; set; }
    public uint Count { get; set; }
    public uint Time { get; set; }
    public uint KindValue { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("AccountAttributeEffect");
    }
}
