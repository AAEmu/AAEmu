namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Wire shape for guild residences.
/// <list type="bullet">
/// <item>
/// <c>SCHouseData</c> (owned-house list) must use
/// <see cref="HousingWireMarkers.ExpeditionResidenceOwnerId"/> so the client caches the
/// residence tl into <c>GetExpeditionHouseId</c>.
/// </item>
/// <item>
/// <c>SCHouseState</c> (placed unit) must carry the real master character id. The client's
/// decorate gate remaps any owner id below 1000 to 0 (deny) unless it is the 601 marker, and
/// the 601 path then requires a per-unit owner field the house stream never fills — so a
/// sub-1000 master id fails either way. Character ids therefore allocate at 1001+
/// (<see cref="AAEmu.Game.Core.Managers.Id.CharacterIdManager"/>).
/// </item>
/// </list>
/// The owner name string is what the info panel labels "Current Guild", so it must be the
/// expedition name on both packets.
/// </summary>
public static class HousingResidenceWireRules
{
    /// <summary>Owner id on <c>SCHouseData</c> list rows (guild marker or character id).</summary>
    public static ulong WireOwnerIdForList(uint ownerId, bool isExpeditionResidence) =>
        isExpeditionResidence
            ? HousingWireMarkers.ExpeditionResidenceOwnerId
            : ownerId;

    /// <summary>Owner id on <c>SCHouseState</c> / live unit body (always the character master id).</summary>
    public static ulong WireOwnerIdForState(uint ownerId) => ownerId;

    /// <summary>Deprecated alias — prefer <see cref="WireOwnerIdForList"/>.</summary>
    public static ulong WireOwnerId(uint ownerId, bool isExpeditionResidence) =>
        WireOwnerIdForList(ownerId, isExpeditionResidence);

    public static string WireOwnerName(string characterName, string expeditionName, bool isExpeditionResidence)
    {
        if (isExpeditionResidence)
        {
            if (!string.IsNullOrEmpty(expeditionName))
                return expeditionName;
        }

        return characterName ?? string.Empty;
    }
}
