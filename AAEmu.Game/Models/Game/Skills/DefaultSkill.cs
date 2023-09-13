using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills
{
    public class DefaultSkill
    {
        public SkillTemplate Template { get; set; }
        public byte Slot { get; set; }
        public bool AddToSlot { get; set; }
    }
}