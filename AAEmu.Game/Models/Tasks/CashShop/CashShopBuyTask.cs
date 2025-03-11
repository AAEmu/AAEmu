using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.CashShop;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Mails;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Tasks.CashShop;

public class CashShopBuyTask : Task
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Character _buyer;
    private readonly Character _targetPlayer;
    private readonly List<IcsSku> _shoppingCart;
    private readonly byte _buyMode;

    public CashShopBuyTask(byte buyMode, Character buyer, Character targetPlayer, List<IcsSku> shoppingCart)
    {
        _buyMode = buyMode;
        _buyer = buyer;
        _targetPlayer = targetPlayer;
        _shoppingCart = shoppingCart;
    }

    public override void Execute()
    {
        #region check_costs
        // Calculate costs (of all different types in the cart)
        // Don't think this is actually possible to mix currencies in the cart, but let's handle it anyway
        var costs = new uint[(byte)CashShopCurrencyType.Max];
        foreach (var sku in _shoppingCart)
            costs[(byte)sku.Currency] += sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price;

        var beforeBuyAccountDetails = AccountManager.Instance.GetAccountDetails(_buyer.AccountId);
        // Check Credits
        if (costs[(byte)CashShopCurrencyType.Credits] > beforeBuyAccountDetails.Credits)
        {
            _buyer.SendErrorMessage(ErrorMessageType.IngameShopNotEnoughAaCash); // Not sure if this is the correct error
            _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
            return;
        }

        // TODO: Check AA Points
        /*
        if (costs[(byte)CashShopCurrencyType.AaPoints] > CashShopManager.Instance.GetAccountAaPoints(_buyer.AccountId))
        {
            _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyFailAaPoint);
            _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
            return;
        }
        */

        // Check Loyalty
        if (costs[(byte)CashShopCurrencyType.Loyalty] > beforeBuyAccountDetails.Loyalty)
        {
            _buyer.SendErrorMessage(ErrorMessageType.IngameShopNotEnoughBmMileage);
            _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
            return;
        }

        // Check Copper Coins
        if (costs[(byte)CashShopCurrencyType.Coins] > _buyer.Money)
        {
            _buyer.SendErrorMessage(ErrorMessageType.NotEnoughCoin);
            _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
            return;
        }
        #endregion

        #region validate_cart
        // Currencies are validated, complete other checks
        foreach (var sku in _shoppingCart)
        {
            // Get ShopItem for this SKU
            if (!CashShopManager.Instance.ShopItems.TryGetValue(sku.ShopId, out var shopItem))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyFail); // generic error
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Event Date
            if ((sku.EventEndDate > DateTime.MinValue) && (DateTime.UtcNow >= sku.EventEndDate))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopExpiredSellByDate);
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Sale Start Date
            if ((shopItem.SaleStart > DateTime.MinValue) && (DateTime.UtcNow <= shopItem.SaleStart))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopExpiredSellByDate);
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Sale End Date
            if ((shopItem.SaleEnd > DateTime.MinValue) && (DateTime.UtcNow >= shopItem.SaleEnd))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopExpiredSellByDate);
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Minimum Level
            if ((shopItem.LevelMin > 0) && (_buyer.Level < shopItem.LevelMin))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyLowLevel);
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Maximum Level
            if ((shopItem.LevelMax > 0) && (_buyer.Level > shopItem.LevelMax))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyLowLevel); // Likely not the correct one, but don't see a shop one for max level
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Minimum Level by Restriction Type
            if ((shopItem.BuyRestrictType == CashShopRestrictSaleType.Level) && (_buyer.Level < shopItem.BuyRestrictId))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyLowLevel);
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Quest by Restriction Type
            if ((shopItem.BuyRestrictType == CashShopRestrictSaleType.Quest) && !_buyer.Quests.HasQuestCompleted(shopItem.BuyRestrictId))
            {
                _buyer.SendErrorMessage(ErrorMessageType.IngameShopBuyQuestIncomplete);
                _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                return;
            }

            // Check Remaining Stock (limited stock items)
            if (shopItem.Remaining >= 0)
            {
                // Count how many of this item are in this transaction
                var totalItemsBoughtOfThisType = 0;
                foreach (var b in _shoppingCart)
                {
                    if (b.ShopId == sku.ShopId)
                        totalItemsBoughtOfThisType++;
                }

                if (shopItem.Remaining < totalItemsBoughtOfThisType)
                {
                    _buyer.SendErrorMessage(ErrorMessageType.IngameShopSoldOut);
                    _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, 0));
                    return;
                }
            }

            // TODO: Check Limited Sales remaining (character or account)

        }
        #endregion

        #region transactions
        // Make the actual sales
        var entriesSold = 0;
        foreach (var sku in _shoppingCart)
        {
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
                    _buyer.AccountId,
                    shopItem.LimitedType == CashShopLimitType.Character ? _buyer.Id : 0,
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
                        if (oldSale.BuyerChar == _buyer.Id)
                            oldSalesCount += oldSKU.ItemCount;
                    }
                    else if (shopItem.LimitedType == CashShopLimitType.Account)
                    {
                        if (oldSale.BuyerAccount == _buyer.AccountId)
                            oldSalesCount += oldSKU.ItemCount;
                    }
                }

                // Check if with the new amount we still stay under the limit
                if (oldSalesCount + sku.ItemCount > shopItem.LimitedStockMax)
                {
                    // Too many sales for this item!!!
                    Logger.Error($"Tried to buy more items than allowed by the limit");
                    _buyer.SendErrorMessage(ErrorMessageType.IngameShopSoldOut);
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
                }
                else
                {
                    // Out of Stock!!!
                    Logger.Error($"Sale validation failed for {_buyer.Name}, ShopItem: {shopItem.ShopId}, Sku: {sku.Sku}, not enough stock remaining {shopItem.Remaining}");
                    _buyer.SendErrorMessage(ErrorMessageType.IngameShopSoldOut);
                    continue;
                }
            }

            // Reduce currency
            switch (sku.Currency)
            {
                case CashShopCurrencyType.Credits:
                    if (!AccountManager.Instance.RemoveCredits(_buyer.AccountId, (int)(sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price)))
                        Logger.Error($"Sale validation failed for {_buyer.Name}, {sku.Currency} x {sku.Price}");
                    break;
                case CashShopCurrencyType.AaPoints:
                    //if (buyer.AaPoint < sku.Price)
                    //    Logger.Error($"Sale validation failed for {buyer.Name}, {sku.Currency} x {sku.Price}");
                    //buyer.AaPoint -= sku.Price;
                    Logger.Warn($"Sale currency not implemented {sku.Currency} for {_buyer.Name}");
                    break;
                case CashShopCurrencyType.Loyalty:
                    if (beforeBuyAccountDetails.Loyalty < sku.Price)
                        Logger.Error($"Sale validation failed for {_buyer.Name}, {sku.Currency} x {sku.Price}");
                    AccountManager.Instance.AddLoyalty(_buyer.AccountId, (int)(sku.Price * -1));
                    break;
                case CashShopCurrencyType.Coins:
                    if (!_buyer.SubtractMoney(SlotType.Bag, (int)sku.Price, ItemTaskType.StoreBuy))
                        Logger.Error($"Sale validation failed for {_buyer.Name}, {sku.Currency} x {sku.Price}");
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

            items.Add(ItemManager.Instance.Create(sku.ItemId, (int)(sku.ItemCount), itemTemplate.FixedGrade >= 0 ? (byte)itemTemplate.FixedGrade : (byte)0, true));

            if ((sku.BonusItemId > 0) && (sku.BonusItemCount > 0))
            {
                var bonusItemTemplate = ItemManager.Instance.GetTemplate(sku.BonusItemId);
                items.Add(ItemManager.Instance.Create(sku.BonusItemId, (int)(sku.BonusItemCount), bonusItemTemplate.FixedGrade >= 0 ? (byte)bonusItemTemplate.FixedGrade : (byte)0, true));
            }

            var mail = new CommercialMail(_targetPlayer.Id, _targetPlayer.Name, _buyer.Name, items, _targetPlayer.Id != _buyer.Id, false, useName);
            mail.FinalizeMail();
            if (!mail.Send())
            {
                // Sending this mail should actually never be able to fail.
                _targetPlayer.SendErrorMessage(ErrorMessageType.IngameShopBuyFail); // This is the wrong error, but likely the most fitting for now
            }

            entriesSold++;

            Logger.Info($"ICSBuyGood {_buyer.Name} -> {_targetPlayer.Name} - {useName} x {sku.ItemCount}, SKU:{sku.Sku}");
            if (!CashShopManager.Instance.LogSale(_buyer.AccountId, _buyer.Id, _targetPlayer.AccountId,
                    _targetPlayer.Id, DateTime.UtcNow, shopItem.ShopId, sku.Sku, (sku.DiscountPrice > 0 ? sku.DiscountPrice : sku.Price), sku.Currency, string.Empty))
                Logger.Error(
                    $"ICSBuyGood {_buyer.Name} -> {_targetPlayer.Name} - {useName} x {sku.ItemCount}, SKU:{sku.Sku}, save failed!");
        }

        if (entriesSold > 0)
        {
            var postSaleAccountDetails = AccountManager.Instance.GetAccountDetails(_buyer.AccountId);
            _buyer.BmPoint = postSaleAccountDetails.Loyalty;
            _buyer.SendPacket(new SCICSCashPointPacket(postSaleAccountDetails.Credits));
            _buyer.SendPacket(new SCBmPointPacket(postSaleAccountDetails.Loyalty));
            _buyer.SendPacket(new SCICSBuyResultPacket(true, _buyMode, _targetPlayer.Name, (int)costs[(byte)CashShopCurrencyType.AaPoints]));
        }
        else
        {
            _buyer.SendPacket(new SCICSBuyResultPacket(false, _buyMode, _targetPlayer.Name, (int)costs[(byte)CashShopCurrencyType.AaPoints]));
        }

        #endregion
    }
}
