using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCShowCommonFarmPacket : GamePacket
    {
        private readonly int _farmType;
        private readonly int _count;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        public SCShowCommonFarmPacket(int farmType, int count, float x, float y, float z) : base(SCOffsets.SCShowCommonFarmPacket, 1)
        {
            _farmType = farmType;
            _count = count;
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_farmType);
            stream.Write(_count);
            stream.Write(Helpers.ConvertLongX(_x));
            stream.Write(Helpers.ConvertLongY(_y));
            stream.Write(_z);
            return stream;
        }

    }
}
