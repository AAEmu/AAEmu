using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    /// <summary>
    /// SpecialEffect linked with adding charges to a buff.
    /// </summary>
    public class Charge : SpecialEffectAction
    {
        public override void Execute(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time, int buffId, int minCharge, int maxCharge, int unused)
        {
            var buff = caster.Effects.GetEffectFromBuffId((uint)buffId);
            var template = SkillManager.Instance.GetBuffTemplate((uint)buffId);
            var newEffect =
                new Effect(target, caster, casterObj, template, skill, time)
                {
                    Charge = Math.Min(Rand.Next(minCharge, maxCharge), template.MaxCharge)
                };

            caster.Effects.AddEffect(newEffect, buff?.Index ?? 0);
        }
    }
}
