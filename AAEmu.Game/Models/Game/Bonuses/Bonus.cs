using AAEmu.Game.Models.Game.Bonuses.Templates;

namespace AAEmu.Game.Models.Game.Bonuses
{
    public class Bonus
    {
        public BonusTemplate Template { get; set; }
        public int Value { get; set; }
    }
}