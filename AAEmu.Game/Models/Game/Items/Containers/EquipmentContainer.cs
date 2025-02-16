using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class EquipmentContainer : ItemContainer
{
    public EquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit) : base(ownerId, containerType, createWithNewId, parentUnit)
    {
        // Fancy way of getting the last enum value + 1 for equipment slots
        ContainerSize = (int)(Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max()) + 1;
    }

    public static List<EquipmentItemSlot> GetAllowedGearSlots(EquipmentItemSlotType slotTypeId)
    {
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
            /*
            case EquipmentItemSlotType.Ammunition:
                allowedSlots.Add(EquipmentItemSlot.Ammunition);
                break;
            */
            case EquipmentItemSlotType.Shield:
                allowedSlots.Add(EquipmentItemSlot.Offhand);
                break;
            case EquipmentItemSlotType.Instrument:
                allowedSlots.Add(EquipmentItemSlot.Musical);
                break;
            /*
            case EquipmentItemSlotType.Bag:
                // allowedSlots.Add(EquipmentItemSlot.Bag);
                break;
            */
            case EquipmentItemSlotType.Face:
                allowedSlots.Add(EquipmentItemSlot.Face);
                break;
            case EquipmentItemSlotType.Hair:
                allowedSlots.Add(EquipmentItemSlot.Hair);
                break;
            case EquipmentItemSlotType.Glasses:
                allowedSlots.Add(EquipmentItemSlot.Glasses);
                break;
            case EquipmentItemSlotType.Horns:
                // maybe for Warborn horns or other race specifics ? I dunno
                allowedSlots.Add(EquipmentItemSlot.Horns);
                break;
            case EquipmentItemSlotType.Tail:
                // Firran and Warborn tails ?
                allowedSlots.Add(EquipmentItemSlot.Tail);
                break;
            case EquipmentItemSlotType.Body:
                allowedSlots.Add(EquipmentItemSlot.Body);
                break;
            case EquipmentItemSlotType.Beard:
                // Mostly for Dwarves I'd assume
                allowedSlots.Add(EquipmentItemSlot.Beard);
                break;
            case EquipmentItemSlotType.Backpack:
                allowedSlots.Add(EquipmentItemSlot.Backpack);
                break;
            case EquipmentItemSlotType.Cosplay:
                allowedSlots.Add(EquipmentItemSlot.Cosplay);
                break;
        }
        return allowedSlots;
    }

    public static List<EquipmentItemSlot> GetAllowedGearSlots(ItemTemplate itemTemplate)
    {
        var slotTypeId = (EquipmentItemSlotType)255; // Dummy value for invalid

        if (itemTemplate is BodyPartTemplate bodyPartTemplate)
            slotTypeId = (EquipmentItemSlotType)bodyPartTemplate.SlotTypeId;
        else if (itemTemplate is WeaponTemplate weaponTemplate)
            slotTypeId = (EquipmentItemSlotType)weaponTemplate.HoldableTemplate.SlotTypeId;
        else if (itemTemplate is ArmorTemplate armorTemplate)
            slotTypeId = (EquipmentItemSlotType)armorTemplate.WearableTemplate.SlotTypeId;
        else if (itemTemplate is AccessoryTemplate accessoryTemplate)
            slotTypeId = (EquipmentItemSlotType)accessoryTemplate.WearableTemplate.SlotTypeId;
        else if (itemTemplate is BackpackTemplate _)
            slotTypeId = EquipmentItemSlotType.Backpack;
        else
        {
            return new List<EquipmentItemSlot>(); // must be a equip-able item
        }

        return GetAllowedGearSlots(slotTypeId);
    }

    public override bool CanAccept(Item item, int targetSlot)
    {
        if (item == null)
            return true; // always allow empty item slot (un-equip a item)

        if (Owner == null)
            return true; // Not applicable to NPCs, they can hold whatever they want anywhere

        if (item.SlotType == SlotType.SlaveEquipment)
            return true; // they can hold whatever they want anywhere

        if (item.Template.ImplId == ItemImplEnum.SlaveEquipment)
            return true; // they can hold whatever they want anywhere

        if ((targetSlot < 0) || (targetSlot >= ContainerSize))
        {
            Logger.Warn($"{Owner?.Name} ({OwnerId}) tried to equip a item that is out of range of the valid slots {targetSlot}/{ContainerSize}");
            return false; // must be in equipment slot range
        }

        var slotTypeId = (EquipmentItemSlotType)255; // Dummy value for invalid

        if (item.Template is BodyPartTemplate bodyPartTemplate)
            slotTypeId = (EquipmentItemSlotType)bodyPartTemplate.SlotTypeId;
        else if (item.Template is WeaponTemplate weaponTemplate)
            slotTypeId = (EquipmentItemSlotType)weaponTemplate.HoldableTemplate.SlotTypeId;
        else if (item.Template is ArmorTemplate armorTemplate)
            slotTypeId = (EquipmentItemSlotType)armorTemplate.WearableTemplate.SlotTypeId;
        else if (item.Template is AccessoryTemplate accessoryTemplate)
            slotTypeId = (EquipmentItemSlotType)accessoryTemplate.WearableTemplate.SlotTypeId;
        else if (item.Template is BackpackTemplate _)
            slotTypeId = EquipmentItemSlotType.Backpack;
        else
        {
            Logger.Warn($"{Owner?.Name} ({OwnerId}) tried to equip a non-equipable item {item.Template.Name} ({item.TemplateId}), Id:{item.Id}");
            return false; // must be a equip-able item
        }

        // No expected slot was defined, we can't accept that here
        if (slotTypeId == (EquipmentItemSlotType)255)
        {
            Logger.Fatal($"{Owner?.Name} ({OwnerId}) tried to equip a equippable item that has no slot defined {item.Template.Name} ({item.TemplateId}), Id:{item.Id}, TargetSlot:{(EquipmentItemSlot)targetSlot}");
            return false;
        }

        var equipSlot = (EquipmentItemSlot)targetSlot;
        var allowedSlots = GetAllowedGearSlots(slotTypeId);

        if (!allowedSlots.Contains(equipSlot))
        {
            Logger.Warn($"{Owner?.Name} ({OwnerId}) tried to equip a item in the wrong slot {item.Template.Name} ({item.TemplateId}), Id:{item.Id}, SlotType: {equipSlot}, TargetSlot:{(EquipmentItemSlot)targetSlot}");
            return false; // not in the list of allowed slots, remove the item
        }

        return true;
    }

    public override void OnEnterContainer(Item item, ItemContainer lastContainer, byte previousSlot)
    {
        base.OnEnterContainer(item, lastContainer, previousSlot); 
        ParentUnit?.BroadcastPacket(new SCUnitEquipmentsChangedPacket(ParentUnit.ObjId, (byte)item.Slot, item), false);
        ParentUnit?.UpdateGearBonuses(item, null);
    }

    public override void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot)
    {
        base.OnLeaveContainer(item, newContainer, previousSlot);
        ParentUnit?.BroadcastPacket(new SCUnitEquipmentsChangedPacket(ParentUnit.ObjId, previousSlot, null), false);
        ParentUnit?.UpdateGearBonuses(null, item);
    }
}
