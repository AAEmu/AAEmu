using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLeaveWorldGrantedPacket : GamePacket
    {
        private readonly byte _target;

        public SCLeaveWorldGrantedPacket(byte target) : base(0x003, 1)
        {
            _target = target;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_target);
            return stream;
        }
    }
}