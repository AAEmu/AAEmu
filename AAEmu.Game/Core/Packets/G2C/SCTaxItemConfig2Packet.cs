using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTaxItemConfig2Packet : GamePacket
    {
        private readonly int _count;
        private readonly ushort _type;
        private readonly bool _declareDominion;

        public SCTaxItemConfig2Packet(int count) : base(SCOffsets.SCTaxItemConfig2Packet, 5)
        {
            _count = count;
            _type = 0;
            _declareDominion = false;

        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_count);
            for (var i = 0; i < _count; i++)
            {
                stream.Write(_type); // type
                stream.Write(_declareDominion); // declareDominion
            }

            return stream;
        }
    }
}
