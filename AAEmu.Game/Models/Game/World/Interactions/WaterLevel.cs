using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class WaterLevel : IWorldInteraction
    {
        public void Execute(IUnit caster, SkillCaster casterType, IBaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            if (target is Doodad doodad)
            {
                doodad.Use(caster, skillId);
            }
        }
    }
}
