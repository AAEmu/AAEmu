using System;
using AAEmu.DB.Game;
using AAEmu.Game.Models.Game.Skills.Templates;

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

        public static explicit operator PassiveBuff(DB.Game.Skills v)
            =>
            new PassiveBuff 
            { 
                Id = v.Id 
            };
    }
}
