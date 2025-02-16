using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncLootItem : DoodadFuncTemplate
{
    // doodad_funcs
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint ItemId { get; set; }
    public int CountMin { get; set; }
    public int CountMax { get; set; }
    public int Percent { get; set; }
    public int RemainTime { get; set; }
    public uint GroupId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is Character)
            Logger.Debug("DoodadFuncLootItem: skillId {0}, nextPhase {1},  ItemId {2}, CountMin {3}, CountMax {4},  Percent {5}, RemainTime {6}, GroupId {7}", skillId, nextPhase, ItemId, CountMin, CountMax, Percent, RemainTime, GroupId);
        else
            Logger.Trace("DoodadFuncLootItem: skillId {0}, nextPhase {1},  ItemId {2}, CountMin {3}, CountMax {4},  Percent {5}, RemainTime {6}, GroupId {7}", skillId, nextPhase, ItemId, CountMin, CountMax, Percent, RemainTime, GroupId);

        var character = (Character)caster;
        var res = true;
        if (character == null)
            return;

        var chance = Rand.Next(0, 10000);
        if (chance > Percent)
            return;

        var count = Rand.Next(CountMin, CountMax);

        if (ItemId == 500)
        {
            character.Money += count;
            res = character.AddMoney(SlotType.Bag, count);
        }
        else
        {
            if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
            {
                var item = ItemManager.Instance.Create(ItemId, count, 0);
                if (character.Inventory.TakeoffBackpack(ItemTaskType.RecoverDoodadItem, true))
                {
                    res = character.Inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem, item,
                        (int)Items.EquipmentItemSlot.Backpack);
                }
            }
            else
            {
                res = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.RecoverDoodadItem, ItemId, count);
            }
        }

        if (res == false)
            character.SendErrorMessage(ErrorMessageType.BagInvalidItem);

        owner.ToNextPhase = true;
    }
}
