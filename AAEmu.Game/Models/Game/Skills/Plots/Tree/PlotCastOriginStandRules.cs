using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

/// <summary>
/// Where a plot Area / RandomArea stand should sit when its basis is the skill's caster.
/// </summary>
/// <remarks>
/// Blink's return-indicator path (plot event "시그널 이펙트 위치") builds an Area stand from
/// OriginalSource after <c>move_to_ground</c> has already moved the caster. Cloning the live
/// Transform plants the summon doodad at the landing; <see cref="Skill.CastOrigin"/> is the
/// cast-from point the indicator must mark.
/// </remarks>
public static class PlotCastOriginStandRules
{
    /// <summary>
    /// True when <paramref name="basis"/> is the cast's caster and the skill recorded a cast-start
    /// mark — the stand must use that mark rather than the caster's (possibly teleported) Transform.
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
        if (basis == null || state?.Caster == null || state.ActiveSkill?.CastOrigin is not { } origin)
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
