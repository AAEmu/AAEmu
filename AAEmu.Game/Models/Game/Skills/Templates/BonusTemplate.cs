using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class BonusTemplate
{
    public UnitAttribute Attribute { get; set; }
    public UnitModifierType ModifierType { get; set; }
    // 10.0.2.13: unit_modifiers.value is integer(8)/BIGINT; sentinel "max" rows reach ~1e16 (overflows int32). Widened to long.
    public long Value { get; set; }
    public int LinearLevelBonus { get; set; }
}