using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionRecruitmentDeletePacket : GamePacket
    {
        public CSExpeditionRecruitmentDeletePacket() : base(CSOffsets.CSExpeditionRecruitmentDeletePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            // empty body
            Logger.Debug("CSExpeditionRecruitmentDeletePacket");
            ExpeditionManager.Instance.RemoveRecruitment(Connection);
        }
    }
}
