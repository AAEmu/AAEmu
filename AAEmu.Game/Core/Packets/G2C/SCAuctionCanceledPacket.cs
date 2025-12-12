using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionCanceledPacket(AuctionLot auctionLot) : GamePacket(SCOffsets.SCAuctionCanceledPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(auctionLot);

        return stream;
    }
}
