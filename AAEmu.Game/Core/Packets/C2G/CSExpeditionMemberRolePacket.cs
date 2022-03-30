using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionMemberRolePacket : GamePacket
    {
        public CSExpeditionMemberRolePacket() : base(CSOffsets.CSExpeditionMemberRolePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionMemberRolePacket");
        }
    }
}
