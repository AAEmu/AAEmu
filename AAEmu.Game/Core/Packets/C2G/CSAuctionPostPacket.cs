using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Auction;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionPostPacket : GamePacket
{
    public CSAuctionPostPacket() : base(CSOffsets.CSAuctionPostPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();
        var itemId = stream.ReadUInt64();
        var startPrice = stream.ReadInt32();
        var buyoutPrice = stream.ReadInt32();
        var duration = (AuctionDuration)stream.ReadByte();
        var minStack = stream.ReadInt32();
        var maxStack = stream.ReadInt32();

        Logger.Warn($"AuctionMyBidList, auctioneerId: {auctioneerId}, auctioneerId2: {auctioneerId2}, itemId: {itemId}, startPrice: {startPrice}, buyoutPrice: {buyoutPrice}, duration: {duration}, minStack: {minStack}, maxStack: {maxStack}");

        AuctionManager.Instance.PostLotOnAuction(Connection.ActiveChar, auctioneerId, auctioneerId2, itemId, startPrice, buyoutPrice, duration, minStack, maxStack);
    }
}
