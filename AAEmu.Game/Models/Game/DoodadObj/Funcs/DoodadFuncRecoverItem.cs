using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncRecoverItem : DoodadFuncTemplate
    {
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncRecoverItem");

            //TODO: itemId currently using itemtemplate but shouldn't, needs to retain original crafter 
            var character = (Character)caster;
            var addedItem = false;
            if (owner.ItemId > 0)
            {
                var item = ItemManager.Instance.GetItemByItemId(owner.ItemId);
                if (item != null)
                {
                    if (ItemManager.Instance.IsAutoEquipTradePack(item.TemplateId))
                    {
                        if (character.Inventory.TakeoffBackpack(ItemTaskType.RecoverDoodadItem, true))
                            if (character.Inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem,
                                item,
                                (int)AAEmu.Game.Models.Game.Items.EquipmentItemSlot.Backpack))
                                addedItem = true;
                    }
                    else
                    {
                        if (character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem, item))
                            addedItem = true;
                    }
                }
            }
            else
            {
                // No itemId was provided with the doodad, need to check what needs to be done with this
                _log.Warn("DoodadFuncRecoverItem: Doodad {0} has no item attached",owner.InstanceId);
            }

            if (addedItem)
                owner.Delete();
        }
    }
}
