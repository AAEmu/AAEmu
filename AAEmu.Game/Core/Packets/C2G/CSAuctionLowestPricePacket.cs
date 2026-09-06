using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionLowestPricePacket() : GamePacket(CSOffsets.CSAuctionLowestPricePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var itemTemplateId = stream.ReadUInt32();
        var itemGrade = stream.ReadByte();
        AuctionManager.Instance.CheapestAuctionLot(Connection.ActiveChar, itemTemplateId, itemGrade);
    }
}
