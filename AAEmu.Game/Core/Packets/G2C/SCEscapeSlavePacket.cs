using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCEscapeSlavePacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;
        private readonly float _rot;
        
        public SCEscapeSlavePacket(uint unitId, float x, float y, float z, float rot) : base(SCOffsets.SCEscapeSlavePacket, 5)
        {
            _unitId = unitId;
            _x = x;
            _y = y;
            _z = z;
            _rot = rot;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.Write(Helpers.ConvertLongX(_x));
            stream.Write(Helpers.ConvertLongY(_y));
            stream.Write(_z);
            stream.Write(_rot);
            return stream;
        }
    }
}
