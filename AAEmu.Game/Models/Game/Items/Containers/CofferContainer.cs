using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class CofferContainer(uint ownerId, bool createWithNewId)
    : ItemContainer(ownerId, SlotType.Trade, createWithNewId, null)
{
    public byte CofferPermission { get; set; } = 0;
    public ChestType CofferType { get; set; }
    public bool IsPrivateCoffer { get; set; }
    public IReadOnlySet<int> AllowedItemCategoryIds { get; set; } = new HashSet<int>();
    public DoodadCoffer Doodad { get; set; }

    // Coffers are considered trade windows in the item manipulation code

    private bool CanAcceptTemplate(ItemTemplate itemTemplate)
    {
        // All Chests will not accept timed items 
        if (itemTemplate.ExpAbsLifetime > 0 ||
            itemTemplate.ExpOnlineLifetime > 0 ||
            itemTemplate.ExpDate > DateTime.MinValue)
            return false;

        if (AllowedItemCategoryIds.Count > 0 && !AllowedItemCategoryIds.Contains(itemTemplate.CategoryId))
            return false;

        // Otherwordly Storage Chest will accept pretty much any other item
        if (CofferType == ChestType.Otherworldly || IsPrivateCoffer)
            return true;

        // Normal Coffer/Chest will accept anything that can't be bound 
        if (itemTemplate.BindType == ItemBindType.BindOnPickup)
            return false;
        if (itemTemplate.BindType == ItemBindType.BindOnPickupPack)
            return false;

        // All other cases should be good (if the item itself isn't bound yet)
        return true;
    }

    public override bool CanAccept(Item item, int targetSlot)
    {
        return item == null ||
               ((IsPrivateCoffer || CofferType == ChestType.Otherworldly || !item.HasFlag(ItemFlag.SoulBound)) &&
                CanAcceptTemplate(item.Template) &&
                base.CanAccept(item, targetSlot));
    }

    public override void Delete()
    {
        // Destroy associated items if any left in this coffer
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            var item = Items[i];
            Logger.Warn($"Destroying item {item.Id} from coffer item_container {ContainerId} due to delete");
            item._holdingContainer.RemoveItem(ItemTaskType.Invalid, item, true);
        }

        // Delete container
        base.Delete();
    }
}
