using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAddBlockedUserPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly string _characterName;
        private readonly bool _success;
        private readonly short _errorMessage;
        
        public SCAddBlockedUserPacket(uint characterId, string characterName, bool success, short errorMessage) : base(SCOffsets.SCAddBlockedUserPacket, 5)
        {
            _characterId = characterId;
            _characterName = characterName;
            _success = success;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_characterName);
            stream.Write(_success);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
