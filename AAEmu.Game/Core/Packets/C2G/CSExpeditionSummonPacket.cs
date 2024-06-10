using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionSummonPacket : GamePacket
    {
        public CSExpeditionSummonPacket() : base(CSOffsets.CSExpeditionSummonPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSExpeditionSummonPacket");
        }
    }
}
