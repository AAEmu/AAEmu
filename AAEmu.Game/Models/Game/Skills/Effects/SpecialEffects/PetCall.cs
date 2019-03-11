using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class PetCall : ISpecialEffect
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time, int Value1, int Value2, int Value3,
            int Value4)
        {
            var owner = (Character)caster;
            var skillData = (SkillItem)casterObj;

            switch (Value1)
            {
                // TODO - maybe not hardcoded
                case 4944: // land
                case 3466: // sea
                    //owner.Mates.SpawnMount(skillData);
                    break;
            }
            owner.Mates.SpawnMount(skillData);
        }
    }
}
