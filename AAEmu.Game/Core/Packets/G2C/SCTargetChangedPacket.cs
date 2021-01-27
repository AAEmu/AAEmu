using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTargetChangedPacket : GamePacket
    {
        private readonly uint _id;
        private readonly uint _targetId;

        public SCTargetChangedPacket(uint id, uint targetId) : base(SCOffsets.SCTargetChangedPacket, 5)
        {
            _id = id;
            _targetId = targetId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_id);       // unit
            stream.WriteBc(_targetId); // target
            return stream;
        }
    }
}
