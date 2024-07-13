using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionSummonReplyPacket : GamePacket
    {
        public CSExpeditionSummonReplyPacket() : base(CSOffsets.CSExpeditionSummonReplyPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSExpeditionSummonReplyPacket");
        }
    }
}
