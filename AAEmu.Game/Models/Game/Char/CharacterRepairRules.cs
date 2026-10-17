using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Bag repair is a Patron perk. The client button is <c>itemRepairInBag</c> (fset 92);
/// fset 97 would also demand special-effect 121, which this compact does not ship.
/// </summary>
/// <remarks>
/// CS <c>inBag</c> is that authority bit, not "repair items sitting in the bag". The client's
/// repair-all collector only walks paper-doll slots, so <see cref="RepairAllUsesEquipment"/> is
/// always true.
/// </remarks>
public static class CharacterRepairRules
{
    public static bool CanRepairWithoutBlacksmith(bool itemRepairInBag, bool isPaidPatron) =>
        itemRepairInBag && isPaidPatron;

    /// <summary>
    /// <c>CSRepairAllEquipments.inBag</c> never selects Inventory as the item source.
    /// </summary>
    public static bool RepairAllUsesEquipment(bool inBagAuthority) => true;

    /// <summary>
    /// A piece with no max durability is not repairable. Restoring it to 0 would brick it.
    /// </summary>
    public static bool NeedsRepair(EquipItem item) =>
        item != null && item.MaxDurability > 0 && item.Durability < item.MaxDurability;

    /// <summary>
    /// Server-side durability already matches max, but the client may still paint a worn bar /
    /// non-zero <c>RepairAllCost</c> from a stale detail. Re-pushing the detail is free and
    /// brings the HUD back in line without inventing a charge.
    /// </summary>
    public static bool ShouldResyncDetail(EquipItem item) =>
        item != null && item.MaxDurability > 0;

    public static bool TryRestore(EquipItem item)
    {
        if (!NeedsRepair(item))
            return false;
        item.Durability = item.MaxDurability;
        item.IsDirty = true;
        return true;
    }
}
