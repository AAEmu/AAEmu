using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSOpenExpeditionImmigrationRequestPacket : GamePacket
    {
        public CSOpenExpeditionImmigrationRequestPacket() : base(CSOffsets.CSOpenExpeditionImmigrationRequestPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSOpenExpeditionImmigrationRequestPacket");
        }
    }
}
