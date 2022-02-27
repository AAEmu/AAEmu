using System;
using System.Linq;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class EquipmentContainer : ItemContainer
    {
        public EquipmentContainer(Character owner, SlotType containerType, bool isPartOfPlayerInventory) : base(owner, containerType, isPartOfPlayerInventory)
        {
            ContainerSize = (int)(Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max()) + 1;
        }

        public override bool CanAccept(Item item, int targetSlot)
        {
            if (item == null)
                return true; // allow empty slot
            if ((targetSlot < 0) || (targetSlot >= ContainerSize))
                return false; // must be in equipment slot range
            if (!(item.Template is EquipItemTemplate equipItemTemplate))
                return false; // must be a equip-able item
            
            // TODO: Check if target slot matches the item template's slot
            
            return true;
        }
    }
}
