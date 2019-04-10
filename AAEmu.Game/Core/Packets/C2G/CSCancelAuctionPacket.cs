using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCancelAuctionPacket : GamePacket
    {
        public CSCancelAuctionPacket() : base(0x0b6, 1)
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
