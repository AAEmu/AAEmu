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
    void BidOnAuctionLot(Character player, uint auctioneerId, uint auctioneerId2, AuctionLot lot, AuctionBid bid);
    void GetBidAuctionLots(Character player, int page);
    void CheapestAuctionLot(Character player, uint templateId, byte itemGrade = 0);
    void AddAuctionLot(AuctionLot lot);
    void UpdateAuctionHouse();
    AuctionLot CreateAuctionLot(uint playerId, string playerName, Item itemToList, int startPrice, int buyoutPrice, AuctionDuration duration, int minStack = 1, int maxStack = 1);
    void SearchAuctionLots(Character player, AuctionSearch search);
    void PostLotOnAuction(Character player, uint npcId, uint npcId2, ulong itemId, int startPrice, int buyoutPrice, AuctionDuration duration);
    (int, int) Save(MySqlConnection connection, MySqlTransaction transaction);
}
