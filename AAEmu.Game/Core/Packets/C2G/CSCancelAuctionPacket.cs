using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCancelAuctionPacket() : GamePacket(CSOffsets.CSCancelAuctionPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var lot = new AuctionLot();
        stream.Read(lot);
        AuctionManager.Instance.CancelAuctionLot(Connection.ActiveChar, lot.Id);
    }
}
