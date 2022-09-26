using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAccountAttendancePacket : GamePacket
    {
        private readonly uint _count;
        private readonly ulong _accountAttendance;

        public SCAccountAttendancePacket(uint count) : base(SCOffsets.SCAccountAttendancePacket, 5)
        {
            _count = count;
            _accountAttendance = 0;

        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < _count; i++)
            {
                stream.Write(_accountAttendance);
            }

            return stream;
        }
    }
}
