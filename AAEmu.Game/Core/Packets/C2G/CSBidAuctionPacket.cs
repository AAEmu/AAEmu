using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBidAuctionPacket : GamePacket
{
    public CSBidAuctionPacket() : base(CSOffsets.CSBidAuctionPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();

        var display = new AuctionDisplay();
        stream.Read(display);

        var bid = new AuctionBid();
        stream.Read(bid);

        Logger.Warn($"AuctionBid, auctioneerId: {auctioneerId}, auctioneerId2: {auctioneerId2}, BidderName: {bid.BidderName}, LotId: {display.Lot.Id}:{bid.LotId}, Money: {bid.Money}");

        AuctionManager.Instance.BidOnAuctionLot(Connection.ActiveChar, auctioneerId, auctioneerId2, display.Lot, bid);
    }
}
