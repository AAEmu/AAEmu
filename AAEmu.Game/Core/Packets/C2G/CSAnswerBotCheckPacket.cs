using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAnswerBotCheckPacket : GamePacket
    {
        public CSAnswerBotCheckPacket() : base(CSOffsets.CSAnswerBotCheckPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSAnswerBotCheckPacket");
        }
    }
}
