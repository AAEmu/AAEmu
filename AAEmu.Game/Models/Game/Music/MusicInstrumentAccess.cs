using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Models.Game.Music;

/// <summary>
/// Who may play through a placed instrument doodad.
/// </summary>
/// <remarks>
/// The gate sits on the play, not on the seat: sitting has no permission check of its own
/// (<c>DoodadFuncAttachment</c> just bonds whoever asked), so this is the point where a stranger at
/// somebody's piano is turned away. The house branch is the same test <c>DoodadFuncUse</c> runs for
/// a house doodad; where the doodad claims a house that no longer resolves, the answer is no rather
/// than public — the refusal has to be definitive, and a dangling owner is not permission.
/// </remarks>
public static class MusicInstrumentAccess
{
    /// <summary>Permission check with the doodad's own house resolved through <see cref="HousingManager"/>.</summary>
    public static bool MayPlayThrough(Character player, Doodad instrument) =>
        MayPlayThrough(player, instrument, ResolveHouse(instrument));

    /// <summary>The house a house-owned doodad points at, or null when it owns no house id.</summary>
    public static House ResolveHouse(Doodad instrument) =>
        instrument != null && instrument.OwnerDbId > 0
            ? HousingManager.Instance.GetHouseById(instrument.OwnerDbId)
            : null;

    /// <summary>
    /// The decision itself, with the house resolved by the caller so it can be exercised without a
    /// world: a house-owned doodad answers through the house's permission, a character-owned one
    /// only for its owner, and a system (world-spawned) one for everyone.
    /// </summary>
    public static bool MayPlayThrough(Character player, Doodad instrument, House house)
    {
        if (player == null || instrument == null)
            return false;

        if (instrument.OwnerDbId > 0)
            return house != null && house.AllowedToInteract(player);

        return instrument.OwnerType switch
        {
            DoodadOwnerType.Character => instrument.OwnerId == player.Id,
            // Claims a house but names none to ask: not permission.
            DoodadOwnerType.Housing => false,
            _ => true,
        };
    }
}
