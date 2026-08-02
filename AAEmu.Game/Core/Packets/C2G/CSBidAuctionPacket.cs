using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSBidAuctionPacket() : GamePacket(CSOffsets.CSBidAuctionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();

        var display = new AuctionDisplay();
        stream.Read(display);

        var bid = new AuctionBid();
        stream.Read(bid);

        Logger.Warn($"AuctionBid, auctioneerId: {auctioneerId}, auctioneerId2: {auctioneerId2}, BidderName: {bid.BidderName}, LotId: {display.Lot.Id}:{bid.LotId}, Money: {bid.Money}");

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var minimumLevel = AppConfiguration.Instance.LevelRestrictions.AuctionBidLevel;
        if (character.Level + character.HeirLevel < minimumLevel)
        {
            Logger.Warn("Rejected auction bid from {0}: total level {1} is below {2}",
                character.Name, character.Level + character.HeirLevel, minimumLevel);
            return;
        }

        AuctionManager.Instance.BidOnAuctionLot(character, auctioneerId, auctioneerId2, display.Lot, bid);
    }
}
