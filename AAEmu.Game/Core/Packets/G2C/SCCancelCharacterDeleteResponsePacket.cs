using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCancelCharacterDeleteResponsePacket : GamePacket
    {
        private readonly uint _characterId;
        private readonly byte _deleteStatus;

        public SCCancelCharacterDeleteResponsePacket(uint characterId, byte deleteStatus) : base(SCOffsets.SCCancelCharacterDeleteResponsePacket, 5)
        {
            _characterId = characterId;
            _deleteStatus = deleteStatus;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_characterId);
            stream.Write(_deleteStatus);
            return stream;
        }
    }
}
