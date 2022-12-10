using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class SummonDoodad : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            var doodad = DoodadManager.Instance.Create(0, (uint)doodadId, caster);
            if (doodad == null)
            {
                return;
            }
            doodad.Transform = target.Transform.CloneDetached(doodad);
            doodad.Spawn();
        }
    }
}
