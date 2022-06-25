using System;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class HudBattlefieldAuthority : SpecialEffectAction
    {
        public override void Execute(IUnit caster,
            SkillCaster casterObj,
            IBaseUnit target,
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
            _log.Warn("Special effects: HudBattlefieldAuthority");
        }
    }
}
