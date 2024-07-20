using System;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.CashShop;

public class AuditIcsSale
{
    public long Id { get; set; }

    /// <summary>
    /// Account ID of the person buying this item
    /// </summary>
    public uint BuyerAccount { get; set; }

    /// <summary>
    /// Character that was logged in when buying
    /// </summary>
    public uint BuyerChar { get; set; }

    /// <summary>
    /// Account of the person receiving the goods
    /// </summary>
    public uint TargetAccount { get; set; }

    /// <summary>
    /// Character that received the goods
    /// </summary>
    public uint TargetChar { get; set; }

    /// <summary>
    /// Time of purchase (in UTC)
    /// </summary>
    public DateTime SaleDate { get; set; }

    /// <summary>
    /// Shop item entry id of the sold item
    /// </summary>
    public uint ShopItemId { get; set; }

    /// <summary>
    /// SKU of the sold item
    /// </summary>
    public uint Sku { get; set; }

    /// <summary>
    /// Amount this item was sold for
    /// </summary>
    public int SaleCost { get; set; }

    /// <summary>
    /// Which currency was used
    /// </summary>
    public CashShopCurrencyType SaleCurrency { get; set; }

    /// <summary>
    /// Added description of this transaction
    /// </summary>
    public string Description { get; set; }
}
