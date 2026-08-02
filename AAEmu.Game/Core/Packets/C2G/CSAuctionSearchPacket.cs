using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Auction.Templates;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionSearchPacket() : GamePacket(CSOffsets.CSAuctionSearchPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();

        var auctionSearch = new AuctionSearch();
        stream.Read(auctionSearch);

        Logger.Warn($"AuctionSearch, auctioneerId: {auctioneerId}, auctioneerId: {auctioneerId2}, Keyword: {auctionSearch.Keyword}");

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var minimumLevel = AppConfiguration.Instance.LevelRestrictions.AuctionSearchLevel;
        if (character.Level + character.HeirLevel < minimumLevel)
        {
            Logger.Warn("Rejected auction search from {0}: total level {1} is below {2}",
                character.Name, character.Level + character.HeirLevel, minimumLevel);
            return;
        }

        AuctionManager.Instance.SearchAuctionLots(character, auctionSearch);
    }
}
