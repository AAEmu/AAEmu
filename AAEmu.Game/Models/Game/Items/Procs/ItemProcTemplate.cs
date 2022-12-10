using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Items.Procs
{
    /// <summary>
    /// DB model for the table item_procs
    /// </summary>
    public class ItemProcTemplate
    {
        public uint Id { get; set; }
        public uint SkillId { get; set; }
        public ProcChanceKind ChanceKind { get; set; }
        public uint ChanceRate { get; set; }
        public uint ChanceParam { get; set; } // Always zero in 1.2
        public uint CooldownSec { get; set; }
        public bool Finisher { get; set; }
        public uint ItemLevelBasedChanceBonus { get; set; }
        
        public SkillTemplate SkillTemplate { get; set; }

    }
}
