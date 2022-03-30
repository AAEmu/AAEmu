using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAddActionPointPacket : GamePacket
    {
        private readonly int _action;
        private readonly int _value;

        public SCAddActionPointPacket(int action, int value) : base(SCOffsets.SCAddActionPointPacket, 5)
        {
            _action = action;
            _value = value;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_action);
            stream.Write(_value);
            return stream;
        }
    }
}
