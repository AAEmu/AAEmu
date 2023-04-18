using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items.Containers
{
    public class MateEquipmentContainer : EquipmentContainer
    {
        public MateEquipmentContainer(uint ownerId, SlotType containerType, bool isPartOfPlayerInventory, bool createWithNewId) : base(ownerId, containerType, isPartOfPlayerInventory, createWithNewId)
        {
            // Fancy way of getting the last enum value + 1 for equipment slots
            ContainerSize = (int)(Enum.GetValues(typeof(EquipmentItemSlot)).Cast<EquipmentItemSlot>().Max()) + 1;
        }

        public override void OnEnterContainer(Item item, ItemContainer lastContainer)
        {
            _log.Debug("mate OnEnterContainer");
            base.OnEnterContainer(item, lastContainer);

            if (Owner == null)
                return;

            var mate = MateManager.Instance.GetActiveMate(Owner.ObjId);

            if (mate == null) 
                return;

            //mate.UpdateGearBonuses(item, null);
            //Owner?.MatesUpdateGearBonuses(item, null);
        }

        public override void OnLeaveContainer(Item item, ItemContainer newContainer)
        {
            _log.Debug("mate OnLeaveContainer");

            base.OnLeaveContainer(item, newContainer);

            if (Owner == null)
                return;

            var mate = MateManager.Instance.GetActiveMate(Owner.ObjId);

            if (mate == null)
                return;

            //mate.UpdateGearBonuses(null, item);
        }
    }
}
