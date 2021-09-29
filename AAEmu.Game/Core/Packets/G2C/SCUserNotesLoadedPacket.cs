using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUserNotesLoadedPacket : GamePacket
    {
        private readonly uint _songId ;
        private readonly SongData _song;

        public SCUserNotesLoadedPacket(uint songId) : base(SCOffsets.SCUserNoteLoadedPacket, 1)
        {
            _songId = songId;
            _song = MusicManager.Instance.GetSongById(_songId);
        }

        public override PacketStream Write(PacketStream stream)
        {
            if (_song != null)
            {
                stream.Write(_song.Id);
                stream.Write((byte)1); // Observed as 1
                stream.Write(_song.Song.Length);
                stream.Write(_song.Title);
                stream.Write(_song.Song+"\0");
            }
            return stream;
        }
    }
}
