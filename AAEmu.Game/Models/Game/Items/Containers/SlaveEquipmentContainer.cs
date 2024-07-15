using System;
using System.Linq;

using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.Items.Containers;

public class SlaveEquipmentContainer : EquipmentContainer
{
    public SlaveEquipmentContainer(uint ownerId, SlotType containerType, bool createWithNewId) : base(ownerId, containerType, createWithNewId)
    {
        // Fancy way of getting the last enum value + 1 for equipment slots
        ContainerSize = (int)Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max() + 1;
    }

    public override void OnEnterContainer(Item item, ItemContainer lastContainer)
    {
        Logger.Debug("slave OnEnterContainer");
        base.OnEnterContainer(item, lastContainer);

        if (Owner == null)
            return;

        var slave = SlaveManager.Instance.GetSlaveByOwnerObjId(Owner.ObjId);
        if (slave != null)
        {
            //mate.UpdateGearBonuses(item, null);
            //Owner?.MatesUpdateGearBonuses(item, null);
        }
    }

    public override void OnLeaveContainer(Item item, ItemContainer newContainer)
    {
        Logger.Debug("slave OnLeaveContainer");

        base.OnLeaveContainer(item, newContainer);

        if (Owner == null)
            return;

        var slave = SlaveManager.Instance.GetSlaveByOwnerObjId(Owner.ObjId);
        if (slave != null)
        {
            //mate.UpdateGearBonuses(null, item);
        }
    }
}
