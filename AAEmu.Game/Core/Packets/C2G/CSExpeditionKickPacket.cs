using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionKickPacket : GamePacket
    {
        public CSExpeditionKickPacket() : base(CSOffsets.CSExpeditionKickPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionKickPacket");
        }
    }
}
