using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Pure vocation rank rules. Production and language are separate ladders; hidden rows stay off both.
/// </summary>
public static class ExpertLimitRules
{
    public static bool UsesLanguageLadder(uint actabilityId) =>
        actabilityId is >= (uint)ActabilityType.NuianLanguage
            and <= (uint)ActabilityType.HaranyaContinentDialect;

    public static bool CountsTowardProductionSlots(uint actabilityId, bool countsTowardExpertLimit) =>
        countsTowardExpertLimit && !UsesLanguageLadder(actabilityId);

    /// <summary>
    /// Shown rows only. Language-flagged rows get their own 0-based index so they cannot sit
    /// between production ranks when the source table is ordered by point cap.
    /// </summary>
    public static void IndexShownRow(
        ExpertLimit row,
        IDictionary<int, ExpertLimit> production,
        IDictionary<int, ExpertLimit> language)
    {
        if (row == null || !row.Show)
            return;

        var map = row.UseLanguageType ? language : production;
        map.Add(map.Count, row);
    }

    public static int ClampPoints(ExpertLimit limit, int point)
    {
        if (limit == null)
            return Math.Max(0, point);
        return Math.Clamp(point, 0, limit.UpLimit);
    }

    /// <summary>
    /// Earning is capped by the selected rank. Already-earned points above that cap stay put
    /// so a downgrade only frees a slot — the total can be spent again on the way back up.
    /// </summary>
    public static int AddEarnedPoints(int current, int delta, int cap)
    {
        if (delta == 0)
            return Math.Max(0, current);
        if (delta < 0)
            return Math.Max(0, current + delta);
        if (current >= cap)
            return current;
        return Math.Min(cap, current + delta);
    }

    public static ErrorMessageType? UpgradeError(ExpertLimit current, ExpertLimit next, int points, bool hasSlot)
    {
        if (current == null)
            return ErrorMessageType.ActabilityCanUpgradeAnyMore;
        if (points < current.UpLimit)
            return ErrorMessageType.ActabilityNotEnoughPoint;
        if (next == null)
            return ErrorMessageType.ActabilityCanUpgradeAnyMore;
        if (!hasSlot)
            return ErrorMessageType.ActabilityCanUpgradeSelectionCountLimit;
        return null;
    }

    public static ErrorMessageType? DowngradeError(byte step, ExpertLimit current, ExpertLimit next)
    {
        if (step == 0 || current == null || next == null)
            return ErrorMessageType.ActabilityCanDowngradeAnyMore;
        return null;
    }

    /// <summary>
    /// Intensified ranks (대가 and the hidden rows above it) spend
    /// <c>downgrade_intensified_expert_ticket</c> (item 49001) to drop one step. Famed and below do not.
    /// </summary>
    public const int IntensifiedDowngradeTicketCount = 1;

    public static bool RequiresIntensifiedDowngradeTicket(ExpertLimit current) =>
        current is { UseIntensified: true };

    public static ErrorMessageType? DowngradeTicketError(ExpertLimit current, uint ticketItemId, bool hasTicket)
    {
        if (!RequiresIntensifiedDowngradeTicket(current))
            return null;
        if (ticketItemId == 0)
            return ErrorMessageType.Invalid;
        if (!hasTicket)
            return ErrorMessageType.NotEnoughItem;
        return null;
    }

    public static ErrorMessageType? ExpandError(ExpandExpertLimit next, int vocationPoint, bool hasRequiredItems)
    {
        if (next == null)
            return ErrorMessageType.ActabilityCanUpgradeAnyMore;
        if (next.LifePoint > vocationPoint)
            return ErrorMessageType.NotEnoughLivingPoint;
        if (!hasRequiredItems)
            return ErrorMessageType.NotEnoughItem;
        return null;
    }

    /// <summary>
    /// Slot check against the <em>next</em> rank. A zero <see cref="ExpertLimit.ExpertLimitCount"/>
    /// is unlimited. Expanded slots add to that count except on intensified ranks, which use the
    /// per-view-group cap only.
    /// </summary>
    public static bool HasSelectionSlot(
        IEnumerable<Actability> actabilities,
        ExpertLimit target,
        int targetStep,
        byte expandedExpert,
        uint viewGroupId)
    {
        if (target == null)
            return false;

        if (target.UseIntensified)
        {
            if (!target.IntensifiedViewGroupLimits.TryGetValue(viewGroupId, out var groupLimit))
                return false;

            var groupCount = CountAtOrAbove(actabilities, targetStep, viewGroupId);
            return groupCount < groupLimit;
        }

        if (target.ExpertLimitCount == 0)
            return true;

        var selectedCount = CountAtOrAbove(actabilities, targetStep, viewGroupId: null);
        return selectedCount < target.ExpertLimitCount + expandedExpert;
    }

    private static int CountAtOrAbove(IEnumerable<Actability> actabilities, int targetStep, uint? viewGroupId)
    {
        var count = 0;
        foreach (var entry in actabilities)
        {
            if (entry?.Template == null)
                continue;
            if (!CountsTowardProductionSlots(entry.Template.Id, entry.Template.CountsTowardExpertLimit))
                continue;
            if (entry.Step < targetStep)
                continue;
            if (viewGroupId.HasValue && entry.Template.ViewGroupId != viewGroupId.Value)
                continue;
            count++;
        }

        return count;
    }
}
