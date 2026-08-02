using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncConvertFish : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncConvertFish");

        if (caster is not Character character)
            return;

        var backpack = character.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack);
        if (backpack is not BigFish caughtFish)
        {
            Fail(character, owner, ErrorMessageType.StoreBackpackNogoods);
            return;
        }

        if (!ItemManager.Instance.TryGetFishConversion(Id, backpack.TemplateId, out var outputItemId))
        {
            Fail(character, owner, ErrorMessageType.StoreBackpackNogoods);
            return;
        }

        if (!backpack.CanDestroy())
        {
            owner.ToNextPhase = false;
            return;
        }

        if (character.Inventory.Bag.SpaceLeftForItem(outputItemId) < 1)
        {
            Fail(character, owner, ErrorMessageType.BagFull);
            return;
        }

        var fish = FishDetailsGameData.Instance.CreateTrophy(outputItemId, caughtFish);
        if (fish == null)
        {
            owner.ToNextPhase = false;
            Logger.Error($"Failed to create converted fish item {outputItemId} from source {backpack.TemplateId}");
            return;
        }

        if (!character.Inventory.Bag.AddOrMoveExistingItem(ItemTaskType.Invalid, fish))
        {
            ItemManager.Instance.ReleaseId(fish.Id);
            Fail(character, owner, ErrorMessageType.BagFull);
            return;
        }

        var removeTask = new ItemRemoveSlot(backpack);
        if (!character.Equipment.RemoveItem(ItemTaskType.Invalid, backpack, true))
        {
            if (!character.Inventory.Bag.RemoveItem(ItemTaskType.Invalid, fish, true))
                Logger.Error($"Failed to roll back converted fish item {fish.Id} for {character.Name}");
            owner.ToNextPhase = false;
            return;
        }

        character.SendPacket(new SCItemTaskSuccessPacket(
            ItemTaskType.ConvertFish,
            [new ItemAdd(fish), removeTask],
            [backpack.Id]));
    }

    private static void Fail(Character character, Doodad owner, ErrorMessageType error)
    {
        owner.ToNextPhase = false;
        character.SendErrorMessage(error);
    }
}
