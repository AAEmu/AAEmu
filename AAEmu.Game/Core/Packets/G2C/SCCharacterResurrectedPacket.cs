using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterResurrectedPacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;
        private readonly float _zRot;
        
        public SCCharacterResurrectedPacket(uint unitId, float x, float y, float z, float zRot) 
            : base(SCOffsets.SCCharacterResurrectedPacket, 5)
        {
            _unitId = unitId;
            _x = x;
            _y = y;
            _z = z;
            _zRot = zRot;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.WritePosition(_x, _y, _z);
            stream.Write(_zRot);
            return stream;
        }
    }
}
