using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Auction.Templates;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionSearchForMultilingualPacket() : GamePacket(CSOffsets.CSAuctionSearchForMultilingualPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var auctionSearch = new AuctionSearch();
        stream.Read(auctionSearch);
        auctionSearch.ReadItemTemplateIds(stream);

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var minimumLevel = AppConfiguration.Instance.LevelRestrictions.AuctionSearchLevel;
        if (character.Level + character.HeirLevel < minimumLevel)
        {
            Logger.Warn("Rejected multilingual auction search from {0}: total level {1} is below {2}",
                character.Name, character.Level + character.HeirLevel, minimumLevel);
            return;
        }

        AuctionManager.Instance.SearchAuctionLots(character, auctionSearch);
    }
}
