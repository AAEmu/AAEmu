using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPrepareLeaveWorldPacket : GamePacket
    {
        private readonly int _time;
        private readonly byte _target;

        public SCPrepareLeaveWorldPacket(int time, byte target) : base(0x002, 1)
        {
            _time = time;
            _target = target;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_time);
            stream.Write(_target);
            return stream;
        }
    }
}