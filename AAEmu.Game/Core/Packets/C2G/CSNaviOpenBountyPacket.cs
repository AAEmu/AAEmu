using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNaviOpenBountyPacket : GamePacket
    {
        public CSNaviOpenBountyPacket() : base(0x0ea, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            
            _log.Warn("NaviOpenBounty, ObjId: {0}", objId);
        }
    }
}
