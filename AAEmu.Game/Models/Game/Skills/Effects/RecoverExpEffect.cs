using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class RecoverExpEffect : EffectTemplate
{
    public bool NeedMoney { get; set; }
    public bool NeedLaborPower { get; set; }

    public bool NeedPriest { get; set; }
    public bool Penaltied { get; set; } // 10.0.2.13: recover_exp_effects.penaltied present again

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        Logger.Trace("RecoverExpEffect");
    }
}
