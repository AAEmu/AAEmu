using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncRecoverItem : DoodadFuncTemplate
{
    // doodad_funcs
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Debug($"DoodadFuncRecoverItem({Id}) - Caster:{caster.Name} - DoodadOwner Template:{owner?.TemplateId} - SkillId:{skillId} - Nextphase:{nextPhase}");

        var character = (Character)caster;
        var addedItem = false;
        var item = ItemManager.Instance.GetItemByItemId(owner?.ItemId ?? 0);
        if (owner?.ItemId > 0)
        {
            if (item != null)
            {
                // Recoverable doodads, should be referencing a item in a System container, if this is not the case,
                // that means that it was already picked up by somebody else
                if (item._holdingContainer?.ContainerType != SlotType.Money)
                {
                    owner.ToNextPhase = false;
                    character.SendErrorMessage(ErrorMessageType.InteractionRecoverParent); // TODO: Not sure what error I need to put here
                    return;
                }

                // If it's on house property, check if the player has access to it
                if (owner.OwnerDbId > 0)
                {
                    GameObject ownerGameObject;
                    switch (owner.OwnerType)
                    {
                        case DoodadOwnerType.Slave:
                            ownerGameObject = SlaveManager.Instance.GetSlaveByObjId(owner.OwnerDbId);
                            break;
                        case DoodadOwnerType.Housing:
                            ownerGameObject = HousingManager.Instance.GetHouseById(owner.OwnerDbId);
                            break;
                        case DoodadOwnerType.Character:
                        case DoodadOwnerType.System:
                        default:
                            ownerGameObject = null;
                            break;
                    }

                    if (ownerGameObject != null && !ownerGameObject.AllowedToInteract(character))
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
        else if (owner?.ItemTemplateId > 0)
        {
            addedItem = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.RecoverDoodadItem, owner.ItemTemplateId, 1);
        }
        else
        {
            // No itemId was provided with the doodad, need to check what needs to be done with this
            Logger.Warn($"DoodadFuncRecoverItem: Doodad {owner?.ObjId} has no item information attached to it");
            addedItem = true; // fake it to get rid of the error state
        }

        if (addedItem && item != null && item._holdingContainer.ContainerType == SlotType.Equipment)
            character.BroadcastPacket(new SCUnitEquipmentsChangedPacket(character.ObjId, (byte)item.Slot, item), false);

        if ((addedItem) && (owner != null))
        {
            // remove the old reference
            owner.ItemId = 0;
            owner.ItemTemplateId = 0;
        }

        if (owner != null)
            owner.ToNextPhase = addedItem;
    }
}
