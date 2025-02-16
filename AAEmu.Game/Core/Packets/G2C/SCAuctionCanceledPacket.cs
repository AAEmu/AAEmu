using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionCanceledPacket : GamePacket
{
    private readonly AuctionLot _auctionLot;

    public SCAuctionCanceledPacket(AuctionLot auctionLot) : base(SCOffsets.SCAuctionCanceledPacket, 5)
    {
        _auctionLot = auctionLot;
    }

    public override PacketStream Write(PacketStream stream)
    {
        _auctionLot.Write(stream);

        return stream;
    }
}
