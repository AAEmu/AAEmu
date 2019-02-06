using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDespawnSlavePacket : GamePacket
    {
        public CSDespawnSlavePacket() : base(0x02d, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var slaveObjId = stream.ReadBc();

            _log.Debug("DespawnSlave, SlaveObjId: {0}", slaveObjId);
        }
    }
}
