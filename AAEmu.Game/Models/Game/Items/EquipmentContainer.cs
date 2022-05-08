using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;
using NLog;

namespace AAEmu.Game.Models.Game.Items
{
    public class EquipmentContainer : ItemContainer
    {
        public EquipmentContainer(Character owner, SlotType containerType, bool isPartOfPlayerInventory) : base(owner,
            containerType, isPartOfPlayerInventory)
        {
            // Fancy way of getting the last enum value + 1 for equipment slots
            ContainerSize = (int)(Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max()) + 1;
        }

        public override bool CanAccept(Item item, int targetSlot)
        {
            if (item == null)
                return true; // always allow empty item slot (un-equip a item)

            if ((targetSlot < 0) || (targetSlot >= ContainerSize))
            {
                _log.Warn($"{Owner.Name} ({Owner.Id}) tried to equip a item that is out of range of the valid slots {targetSlot}/{ContainerSize}");
                return false; // must be in equipment slot range
            }

            var slotTypeId = (EquipmentItemSlotType)0; // Dummy value for invalid

            if (item.Template is WeaponTemplate weaponTemplate)
                slotTypeId = (EquipmentItemSlotType)weaponTemplate.HoldableTemplate.SlotTypeId;
            else if (item.Template is ArmorTemplate armorTemplate)
                slotTypeId = (EquipmentItemSlotType)armorTemplate.WearableTemplate.SlotTypeId;
            else if (item.Template is AccessoryTemplate accessoryTemplate)
                slotTypeId = (EquipmentItemSlotType)accessoryTemplate.WearableTemplate.SlotTypeId;
            else
            {
                _log.Warn($"{Owner.Name} ({Owner.Id}) tried to equip a non-equipable item {item.Template.Name} ({item.TemplateId}), Id:{item.Id}");
                return false; // must be a equip-able item
            }

            // No expected slot was defined, we can't accept that here
            if (slotTypeId == (EquipmentItemSlotType)0)
                return false;

            var equipSlot = (EquipmentItemSlot)targetSlot;
            var allowedSlots = new List<EquipmentItemSlot>();
            switch (slotTypeId)
            {
                case EquipmentItemSlotType.Head:
                    allowedSlots.Add(EquipmentItemSlot.Head);
                    break;
                case EquipmentItemSlotType.Neck:
                    allowedSlots.Add(EquipmentItemSlot.Neck);
                    break;
                case EquipmentItemSlotType.Chest:
                    allowedSlots.Add(EquipmentItemSlot.Chest);
                    break;
                case EquipmentItemSlotType.Waist:
                    allowedSlots.Add(EquipmentItemSlot.Waist);
                    break;
                case EquipmentItemSlotType.Legs:
                    allowedSlots.Add(EquipmentItemSlot.Legs);
                    break;
                case EquipmentItemSlotType.Hands:
                    allowedSlots.Add(EquipmentItemSlot.Hands);
                    break;
                case EquipmentItemSlotType.Feet:
                    allowedSlots.Add(EquipmentItemSlot.Feet);
                    break;
                case EquipmentItemSlotType.Arms:
                    allowedSlots.Add(EquipmentItemSlot.Arms);
                    break;
                case EquipmentItemSlotType.Back:
                    allowedSlots.Add(EquipmentItemSlot.Back);
                    break;
                case EquipmentItemSlotType.Ear:
                    allowedSlots.Add(EquipmentItemSlot.Ear1);
                    allowedSlots.Add(EquipmentItemSlot.Ear2);
                    break;
                case EquipmentItemSlotType.Finger:
                    allowedSlots.Add(EquipmentItemSlot.Finger1);
                    allowedSlots.Add(EquipmentItemSlot.Finger2);
                    break;
                case EquipmentItemSlotType.Undershirt:
                    allowedSlots.Add(EquipmentItemSlot.Undershirt);
                    break;
                case EquipmentItemSlotType.Underpants:
                    allowedSlots.Add(EquipmentItemSlot.Underpants);
                    break;
                case EquipmentItemSlotType.Mainhand:
                    allowedSlots.Add(EquipmentItemSlot.Mainhand);
                    break;
                case EquipmentItemSlotType.Offhand:
                    allowedSlots.Add(EquipmentItemSlot.Offhand);
                    break;
                case EquipmentItemSlotType.TwoHanded:
                    allowedSlots.Add(EquipmentItemSlot.Mainhand);
                    break;
                case EquipmentItemSlotType.OneHanded:
                    allowedSlots.Add(EquipmentItemSlot.Mainhand);
                    allowedSlots.Add(EquipmentItemSlot.Offhand);
                    break;
                case EquipmentItemSlotType.Ranged:
                    allowedSlots.Add(EquipmentItemSlot.Ranged);
                    break;
                case EquipmentItemSlotType.Ammunition:
                    // allowedSlots.Add(EquipmentItemSlot.Ammunition);
                    break;
                case EquipmentItemSlotType.Shield:
                    allowedSlots.Add(EquipmentItemSlot.Offhand);
                    break;
                case EquipmentItemSlotType.Instrument:
                    allowedSlots.Add(EquipmentItemSlot.Musical);
                    break;
                case EquipmentItemSlotType.Bag:
                    // allowedSlots.Add(EquipmentItemSlot.Bag);
                    break;
                case EquipmentItemSlotType.Face:
                    allowedSlots.Add(EquipmentItemSlot.Face);
                    break;
                case EquipmentItemSlotType.Hair:
                    allowedSlots.Add(EquipmentItemSlot.Hair);
                    break;
                case EquipmentItemSlotType.Glasses:
                    allowedSlots.Add(EquipmentItemSlot.Glasses);
                    break;
                case EquipmentItemSlotType.Reserved:
                    allowedSlots.Add(EquipmentItemSlot
                        .Reserved); // maybe for Warborn horns or other race specifics ? I dunno 
                    break;
                case EquipmentItemSlotType.Tail:
                    allowedSlots.Add(EquipmentItemSlot.Tail); // Firran and Warborn tails ?
                    break;
                case EquipmentItemSlotType.Body:
                    allowedSlots.Add(EquipmentItemSlot.Body);
                    break;
                case EquipmentItemSlotType.Beard:
                    allowedSlots.Add(EquipmentItemSlot.Beard); // Mostly for Dwarves I'd assume
                    break;
                case EquipmentItemSlotType.Backpack:
                    allowedSlots.Add(EquipmentItemSlot.Backpack);
                    break;
                case EquipmentItemSlotType.Cosplay:
                    allowedSlots.Add(EquipmentItemSlot.Cosplay);
                    break;
                default:
                    _log.Warn($"{Owner.Name} ({Owner.Id}) tried to equip a item with no valid defined SlotType {item.Template.Name} ({item.TemplateId}), SlotType:{slotTypeId}");
                    return false; // unknown slot
            }

            if (!allowedSlots.Contains(equipSlot))
            {
                _log.Warn($"{Owner.Name} ({Owner.Id}) tried to equip a item in the wrong slot {item.Template.Name} ({item.TemplateId}), Id:{item.Id}, SlotType: {equipSlot}, TargetSlot:{(EquipmentItemSlot)targetSlot}");
                return false; // not in the list of allowed slots
            }

            return true;
        }
    }
}
