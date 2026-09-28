using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Bag repair is a Patron perk. The client button is <c>itemRepairInBag</c> (fset 92);
/// fset 97 would also demand special-effect 121, which this compact does not ship.
/// </summary>
public static class CharacterRepairRules
{
    public static bool CanRepairWithoutBlacksmith(bool itemRepairInBag, bool isPaidPatron) =>
        itemRepairInBag && isPaidPatron;

    public static bool CanRepairAtNpc(NpcTemplate npc) => npc?.Repairman == true;

    /// <summary>
    /// A piece with no max durability is not repairable. Restoring it to 0 would brick it.
    /// </summary>
    public static bool NeedsRepair(EquipItem item) =>
        item != null && item.MaxDurability > 0 && item.Durability < item.MaxDurability;

    public static bool TryRestore(EquipItem item)
    {
        if (!NeedsRepair(item))
            return false;
        item.Durability = item.MaxDurability;
        item.IsDirty = true;
        return true;
    }
}
