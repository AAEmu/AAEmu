using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
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
            return;
        }

        // Sail fold/unfold (InteractionEffect Use) is cast on the hull (target_type self).
        // The funcs live on attached sail/mast doodads — drive those from the parent slave.
        if (target is Slave slave)
        {
            foreach (var attached in slave.AttachedDoodads)
            {
                if (attached == null)
                    continue;
                var ap = attached.AttachPoint;
                if (ap is AttachPointKind.Sail0 or AttachPointKind.Sail1 or AttachPointKind.Sail2
                    or AttachPointKind.Mast0 or AttachPointKind.Mast1 or AttachPointKind.Mast2)
                {
                    attached.Use(caster, skillId);
                }
            }
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
