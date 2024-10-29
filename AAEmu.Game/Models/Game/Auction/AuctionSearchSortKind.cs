namespace AAEmu.Game.Models.Game.Auction;

public enum AuctionSearchSortKind : byte
{
    Default = 0,
    Evaluated = 1,
    ItemName = 2,
    ItemLevel = 3,
    ExpireDate = 4,
    BidPrice = 5,
    DirectPrice = 6
}
