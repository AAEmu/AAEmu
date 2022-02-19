using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Use : IWorldInteraction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            _log.Debug("World interaction SkillID: {0}", skillId);
            if (target is Doodad doodad)
            {
                doodad.Use(caster, skillId);
            }
        }
    }
}
