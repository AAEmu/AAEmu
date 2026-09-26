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
    /// How far above the water surface a reported hit still counts as a surface hit.
    /// </summary>
    /// <remarks>
    /// The client's cast point is where the bobber lands, which is the surface itself or the top
    /// of a wave. <see cref="WorldInstance.IsWater"/> only accepts a point at or below the surface,
    /// and a river or lake only within its own depth, so a hit a little above the plane was
    /// rejected with <c>InvalidTarget</c>. Comparing against the reported surface with a small
    /// tolerance forgives that without the reach a fixed descent has: a descent deep enough to
    /// clear a wave also accepts dry ground the same distance above sea level, and it misses a hit
    /// on a body shallower than itself entirely. This is a geometric band for a surface probe, not
    /// a content value, and it sits in the same band as the wave-noise margin the hull rules use.
    /// </remarks>
    public const float WaterSurfaceToleranceMetres = 0.5f;

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
    /// True when the reported hit sits in the water volume, or within
    /// <see cref="WaterSurfaceToleranceMetres"/> above the water surface at that spot.
    /// </summary>
    /// <remarks>
    /// The surface is read at the reported point rather than by descending, so the test follows the
    /// body the point is actually over: open sea answers with the ocean plane, and a river or lake
    /// answers with its own surface at whatever depth it is. A fixed descent instead reached the
    /// same height above dry ground, and dropped past a body shallower than the descent.
    /// </remarks>
    private static bool IsWaterAtOrBelowSurface(WorldInstance world, System.Numerics.Vector3 position)
    {
        if (world.IsWater(position, out _))
            return true;

        var surface = world.Water?.GetWaterSurface(position, out _) ?? world.Template.OceanLevel;
        return position.Z <= surface + WaterSurfaceToleranceMetres;
    }

    /// <summary>
    /// The water gate is driven by the shipped <c>skills.target_only_water</c> column, not by a
    /// plot id list.
    /// </summary>
    /// <remarks>
    /// Of the 37 shipped skills that carry <c>target_only_water = true</c> <em>and</em> a plot, 35
    /// are rod casts spread over 34 plots; restricting the gate to the two original plots let a
    /// dry-target cast through for the other 32. The remaining two are the Kraken ink sprays
    /// (27200, 49524), which carry the column because they are used from water, but target a
    /// hostile unit rather than a point - gating them rejected the spray whenever the Kraken aimed
    /// at a player on a ship deck, so a rod-only rule also requires the position target type.
    ///
    /// The plot is still required because 52 of the 89 flagged skills have no plot and are not rod
    /// casts - they are underwater-usable self buffs (experience, drop-rate, death-penalty and
    /// honor potions) plus bait scatter and release. 47 of those 52 are position-targeted too, so
    /// the target type alone would gate them; gating those would reject legitimate use.
    /// </remarks>
    public static bool RequiresWater(SkillTemplate template) =>
        template?.Plot != null &&
        template.TargetOnlyWater &&
        template.TargetType == SkillTargetType.Pos;
}
