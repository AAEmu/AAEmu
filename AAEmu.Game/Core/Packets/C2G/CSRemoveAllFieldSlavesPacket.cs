using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRemoveAllFieldSlavesPacket : GamePacket
    {
        public CSRemoveAllFieldSlavesPacket() : base(CSOffsets.CSRemoveAllFieldSlavesPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRemoveAllFieldSlavesPacket");
        }
    }
}
