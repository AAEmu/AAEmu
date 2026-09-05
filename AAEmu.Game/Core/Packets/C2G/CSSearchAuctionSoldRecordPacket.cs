using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSearchAuctionSoldRecordPacket() : GamePacket(CSOffsets.CSSearchAuctionSoldRecordPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var templateId = stream.ReadUInt32();
        var grade = stream.ReadByte();
        var askMarketPriceUi = stream.ReadBoolean();
        AuctionManager.Instance.SearchSoldRecords(Connection.ActiveChar, templateId, grade, askMarketPriceUi);
    }
}
