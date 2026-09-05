using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionSearchedPacket(int page, IReadOnlyList<AuctionLot> lots, short errorMsg, DateTime serverTime)
    : GamePacket(SCOffsets.SCAuctionSearchedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var pageLots = lots ?? [];
        var count = Math.Min(pageLots.Count, AuctionHouseRules.SearchPageSize);

        stream.Write(page);
        stream.Write(count);
        for (var i = 0; i < count; i++)
            stream.Write(pageLots[i]);
        stream.Write(errorMsg);
        stream.Write(serverTime);
        return stream;
    }
}
