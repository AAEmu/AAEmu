using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitGmModeChangedPacket : GamePacket
    {
        private readonly uint _unitId;
        private readonly int _mode;
        private readonly byte _value;

        public SCUnitGmModeChangedPacket(uint unitId, int mode, byte value) : base(SCOffsets.SCUnitGmModeChangedPacket, 5)
        {
            _unitId = unitId;
            _mode = mode;
            _value = value;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitId);
            stream.Write(_mode);
            stream.Write(_value);
            return stream;
        }
    }
}
