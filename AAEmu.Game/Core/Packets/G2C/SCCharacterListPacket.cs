using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterListPacket : GamePacket
    {
        private readonly bool _last;
        private readonly ICharacter[] _characters;

        public SCCharacterListPacket(bool last, ICharacter[] characters) : base(SCOffsets.SCCharacterListPacket, 1)
        {
            _last = last;
            _characters = characters;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_last);
            stream.Write((byte) _characters.Length);
            foreach (var character in _characters)
                character.Write(stream);

            return stream;
        }
    }
}
