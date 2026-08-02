namespace AAEmu.Game.Models.Game.Items;

public class AuctionSettings(int categoryA, int categoryB, int categoryC, int auctionCharge, bool auctionChargeDefault)
{
    public int CategoryA = categoryA;
    public int CategoryB = categoryB;
    public int CategoryC = categoryC;

    /// <summary>
    /// Sale commission for this item in basis points, overriding the global <c>auction_charge</c>.
    /// Only meaningful when <see cref="AuctionChargeDefault"/> is false: 50944 of the 50978 shipped items
    /// carry 0 here and defer to the global rate, the remaining rows carry 100, 500 or 1000.
    /// </summary>
    public int AuctionCharge = auctionCharge;

    /// <summary>When true the item uses the global auction_charge rate and ignores <see cref="AuctionCharge"/>.</summary>
    public bool AuctionChargeDefault = auctionChargeDefault;

    /// <summary>Rate to bill a sale of this item at, 0 meaning "use the house default".</summary>
    public int EffectiveChargeRate => AuctionChargeDefault ? 0 : AuctionCharge;
}
