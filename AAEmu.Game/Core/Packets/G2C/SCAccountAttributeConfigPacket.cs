using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAccountAttributeConfigPacket : GamePacket
    {
        private readonly bool[] _used;

        public SCAccountAttributeConfigPacket(bool[] used) : base(SCOffsets.SCAccountAttributeConfigPacket, 1)
        {
            _used = used;
        }

        public override PacketStream Write(PacketStream stream)
        {
            for (var i = 0; i < 2; i++) // 2
            {
                stream.Write(_used[i]);
            }
            return stream;
        }
    }
}
