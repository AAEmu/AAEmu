using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMySlavePacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly ushort _tl; // TODO slaveId
        private readonly string _slaveName;
        private readonly uint _templateId;
        private readonly int _hp;
        private readonly int _maxHp;
        private readonly float _x;
        private readonly float _y;
        private readonly float _z;

        public SCMySlavePacket(uint unitId, ushort tl, string slaveName, uint templateId, int hp, int maxHp, float x, float y, float z)
            : base(SCOffsets.SCMySlavePacket, 5)
        {
            _unitId = unitId;
            _tl = tl;
            _slaveName = slaveName;
            _templateId = templateId;
            _hp = hp;
            _maxHp = maxHp;
            _x = x;
            _y = y;
            _z = z;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);   // unitId
            stream.Write(_tl);         // tl | slaveId
            stream.Write(_slaveName);  // slaveName
            stream.Write(_templateId); // type
            stream.Write(_hp);         // hp
            stream.Write(_maxHp);      // maxHp
            stream.Write(Helpers.ConvertLongX(_x));
            stream.Write(Helpers.ConvertLongY(_y));
            stream.Write(_z);

            return stream;
        }
    }
}
