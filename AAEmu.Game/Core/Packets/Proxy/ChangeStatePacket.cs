using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class ChangeStatePacket : GamePacket
    {
        private readonly int _state;

        public ChangeStatePacket(int state) : base(PPOffsets.ChangeStatePacket, 2)
        {
            _state = state;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_state);
            return stream;
        }
    }
}
