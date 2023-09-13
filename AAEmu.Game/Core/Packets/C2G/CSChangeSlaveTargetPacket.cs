using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeSlaveTargetPacket : GamePacket
    {
        public CSChangeSlaveTargetPacket() : base(CSOffsets.CSChangeSlaveTargetPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var targetId = stream.ReadBc();
            var slaveId = stream.ReadBc();

            _log.Debug("ChangeSlaveTarget, Target: {0}, Slave: {1}", targetId, slaveId);
        }
    }
}
