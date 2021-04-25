using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class PetCall : SpecialEffectAction
    {
        public override void Execute(Unit caster,
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
            var owner = (Character)caster;
            var skillData = (SkillItem)casterObj;

            switch (value1)
            {
                // TODO - maybe not hardcoded
                case 4944: // land
                case 3466: // sea
                    //owner.Mates.SpawnMount(skillData);
                    break;
            }
            owner.Mates.SpawnMount(skillData);
            _log.Warn("Special effects: PetCall");
        }
    }
}
