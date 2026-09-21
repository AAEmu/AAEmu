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

    public static bool TryReadWorldSeed(SkillCastTarget target, out float x, out float y, out float z, out float yaw)
    {
        switch (target)
        {
            case SkillCastPositionTarget position:
                x = position.PosX;
                y = position.PosY;
                z = position.PosZ;
                yaw = position.PosRot;
                return HasWorldSeed(x, y, z);
            case SkillCastPosition2Target position2:
                x = position2.PosX;
                y = position2.PosY;
                z = position2.PosZ;
                yaw = 0f;
                return HasWorldSeed(x, y, z);
            case SkillCastPosition3Target position3:
                x = position3.PosX;
                y = position3.PosY;
                z = position3.PosZ;
                yaw = position3.Pitch;
                return HasWorldSeed(x, y, z);
            default:
                x = 0f;
                y = 0f;
                z = 0f;
                yaw = 0f;
                return false;
        }
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
