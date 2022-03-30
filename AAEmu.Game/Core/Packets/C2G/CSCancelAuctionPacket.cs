using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelAuctionPacket : GamePacket
    {
        public CSCancelAuctionPacket() : base(CSOffsets.CSCancelAuctionPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var npcObjId = stream.ReadBc();
            // TODO struct

            _log.Warn("CancelAuction, NpcObjId: {0}", npcObjId);
        }
    }
}
