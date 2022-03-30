using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSFactionSetExpeditionLevelUpPacket : GamePacket
    {
        public CSFactionSetExpeditionLevelUpPacket() : base(CSOffsets.CSFactionSetExpeditionLevelUpPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSFactionSetExpeditionLevelUpPacket");
        }
    }
}
