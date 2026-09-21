using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Slaves;

/// <summary>
/// Client SummonPos / CSSpawnSlave seed. The portal slides onto this stand; a later
/// water-search plant is what made the hull jump after the animation.
/// </summary>
public static class SlaveSummonSeedRules
{
    /// <summary>
    /// Same-stand test for "the CS packet already planted this item at the skill seed".
    /// Geometry epsilon, not a content radius.
    /// </summary>
    public const float SamePlantMetres = 1f;

    public static bool TryReadWorldSeed(SkillCastTarget target, out float x, out float y, out float z, out float yaw) =>
        TryReadWorldSeed(target, null, null, null, out x, out y, out z, out yaw);

    /// <summary>
    /// <paramref name="resolvedX"/> / Y / Z are the world stand <see cref="Skill"/> already built
    /// for a position target (local <c>ObjId1</c> basis included). Raw Pos* is world only when
    /// that basis is unset.
    /// </summary>
    public static bool TryReadWorldSeed(
        SkillCastTarget target,
        float? resolvedX,
        float? resolvedY,
        float? resolvedZ,
        out float x,
        out float y,
        out float z,
        out float yaw)
    {
        yaw = ReadYaw(target);
        if (resolvedX is float rx && resolvedY is float ry && resolvedZ is float rz && HasWorldSeed(rx, ry, rz))
        {
            x = rx;
            y = ry;
            z = rz;
            return true;
        }

        switch (target)
        {
            case SkillCastPositionTarget position when position.ObjId1 == 0:
                x = position.PosX;
                y = position.PosY;
                z = position.PosZ;
                return HasWorldSeed(x, y, z);
            case SkillCastPosition2Target position2:
                x = position2.PosX;
                y = position2.PosY;
                z = position2.PosZ;
                return HasWorldSeed(x, y, z);
            case SkillCastPosition3Target position3:
                x = position3.PosX;
                y = position3.PosY;
                z = position3.PosZ;
                return HasWorldSeed(x, y, z);
            default:
                x = 0f;
                y = 0f;
                z = 0f;
                yaw = 0f;
                return false;
        }
    }

    public static float ReadYaw(SkillCastTarget target) =>
        target switch
        {
            SkillCastPositionTarget position => position.PosRot,
            SkillCastPosition3Target position3 => position3.Pitch,
            _ => 0f
        };

    /// <summary>
    /// Horizontal reach of a client-chosen stand. <paramref name="rangeMetres"/> is
    /// <c>slaves.spawn_valid_area_range</c> on the template.
    /// </summary>
    public static bool IsWithinValidArea(float casterX, float casterY, float seedX, float seedY, uint rangeMetres)
    {
        var dx = seedX - casterX;
        var dy = seedY - casterY;
        var range = (float)rangeMetres;
        return dx * dx + dy * dy <= range * range;
    }

    public static bool HasWorldSeed(float x, float y, float z) =>
        float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z)
        && (x != 0f || y != 0f || z != 0f);

    public static bool IsAlreadyPlantedAtSeed(float hullX, float hullY, float seedX, float seedY)
    {
        var dx = hullX - seedX;
        var dy = hullY - seedY;
        return dx * dx + dy * dy <= SamePlantMetres * SamePlantMetres;
    }

    /// <summary>
    /// CSSpawnSlave already created this item's hull. The skill must not Delete it and
    /// water-search a second stand. A re-summon to a new seed still replaces.
    /// </summary>
    public static bool ShouldKeepExistingPlant(bool hasActiveSameItem, bool hasSeed, bool alreadyAtSeed)
    {
        if (!hasActiveSameItem)
            return false;
        return !hasSeed || alreadyAtSeed;
    }

    public static void ApplySeed(PositionAndRotation dest, float x, float y, float z, float yaw)
    {
        dest.SetPosition(x, y, z);
        dest.SetRotation(0f, 0f, yaw);
    }
}
