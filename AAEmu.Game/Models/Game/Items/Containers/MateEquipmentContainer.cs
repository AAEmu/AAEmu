using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Units;

// The Mate namespace sits beside this one, so the type needs naming apart from it.
using MateUnit = AAEmu.Game.Models.Game.Units.Mate;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class MateEquipmentContainer : EquipmentContainer
{
    public MateEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId, Unit parentUnit) : base(ownerId, containerType, createWithNewId, parentUnit)
    {
        // Fancy way of getting the last enum value + 1 for equipment slots
        ContainerSize = (int)Enum.GetValues<EquipmentItemSlot>().Max() + 1;
    }

    public override bool CanAccept(Item item, int targetSlot)
    {
        if (!base.CanAccept(item, targetSlot))
            return false;

        // What an item counts as in a mate's position, and which kinds that position takes, are the
        // tables' (slave_equip_kind_lists against the item's own kind): a piece the position does not
        // list stays out rather than being worn and never drawn.
        if (item == null || ParentUnit is not MateUnit mate)
            return true;

        if (targetSlot < 0 || targetSlot >= ContainerSize)
            return true; // slot range is the base container's business, and it has already had its say

        if (SlaveGameData.Instance.PositionTakesItem(mate.TemplateId, (byte)targetSlot, item.TemplateId))
            return true;

        Logger.Warn(
            "{0} tried to put {1} ({2}) into {3}'s slot {4}, which does not take its kind {5}",
            Owner?.Name, item.Template?.Name, item.TemplateId, mate.Name, targetSlot,
            SlaveGameData.Instance.GetItemSlaveEquipKind(item.TemplateId));
        return false;
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
