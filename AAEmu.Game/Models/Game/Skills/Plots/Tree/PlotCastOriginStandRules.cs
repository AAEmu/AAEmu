using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Where a plot Area / RandomArea stand should sit when its basis is the skill's caster.
/// </summary>
/// <remarks>
/// Blink's return-indicator path (plot event "시그널 이펙트 위치") builds an Area stand from
/// OriginalSource after <c>move_to_ground</c> has already moved the caster. Cloning the live
/// Transform plants the summon doodad at the landing; <see cref="Skill.CastOrigin"/> is the
/// cast-from point the indicator must mark. The freeze applies only after a soft move/teleport
/// effect on this cast — leap controllers also leave <see cref="Skill.CastOrigin"/> set, and
/// pinning those Area stands back to the take-off spot is wrong.
/// </remarks>
public static class PlotCastOriginStandRules
{
    /// <summary>
    /// True when <paramref name="basis"/> is the cast's caster, a soft move/teleport has run on
    /// this skill, and the skill recorded a cast-start mark — the stand must use that mark rather
    /// than the caster's (already teleported) Transform.
    /// </summary>
    public static bool TryGetFrozenCasterOrigin(
        BaseUnit basis,
        PlotState state,
        out float x,
        out float y,
        out float z,
        out float yawRad)
    {
        x = y = z = yawRad = 0f;
        if (basis == null || state?.Caster == null)
            return false;
        if (state.ActiveSkill is not { SoftMovedCaster: true, CastOrigin: { } origin })
            return false;
        if (basis.ObjId != state.Caster.ObjId)
            return false;

        x = origin.X;
        y = origin.Y;
        z = origin.Z;
        yawRad = origin.YawRad;
        return true;
    }
}
