using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionLowestPricePacket : GamePacket
    {
        public CSAuctionLowestPricePacket() : base(0x0bc, 1) //TODO 1.0 opcode: 0x0b8
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var itemTemplateId = stream.ReadUInt32();
            var itemGrade = stream.ReadByte();

            _log.Warn("AuctionLowestPrice, NpcObjId: {0}, TemplateId: {1}, Grade: {2}", npcObjId, itemTemplateId, itemGrade);
        }
    }
}
