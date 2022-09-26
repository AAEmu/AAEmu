using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDetachFromDoodadPacket : GamePacket
    {
        private readonly uint _characterObjId;
        private readonly uint _characterId;
        private readonly uint _doodadObjId;

        public SCDetachFromDoodadPacket(uint characterObjId, uint characterId, uint doodadObjId) : base(SCOffsets.SCDetachFromDoodadPacket, 5)
        {
            _characterObjId = characterObjId;
            _characterId = characterId;
            _doodadObjId = doodadObjId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_characterObjId);
            stream.Write(_characterId);
            stream.WriteBc(_doodadObjId);
            return stream;
        }
    }
}
