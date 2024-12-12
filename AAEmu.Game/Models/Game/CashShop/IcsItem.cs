using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// Table: ics_shop_items
/// </summary>
public class IcsItem
{
    /// <summary>
    /// Id for use on the tab
    /// </summary>
    public uint ShopId { get; set; }

    /// <summary>
    /// Item Template to display on the sub tab for this sales group
    /// </summary>
    public uint DisplayItemId { get; set; }

    /// <summary>
    /// Used to override name on the tabs, if blank, the first item name gets used
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Used to indicate if this item can only be sold a limited amount of times per character or account
    /// Unsure if this means item count, or if it means number of sales of any of the SKUs
    /// </summary>
    public CashShopLimitType LimitedType { get; set; }

    /// <summary>
    /// Number for use with LimitType
    /// </summary>
    public ushort LimitedStockMax { get; set; }

    /// <summary>
    /// Minimum level required to buy this item (0 means no minimum level)
    /// </summary>
    public byte LevelMin { get; set; }

    /// <summary>
    /// Maximum level allowed to buy this item (0 means no maximum level)
    /// </summary>
    public byte LevelMax { get; set; }

    /// <summary>
    /// Used to hide/disable the sale of this shop entry unless a specific quest is complete, or have a minimum level.
    /// This setting is different from LevelMin
    /// </summary>
    public CashShopRestrictSaleType BuyRestrictType { get; set; }

    /// <summary>
    /// Id to use for BuyRestrictType, either character Level or Quest Id
    /// </summary>
    public uint BuyRestrictId { get; set; } // either QuestId or LevelId

    /// <summary>
    /// Supposed to show if a item is on sale?
    /// </summary>
    public bool IsSale { get; set; }

    /// <summary>
    /// Supposed to show if a item is hidden? Setting does not seem to affect anything.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Start Date that this item can be bought. MinValue means disabled.
    /// </summary>
    public DateTime SaleStart { get; set; }

    /// <summary>
    /// End Date until this item can be bought. MinValue means disabled.
    /// </summary>
    public DateTime SaleEnd { get; set; }

    /// <summary>
    /// Number of items that are still available for limited sales.
    /// Only usable on the featured tab (1-1), and other tab will not display this amount.
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    /// Used to disable the buy/gift/cart buttons or some combination of them
    /// </summary>
    public CashShopCmdUiType ShopButtons { get; set; }

    /// <summary>
    /// List of actual SKUs to use for this entry
    /// </summary>
    public Dictionary<uint, IcsSku> Skus { get; set; } = [];

    public IcsSku FirstSku
    {
        get
        {
            if (Skus.Count <= 0)
                return null;
            return Skus.Values.First();
        }
    }
}
