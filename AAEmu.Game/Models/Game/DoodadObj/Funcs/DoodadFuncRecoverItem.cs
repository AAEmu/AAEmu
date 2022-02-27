using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRecoverItem : DoodadFuncTemplate
    {
        // doodad_funcs
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncRecoverItem");

            var character = (Character)caster;
            var addedItem = false;
            var item = ItemManager.Instance.GetItemByItemId(owner.ItemId);
            if (owner.ItemId > 0)
            {
                if (item != null)
                {
                    // Recoverable doodads, should be referencing a item in a System container, if this is not the case,
                    // that means that it was already picked up by somebody else
                    if (item._holdingContainer?.ContainerType != SlotType.System)
                    {
            
                        // character.SendErrorMessage(ErrorMessageType.Backpack); // TODO: Not sure what error I need to put here
                        return;
                    }
                    
                    // If it's on house property, check if the player has access to it
                    if (owner.DbHouseId > 0)
                    {
                        var house = HousingManager.Instance.GetHouseById(owner.DbHouseId);
                        if ((house != null) && (!house.AllowedToInteract(character)))
                        {
                            character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                            return;
                        }
                    }
                    
                    if (ItemManager.Instance.IsAutoEquipTradePack(item.TemplateId))
                    {
                        if (character.Inventory.TakeoffBackpack(ItemTaskType.RecoverDoodadItem, true))
                        {
                            if (character.Inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem,
                                item,
                                (int)EquipmentItemSlot.Backpack))
                                addedItem = true;
                        }
                    }
                    else
                    {
                        if (character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem, item))
                            addedItem = true;
                    }
                }
            }
            else if (owner.ItemTemplateId > 0)
            {
                addedItem = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.RecoverDoodadItem, owner.ItemTemplateId, 1);
            }
            else
            {
                // No itemId was provided with the doodad, need to check what needs to be done with this
                _log.Warn("DoodadFuncRecoverItem: Doodad {0} has no item information attached to it", owner.InstanceId);
            }

            if ((addedItem) && (item != null) && (item._holdingContainer.ContainerType == SlotType.Equipment))
                character.BroadcastPacket(new SCUnitEquipmentsChangedPacket(character.ObjId,(byte)item.Slot,item), false);
        }
    }
}
