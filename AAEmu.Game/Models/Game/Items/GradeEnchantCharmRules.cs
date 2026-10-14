namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// Whether a regrade charm may sit on a given piece of gear.
/// </summary>
/// <remarks>
/// <c>restrict_item_tag_id</c> is a blacklist (the charm is refused when the gear carries that tag —
/// shipped as tag 6059 on the Ipnysh earring set). <c>exclusive_item_tag_id</c> is a whitelist (the
/// gear must carry that tag). Scale bounds are <c>enchant_scale_ratios</c> row ids compared to the
/// piece's tempering step; <c>31/31</c> is the shipped "no scale gate" sentinel because the ladder
/// tops out at 30.
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
}
