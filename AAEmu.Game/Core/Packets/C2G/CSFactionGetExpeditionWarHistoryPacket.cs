using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionGetExpeditionWarHistoryPacket : GamePacket
    {
        public CSFactionGetExpeditionWarHistoryPacket() : base(CSOffsets.CSFactionGetExpeditionWarHistoryPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFactionGetExpeditionWarHistoryPacket");
        }
    }
}
