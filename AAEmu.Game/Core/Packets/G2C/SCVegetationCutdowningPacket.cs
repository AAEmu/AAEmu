using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCVegetationCutdowningPacket : GamePacket
    {
        private readonly uint _unitObjId;
        private readonly uint _doodadObjId;

        public SCVegetationCutdowningPacket(uint unitObjId, uint doodadObjId) : base(SCOffsets.SCVegetationCutdowningPacket, 1)
        {
            _unitObjId = unitObjId;
            _doodadObjId = doodadObjId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_unitObjId);
            stream.WriteBc(_doodadObjId);
            return stream;
        }
    }
}
