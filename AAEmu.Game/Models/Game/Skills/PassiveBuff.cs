using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public class PassiveBuff
    {
        public uint Id { get; set; }
        public PassiveBuffTemplate Template { get; set; }

        public PassiveBuff()
        {
        }

        public PassiveBuff(PassiveBuffTemplate template)
        {
            Id = template.Id;
            Template = template;
        }

        public void Apply(Unit owner)
        {
            // owner.Modifiers.AddModifiers(Template.BuffId);
            var template = SkillManager.Instance.GetBuffTemplate(Template.BuffId);
            var newEffect =
                new Effect(owner, owner, new SkillCasterUnit(), template, null, DateTime.Now)
                {
                    Passive = true
                };

            owner.Effects.AddEffect(newEffect);
        }

        public void Remove(Unit owner)
        {
            // owner.Modifiers.RemoveModifiers(Template.BuffId);
            owner.Effects.RemoveBuff(Template.BuffId);
        }
    }
}
