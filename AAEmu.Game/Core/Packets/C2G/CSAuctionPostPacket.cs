using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionPostPacket : GamePacket
    {
        public CSAuctionPostPacket() : base(0x0b7, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var itemId = stream.ReadUInt64();
            var startPrice = stream.ReadInt32();
            var buyoutPrice = stream.ReadInt32();
            var duration = stream.ReadByte();

            _log.Warn("AuctionPost, NpcObjId: {0}, ItemId: {1}", npcObjId, itemId);
        }
    }
}
