using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDeleteFriendPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly bool _success;
        private readonly string _friendName;
        private readonly short _errorMessage;

        public SCDeleteFriendPacket(uint characterId, bool success, string friendName, short errorMessage) : base(SCOffsets.SCDeleteFriendPacket, 5)
        {
            _characterId = characterId;
            _success = success;
            _friendName = friendName;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_success);
            stream.Write(_friendName);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
