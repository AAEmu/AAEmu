using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLeaveExpeditionPacket : GamePacket
    {
        public CSLeaveExpeditionPacket() : base(CSOffsets.CSLeaveExpeditionPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("LeaveExpedition");
            ExpeditionManager.Instance.Leave(Connection);
        }
    }
}
