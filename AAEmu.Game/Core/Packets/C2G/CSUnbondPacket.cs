using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUnbondPacket : GamePacket
    {
        public CSUnbondPacket() : base(CSOffsets.CSUnbondPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSUnbondPacket");
        }
    }
}
