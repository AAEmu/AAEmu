using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncPurchase : DoodadFuncTemplate
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
    public uint CoinItemId { get; set; }
    public int CoinCount { get; set; }
    public uint CurrencyId { get; set; }

    public override bool CompletesFromClientPacket => true;

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        // Native PurchaseDlgTask only opens the confirmation UI here. The result is committed by
        // CSDoodadPurchaseItemPacket after the client confirms its currency or token dialog.
    }

    public bool TryPurchase(Character character, bool useAaPoint)
    {
        if (character == null || Count <= 0 || ItemManager.Instance.GetTemplate(ItemId) == null)
        {
            character?.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var autoEquipTradePack = ItemManager.Instance.IsAutoEquipTradePack(ItemId);
        if (autoEquipTradePack)
        {
            if (!character.Inventory.CanReplaceGliderInBackpackSlot())
            {
                character.SendErrorMessage(ErrorMessageType.BackpackOccupied);
                return false;
            }
        }
        else if (character.Inventory.Bag.SpaceLeftForItem(ItemId) < Count)
        {
            character.SendErrorMessage(ErrorMessageType.BagFull);
            return false;
        }

        if (!TryPay(character, useAaPoint, out var refund))
            return false;

        var acquired = autoEquipTradePack
            ? character.Inventory.TryEquipNewBackPack(ItemTaskType.DoodadInteraction, ItemId, Count)
            : character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.DoodadInteraction, ItemId, Count);
        if (acquired)
            return true;

        Logger.Error(
            "Doodad purchase acquisition failed after preflight for character {0}, purchase {1}, item {2}, count {3}",
            character.Id, Id, ItemId, Count);
        refund();
        character.SendErrorMessage(autoEquipTradePack ? ErrorMessageType.BackpackOccupied : ErrorMessageType.BagFull);
        return false;
    }

    private bool TryPay(Character character, bool useAaPoint, out Action refund)
    {
        refund = static () => { };

        // PurchaseCoinDlgTask is selected only when both native descriptor fields are populated.
        if (CoinItemId != 0 && CoinCount > 0)
        {
            character.Inventory.Bag.GetAllItemsByTemplate(CoinItemId, -1, out _, out var available);
            if (available < CoinCount)
            {
                character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
                return false;
            }

            if (character.Inventory.Bag.ConsumeItem(
                    ItemTaskType.DoodadInteraction, CoinItemId, CoinCount, null) != CoinCount)
                return false;

            refund = () =>
            {
                if (!character.Inventory.Bag.AcquireDefaultItem(
                        ItemTaskType.DoodadInteraction, CoinItemId, CoinCount))
                {
                    Logger.Error("Failed to refund doodad purchase token {0} x{1} to character {2}",
                        CoinItemId, CoinCount, character.Id);
                }
            };
            return true;
        }

        var currency = (ContentCurrencyType)CurrencyId;
        // to the gold row in item_prices while retaining the original currency for payment.
        var priceCurrency = currency is ContentCurrencyType.AaPoint
            or ContentCurrencyType.GoldWithAaPoint
            or ContentCurrencyType.ItemPoint
            ? ShopCurrencyType.Money
            : (ShopCurrencyType)CurrencyId;
        var unitPrice = ItemManager.Instance.GetShopPrice(ItemId, priceCurrency);
        if (unitPrice is null || unitPrice < 0 || unitPrice > int.MaxValue / Count)
        {
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var totalPrice = unitPrice.Value * Count;
        switch (currency)
        {
            case ContentCurrencyType.Gold:
            case ContentCurrencyType.GoldWithAaPoint:
                if (useAaPoint)
                {
                    if (!character.SubtractAAPoint(SlotType.Inventory, totalPrice, ItemTaskType.DoodadInteraction))
                        return false;
                    refund = () => character.AddAAPoint(
                        SlotType.Inventory, totalPrice, ItemTaskType.DoodadInteraction);
                }
                else
                {
                    if (!character.SubtractMoney(SlotType.Inventory, totalPrice, ItemTaskType.DoodadInteraction))
                        return false;
                    refund = () => character.AddMoney(
                        SlotType.Inventory, totalPrice, ItemTaskType.DoodadInteraction);
                }
                return true;
            case ContentCurrencyType.HonorPoint:
                if (character.HonorPoint < totalPrice)
                {
                    character.SendErrorMessage(ErrorMessageType.NotEnoughHonorPoint);
                    return false;
                }
                character.ChangeGamePoints(GamePointKind.Honor, -totalPrice);
                refund = () => character.ChangeGamePoints(GamePointKind.Honor, totalPrice);
                return true;
            case ContentCurrencyType.LivingPoint:
                if (character.VocationPoint < totalPrice)
                {
                    character.SendErrorMessage(ErrorMessageType.NotEnoughLivingPoint);
                    return false;
                }
                character.ChangeGamePoints(GamePointKind.Vocation, -totalPrice);
                refund = () => character.ChangeGamePoints(GamePointKind.Vocation, totalPrice, false);
                return true;
            case ContentCurrencyType.AaPoint:
                if (!character.SubtractAAPoint(
                        SlotType.Inventory, totalPrice, ItemTaskType.DoodadInteraction))
                    return false;
                refund = () => character.AddAAPoint(
                    SlotType.Inventory, totalPrice, ItemTaskType.DoodadInteraction);
                return true;
            case ContentCurrencyType.ContributionPoint:
                if (character.Expedition?.GetMember(character)?.ContributionPoint < totalPrice)
                {
                    character.SendErrorMessage(ErrorMessageType.NotEnoughRequiredItem);
                    return false;
                }
                if (!ExpeditionManager.Instance.TryChangeContributionPoints(character, -totalPrice, false))
                    return false;
                refund = () =>
                {
                    if (!ExpeditionManager.Instance.TryChangeContributionPoints(character, totalPrice, false))
                    {
                        Logger.Error("Failed to refund {0} contribution points to character {1}",
                            totalPrice, character.Id);
                    }
                };
                return true;
            default:
                character.SendErrorMessage(ErrorMessageType.Invalid);
                Logger.Error("Unsupported content currency {0} on doodad purchase {1}", CurrencyId, Id);
                return false;
        }
    }

    public static bool HasPermission(Character character, Doodad owner, DoodadFunc func)
    {
        switch ((Static.DoodadFuncPermission)func.PermId)
        {
            case Static.DoodadFuncPermission.Any:
                return true;
            case Static.DoodadFuncPermission.Expedition:
                var ownerCharacter = owner.GetOwnerCharacter();
                var permitted = ownerCharacter?.Expedition?.Id is > 0 &&
                    ownerCharacter.Expedition.Id == character.Expedition?.Id;
                if (!permitted)
                    character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                return permitted;
            default:
                character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                return false;
        }
    }
}
