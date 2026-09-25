namespace AAEmu.Game.Models.Game.OpenPortal;

/// <summary>
/// Content ordering for the reagent rows consumed by an open-portal effect.
/// The <c>priority</c> column is the shipped order; the row id only makes ties deterministic.
/// </summary>
public static class OpenPortalReagentRules
{
    public static IReadOnlyList<OpenPortalReagents> ForEffect(
        IEnumerable<OpenPortalReagents> reagents,
        uint openPortalEffectId) =>
        reagents
            .Where(reagent => reagent.OpenPortalEffectId == openPortalEffectId)
            .OrderBy(reagent => reagent.Priority)
            .ThenBy(reagent => reagent.Id)
            .ToArray();
}
