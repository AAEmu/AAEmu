using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCSendUserMusicPacket : GamePacket
    {
        private readonly uint _playerObjId;
        private readonly string _author; // not sure yet it this is the actual author or the person playing the music
        private readonly byte[] _midiData;

        public SCSendUserMusicPacket(uint playerObjId, string author, byte[] midiData) : base(SCOffsets.SCSendUserMusicPacket, 5)
        {
            _playerObjId = playerObjId;
            _author = author;
            _midiData = midiData;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((uint)_midiData.Length); // total size maybe if multiple blocks ?
            stream.WriteBc(_playerObjId);
            stream.Write(_author);
            stream.Write(_midiData, true);
            return stream;
        }
    }
}
