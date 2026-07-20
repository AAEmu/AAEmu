using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionBidPacket(AuctionBid bid, bool isBuyout, uint itemId)
    : GamePacket(SCOffsets.SCAuctionBidPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(bid);
        stream.Write(isBuyout);
        stream.Write(itemId);

        return stream;
    }
}
