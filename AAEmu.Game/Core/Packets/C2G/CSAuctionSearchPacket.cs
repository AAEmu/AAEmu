using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Auction.Templates;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionSearchPacket : GamePacket
{

    public CSAuctionSearchPacket() : base(CSOffsets.CSAuctionSearchPacket, 1)
    {
    }

    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();

        var auctionSearch = new AuctionSearch();
        stream.Read(auctionSearch);

        Logger.Warn($"AuctionSearch, auctioneerId: {auctioneerId}, auctioneerId: {auctioneerId2}, Keyword: {auctionSearch.Keyword}");

        AuctionManager.Instance.SearchAuctionLots(Connection.ActiveChar, auctionSearch);
    }
}
