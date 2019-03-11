using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNaviOpenPortalPacket : GamePacket
    {
        public CSNaviOpenPortalPacket() : base(0x0e8, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            
            _log.Warn("NaviOpenPortal, ObjId: {0}", objId);
        }
    }
}
