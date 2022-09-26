using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnitIdleStatusPacket : GamePacket
    {
        private readonly uint _id;
        private readonly bool _status;

        public SCUnitIdleStatusPacket(uint id, bool status) : base(SCOffsets.SCUnitIdleStatusPacket, 5)
        {
            _id = id;
            _status = status;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);
            stream.Write(_status);
            return stream;
        }
    }
}
