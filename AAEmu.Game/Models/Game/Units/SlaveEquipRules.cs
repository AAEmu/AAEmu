using System.Linq;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Whether an item fits a slave's or a mate's equipment position. The tables say so:
/// <c>slave_equip_kind_lists</c> gives each position — named by its <c>slave_equip_slots</c> row — the
/// kinds it takes, and an item's kind is <c>item_slave_equipments.slave_equip_kind_id</c>.
/// </summary>
/// <remarks>
/// A position that lists no kinds says nothing, so anything its template allows is taken; the tables are
/// silent there rather than restrictive. An item the tables give no kind at all is not slave equipment, so
/// a position that does list kinds does not take it.
/// </remarks>
public static class SlaveEquipRules
{
    /// <summary>
    /// <c>slave_equip_kinds</c> 1-13 are named groups. Item rows use 4 or 14-70, so a position that
    /// lists only a group can never match an item kind. Those positions are left open rather than
    /// inventing a group-to-member map the tables do not carry.
    /// </summary>
    public static bool IsGroupKind(uint kind) => kind is >= 1 and <= 13;

    /// <summary>Whether a position that takes these kinds takes an item of that kind.</summary>
    public static bool PositionTakesKind(IReadOnlyCollection<uint> slotKinds, uint itemKind)
    {
        if (slotKinds == null || slotKinds.Count == 0)
            return true;

        if (itemKind != 0 && slotKinds.Contains(itemKind))
            return true;

        return itemKind != 0 && slotKinds.Any(IsGroupKind);
    }

    /// <summary>
    /// Whether a slave may use an item's equipment pack. An item that belongs to no pack, or a slave the
    /// table says nothing about, is left to the position's own rule rather than turned away here.
    /// </summary>
    public static bool PackAllowed(uint itemPackId, IReadOnlyCollection<uint> slavePacks) =>
        PackAllowed(itemPackId == 0 ? [] : [itemPackId], slavePacks);

    /// <summary>
    /// Whether any of an item's packs is allowed on the slave. An item lists several packs in
    /// <c>item_slave_equipment_slave_equipslot_packs</c>; the single column on
    /// <c>item_slave_equipments</c> is not enough.
    /// </summary>
    public static bool PackAllowed(IEnumerable<uint> itemPackIds, IReadOnlyCollection<uint> slavePacks)
    {
        if (slavePacks == null || slavePacks.Count == 0)
            return true;

        var packs = (itemPackIds ?? []).Where(id => id != 0).Distinct().ToList();
        if (packs.Count == 0)
            return true;

        return packs.Any(slavePacks.Contains);
    }
}
