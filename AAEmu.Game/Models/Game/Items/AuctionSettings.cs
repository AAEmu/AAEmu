namespace AAEmu.Game.Models.Game.Items;

public class AuctionSettings
{
    public int CategoryA;
    public int CategoryB;
    public int CategoryC;
    public uint AuctionCharge;
    public bool AuctionChargeDefault;

    public AuctionSettings(int categoryA, int categoryB, int categoryC, uint auctionCharge, bool auctionChargeDefault)
    {
        CategoryA = categoryA;
        CategoryB = categoryB;
        CategoryC = categoryC;
        AuctionCharge = auctionCharge;
        AuctionChargeDefault = auctionChargeDefault;
    }
}
