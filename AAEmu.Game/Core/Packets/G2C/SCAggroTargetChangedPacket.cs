using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAggroTargetChangedPacket : GamePacket
    {
        private readonly uint _npcId;
        private readonly uint _targetId;

        public SCAggroTargetChangedPacket(uint npcId, uint targetId) : base(SCOffsets.SCAggroTargetChanged, 1)
        {
            _npcId = npcId;
            _targetId = targetId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_npcId);
            stream.WriteBc(_targetId);

            return stream;
        }
    }
}
