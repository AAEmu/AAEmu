using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionLowestPricePacket : GamePacket
    {
        public CSAuctionLowestPricePacket() : base(0x0bc, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var itemTemplateId = stream.ReadUInt32();
            var itemGrade = stream.ReadByte();

            _log.Warn("AuctionLowestPrice, NpcObjId: {0}, TemplateId: {1}, Grade: {2}", npcObjId, itemTemplateId, itemGrade);

            var cheapestItem = AuctionManager.Instance.GetCheapestAuctionItem(itemTemplateId);

            Connection.ActiveChar.SendPacket(new SCAuctionLowestPricePacket(cheapestItem));

        }
    }
}
