using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveTrialAudiencePacket : GamePacket
    {
        public CSLeaveTrialAudiencePacket() : base(0x076, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Warn("LeaveTrialAudience");
        }
    }
}
