using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCBuffLearnedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _buffId;

        public SCBuffLearnedPacket(uint objId, uint buffId) : base(0x105, 1) // TODO 1.0 opcode: 0x101
        {
            _objId = objId;
            _buffId = buffId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_buffId);
            return stream;
        }
    }
}
