namespace AAEmu.Game.Models.Game.Items;

public class AuctionSettings(int categoryA, int categoryB, int categoryC)
{
    public int CategoryA = categoryA;
    public int CategoryB = categoryB;
    public int CategoryC = categoryC;
    //public uint AuctionCharge; // added in 3+
    //public bool AuctionChargeDefault; // added in 3+

    /*, uint auctionCharge, bool auctionChargeDefault*/
    //AuctionCharge = auctionCharge;
    //AuctionChargeDefault = auctionChargeDefault;
}
