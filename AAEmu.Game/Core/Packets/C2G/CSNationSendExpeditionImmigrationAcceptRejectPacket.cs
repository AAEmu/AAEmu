using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSNationSendExpeditionImmigrationAcceptRejectPacket : GamePacket
    {
        public CSNationSendExpeditionImmigrationAcceptRejectPacket() : base(CSOffsets.CSNationSendExpeditionImmigrationAcceptRejectPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSNationSendExpeditionImmigrationAcceptRejectPacket");
        }
    }
}
