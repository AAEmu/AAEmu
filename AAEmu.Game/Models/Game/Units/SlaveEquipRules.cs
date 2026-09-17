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
    /// <summary>Whether a position that takes these kinds takes an item of that kind.</summary>
    public static bool PositionTakesKind(IReadOnlyCollection<uint> slotKinds, uint itemKind)
    {
        if (slotKinds == null || slotKinds.Count == 0)
            return true;

        return itemKind != 0 && slotKinds.Contains(itemKind);
    }
}
