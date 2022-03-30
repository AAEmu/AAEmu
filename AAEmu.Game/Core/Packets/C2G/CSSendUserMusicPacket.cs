using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSendUserMusicPacket : GamePacket
    {
        public CSSendUserMusicPacket() : base(CSOffsets.CSSendUserMusicPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSSendUserMusicPacket");
        }
    }
}
