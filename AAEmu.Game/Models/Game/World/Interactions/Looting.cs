using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Looting : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint itemId, DoodadFuncTemplate objectFunc)
        {
            if (target is Doodad doodad)
            {
                doodad.Use(caster, skillId);
            }
        }
    }
}
