using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Bids on a plot auction: the client sends the activity, the config id (its script bind passes
/// the config id in the auctionConfigId slot — limited_auction_tab.lua line 1542) and the bid.
/// </summary>
public class CSPlotAuctionPlaceBidPacket() : GamePacket(CSOffsets.CSPlotAuctionPlaceBidPacket, 1)
{
    public uint ActivityId { get; private set; }
    public uint AuctionConfigId { get; private set; }
    public uint BidAmount { get; private set; }

    public override void Read(PacketStream stream)
    {
        ActivityId = stream.ReadUInt32();
        AuctionConfigId = stream.ReadUInt32();
        BidAmount = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        PlotAuctionManager.Instance.PlaceBid(character, ActivityId, AuctionConfigId, BidAmount);
    }
}
