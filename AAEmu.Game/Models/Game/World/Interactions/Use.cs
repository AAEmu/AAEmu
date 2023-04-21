using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions
{
    public class Use : IWorldInteraction
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
            uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
        {
            _log.Debug("World interaction SkillID: {0}", skillId);
            if (target is Doodad doodad)
            {
                doodad.Use(caster, skillId);
            }

            // TODO ID=21902, View Fish Finder: Scan around with the Fish Finder. Detected schools of fish will be displayed on the map.
            if (skillId == SkillsEnum.ViewFishFinder)
            {
                var doodads = WorldManager.Instance.GetAround<Doodad>(caster, 1f);
                if (doodads != null)
                {
                    foreach (var d in doodads)
                    {
                        FishSchoolManager.Instance.FishFinderStart((Character)caster);
                    }
                }
            }
        }
    }
}
