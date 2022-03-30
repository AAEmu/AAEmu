using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSPauseUserMusicPacket : GamePacket
    {
        public CSPauseUserMusicPacket() : base(CSOffsets.CSPauseUserMusicPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSPauseUserMusicPacket");
        }
    }
}
