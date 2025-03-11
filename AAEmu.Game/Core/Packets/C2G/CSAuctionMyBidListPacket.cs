using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionMyBidListPacket : GamePacket
{
    public CSAuctionMyBidListPacket() : base(CSOffsets.CSAuctionMyBidListPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();
        var page = stream.ReadInt32();

        Logger.Warn($"AuctionMyBidList, auctioneerId: {auctioneerId}, auctioneerId2: {auctioneerId2}, Page: {page}");

        AuctionManager.Instance.GetBidAuctionLots(Connection.ActiveChar, page);
    }
}
