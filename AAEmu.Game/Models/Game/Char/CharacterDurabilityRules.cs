using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Combat / death durability decision table.
/// Chance comes from <c>item_configs.durability_decrement_chance</c>, a percent (0.8 is 0.8%),
/// scaled by the World PvE/PvP rates; a negative world rate forces the loss (death-style).
/// </summary>
public static class CharacterDurabilityRules
{
    public static float EffectiveChance(float worldRate, float decrementChance)
    {
        if (worldRate < 0f)
            return 1f;
        if (worldRate <= 0f || decrementChance <= 0f)
            return 0f;
        return Math.Clamp(worldRate * decrementChance / 100f, 0f, 1f);
    }

    /// <param name="roll01">Uniform roll in [0, 1).</param>
    public static bool ShouldLose(float worldRate, float decrementChance, double roll01)
    {
        if (worldRate < 0f)
            return true;
        var chance = EffectiveChance(worldRate, decrementChance);
        return chance > 0f && roll01 < chance;
    }

    public static byte ApplyLoss(byte current, byte loss)
    {
        if (loss == 0 || current == 0)
            return current;
        return (byte)Math.Max(0, current - loss);
    }

    /// <summary>
    /// Armor wear only follows normal combat / PvP kill reasons — not fall, drown, GM, etc.
    /// </summary>
    public static float ArmorWorldRate(
        KillReason reason,
        bool attackerIsCharacter,
        float pvpRate,
        float pveRate)
    {
        switch (reason)
        {
            case KillReason.PvpSiege:
            case KillReason.PvpBattleField:
            case KillReason.PvpHonorWar:
            case KillReason.PvpEnemy:
            case KillReason.PvpAlly:
                return pvpRate;
            case KillReason.Unknown:
            case KillReason.Damage:
                return attackerIsCharacter ? pvpRate : pveRate;
            default:
                return 0f;
        }
    }

    public static DurabilityLossTargets WeaponTarget(
        bool useMainhand,
        bool useOffhand,
        bool useRanged)
    {
        if (useMainhand)
            return DurabilityLossTargets.PrimaryWeapon;
        if (useOffhand)
            return DurabilityLossTargets.SecondaryWeapon;
        if (useRanged)
            return DurabilityLossTargets.RangedWeapon;
        return DurabilityLossTargets.AllWeapons;
    }
}
