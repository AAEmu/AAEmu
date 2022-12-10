using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items.Containers
{
    public enum ChestType
    {
        Otherworldly = 0, // private character bound items only
        Chest = 1, // normal house storage chest with settable permissions
    }
    
    public class CofferContainer : ItemContainer
    {
        public byte CofferPermission { get; set; }
        public ChestType CofferType { get; set; }
        
        public CofferContainer(uint ownerId, bool isPartOfPlayerInventory, bool createWithNewId) : base(ownerId, SlotType.Trade, isPartOfPlayerInventory, createWithNewId)
        {
            // Coffers are considered trade windows in the item manipulation code
            CofferPermission = 0;
        }

        private bool CanAcceptTemplate(ItemTemplate itemTemplate)
        {
            // All Chests will not accept timed items 
            if ((itemTemplate.ExpAbsLifetime > 0) || 
                (itemTemplate.ExpOnlineLifetime > 0) ||
                (itemTemplate.ExpDate > DateTime.MinValue))
                return false;

            // Otherwordly Storage Chest will accept pretty much any other item
            if (CofferType == ChestType.Otherworldly)
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
            return (item == null) || (!item.HasFlag(ItemFlag.SoulBound) && 
                   CanAcceptTemplate(item.Template) && 
                   base.CanAccept(item, targetSlot));
        }

        public override void Delete()
        {
            // Destroy associated items if any left in this coffer
            for (var i = Items.Count - 1; i >= 0; i--)
            {
                var item = Items[i]; 
                _log.Warn($"Destroying item {item.Id} from coffer item_container {ContainerId} due to delete");
                item._holdingContainer.RemoveItem(ItemTaskType.Invalid, item, true);
            }
            
            // Delete container
            base.Delete();
        }
    }
}
