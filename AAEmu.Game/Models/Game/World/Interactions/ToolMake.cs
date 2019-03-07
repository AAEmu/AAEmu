using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class ToolMake : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster,
            uint skillId)
        {
            if (caster is Character character)
                character.Craft.EndCraft();
        }
    }
}
