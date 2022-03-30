using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNotifyPacket : GamePacket
    {
        private readonly uint _size;
        
        public SCNotifyPacket() : base(SCOffsets.SCNotifyPacket, 5)
        {
            _size = 0u;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(false); // notify
            stream.Write(_size); // size
            // infos
            for (var i = 0; i < _size; i++)
            {
                // pair
                stream.Write((ushort)0); // k
                stream.Write((ulong)0); // v
            }

            return stream;
        }
    }
}
