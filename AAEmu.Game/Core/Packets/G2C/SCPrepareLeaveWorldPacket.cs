using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPrepareLeaveWorldPacket : GamePacket
    {
        private readonly int _time;
        private readonly byte _target;
        private readonly bool _idleKick;

        public SCPrepareLeaveWorldPacket(int time, byte target, bool idleKick) : base(SCOffsets.SCPrepareLeaveWorldPacket, 5)
        {
            _time = time;
            _target = target;
            _idleKick = idleKick;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_time);
            stream.Write(_target);
            stream.Write(_idleKick);
            return stream;
        }
    }
}
