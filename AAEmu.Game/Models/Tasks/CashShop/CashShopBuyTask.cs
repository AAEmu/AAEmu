using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.StaticValues;
using NLog;

namespace AAEmu.Game.Models.Tasks.CashShop;

public class CashShopBuyTask(byte buyMode, Character buyer, Character targetPlayer, List<IcsPurchase> shoppingCart)
    : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        #region check_costs
        // Calculate costs (of all different types in the cart)
        // Don't think this is actually possible to mix currencies in the cart, but let's handle it anyway
        var costs = new uint[(byte)CashShopCurrencyType.Max];
        foreach (var purchase in shoppingCart)
        {
            var sku = purchase.Sku;
            costs[(byte)sku.Currency] += sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price;
        }

        var beforeBuyAccountDetails = AccountManager.Instance.GetAccountDetails(buyer.AccountId);
        // Check Credits
        if (costs[(byte)CashShopCurrencyType.Credits] > beforeBuyAccountDetails.Credits)
        {
            buyer.SendErrorMessage(ErrorMessageType.IngameShopNotEnoughAaCash); // Not sure if this is the correct error
            buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
            return;
        }

        // TODO: Check AA Points
        /*
        if (costs[(byte)CashShopCurrencyType.AaPoints] > CashShopManager.Instance.GetAccountAaPoints(_buyer.AccountId))
        {
            _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyFailAaPoint);
            _buyer.SendPacket(new SCICSBuyFailedPacket(_buyMode, SCICSBuyFailedPacket.ReasonGeneric));
            return;
        }
        */

        // Check Loyalty
        if (costs[(byte)CashShopCurrencyType.Loyalty] > beforeBuyAccountDetails.Loyalty)
        {
            buyer.SendErrorMessage(ErrorMessageType.IngameShopNotEnoughBmMileage);
            buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
            return;
        }

        // Check Copper Coins
        if (costs[(byte)CashShopCurrencyType.Coins] > buyer.Money)
        {
            buyer.SendErrorMessage(ErrorMessageType.NotEnoughCoin);
            buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
            return;
        }
        #endregion

        #region validate_cart
        // Currencies are validated, complete other checks
        foreach (var purchase in shoppingCart)
        {
            var sku = purchase.Sku;
            // Get ShopItem for this SKU
            if (!CashShopManager.Instance.ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyFail); // generic error
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Event Date
            if (sku.EventEndDate > DateTime.MinValue && DateTime.UtcNow >= sku.EventEndDate)
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopExpiredSellByDate);
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Sale Start Date
            if (shopItem.SaleStart > DateTime.MinValue && DateTime.UtcNow <= shopItem.SaleStart)
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopExpiredSellByDate);
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Sale End Date
            if (shopItem.SaleEnd > DateTime.MinValue && DateTime.UtcNow >= shopItem.SaleEnd)
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopExpiredSellByDate);
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Minimum Level
            if (shopItem.LevelMin > 0 && buyer.Level < shopItem.LevelMin)
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyLowLevel);
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Maximum Level
            if (shopItem.LevelMax > 0 && buyer.Level > shopItem.LevelMax)
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyLowLevel); // Likely not the correct one, but don't see a shop one for max level
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Minimum Level by Restriction Type
            if (shopItem.BuyRestrictType == CashShopRestrictSaleType.Level && buyer.Level < shopItem.BuyRestrictId)
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyLowLevel);
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Quest by Restriction Type
            if (shopItem.BuyRestrictType == CashShopRestrictSaleType.Quest && !buyer.Quests.HasQuestCompleted(shopItem.BuyRestrictId))
            {
                buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyQuestIncomplete);
                buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                return;
            }

            // Check Remaining Stock (limited stock items)
            if (shopItem.Remaining >= 0)
            {
                // Count how many of this item are in this transaction
                var totalItemsBoughtOfThisType = 0;
                foreach (var b in shoppingCart)
                {
                    if (b.Sku.ShopId == sku.ShopId)
                        totalItemsBoughtOfThisType++;
                }

                if (shopItem.Remaining < totalItemsBoughtOfThisType)
                {
                    buyer.SendErrorMessage(ErrorMessageType.IngameShopSoldOut);
                    buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
                    return;
                }
            }

            // TODO: Check Limited Sales remaining (character or account)

        }
        #endregion

        #region transactions
        // Make the actual sales
        var entriesSold = 0;
        var soldItems = new List<(uint cashShopId, byte detailIndex)>(); // for SCICSBuySucceeded buyItem/remainBuyCount
        var stockToSync = new Dictionary<uint, int>();                   // shopId -> new remaining, for SCICSSyncGood
        foreach (var purchase in shoppingCart)
        {
            var sku = purchase.Sku;
            if (!CashShopManager.Instance.ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                Logger.Error($"Something went wrong in region transactions detecting shopItem");
                continue;
            }

            // Validate Limited Sales
            if (shopItem.LimitedType != CashShopLimitType.None)
            {
                // If there is a limit type set, grab previous sales of this ShopItem (any SKU attached)
                var oldSales = CashShopManager.Instance.GetSalesForShopItem(
                    buyer.AccountId,
                    shopItem.LimitedType == CashShopLimitType.Character ? buyer.Id : 0,
                    shopItem.ShopId);

                // Calculate old amount bought
                var oldSalesCount = 0u;
                foreach (var oldSale in oldSales)
                {
                    // Ignore if SKU no longer exists
                    if (!CashShopManager.Instance.SKUs.TryGetValue(oldSale.Sku, out var oldSKU))
                        continue;

                    if (shopItem.LimitedType == CashShopLimitType.Character)
                    {
                        if (oldSale.BuyerChar == buyer.Id)
                            oldSalesCount += oldSKU.ItemCount;
                    }
                    else if (shopItem.LimitedType == CashShopLimitType.Account)
                    {
                        if (oldSale.BuyerAccount == buyer.AccountId)
                            oldSalesCount += oldSKU.ItemCount;
                    }
                }

                // Check if with the new amount we still stay under the limit
                if (oldSalesCount + sku.ItemCount > shopItem.LimitedStockMax)
                {
                    // Too many sales for this item!!!
                    Logger.Error($"Tried to buy more items than allowed by the limit");
                    buyer.SendErrorMessage(ErrorMessageType.IngameShopSoldOut);
                    continue;
                }
            }

            // Reduce remaining stock if needed
            if (shopItem.Remaining >= 0)
            {
                if (shopItem.Remaining >= sku.ItemCount)
                {
                    shopItem.Remaining -= (int)sku.ItemCount;
                    CashShopManager.Instance.UpdateRemainingShopItemStock(shopItem.ShopId, shopItem.Remaining);
                    stockToSync[shopItem.ShopId] = shopItem.Remaining;
                }
                else
                {
                    // Out of Stock!!!
                    Logger.Error($"Sale validation failed for {buyer.Name}, ShopItem: {shopItem.ShopId}, Sku: {sku.Sku}, not enough stock remaining {shopItem.Remaining}");
                    buyer.SendErrorMessage(ErrorMessageType.IngameShopSoldOut);
                    continue;
                }
            }

            // Reduce currency
            switch (sku.Currency)
            {
                case CashShopCurrencyType.Credits:
                    if (!AccountManager.Instance.RemoveCredits(buyer.AccountId, (int)(sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price)))
                        Logger.Error($"Sale validation failed for {buyer.Name}, {sku.Currency} x {sku.Price}");
                    break;
                case CashShopCurrencyType.AaPoints:
                    //if (buyer.AaPoint < sku.Price)
                    //    Logger.Error($"Sale validation failed for {buyer.Name}, {sku.Currency} x {sku.Price}");
                    //buyer.AaPoint -= sku.Price;
                    Logger.Warn($"Sale currency not implemented {sku.Currency} for {buyer.Name}");
                    break;
                case CashShopCurrencyType.Loyalty:
                    if (beforeBuyAccountDetails.Loyalty < sku.Price)
                        Logger.Error($"Sale validation failed for {buyer.Name}, {sku.Currency} x {sku.Price}");
                    AccountManager.Instance.AddLoyalty(buyer.AccountId, (int)(sku.Price * -1));
                    break;
                case CashShopCurrencyType.Coins:
                    if (!buyer.SubtractMoney(SlotType.Inventory, (int)sku.Price, ItemTaskType.StoreBuy))
                        Logger.Error($"Sale validation failed for {buyer.Name}, {sku.Currency} x {sku.Price}");
                    break;
                default:
                    Logger.Error($"Invalid Currency {sku.Currency}");
                    break;
            }

            var items = new List<Game.Items.Item>();
            var itemTemplate = ItemManager.Instance.GetTemplate(sku.ItemId);
            var useName = !string.IsNullOrWhiteSpace(shopItem.Name)
                ? shopItem.Name
                : LocalizationManager.Instance.Get("items", "name", sku.ItemId);

            items.Add(ItemManager.Instance.Create(sku.ItemId, (int)sku.ItemCount, itemTemplate.FixedGrade >= 0 ? (byte)itemTemplate.FixedGrade : (byte)0, true));

            if (sku.BonusItemId > 0 && sku.BonusItemCount > 0)
            {
                var bonusItemTemplate = ItemManager.Instance.GetTemplate(sku.BonusItemId);
                items.Add(ItemManager.Instance.Create(sku.BonusItemId, (int)sku.BonusItemCount, bonusItemTemplate.FixedGrade >= 0 ? (byte)bonusItemTemplate.FixedGrade : (byte)0, true));
            }

            var mail = new CommercialMail(targetPlayer.Id, targetPlayer.Name, buyer.Name, items, targetPlayer.Id != buyer.Id, false, useName);
            mail.FinalizeMail();
            if (!mail.Send())
            {
                // Sending this mail should actually never be able to fail.
                targetPlayer.SendErrorMessage(ErrorMessageType.IngameShopBuyFail); // This is the wrong error, but likely the most fitting for now
            }

            entriesSold++;
            // Echo this line back in the buy-result so the client can show it (max 10 slots on the wire).
            if (soldItems.Count < 10)
                soldItems.Add((sku.ShopId, purchase.DetailIndex));

            Logger.Info($"ICSBuyGood {buyer.Name} -> {targetPlayer.Name} - {useName} x {sku.ItemCount}, SKU:{sku.Sku}");
            if (!CashShopManager.Instance.LogSale(buyer.AccountId, buyer.Id, targetPlayer.AccountId,
                    targetPlayer.Id, DateTime.UtcNow, shopItem.ShopId, sku.Sku, sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price, sku.Currency, string.Empty))
                Logger.Error(
                    $"ICSBuyGood {buyer.Name} -> {targetPlayer.Name} - {useName} x {sku.ItemCount}, SKU:{sku.Sku}, save failed!");
        }

        if (entriesSold > 0)
        {
            var postSaleAccountDetails = AccountManager.Instance.GetAccountDetails(buyer.AccountId);
            buyer.BmPoint = postSaleAccountDetails.Loyalty;
            // Balance resync (updated credits + loyalty HUD).
            buyer.SendPacket(new SCICSCashPointPacket(postSaleAccountDetails.Credits));
            buyer.SendPacket(new SCBmPointPacket(postSaleAccountDetails.Loyalty));
            // Refresh remaining-stock on the client for any limited-stock goods that changed.
            foreach (var (shopId, remaining) in stockToSync)
                buyer.SendPacket(new SCICSSyncGoodPacket((int)shopId, remaining));
            // The success result: fires the client finalize (event 0x27F) that clears the loading overlay.
            buyer.SendPacket(new SCICSBuySucceededPacket(buyMode, SCICSBuySucceededPacket.ReceiveWayChargedMail,
                string.Empty, (int)costs[(byte)CashShopCurrencyType.AaPoints], soldItems));
        }
        else
        {
            buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, SCICSBuyFailedPacket.ReasonGeneric));
        }

        #endregion
    }
}
