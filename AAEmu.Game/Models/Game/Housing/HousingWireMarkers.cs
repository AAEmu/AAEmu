namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Wire-only owner ids the client treats as non-character house owners.
/// Temp housing / rotate preview uses 600; guild residences use 601 so
/// <c>SCHouseData</c> can set <c>X2Faction:GetExpeditionHouseId</c> from the entry's tl.
/// </summary>
public static class HousingWireMarkers
{
    /// <summary>Temporary / preview house owner on the SC house list wire.</summary>
    public const ulong TempPreviewOwnerId = 600;

    /// <summary>Guild residence owner on the SC house list wire.</summary>
    public const ulong ExpeditionResidenceOwnerId = 601;
}
