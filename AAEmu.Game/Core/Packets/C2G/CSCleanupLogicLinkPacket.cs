using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCleanupLogicLinkPacket : GamePacket
    {
        public CSCleanupLogicLinkPacket() : base(CSOffsets.CSCleanupLogicLinkPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            
            _log.Warn("CleanupLogicLink, ObjId: {0}", objId);
        }
    }
}
