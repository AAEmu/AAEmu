using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCNationalMonumentChangedPacket : GamePacket
    {
        private readonly ushort _id;
        private readonly long _type; // TODO id?
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;
        
        public SCNationalMonumentChangedPacket(ushort id, long type, float x, float y, float z) : base(SCOffsets.SCNationalMonumentChangedPacket, 5)
        {
            _id = id;
            _type = type;
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_type);
            stream.Write(Helpers.ConvertLongX(_x));
            stream.Write(Helpers.ConvertLongY(_y));
            stream.Write(_z);
            return stream;
        }
    }
}
