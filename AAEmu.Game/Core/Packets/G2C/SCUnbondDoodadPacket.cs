using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCUnbondDoodadPacket : GamePacket
    {
        private readonly uint _characterObjId;
        private readonly uint _characterId;
        private readonly uint _doodadObjId;

        public SCUnbondDoodadPacket(uint characterObjId, uint characterId, uint doodadObjId) : base(SCOffsets.SCUnbondDoodadPacket, 5)
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
