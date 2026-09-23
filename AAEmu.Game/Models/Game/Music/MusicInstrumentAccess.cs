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
    /// <summary>Permission check with the doodad's owner resolved from its <see cref="Doodad.OwnerType"/>.</summary>
    public static bool MayPlayThrough(Character player, Doodad instrument) =>
        MayPlayThrough(player, instrument, ResolveHouse(instrument), ResolveSlaveOwnerId(instrument));

    /// <summary>The house a house-owned doodad points at, or null when it is not a house doodad.</summary>
    public static House ResolveHouse(Doodad instrument) =>
        instrument is { OwnerType: DoodadOwnerType.Housing, OwnerDbId: > 0 }
            ? HousingManager.Instance.GetHouseById(instrument.OwnerDbId)
            : null;

    /// <summary>
    /// The character who owns the slave a slave-mounted doodad is attached to. The slave is looked up
    /// by the doodad's owner database id; when that slave is not in the world, the character id stamped
    /// on the doodad is used. Zero when the doodad is not a slave's.
    /// </summary>
    public static uint ResolveSlaveOwnerId(Doodad instrument)
    {
        if (instrument is not { OwnerType: DoodadOwnerType.Slave })
            return 0;

        var fromSlave = instrument.OwnerDbId > 0
            ? instrument.ParentWorld?.SlaveManager?.FindSlaveByDbId(instrument.OwnerDbId)?.GetOwnerCharacter()?.Id ?? 0
            : 0;
        return fromSlave != 0 ? fromSlave : instrument.OwnerId;
    }

    /// <summary>
    /// The decision itself, with the owner resolved by the caller so it can be exercised without a
    /// world. A house doodad answers through the house's permission, a slave doodad only for the
    /// slave's owner, a character-owned one only for that character, and a system one for everyone.
    /// </summary>
    public static bool MayPlayThrough(Character player, Doodad instrument, House house, uint slaveOwnerCharacterId)
    {
        if (player == null || instrument == null)
            return false;

        return instrument.OwnerType switch
        {
            DoodadOwnerType.Housing => house != null && house.AllowedToInteract(player),
            DoodadOwnerType.Slave => slaveOwnerCharacterId != 0 && slaveOwnerCharacterId == player.Id,
            DoodadOwnerType.Character => instrument.OwnerId == player.Id,
            _ => true,
        };
    }
}
