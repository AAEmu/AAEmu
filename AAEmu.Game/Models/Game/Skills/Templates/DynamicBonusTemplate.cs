using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public class DynamicBonusTemplate
    {
        public UnitAttribute Attribute { get; set; }
        public UnitModifierType ModifierType { get; set; }
        public uint FuncId { get; set; }
        public string FuncType { get; set; }
    }
}