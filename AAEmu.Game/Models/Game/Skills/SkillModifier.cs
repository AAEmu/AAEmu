using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills
{
    public class SkillModifier
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string OwnerType { get; set; } // unused - always "Buff"
        public uint TagId { get; set; }
        public SkillAttribute SkillAttribute { get; set; }
        public UnitModifierType UnitModifierType { get; set; }
        public int Value { get; set; }
        public uint SkillId { get; set; }
        public bool Synergy { get; set; }
    }
}
