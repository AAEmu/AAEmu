using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class MateEquipmentContainer : EquipmentContainer
{
    public MateEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit) : base(ownerId, containerType, createWithNewId, parentUnit)
    {
        // Fancy way of getting the last enum value + 1 for equipment slots
        ContainerSize = (int)Enum.GetValues<EquipmentItemSlot>().Max() + 1;
    }

    public override void OnEnterContainer(Item item, ItemContainer lastContainer, byte previousSlot)
    {
        base.OnEnterContainer(item, lastContainer, previousSlot);

        // The request handler owns the single equipment-change reply.
    }

    public override void OnLeaveContainer(Item item, ItemContainer newContainer, byte previousSlot)
    {
        base.OnLeaveContainer(item, newContainer, previousSlot);

        // Reply owned by CSChangeMateEquipmentPacket — see OnEnterContainer.
    }
}
