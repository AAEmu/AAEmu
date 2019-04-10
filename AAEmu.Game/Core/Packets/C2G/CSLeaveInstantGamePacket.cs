using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveInstantGamePacket : GamePacket
    {
        public CSLeaveInstantGamePacket() : base(0x0e5, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Warn("LeaveInstantGame");
        }
    }
}
