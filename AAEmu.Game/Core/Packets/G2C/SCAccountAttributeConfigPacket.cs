using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAccountAttributeConfigPacket : GamePacket
    {
        private readonly bool[] _used;

        public SCAccountAttributeConfigPacket(bool[] used) : base(SCOffsets.SCAccountAttributeConfigPacket, 5)
        {
            _used = used;
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 2; i++) // in 1.2 = 2, in 1.7 = 2, in 3.5 = 3
            {
                stream.Write(_used[i]); // 0 = false, 1 = true
            }
            return stream;
        }
    }
}
