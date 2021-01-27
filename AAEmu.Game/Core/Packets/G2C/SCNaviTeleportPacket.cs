using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNaviTeleportPacket : GamePacket
    {
        private float _x;
        private float _y;
        private float _z;
        
        public SCNaviTeleportPacket(float x, float y, float z) : base(SCOffsets.SCNaviTeleportPacket, 5)
        {
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(Helpers.ConvertLongX(_x));
            stream.Write(Helpers.ConvertLongY(_y));
            stream.Write(_z);
            return stream;
        }
    }
}
