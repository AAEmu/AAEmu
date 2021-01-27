using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCharacterRenamedPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly string _oldName;
        private readonly string _newName;
        
        public SCCharacterRenamedPacket(uint characterId, string oldName, string newName) : base(SCOffsets.SCCharacterRenamedPacket, 5)
        {
            _characterId = characterId;
            _oldName = oldName;
            _newName = newName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_oldName); // TODO max length 128
            stream.Write(_newName); // TODO max length 128
            return stream;
        }
    }
}
