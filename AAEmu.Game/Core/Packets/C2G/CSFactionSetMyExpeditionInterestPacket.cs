using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionSetMyExpeditionInterestPacket : GamePacket
    {
        public CSFactionSetMyExpeditionInterestPacket() : base(CSOffsets.CSFactionSetMyExpeditionInterestPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            Logger.Debug("CSFactionSetMyExpeditionInterestPacket");
        }
    }
}
