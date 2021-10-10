using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCPauseUserMusicPacket : GamePacket
    {
        private readonly uint _playerObjId;

        public SCPauseUserMusicPacket(uint playerObjId) : base(SCOffsets.SCPauseUserMusicPacket, 1)
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
