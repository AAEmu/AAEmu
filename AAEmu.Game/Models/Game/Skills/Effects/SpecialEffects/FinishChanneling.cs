using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class FinishChanneling : SpecialEffectAction
{
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
        if (caster is Character) { Logger.Debug("Special effects: FinishChanneling value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (target is Unit unit)
        {
            unit.ActivePlotState?.FinishChanneling();

        }

    }
}
