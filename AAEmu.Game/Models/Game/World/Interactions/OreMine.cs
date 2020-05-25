using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class OreMine : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, uint skillId)
        {
            if (target is Doodad doodad) // TODO only for test
            {
                DoodadManager.Instance.TriggerActionFunc(GetType().Name, caster, doodad, skillId);

            }
        }
    }
}
