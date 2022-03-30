using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCGlideBuffRemovedPacket : GamePacket
    {
        private readonly uint _objId;
        private readonly uint _index;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        public SCGlideBuffRemovedPacket(uint objId, uint index, float x, float y, float z) : base(SCOffsets.SCGlideBuffRemovedPacket, 5)
        {
            _objId = objId;
            _index = index;
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_objId); // targetId
            stream.Write(_index);   // buffId (type)
            stream.WritePosition(_x, _y, _z); // add in 3.5.0.3

            return stream;
        }
    }
}
