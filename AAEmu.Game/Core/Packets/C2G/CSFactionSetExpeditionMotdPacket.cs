using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionSetExpeditionMotdPacket : GamePacket
    {
        public CSFactionSetExpeditionMotdPacket() : base(CSOffsets.CSFactionSetExpeditionMotdPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSFactionSetExpeditionMotdPacket");
        }
    }
}
