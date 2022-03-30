using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionLeavePacket : GamePacket
    {
        public CSExpeditionLeavePacket() : base(CSOffsets.CSExpeditionLeavePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionLeavePacket");
        }
    }
}
