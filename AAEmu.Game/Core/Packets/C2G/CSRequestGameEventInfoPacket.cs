using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestGameEventInfoPacket : GamePacket
    {
        public CSRequestGameEventInfoPacket() : base(CSOffsets.CSRequestGameEventInfoPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            // empty
            _log.Warn("CSRequestGameEventInfoPacket");
        }
    }
}
