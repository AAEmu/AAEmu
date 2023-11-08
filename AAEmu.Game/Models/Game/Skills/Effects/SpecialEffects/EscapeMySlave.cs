using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class EscapeMySlave : SpecialEffectAction
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
        // TODO ...
        if ((caster is Character player) && (targetObj is SkillCastPositionTarget skillCastPositionTarget))
        {
            Logger.Debug($"Special effects: EscapeMySlave value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}");

            SlaveManager.Instance.RidersEscape(player, skillCastPositionTarget);
        }
    }
}
