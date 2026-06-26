using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

public class Bonus
{
    public BonusTemplate Template { get; set; }
    // Carries BonusTemplate.Value (long); summed into the double attribute accumulator in Unit.CalculateWithBonuses.
    public long Value { get; set; }
}