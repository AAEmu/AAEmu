using AAEmu.Game.Models.Game.Faction;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Housing-family checks. Guild residences are the <c>housings.family</c> prefix the compact already
/// uses for that set — not a compiled list of design ids.
/// </summary>
public static class HousingResidenceRules
{
    /// <summary>Shipped <c>housings.family</c> prefix for guild residences (complete + any later variants).</summary>
    public const string ExpeditionHouseFamilyPrefix = "hs_expedition_house";

    public static bool IsExpeditionResidenceFamily(string family) =>
        !string.IsNullOrEmpty(family)
        && family.StartsWith(ExpeditionHouseFamilyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Guild residences must carry the expedition object as <c>Faction</c>. System-faction lookup by
    /// expedition id misses every dynamic guild, so the house unit would have no expedition id and
    /// the client refuses decorate (IsMyHouse / no_perm).
    /// </summary>
    public static SystemFaction AssignExpeditionResidenceFaction(SystemFaction current, SystemFaction expedition)
    {
        if (expedition == null)
            return current;
        if (current != null && current.Id == expedition.Id)
            return current;
        return expedition;
    }

    /// <summary>
    /// Prefer the guild that still names this house as its residence. The placer's current
    /// membership is only a fallback (placement, before <c>residence_house_id</c> is written).
    /// </summary>
    public static SystemFaction ResolveOwningExpedition(
        SystemFaction residenceNamedGuild,
        SystemFaction ownerCurrentGuild) =>
        residenceNamedGuild ?? ownerCurrentGuild;
}
