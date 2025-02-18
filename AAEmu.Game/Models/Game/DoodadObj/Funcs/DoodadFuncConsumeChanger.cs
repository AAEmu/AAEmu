using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConsumeChanger : DoodadFuncTemplate
{
    // doodad_funcs
    public uint SlotId { get; set; }
    public int Count { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncConsumeChanger");
        // Store trade-pack into a trade-pack storage
        if (caster is not Character player)
            return;
        var sourceItem = player.Equipment.GetItemBySlot((int)SlotId);
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

        // TODO: If this item count would be different from 1, this would cause issues
        // Just toss a warning in the logs for now, all entries should be 1 anyway
        if (Count > 1)
            Logger.Warn($"DoodadFuncConsumeChanger, expected source count is higher than one ({Count}) for slot {SlotId} on doodad type {owner.TemplateId} ({owner.OwnerId}) from player {player.Name}");

        // Move the actual item to the owner's SystemContainer
        player.Inventory.SystemContainer.AddOrMoveExistingItem(ItemTaskType.DoodadItemChanger, sourceItem);
        owner.ToNextPhase = true;
    }
}
