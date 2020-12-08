using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelAuctionPacket : GamePacket
    {
        public CSCancelAuctionPacket() : base(CSOffsets.CSCancelAuctionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            var npcObjId2 = stream.ReadBc();

            var auctionId = stream.ReadUInt64();
            var duration = stream.ReadByte();
            var itemId = stream.ReadUInt32();
            var objectId = stream.ReadUInt64();
            var grade = stream.ReadByte();
            var bound = stream.ReadByte();
            var stackSize = stream.ReadUInt32();
            var detailType = stream.ReadByte();
            var creationTime = stream.ReadDateTime();
            var lifeSpan = stream.ReadUInt32();
            var type1 = stream.ReadUInt32();
            var worldID = stream.ReadByte();
            var unsecureTime = stream.ReadDateTime();
            var unpackTime = stream.ReadDateTime();
            var worldId2 = stream.ReadByte();
            var type2 = stream.ReadUInt32();
            var clientName = stream.ReadString();
            var startMoney = stream.ReadUInt32();
            var directMoney = stream.ReadUInt32();
            var timeLeft = stream.ReadUInt64();
            var bidWorld = stream.ReadByte();
            var type3 = stream.ReadUInt32();
            var bidderName = stream.ReadString();
            var bidMoney = stream.ReadUInt32();
            var extra = stream.ReadUInt32();
            var player = Connection.ActiveChar;

            AuctionManager.Instance.CancelAuctionItem(player, auctionId);
        }
    }
}
