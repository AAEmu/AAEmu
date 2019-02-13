using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCExpertLimitModifiedPacket : GamePacket
    {
        private readonly bool _isUpgrade;
        private readonly uint _id;
        private readonly byte _step;

        public SCExpertLimitModifiedPacket(bool isUpgrade, uint id, byte step) : base(0x1be, 1) // TODO 1.0 opcode: 0x1b6
        {
            _isUpgrade = isUpgrade;
            _id = id;
            _step = step;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_isUpgrade);
            stream.Write(_id);
            stream.Write(_step);
            return stream;
        }
    }
}
