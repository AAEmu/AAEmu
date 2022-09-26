using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCLoadInstancePacket : GamePacket
    {
        private readonly uint _instanceId;
        private readonly uint _zoneId;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;
        private readonly float _angX;
        private readonly float _angY;
        private readonly float _angZ;
        
        public SCLoadInstancePacket(uint instanceId, uint zoneId, float x, float y, float z, float angX, float angY, float angZ) 
            : base(SCOffsets.SCLoadInstancePacket, 5)
        {
            _instanceId = instanceId;
            _zoneId = zoneId;
            _x = x;
            _y = y;
            _z = z;
            _angX = angX;
            _angY = angY;
            _angZ = angZ;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_instanceId);
            stream.Write(_zoneId);
            stream.Write(_x);
            stream.Write(_y);
            stream.Write(_z);
            stream.Write(_angX);
            stream.Write(_angY);
            stream.Write(_angZ);
            return stream;
        }
    }
}
