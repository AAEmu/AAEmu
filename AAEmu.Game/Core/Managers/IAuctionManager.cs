using System.Collections.Concurrent;

using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Core.Managers;

public interface IAuctionManager : ILoadable
{
    ConcurrentDictionary<ulong, AuctionLot> AuctionLots { get; }
    void CancelAuctionLot(Character player, ulong auctionId);
    void BidOnAuctionLot(Character player, AuctionBid bid);
    void GetBidAuctionLots(Character player, int page);
    void CheapestAuctionLot(Character player, uint templateId, byte itemGrade = 0);
    void SearchSoldRecords(Character player, uint templateId, byte grade, bool askMarketPriceUi);
    void AddAuctionLot(AuctionLot lot);
    void UpdateAuctionHouse();
    AuctionLot CreateAuctionLot(uint playerId, string playerName, Item itemToList, long startPrice, long buyoutPrice, AuctionDuration duration, int minStack = 1, int maxStack = 1);
    void SearchAuctionLots(Character player, AuctionSearch search);
    bool PostLotOnAuction(Character player, ulong itemId, long startPrice, long buyoutPrice, AuctionDuration duration, int minStack, int maxStack);
    (int, int) Save(MySqlConnection connection, MySqlTransaction transaction);
}
