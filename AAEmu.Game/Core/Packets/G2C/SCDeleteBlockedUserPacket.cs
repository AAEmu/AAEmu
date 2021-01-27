using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDeleteBlockedUserPacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly bool _success;
        private readonly string _blockedName;
        private readonly short _errorMessage;
        
        public SCDeleteBlockedUserPacket(uint characterId, bool success, string blockedName, short errorMessage) : base(SCOffsets.SCDeleteBlockedUserPacket, 5)
        {
            _characterId = characterId;
            _success = success;
            _blockedName = blockedName;
            _errorMessage = errorMessage;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_success);
            stream.Write(_blockedName);
            stream.Write(_errorMessage);
            return stream;
        }
    }
}
