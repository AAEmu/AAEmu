using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSAuctionSearchSoldRecordPacket : GamePacket
{
    public CSAuctionSearchSoldRecordPacket() : base(CSOffsets.CSAuctionSearchSoldRecordPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var itemTemplateId = stream.ReadUInt32();
        var itemGrade = stream.ReadByte();

        var solds = AuctionManager.Instance.GetSoldAuctionLots(itemTemplateId, itemGrade);

        Connection.ActiveChar.SendPacket(new SCAuctionSoldRecordPacket(itemTemplateId, itemGrade, solds));
    }
}
