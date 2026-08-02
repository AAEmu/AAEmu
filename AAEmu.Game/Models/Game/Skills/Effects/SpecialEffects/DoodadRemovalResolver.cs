using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

internal static class DoodadRemovalResolver
{
    private const float MillimetersPerMeter = 1000f;

    public static bool TryGetCandidates(BaseUnit caster, BaseUnit target, Skill skill, int radiusMillimeters,
        out List<Doodad> candidates)
    {
        candidates = [];
        if (radiusMillimeters <= 0 || skill?.Template is null)
            return false;

        BaseUnit anchor;
        var includeTargetDoodad = false;
        switch (skill.Template.TargetSelection)
        {
            case SkillTargetSelection.Source:
                anchor = caster;
                break;
            case SkillTargetSelection.Target:
                anchor = target;
                includeTargetDoodad = true;
                break;
            default:
                return false;
        }

        if (anchor?.Region is null)
            return false;

        candidates = WorldManager.GetAround<Doodad>(anchor, radiusMillimeters / MillimetersPerMeter);

        // GetAround excludes the anchor ObjId. A directly targeted doodad is part of a Target-centered effect.
        // Source-centered effects intentionally do not infer that the caster should remove itself.
        if (includeTargetDoodad && anchor is Doodad targetDoodad)
            candidates.Add(targetDoodad);

        return true;
    }
}
