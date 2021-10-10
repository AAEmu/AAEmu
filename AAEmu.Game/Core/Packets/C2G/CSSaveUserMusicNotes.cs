using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSaveUserMusicNotesPacket : GamePacket
    {
        public CSSaveUserMusicNotesPacket() : base(CSOffsets.CSSaveUserMusicNotesPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var songSize = stream.ReadUInt32(); // this is the size without the trailing null terminator 0x00
            var itemId = stream.ReadUInt64(); // itemId of source item (music sheet)
            var value2 = stream.ReadUInt16(); // music rank, random value ?
            var title = stream.ReadString();
            var song = stream.ReadString();
            //var value3 = stream.ReadUInt64(); // 3.x+ random ? 
            //var value4 = stream.ReadByte(); // 3.x status ? (observed 0)

            MusicManager.Instance.UploadSong(Connection.ActiveChar.Id, title, song, itemId);
            _log.Debug("Uploading song, title: {0}, songsize: {1}, value2:{2}", title, songSize, value2);
            _log.Trace("Song data: {0}", song);
        }
    }
}
