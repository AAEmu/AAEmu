namespace AAEmu.Game.Models.Game.Auction;

/// <summary>
/// <c>SCAuctionMessage</c> toast. The client only branches on 0 and 1.
/// </summary>
public enum AuctionMessageKind : byte
{
    Sold = 0,
    Outbid = 1
}
