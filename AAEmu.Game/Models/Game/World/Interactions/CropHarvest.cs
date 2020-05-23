using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using SQLitePCL;
using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class CropHarvest : IWorldInteraction
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void Execute(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, uint skillId)
        {
            if (target is Doodad doodad) // TODO only for test
            {
                _log.Warn("Func group ID = " + doodad.FuncGroupId);
                _log.Warn("Func skillID = " + skillId);
                var func = DoodadManager.Instance.GetFunc(doodad.FuncGroupId, skillId);
                if (func == null)
                    return;

                func.Use(caster, doodad, skillId);
            }
        }
    }
}
