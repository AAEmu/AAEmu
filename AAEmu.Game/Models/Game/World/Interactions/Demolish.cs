using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Demolish : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster,
            uint skillId)
        {
            if (target is House house && caster is Character character)
                HousingManager.Instance.Demolish(character.Connection, house);
        }
    }
}
