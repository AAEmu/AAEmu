using System;
using System.Linq;

using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Containers
{
    public class MateEquipmentContainer : EquipmentContainer
    {
        public MateEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit) : base(ownerId, containerType, createWithNewId, parentUnit)
        {
            // Fancy way of getting the last enum value + 1 for equipment slots
            ContainerSize = (int)Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max() + 1;
        }

        public override void OnEnterContainer(Item item, ItemContainer lastContainer, byte previousSlot)
        {
            base.OnEnterContainer(item, lastContainer, previousSlot); // base EquipmentContainer

            // Extra pockets for mates
            if (ParentUnit is not Units.Mate mate)
            {
                return;
            }

            var petItem = new ItemAndLocation()
            {
                Item = item,
                SlotType = lastContainer.ContainerType, // ContainerType,
                SlotNumber = previousSlot,
            };
            var inventoryItem = new ItemAndLocation()
            {
                Item = null,
                SlotType = ContainerType,
                SlotNumber = (byte)item.Slot,
            };
            // Owner.SendMessage($"MateEquipmentContainer - {petItem} -> {inventoryItem}, MateTl: {mate.TlId}");
            Owner.SendPacket(new SCMateEquipmentChangedPacket(petItem, inventoryItem, mate.TlId, Owner.Id, 0, false, true, DateTime.MinValue));
        }

        public override void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot)
        {
            base.OnLeaveContainer(item, newContainer, previousSlot); // base EquipmentContainer

            // Extra pockets for mates
            if (ParentUnit is not Units.Mate mate)
            {
                return;
            }

            var petItem = new ItemAndLocation()
            {
                Item = null,
                SlotType = item.SlotType, // newContainer
                SlotNumber = (byte)item.Slot,
            };
            var inventoryItem = new ItemAndLocation()
            {
                Item = item,
                SlotType = ContainerType,
                SlotNumber = previousSlot,
            };
            // Owner.SendMessage($"MateEquipmentContainer - {petItem} -> {inventoryItem}, MateTl: {mate.TlId}");
            Owner.SendPacket(new SCMateEquipmentChangedPacket(petItem, inventoryItem, mate.TlId, Owner.Id, 0, false, true, DateTime.MinValue));
        }
    }
}
