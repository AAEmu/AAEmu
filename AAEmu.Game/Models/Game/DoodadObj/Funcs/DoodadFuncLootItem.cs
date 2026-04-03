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
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public WorldInteractionType WorldInteractionId { get; set; }
    public uint ItemId { get; init; }
    public int CountMin { get; init; }
    public int CountMax { get; init; }
    public int Percent { get; init; }
    public int RemainTime { get; init; }
    public uint GroupId { get; init; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is Character)
            Logger.Debug($"DoodadFuncLootItem: skillId {skillId}, nextPhase {nextPhase},  ItemId {ItemId}, CountMin {CountMin}, CountMax {CountMax},  Percent {Percent}, RemainTime {RemainTime}, GroupId {GroupId}");
        else
            Logger.Trace($"DoodadFuncLootItem: skillId {skillId}, nextPhase {nextPhase},  ItemId {ItemId}, CountMin {CountMin}, CountMax {CountMax},  Percent {Percent}, RemainTime {RemainTime}, GroupId {GroupId}");

        var character = (Character)caster;
        var res = true;
        if (character == null)
            return;

        var chance = Random.Shared.Next(0, 10000);
        if (chance > Percent)
            return;

        var count = Random.Shared.Next(CountMin, CountMax);

        if (ItemId == 500)
        {
            character.Money += count;
            res = character.AddMoney(SlotType.Inventory, count);
        }
        else
        {
            if (ItemManager.Instance.IsAutoEquipTradePack(ItemId))
            {
                var item = ItemManager.Instance.Create(ItemId, count, 0);
                if (character.Inventory.TakeoffBackpack(ItemTaskType.RecoverDoodadItem, true))
                {
                    res = character.Inventory.Equipment.AddOrMoveExistingItem(ItemTaskType.RecoverDoodadItem, item,
                        (int)EquipmentItemSlot.Backpack);
                }
            }
            else
            {
                res = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.RecoverDoodadItem, ItemId, count);
            }
        }

        if (res == false)
            character.SendErrorMessage(ErrorMessageType.BagInvalidItem);

        // Move to next phase only when loot was actually granted.
        owner.ToNextPhase = res;
    }
}
