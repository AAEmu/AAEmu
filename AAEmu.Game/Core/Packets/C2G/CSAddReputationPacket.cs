using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAddReputationPacket : GamePacket
    {
        public CSAddReputationPacket() : base(CSOffsets.CSAddReputationPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSAddReputationPacket");
        }
    }
}
