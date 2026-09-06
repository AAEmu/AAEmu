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
        var lot = new AuctionLot();
        stream.Read(lot);
        var bid = new AuctionBid();
        stream.Read(bid);

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

        if (bid.LotId == 0)
            bid.LotId = lot.Id;

        AuctionManager.Instance.BidOnAuctionLot(character, bid);
    }
}
