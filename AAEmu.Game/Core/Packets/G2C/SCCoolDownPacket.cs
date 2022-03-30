using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCooldownsPacket : GamePacket
    {
        private uint _skillId;
        private int _skillCount;
        private int _tagCount;
        private int _chargeCount;

        public SCCooldownsPacket() : base(SCOffsets.SCCooldownsPacket, 5)
        {
            _skillCount = 0;
            _tagCount = 0;
            _chargeCount = 0;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //TODO заготовка для пакета

            stream.Write(_skillCount); // skillCount
            for (var i = 0; i < _skillCount; i++)
            {
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
            }
            stream.Write(_tagCount); // tagCount
            for (var i = 0; i < _tagCount; i++)
            {
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
            }
            stream.Write(_chargeCount); // chargeCount
            for (var i = 0; i < _tagCount; i++)
            {
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
                stream.Write(0u); // type(id)
            }

            return stream;
        }
    }
}
