using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSICSMenuListPacket : GamePacket
    {
        public CSICSMenuListPacket() : base(CSOffsets.CSICSMenuListPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("ICSMenuList");
            
            Connection.SendPacket(new SCICSMenuListPacket(1));
        }
    }
}
