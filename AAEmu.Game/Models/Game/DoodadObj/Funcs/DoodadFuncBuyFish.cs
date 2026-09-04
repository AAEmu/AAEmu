using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuyFish : DoodadFuncTemplate
{
    public IReadOnlySet<uint> AllowedItemIds { get; init; } = new HashSet<uint>();

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncBuyFish");

        if (caster is not Character character)
            return;

        var backpack = character.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
        if (backpack is not BigFish fish || !AllowedItemIds.Contains(backpack.TemplateId) ||
            !FishDetailsGameData.Instance.TryCalculateSalePrice(fish, out var total) || total <= 0)
        {
            Fail(character, owner);
            return;
        }

        if (!backpack.CanDestroy())
        {
            owner.ToNextPhase = false;
            return;
        }

        try
        {
            _ = checked(character.Money + total);
        }
        catch (OverflowException)
        {
            owner.ToNextPhase = false;
            Logger.Error($"Fish sale would overflow money for {character.Name}");
            return;
        }

        // Remove first (own packet). Gold is a separate money task — putting Seize and
        // MoneyChange in the same list leaves the client wallet unchanged after the pack is gone.
        if (!character.Equipment.RemoveItem(ItemTaskType.Fishing, backpack, true))
        {
            owner.ToNextPhase = false;
            return;
        }

        owner.ItemTemplateId = backpack.TemplateId;
        if (!character.AddMoney(SlotType.Inventory, total, ItemTaskType.Fishing))
        {
            owner.ToNextPhase = false;
            Logger.Error($"Fish sale paid {total} but the wallet update failed for {character.Name}");
            return;
        }

        Logger.Info(
            $"Fish stand sale {character.Name} item={backpack.TemplateId} grade={backpack.Grade} weight={fish.Weight} price={total}");
    }

    private static void Fail(Character character, Doodad owner)
    {
        owner.ToNextPhase = false;
        character.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
    }
}
