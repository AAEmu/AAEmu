using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSEventCenterAddAttendancePacket : GamePacket
    {
        public CSEventCenterAddAttendancePacket() : base(CSOffsets.CSEventCenterAddAttendancePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSEventCenterAddAttendancePacket");
        }
    }
}
