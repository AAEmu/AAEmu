using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionCreatePacket : GamePacket
    {
        public CSExpeditionCreatePacket() : base(CSOffsets.CSExpeditionCreatePacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSExpeditionCreatePacket");
        }
    }
}
