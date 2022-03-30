using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSGetExpeditionMyRecruitmentsPacket : GamePacket
    {
        public CSGetExpeditionMyRecruitmentsPacket() : base(CSOffsets.CSGetExpeditionMyRecruitmentsPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSGetExpeditionMyRecruitmentsPacket");
        }
    }
}
