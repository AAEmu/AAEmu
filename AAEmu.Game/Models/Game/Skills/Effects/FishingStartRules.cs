using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Server-side admission for the shipped rod casts.
/// The client supplies a water position, but the server must not accept a rod
/// cast aimed at a dry target.
/// </summary>
public static class FishingStartRules
{
    /// <summary>
    /// How far below the reported hit the water volume is probed.
    /// </summary>
    /// <remarks>
    /// The client's cast point is a surface hit, and <see cref="WorldInstance.IsWater"/>
    /// accepts a point only at or below the surface (a wave, or a hit a little above the
    /// plane, is not water). Probing a short descent first means an otherwise valid cast
    /// on the surface is not rejected. This is a geometric tolerance for a surface probe,
    /// not a content value, and it mirrors the existing drowning margin in
    /// <c>Unit.ResolveWorldDrownThreshold</c>.
    /// </remarks>
    public const float WaterProbeDescentMetres = 2f;

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

        return ValidateStart(template, IsWaterAtOrBelowSurface(caster.ParentWorld, target.Transform.World.Position));
    }

    /// <summary>
    /// True when the reported hit sits in the water volume, allowing a short descent so a
    /// surface hit (or a wave crest) is not mistaken for dry ground.
    /// </summary>
    private static bool IsWaterAtOrBelowSurface(WorldInstance world, System.Numerics.Vector3 position)
    {
        if (world.IsWater(position, out _))
            return true;

        var probe = position;
        probe.Z -= WaterProbeDescentMetres;
        return world.IsWater(probe, out _);
    }

    /// <summary>
    /// The water gate is driven by the shipped <c>skills.target_only_water</c> column, not by a
    /// plot id list.
    /// </summary>
    /// <remarks>
    /// All 36 shipped skills that carry <c>target_only_water = true</c> <em>and</em> a plot are rod
    /// casts, spread over 34 plots; restricting the gate to the two original plots let a
    /// dry-target cast through for the other 32. The plot is still required because 51 of the 87
    /// flagged skills have no plot and are not rod casts - they are underwater-usable self buffs
    /// (experience, drop-rate, death-penalty and honor potions) plus bait scatter and release -
    /// and gating those would reject legitimate use.
    /// </remarks>
    public static bool RequiresWater(SkillTemplate template) =>
        template?.Plot != null && template.TargetOnlyWater;
}
