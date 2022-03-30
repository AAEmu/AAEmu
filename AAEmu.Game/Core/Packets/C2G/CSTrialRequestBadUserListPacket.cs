using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTrialRequestBadUserListPacket : GamePacket
    {
        public CSTrialRequestBadUserListPacket() : base(CSOffsets.CSTrialRequestBadUserListPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSTrialRequestBadUserListPacket");
        }
    }
}
