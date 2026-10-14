namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Whether a regrade / tempering support may sit on a given piece of gear, and how it shifts odds.
/// </summary>
/// <remarks>
/// <c>restrict_item_tag_id</c> is a blacklist (the charm is refused when the gear carries that tag —
/// shipped as tag 6059 on the Ipnysh earring set). <c>exclusive_item_tag_id</c> is a whitelist (the
/// gear must carry that tag). Scale bounds are <c>enchant_scale_ratios</c> row ids compared to the
/// piece's tempering step; <c>31/31</c> is the shipped "no scale gate" sentinel because the ladder
/// tops out at 30. Temper additives all ship a real scale band plus <c>require_grade</c> 1/1 — that
/// pair is not a grade band (every temper support uses it); only scale/tag gates apply for those.
/// </remarks>
public static class GradeEnchantCharmRules
{
    /// <summary>Both scale columns equal this → the charm ignores tempering.</summary>
    public const int NoScaleRequirementId = 31;

    /// <summary>Whether the charm's scale columns actually constrain the target.</summary>
    public static bool HasScaleRequirement(ItemGradeEnchantingSupport charm) =>
        charm != null
        && !(charm.ReqScaleMinId == NoScaleRequirementId && charm.ReqScaleMaxId == NoScaleRequirementId);

    /// <summary>
    /// True when this charm may be used on gear with the given tags and tempering step.
    /// </summary>
    public static bool IsAllowed(
        ItemGradeEnchantingSupport charm,
        bool itemHasRestrictTag,
        bool itemHasExclusiveTag,
        int itemEnchantScaleId)
    {
        if (charm == null)
            return true;

        if (charm.RestrictItemTagId != 0 && itemHasRestrictTag)
            return false;

        if (charm.ExclusiveItemTagId != 0 && !itemHasExclusiveTag)
            return false;

        if (!HasScaleRequirement(charm))
            return true;

        var min = Math.Min(charm.ReqScaleMinId, charm.ReqScaleMaxId);
        var max = Math.Max(charm.ReqScaleMinId, charm.ReqScaleMaxId);
        return itemEnchantScaleId >= min && itemEnchantScaleId <= max;
    }

    /// <summary>
    /// Regrade charms use <c>require_grade_min/max</c> as a grade band (<c>-1</c> = open).
    /// Temper additives ship <c>1/1</c> with a scale gate and are not grade-banded.
    /// </summary>
    public static bool PassesGradeRequirement(ItemGradeEnchantingSupport charm, int itemGrade)
    {
        if (charm == null || HasScaleRequirement(charm))
            return true;

        if (charm.RequireGradeMin != -1 && itemGrade < charm.RequireGradeMin)
            return false;

        if (charm.RequireGradeMax != -1 && itemGrade > charm.RequireGradeMax)
            return false;

        return true;
    }

    /// <summary>
    /// Regrade support slot: only charms with no scale gate. Temper additives share the same
    /// table but must not sit here (their scale band would skip the grade check and inflate odds).
    /// </summary>
    public static bool IsRegradeSupport(ItemGradeEnchantingSupport charm) =>
        charm != null && !HasScaleRequirement(charm);

    /// <summary>
    /// Tempering support slot: only scale-gated additives. Regrade charms (31/31) must not sit here.
    /// </summary>
    public static bool IsTemperSupport(ItemGradeEnchantingSupport charm) =>
        HasScaleRequirement(charm);

    /// <summary>
    /// Flat and proportional adjustment to one chance (per 10000). Both are optional and signed —
    /// break-suppressing charms ship a multiplier of -100.
    /// </summary>
    public static int AdjustChance(int baseChance, int? addRatio, int? addMul)
    {
        if (addRatio == null && addMul == null)
            return baseChance;

        var adjusted = baseChance + (addRatio ?? 0) + (int)(baseChance * ((addMul ?? 0) / 100.0));
        return Math.Clamp(adjusted, 0, 10000);
    }
}
