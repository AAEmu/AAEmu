using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAuctionSearchedPacket(int page, int count, List<AuctionLot> lots, short errorMsg, DateTime serverTime)
    : GamePacket(SCOffsets.SCAuctionSearchedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(page);
        stream.Write(count);

        foreach (var lot in lots) // TODO не более 9
        {
            stream.Write(lot);
        }

        stream.Write(errorMsg);
        stream.Write(serverTime);

        return stream;
    }
}
