using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSQuestStartWithParamPacket : GamePacket
    {
        public CSQuestStartWithParamPacket() : base(CSOffsets.CSQuestStartWithParamPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSQuestStartWithParamPacket");
        }
    }
}
