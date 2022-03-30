using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCKnockBackUnitPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        public SCKnockBackUnitPacket(uint objId, float x, float y, float z) : base(SCOffsets.SCKnockBackUnitPacket, 5)
        {
            _objId = objId;
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId);
            stream.Write(_x);
            stream.Write(_y);
            stream.Write(_z);
            return stream;
        }
    }
}
