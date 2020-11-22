using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Use : IWorldInteraction
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType, uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            _log.Trace("World interaction SkillID: {0}", skillId);
            if (target is Doodad doodad)
            {
                // var action = DoodadManager.Instance.GetFunc(doodad.FuncGroupId, skillId);
                // if (action != null)
                // {                                  
                //     action.Use(caster, doodad, action.SkillId);
                // }
                doodad.Use(caster, skillId);
            }            
        }
    }
}
