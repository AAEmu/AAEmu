using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class Use : IWorldInteraction
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
    {
        Logger.Debug($"World interaction SkillID: {skillId}");
        if (target is Doodad doodad)
        {
            doodad.Use(caster, skillId);
        }

        /*
        // TODO ID=21902, View Fish Finder: Scan around with the Fish Finder. Detected schools of fish will be displayed on the map.
        if (skillId == SkillsEnum.ViewFishFinder)
        {
            var characterTarget = target as Character;
            var doodads = WorldManager.GetAround<Doodad>(caster, 1f);
            if ((doodads != null) && (characterTarget != null))
            {
                foreach (var d in doodads)
                {
                    FishSchoolManager.FishFinderStart(characterTarget);
                }
            }
        }
        */
    }
}
