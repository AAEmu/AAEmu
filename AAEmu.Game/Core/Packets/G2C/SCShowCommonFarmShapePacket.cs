using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCShowCommonFarmShapePacket : GamePacket
    {
        private readonly int _farmType;
        private readonly int _count;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        public SCShowCommonFarmShapePacket(int farmType, int count, Vector3 pos) : base(SCOffsets.SCShowCommonFarmShapePacket, 5)
        {
            _farmType = farmType;
            _count = count;
            _x = pos.X;
            _y = pos.Y;
            _z = pos.Z;
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
