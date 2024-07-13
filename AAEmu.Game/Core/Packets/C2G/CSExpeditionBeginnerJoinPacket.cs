using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSExpeditionBeginnerJoinPacket : GamePacket
    {
        public CSExpeditionBeginnerJoinPacket() : base(CSOffsets.CSExpeditionBeginnerJoinPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSExpeditionBeginnerJoinPacket");
        }
    }
}
