using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class ConsumeLaborPower : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ConsumeLaborPower;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character player || skill?.Template == null)
            return;

        // value1 is zero on all 71 shipped rows, so the template's cost is what they charge; a row that sets
        // the slot charges that instead.
        var cost = LaborPowerRules.ResolveCost(value1, skill.Template.ConsumeLaborPower);
        if (cost > 0)
            player.ChangeLabor((short)-cost, skill.Template.ActabilityGroupId);
    }
}
