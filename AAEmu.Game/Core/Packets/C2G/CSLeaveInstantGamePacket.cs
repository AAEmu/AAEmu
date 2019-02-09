using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveInstantGamePacket : GamePacket
    {
        public CSLeaveInstantGamePacket() : base(0x0e1, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("LeaveInstantGame");
        }
    }
}
