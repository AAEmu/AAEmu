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
        var costs = new uint[(byte)CashShopCurrencyType.Max];
        foreach (var sku in shoppingCart.Select(purchase => purchase.Sku))
            costs[(byte)sku.Currency] += sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price;

        var beforeBuyAccountDetails = AccountManager.Instance.GetAccountDetails(buyer.AccountId);
        if (costs[(byte)CashShopCurrencyType.Credits] > beforeBuyAccountDetails.Credits)
        {
            // 591 → BFR_CASH
            FailBuy(ErrorMessageType.IngameShopNotEnoughAaCash);
            return;
        }

        if (costs[(byte)CashShopCurrencyType.Loyalty] > beforeBuyAccountDetails.Loyalty)
        {
            // 784 → BFR_BM_MILEAGE
            FailBuy(ErrorMessageType.IngameShopNotEnoughBmMileage);
            return;
        }

        if (costs[(byte)CashShopCurrencyType.Coins] > buyer.Money)
        {
            // Prefer mappable gold-ish: NotEnoughCoin is outside 391153A0 table → spinner.
            // 591 is cash; use buy-fail + chat toast so wait still clears.
            FailBuy(ErrorMessageType.IngameShopBuyFail, ErrorMessageType.NotEnoughCoin);
            return;
        }
        #endregion

        #region validate_cart
        foreach (var sku in shoppingCart.Select(purchase => purchase.Sku))
        {
            if (!CashShopManager.Instance.ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                // 590 → BFR_NORMAL
                FailBuy(ErrorMessageType.IngameShopBuyFail, shopId: sku.ShopId);
                return;
            }

            if (sku.EventEndDate > DateTime.MinValue && DateTime.UtcNow >= sku.EventEndDate)
            {
                // 596 → BFR_EXPIRED_DATE
                FailBuy(ErrorMessageType.IngameShopExpiredSellByDate, shopId: sku.ShopId);
                return;
            }

            if (shopItem.SaleStart > DateTime.MinValue && DateTime.UtcNow <= shopItem.SaleStart)
            {
                FailBuy(ErrorMessageType.IngameShopExpiredSellByDate, shopId: sku.ShopId);
                return;
            }

            if (shopItem.SaleEnd > DateTime.MinValue && DateTime.UtcNow >= shopItem.SaleEnd)
            {
                FailBuy(ErrorMessageType.IngameShopExpiredSellByDate, shopId: sku.ShopId);
                return;
            }

            if (shopItem.LevelMin > 0 && buyer.Level < shopItem.LevelMin)
            {
                FailBuy(ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyLowLevel, sku.ShopId);
                return;
            }

            if (shopItem.LevelMax > 0 && buyer.Level > shopItem.LevelMax)
            {
                FailBuy(ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyLowLevel, sku.ShopId);
                return;
            }

            if (shopItem.BuyRestrictType == CashShopRestrictSaleType.Level && buyer.Level < shopItem.BuyRestrictId)
            {
                FailBuy(ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyLowLevel, sku.ShopId);
                return;
            }

            if (shopItem.BuyRestrictType == CashShopRestrictSaleType.Quest && !buyer.Quests.HasQuestCompleted(shopItem.BuyRestrictId))
            {
                FailBuy(ErrorMessageType.IngameShopBuyFail, ErrorMessageType.IngameShopBuyQuestIncomplete, sku.ShopId);
                return;
            }

            if (shopItem.Remaining >= 0)
            {
                var totalItemsBoughtOfThisType = shoppingCart.Count(
                    purchase => purchase.Sku.ShopId == sku.ShopId);
                if (shopItem.Remaining < totalItemsBoughtOfThisType)
                {
                    // 595 → BFR_SOLD_OUT
                    FailBuy(ErrorMessageType.IngameShopSoldOut, shopId: sku.ShopId);
                    return;
                }
            }

            if (shopItem.LimitedType != CashShopLimitType.None)
            {
                var bought = CashShopManager.Instance.GetPurchasedItemCount(buyer.AccountId, buyer.Id, shopItem);
                if (bought + sku.ItemCount > shopItem.LimitedStockMax)
                {
                    // Account cap: 683 → BFR_COUNT_PER_ACCOUNT (client "count per account" copy).
                    // Character / remaining: 595 → BFR_SOLD_OUT.
                    var wire = shopItem.LimitedType == CashShopLimitType.Account
                        ? ErrorMessageType.IngameShopBuyNoDuplicateItem
                        : ErrorMessageType.IngameShopSoldOut;
                    Logger.Info(
                        "ICS buy denied sold-out/limit shopId={0} bought={1} max={2} type={3} wire={4}",
                        shopItem.ShopId, bought, shopItem.LimitedStockMax, shopItem.LimitedType, (int)wire);
                    // Toast uses the sold-out / limit string; fail wire carries the mapper id.
                    FailBuy(wire, ErrorMessageType.IngameShopSoldOut, sku.ShopId);
                    return;
                }
            }
        }
        #endregion

        #region transactions
        var entriesSold = 0;
        var soldItems = new List<(uint CashShopId, byte DetailIndex)>();
        var stockToSync = new Dictionary<uint, int>();
        foreach (var purchase in shoppingCart)
        {
            var sku = purchase.Sku;
            if (!CashShopManager.Instance.ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                Logger.Error("ICS buy missing shopItem for sku {0}", sku.Sku);
                continue;
            }

            if (shopItem.LimitedType != CashShopLimitType.None)
            {
                var bought = CashShopManager.Instance.GetPurchasedItemCount(buyer.AccountId, buyer.Id, shopItem);
                if (bought + sku.ItemCount > shopItem.LimitedStockMax)
                {
                    Logger.Info("ICS buy aborted mid-cart limit shopId={0}", shopItem.ShopId);
                    if (entriesSold == 0)
                    {
                        var wire = shopItem.LimitedType == CashShopLimitType.Account
                            ? ErrorMessageType.IngameShopBuyNoDuplicateItem
                            : ErrorMessageType.IngameShopSoldOut;
                        FailBuy(wire, ErrorMessageType.IngameShopSoldOut, sku.ShopId);
                        return;
                    }

                    break;
                }
            }

            if (shopItem.Remaining >= 0)
            {
                if (shopItem.Remaining < sku.ItemCount)
                {
                    if (entriesSold == 0)
                    {
                        FailBuy(ErrorMessageType.IngameShopSoldOut, shopId: sku.ShopId);
                        return;
                    }

                    break;
                }

                shopItem.Remaining -= (int)sku.ItemCount;
                CashShopManager.Instance.UpdateRemainingShopItemStock(shopItem.ShopId, shopItem.Remaining);
                stockToSync[shopItem.ShopId] = shopItem.Remaining;
            }

            switch (sku.Currency)
            {
                case CashShopCurrencyType.Credits:
                    if (!AccountManager.Instance.RemoveCredits(buyer.AccountId, (int)(sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price)))
                        Logger.Error("ICS credit debit failed for {0}", buyer.Name);
                    break;
                case CashShopCurrencyType.AaPoints:
                    Logger.Warn("ICS AA-point currency not implemented for {0}", buyer.Name);
                    break;
                case CashShopCurrencyType.Loyalty:
                    AccountManager.Instance.AddLoyalty(buyer.AccountId, (int)(sku.Price * -1));
                    break;
                case CashShopCurrencyType.Coins:
                    if (!buyer.SubtractMoney(SlotType.Inventory, (int)sku.Price, ItemTaskType.StoreBuy))
                        Logger.Error("ICS coin debit failed for {0}", buyer.Name);
                    break;
                default:
                    Logger.Error("Invalid ICS currency {0}", sku.Currency);
                    break;
            }

            var items = new List<Game.Items.Item>();
            var itemTemplate = ItemManager.Instance.GetTemplate(sku.ItemId);
            var useName = !string.IsNullOrWhiteSpace(shopItem.Name)
                ? shopItem.Name
                : LocalizationManager.Instance.Get("items", "name", sku.ItemId);

            items.Add(ItemManager.Instance.Create(
                sku.ItemId, (int)sku.ItemCount,
                itemTemplate.FixedGrade >= 0 ? (byte)itemTemplate.FixedGrade : (byte)0, true));

            if (sku.BonusItemId > 0 && sku.BonusItemCount > 0)
            {
                var bonusItemTemplate = ItemManager.Instance.GetTemplate(sku.BonusItemId);
                items.Add(ItemManager.Instance.Create(
                    sku.BonusItemId, (int)sku.BonusItemCount,
                    bonusItemTemplate.FixedGrade >= 0 ? (byte)bonusItemTemplate.FixedGrade : (byte)0, true));
            }

            var mail = new CommercialMail(
                targetPlayer.Id, targetPlayer.Name, buyer.Name, items,
                targetPlayer.Id != buyer.Id, false, useName);
            mail.FinalizeMail();
            if (!mail.Send())
                targetPlayer.SendErrorMessage(ErrorMessageType.IngameShopBuyFail);

            entriesSold++;
            soldItems.Add((sku.ShopId, purchase.DetailIndex));

            Logger.Info("ICSBuyGood {0} -> {1} - {2} x {3}, SKU:{4}",
                buyer.Name, targetPlayer.Name, useName, sku.ItemCount, sku.Sku);
            if (!CashShopManager.Instance.LogSale(
                    buyer.AccountId, buyer.Id, targetPlayer.AccountId, targetPlayer.Id,
                    DateTime.UtcNow, shopItem.ShopId, sku.Sku,
                    sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price, sku.Currency, string.Empty))
                Logger.Error("ICSBuyGood sale log failed for SKU {0}", sku.Sku);
        }

        if (entriesSold > 0)
        {
            var postSale = AccountManager.Instance.GetAccountDetails(buyer.AccountId);
            buyer.BmPoint = postSale.Loyalty;
            buyer.SendPacket(new SCICSCashPointPacket(postSale.Credits));
            buyer.SendPacket(new SCBmPointPacket(postSale.Loyalty));

            foreach (var (shopId, remaining) in stockToSync)
                buyer.SendPacket(new SCICSSyncGoodPacket((int)shopId, remaining));

            buyer.SendPacket(new SCICSBuySucceededPacket(
                buyMode,
                SCICSBuySucceededPacket.ReceiveWayChargedMail,
                targetPlayer.Name,
                (int)costs[(byte)CashShopCurrencyType.AaPoints],
                soldItems));
            CashShopManager.Instance.SendBuyCounts(buyer.Connection, buyer.AccountId, buyer.Id);
        }
        else
        {
            FailBuy(ErrorMessageType.IngameShopBuyFail);
        }
        #endregion
    }

    /// <summary>Sends a failure reply and an optional separate chat notification.</summary>
    private void FailBuy(
        ErrorMessageType wireError,
        ErrorMessageType? toast = null,
        uint shopId = 0)
    {
        buyer.SendErrorMessage(toast ?? wireError);
        IReadOnlyList<(uint CashShopId, ErrorMessageType Reason)> itemFailures =
            shopId == 0 ? [] : [(shopId, wireError)];
        buyer.SendPacket(new SCICSBuyFailedPacket(buyMode, wireError, itemFailures));
        CashShopManager.Instance.SendBuyCounts(buyer.Connection, buyer.AccountId, buyer.Id);
    }
}
