using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates;

/// <summary>
/// How a buff's <see cref="UnitAttribute.MoveSpeedMul"/> flat should be applied.
/// </summary>
/// <remarks>
/// Fishing-boat engines store 1000, 1050, … 1550 as the speed <c>rating</c> (100% … 155% of
/// the hull's model velocity), not as a bonus on the 1000 baseline the attribute already
/// starts from. Applying those rows raw makes a basic engine run at twice the model speed.
/// Sail trim (+6) and square-sail flats (+400…+900) stay deltas — they never pair that
/// rating with a large hull-HP bonus.
/// </remarks>
public static class MoveSpeedMulRules
{
    public const int Baseline = 1000;
    public const int PropulsionHullHpMin = 5000;

    public static long FlatBonus(long stored, bool isPropulsionRating) =>
        isPropulsionRating ? stored - Baseline : stored;

    /// <summary>
    /// Engine-family rows pair a move-speed rating of at least 1000 with hull HP of at least
    /// 5000. Player dashes and sail unfurls do not.
    /// </summary>
    public static bool IsPropulsionRating(IEnumerable<BonusTemplate> bonuses)
    {
        ArgumentNullException.ThrowIfNull(bonuses);
        var hasRatingSpeed = false;
        var hasHullHp = false;
        foreach (var bonus in bonuses)
        {
            if (bonus.ModifierType != UnitModifierType.Value)
                continue;
            if (bonus.Attribute == UnitAttribute.MoveSpeedMul && bonus.Value >= Baseline)
                hasRatingSpeed = true;
            else if (bonus.Attribute == UnitAttribute.MaxHealth && bonus.Value >= PropulsionHullHpMin)
                hasHullHp = true;
        }

        return hasRatingSpeed && hasHullHp;
    }

    /// <summary>
    /// A basic engine stores exactly the baseline. Relaying that buff lets the zone add
    /// another 1000 and the hull runs at double the model speed. Higher grades store more
    /// than 1000; those extras are the upgrade and must still reach the zone.
    /// </summary>
    public static bool ShouldRelayToZone(IEnumerable<BonusTemplate> bonuses)
    {
        if (!IsPropulsionRating(bonuses))
            return true;

        foreach (var bonus in bonuses)
        {
            if (bonus.Attribute == UnitAttribute.MoveSpeedMul
                && bonus.ModifierType == UnitModifierType.Value
                && bonus.Value > Baseline)
                return true;
        }

        return false;
    }
}
