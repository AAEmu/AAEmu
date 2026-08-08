namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// One resolved cart line for a cash-shop purchase: the SKU plus the client's <see cref="DetailIndex"/>
/// (the SKU's position within its shop item's SKUs, as sent in CSICSBuyGood). The index is preserved so
/// the buy-result (SCICSBuySucceeded) can echo back buyItem/remainBuyCount for the client to resolve.
/// </summary>
public record IcsPurchase(IcsSku Sku, byte DetailIndex);
