using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionCheckExpeditionExpNextDayPacket : GamePacket
    {
        public CSFactionCheckExpeditionExpNextDayPacket() : base(CSOffsets.CSFactionCheckExpeditionExpNextDayPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFactionCheckExpeditionExpNextDayPacket");
        }
    }
}
