using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionLowestPricePacket : GamePacket
{
    public CSAuctionLowestPricePacket() : base(CSOffsets.CSAuctionLowestPricePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var auctioneerId = stream.ReadBc();
        var auctioneerId2 = stream.ReadBc();
        var itemTemplateId = stream.ReadUInt32();
        var itemGrade = stream.ReadByte();

        Logger.Warn($"AuctionLowestPrice, auctioneerId: {auctioneerId}, auctioneerId2: {auctioneerId2}, TemplateId: {itemTemplateId}, Grade: {itemGrade}");

        AuctionManager.Instance.CheapestAuctionLot(Connection.ActiveChar, itemTemplateId, itemGrade);
    }
}
