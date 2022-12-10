using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterDeletedPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly string _characterName;

        public SCCharacterDeletedPacket(uint characterId, string characterName) : base(SCOffsets.SCCharacterDeletedPacket, 1)
        {
            _characterId = characterId;
            _characterName = characterName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_characterName);
            return stream;
        }
    }
}
