using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConvertFish : DoodadFuncTemplate
{
    // doodad_funcs
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncConvertFish");
        if (caster is Character character)
        {
            var backpack = character.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
            if (backpack == null)
            {
                character.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
                return;
            }

            // TODO receiving trophy and removing the back pack
            character.Inventory.SystemContainer.RemoveItem(ItemTaskType.Fishing, backpack, true);

            var trophys = ItemManager.Instance.GetLootConvertFish(backpack.TemplateId);
            if (trophys == null) { return; }
            foreach (var trophy in trophys)
            {
                var fish = FishDetailsGameData.Instance.Create(trophy);
                character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Fishing, fish);

                break;
            }
        }
    }
}
