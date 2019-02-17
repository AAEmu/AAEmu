using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSDiscardSlavePacket : GamePacket
    {
        public CSDiscardSlavePacket() : base(0x032, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16();
            
            _log.Debug("DiscardSlave, Tl: {0}", tl);
        }
    }
}
