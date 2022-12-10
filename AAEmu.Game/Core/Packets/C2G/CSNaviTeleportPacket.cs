using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNaviTeleportPacket : GamePacket
    {
        public CSNaviTeleportPacket() : base(CSOffsets.CSNaviTeleportPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            
            _log.Warn("NaviTeleport, ObjId: {0}", objId);
        }
    }
}
