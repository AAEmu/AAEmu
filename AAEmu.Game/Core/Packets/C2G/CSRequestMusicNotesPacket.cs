using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRequestMusicNotesPacket : GamePacket
    {
        public CSRequestMusicNotesPacket() : base(CSOffsets.CSRequestMusicNotesPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSRequestMusicNotesPacket");
        }
    }
}
