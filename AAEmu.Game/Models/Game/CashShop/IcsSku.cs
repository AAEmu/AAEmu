using System;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// Table: ics_skus
/// </summary>
public class IcsSku
{
    /// <summary>
    /// Unique Shop Id for this specific selection
    /// </summary>
    public uint Sku { get; set; }

    /// <summary>
    /// Id of the shop entry on the sub tab
    /// </summary>
    public uint ShopId { get; set; }

    /// <summary>
    /// Position inside this shop entry
    /// </summary>
    public int Position { get; set; } // Used to change item order

    /// <summary>
    /// Actual Item Template of the item being sold
    /// </summary>
    public uint ItemId { get; set; }

    /// <summary>
    /// Number of items for this entry
    /// </summary>
    public uint ItemCount { get; set; }

    /// <summary>
    /// SelectType
    /// </summary>
    public byte SelectType { get; set; }

    /// <summary>
    /// Indicates this selection should be the default
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Event label on the item (seems to not work in 1.2)
    /// 4 should be "New"
    /// </summary>
    public byte EventType { get; set; }

    /// <summary>
    /// End of the Event Date
    /// </summary>
    public DateTime EventEndDate { get; set; } = DateTime.MinValue;

    /// <summary>
    /// What type of currency this item is sold for
    /// </summary>
    public CashShopCurrencyType Currency { get; set; }

    /// <summary>
    /// Price this selection is sold for if not discounted
    /// </summary>
    public uint Price { get; set; }

    /// <summary>
    /// Price listed as the discounted price
    /// </summary>
    public uint DiscountPrice { get; set; }

    /// <summary>
    /// Bonus Item included with this purchase
    /// </summary>
    public uint BonusItemId { get; set; }

    /// <summary>
    /// Number of bonus items with this purchase
    /// </summary>
    public uint BonusItemCount { get; set; }
}
