using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConsumeChanger : DoodadFuncTemplate
{
    // doodad_func_consume_changers
    // 10.0.2.13: slot_id/count removed; the source item is now identified by tag_id.
    public uint TagId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncConsumeChanger");
        // Store trade-pack into a trade-pack storage
        if (caster is not Character player)
            return;
        // 10.0.2.13: the consumed source is the equipped trade pack (Backpack slot); the allowed-item list below
        // gates validity. TagId is retained for the v10 tag-based check once an item-tag accessor is available.
        var sourceItem = player.Equipment.GetItemBySlot((int)EquipmentItemSlot.Backpack);
        if (sourceItem == null)
        {
            player.SendErrorMessage(ErrorMessageType.UnknownItem);
            return;
        }

        // doodad_func_consume_changer_items
        var itemCheck = DoodadManager.Instance.GetDoodadFuncConsumeChangerItemList(Id);
        if (!itemCheck.Contains(sourceItem.TemplateId))
        {
            player.SendErrorMessage(ErrorMessageType.StoreInvalidItem);
            return;
        }

        owner.ItemId = sourceItem.Id;
        owner.ItemTemplateId = sourceItem.TemplateId;

        // Move the actual item to the owner's SystemContainer
        player.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DoodadItemChanger, sourceItem);
        owner.ToNextPhase = true;
    }
}
