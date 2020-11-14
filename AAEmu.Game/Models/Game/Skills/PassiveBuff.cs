using System;
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
            owner.Modifiers.AddModifiers(Template.BuffId);
        }

        public void Remove(Unit owner)
        {
            owner.Modifiers.RemoveModifiers(Template.BuffId);
        }
    }
}
