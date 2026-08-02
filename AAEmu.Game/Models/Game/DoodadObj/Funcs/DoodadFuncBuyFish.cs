using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
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
            !FishDetailsGameData.Instance.TryCalculateSalePrice(fish, out var total))
        {
            Fail(character, owner);
            return;
        }

        if (!backpack.CanDestroy())
        {
            owner.ToNextPhase = false;
            return;
        }

        long updatedMoney;
        try
        {
            updatedMoney = checked(character.Money + total);
        }
        catch (OverflowException)
        {
            owner.ToNextPhase = false;
            Logger.Error($"Fish sale would overflow money for {character.Name}");
            return;
        }

        var removeTask = new ItemRemoveSlot(backpack);
        if (!character.Equipment.RemoveItem(ItemTaskType.Invalid, backpack, true))
        {
            owner.ToNextPhase = false;
            return;
        }

        owner.ItemTemplateId = backpack.TemplateId;
        character.Money = updatedMoney;
        character.SendPacket(new SCItemTaskSuccessPacket(
            ItemTaskType.Fishing,
            [removeTask, new MoneyChange(total)],
            [backpack.Id]));
    }

    private static void Fail(Character character, Doodad owner)
    {
        owner.ToNextPhase = false;
        character.SendErrorMessage(ErrorMessageType.StoreBackpackNogoods);
    }
}
