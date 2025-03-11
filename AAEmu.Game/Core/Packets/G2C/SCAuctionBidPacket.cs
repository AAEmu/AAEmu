using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionBidPacket : GamePacket
{
    private readonly AuctionBid _bid;
    private readonly bool _isBuyout;
    private readonly uint _itemId;

    public SCAuctionBidPacket(AuctionBid bid, bool isBuyout, uint itemId) : base(SCOffsets.SCAuctionBidPacket, 5)
    {
        _bid = bid;
        _isBuyout = isBuyout;
        _itemId = itemId;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_bid);
        stream.Write(_isBuyout);
        stream.Write(_itemId);

        return stream;
    }
}
