using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Server-side admission for the two shipped rod plots (809/821).
/// The client supplies a water position, but the server must not accept a rod
/// cast aimed at a dry target.
/// </summary>
public static class FishingStartRules
{
    public static SkillResult ValidateStart(SkillTemplate template, bool targetIsWater)
    {
        if (!RequiresWater(template))
            return SkillResult.Success;

        return targetIsWater ? SkillResult.Success : SkillResult.InvalidTarget;
    }

    public static SkillResult ValidateStart(SkillTemplate template, BaseUnit caster, BaseUnit target)
    {
        if (!RequiresWater(template))
            return SkillResult.Success;

        if (target == null || caster?.ParentWorld == null)
            return SkillResult.InvalidTarget;

        var position = target.Transform.World.Position;
        return ValidateStart(template, caster.ParentWorld.IsWater(position, out _));
    }

    public static bool RequiresWater(SkillTemplate template) =>
        template?.Plot != null &&
        SportFishCombat.IsRodPlot(template.Plot.Id) &&
        template.TargetOnlyWater;
}
