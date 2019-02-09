using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSQuestStartWithPacket : GamePacket
    {
        public CSQuestStartWithPacket() : base(0x0d7, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO all unks types
            _log.Warn("QuestStartWith");
        }
    }
}
