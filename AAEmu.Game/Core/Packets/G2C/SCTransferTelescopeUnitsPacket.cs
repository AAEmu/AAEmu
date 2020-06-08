using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTransferTelescopeUnitsPacket : GamePacket
    {
        private readonly byte _last;
        private readonly byte _count;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        public SCTransferTelescopeUnitsPacket(byte last, byte count, float x, float y, float z) : base(SCOffsets.SCTransferTelescopeUnitsPacket, 1)
        {
            _last = last;
            _count = count;
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            //stream.Write(_last);
            //stream.Write(_count); //TODO max 10
            //for (int i = 0; i < _count; i++)
            //{
            //stream.WriteBc(objId);
            //stream.Write(type);
            //stream.WritePosition(_x, _y, _z);
            //stream.Write(name);
            //}
            stream.Write((byte)1);
            stream.Write((byte)3);

            stream.WriteBc(30);
            stream.Write(132);
            stream.WritePosition(14103.94f, 14396.37f, 422.774f);
            stream.Write("фермы-ривертон");
            stream.WriteBc(31);
            stream.Write(133);
            stream.WritePosition(15044.0f, 14089.78f, 481.798f);
            stream.Write("фермы-гардуэй-крепость полумесяца");
            stream.WriteBc(32);
            stream.Write(52);
            stream.WritePosition(14602.54f, 14045.79f, 657.798f);
            stream.Write("гвинедар-солрид");

            return stream;
        }
    }
}
