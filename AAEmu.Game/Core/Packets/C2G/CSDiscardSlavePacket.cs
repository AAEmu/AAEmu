using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDiscardSlavePacket : GamePacket
    {
        public CSDiscardSlavePacket() : base(CSOffsets.CSDiscardSlavePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tlId = stream.ReadUInt16();

            //_log.Debug("DiscardSlave, Tl: {0}", tlId);
            SlaveManager.Instance.UnbindSlave(Connection, tlId);
        }
    }
}
