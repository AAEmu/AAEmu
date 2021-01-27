using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitPvPPointsChangedPacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly byte _kind;
        private readonly int _point;

        public SCUnitPvPPointsChangedPacket(uint unitId, byte kind, int point) : base(SCOffsets.SCUnitPvPPointsChangedPacket, 5)
        {
            _unitId = unitId;
            _kind = kind;
            _point = point;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.Write(_kind);
            stream.Write(_point);
            return stream;
        }
    }
}
