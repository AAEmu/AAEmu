using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuyFish : DoodadFuncTemplate
{
    // doodad_funcs
    public uint ItemId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncBuyFish");

        if (caster is Character character)
        {
            var backpack = character.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
            if (backpack == null)
            {
                character.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
                return;
            }

            owner.ItemTemplateId = backpack.TemplateId; // to display the phase animation correctly for doodad

            // TODO receiving money and removing the back pack
            var total = backpack.Template.Refund;
            character.Money += total;

            character.Equipment.RemoveItem(ItemTaskType.SkillEffectConsumption, backpack, true);
            character.AddMoney(SlotType.Bag, total);
        }
    }
}
