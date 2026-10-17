namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Travel delay for <c>skills.effect_speed</c> (metres per second). Real projectiles sit well above
/// 1 m/s; a handful of quest captures ship a near-zero speed that must not schedule a multi-minute wait.
/// </summary>
public static class SkillEffectTravelRules
{
    /// <summary>Minimum speed that still contributes a travel delay. Below this, delay is zero.</summary>
    public const float MinTravelSpeedMetresPerSecond = 1f;

    /// <summary>
    /// Milliseconds of projectile travel for <paramref name="distanceMetres"/> at
    /// <paramref name="effectSpeedMetresPerSecond"/>. Speeds below
    /// <see cref="MinTravelSpeedMetresPerSecond"/> yield zero (no travel wait).
    /// </summary>
    public static int TravelDelayMs(float effectSpeedMetresPerSecond, float distanceMetres)
    {
        if (effectSpeedMetresPerSecond < MinTravelSpeedMetresPerSecond || distanceMetres <= 0f)
            return 0;
        return (int)(distanceMetres / effectSpeedMetresPerSecond * 1000.0f);
    }
}
