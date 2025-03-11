using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionCancelPacket : GamePacket
{
    public CSAuctionCancelPacket() : base(CSOffsets.CSAuctionCancelPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();

        var lot = new AuctionLot();
        //lot.Read(stream);
        stream.Read(lot);

        Logger.Warn($"AuctionCancel, auctioneerId: {auctioneerId}, auctioneerId2: {auctioneerId2}, ClientName: {lot.ClientName}, LotId: {lot.Id}");

        AuctionManager.Instance.CancelAuctionLot(Connection.ActiveChar, lot.Id);
    }
}
