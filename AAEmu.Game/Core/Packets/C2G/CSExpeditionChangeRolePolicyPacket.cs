using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionChangeRolePolicyPacket : GamePacket
    {
        public CSExpeditionChangeRolePolicyPacket() : base(CSOffsets.CSExpeditionChangeRolePolicyPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionChangeRolePolicyPacket");
        }
    }
}
