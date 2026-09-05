using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionMyBidListPacket() : GamePacket(CSOffsets.CSAuctionMyBidListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var page = stream.ReadInt32();
        AuctionManager.Instance.GetBidAuctionLots(Connection.ActiveChar, page);
    }
}
