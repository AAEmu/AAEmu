using System;
using System.Linq;

using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Containers
{
    public class SlaveEquipmentContainer : EquipmentContainer
    {
        public SlaveEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit) : base(ownerId, containerType, createWithNewId, parentUnit)
        {
            // Fancy way of getting the last enum value + 1 for equipment slots
            ContainerSize = (int)Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max() + 1;
        }

        //public override void OnEnterContainer(Item item, ItemContainer lastContainer, byte previousSlot)
        //{
        //    base.OnEnterContainer(item, lastContainer, previousSlot); // base EquipmentContainer

        //    // Extra pockets for slaves
        //    if (ParentUnit is not Units.Slave slave)
        //    {
        //        return;
        //    }

        //    var slaveItem = new ItemAndLocation()
        //    {
        //        Item = null,
        //        SlotType = item.SlotType, // newContainer
        //        SlotNumber = (byte)item.Slot,
        //    };
        //    var inventoryItem = new ItemAndLocation()
        //    {
        //        Item = item,
        //        SlotType = ContainerType,
        //        SlotNumber = previousSlot,
        //    };
        //    // Owner.SendMessage($"SlaveEquipmentContainer - {slaveItem} -> {inventoryItem}, SlaveTl: {slave.TlId}");
        //    //Owner.SendPacket(new SCSlaveEquipmentChangedPacket(slaveItem, inventoryItem, slave.TlId, Owner.Id, 0, false, true, DateTime.MinValue));
        //}

        //public override void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot)
        //{
        //    base.OnLeaveContainer(item, newContainer, previousSlot); // base EquipmentContainer

        //    // Extra pockets for slaves
        //    if (ParentUnit is not Units.Slave slave)
        //    {
        //        return;
        //    }

        //    var slaveItem = new ItemAndLocation()
        //    {
        //        Item = null,
        //        SlotType = item.SlotType, // newContainer
        //        SlotNumber = (byte)item.Slot,
        //    };
        //    var inventoryItem = new ItemAndLocation()
        //    {
        //        Item = item,
        //        SlotType = ContainerType,
        //        SlotNumber = previousSlot,
        //    };
        //    // Owner.SendMessage($"SlaveEquipmentContainer - {slaveItem} -> {inventoryItem}, SlaveTl: {slave.TlId}");
        //    //Owner.SendPacket(new SCSlaveEquipmentChangedPacket(slaveItem, inventoryItem, slave.TlId, Owner.Id, 0, false, true, DateTime.MinValue));
        //}
    }
}
