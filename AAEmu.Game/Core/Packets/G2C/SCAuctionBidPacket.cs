using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// 10.0.2.13 body, named by its own serializer: u64 lot, i8 worldId, u64 bidder, string name, u64
/// moneyAmount, u32 stackSize, bool isBuyout, i32 item. The first six are the AuctionBid block, which has
/// been widened to match; 1.2 sent the bidder and money as u32 and had no stack size.
/// </remarks>
public class SCAuctionBidPacket(AuctionBid bid, bool isBuyout, uint itemId)
    : GamePacket(SCOffsets.SCAuctionBidPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(bid);
        stream.Write(isBuyout);
        stream.Write((int)itemId);

        return stream;
    }
}
