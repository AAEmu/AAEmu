using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPauseUserMusicPacket : GamePacket
    {
        private readonly uint _playerObjId;

        public SCPauseUserMusicPacket(uint playerObjId) : base(SCOffsets.SCPauseUserMusicPacket, 5)
        {
            _playerObjId = playerObjId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_playerObjId);
            return stream;
        }
    }
}
