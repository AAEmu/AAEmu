using System;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class SpecialEffect : EffectTemplate
    {
        public SpecialType SpecialEffectTypeId { get; set; }
        public int Value1 { get; set; }
        public int Value2 { get; set; }
        public int Value3 { get; set; }
        public int Value4 { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, Skill skill, SkillObject skillObject, DateTime time)
        {
            _log.ConditionalTrace(
                "SpecialEffect, Special: {0}, Value1: {1}, Value2: {2}, Value3: {3}, Value4: {4}",
                SpecialEffectTypeId, Value1, Value2, Value3, Value4
            );

            var classType = Type.GetType("AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects." + SpecialEffectTypeId);
            if (classType == null)
            {
                _log.Warn("Unknown special effect: {0}", SpecialEffectTypeId);
                return;
            }

            var action = (SpecialEffectAction)Activator.CreateInstance(classType);
            action.Execute(caster, casterObj, target, targetObj, castObj, skill, skillObject, time, Value1, Value2,
                Value3, Value4);
        }
    }
}
