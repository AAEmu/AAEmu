using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCRawPacket : GamePacket
    {
         private byte[] _payload;
        public SCRawPacket(ushort opcode, byte[] payload) : base(opcode, 1)
        {
            _payload = payload;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_payload);
            return stream;
        }
    }
}
