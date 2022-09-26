using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadAcceptQuestPacket : GamePacket
    {
        private readonly uint _doodadObjId;
        private readonly uint _questContextId;

        public SCDoodadAcceptQuestPacket(uint doodadObjId, uint questContextId) : base(SCOffsets.SCDoodadAcceptQuestPacket, 5)
        {
            _doodadObjId = doodadObjId;
            _questContextId = questContextId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.WriteBc(_doodadObjId);
            stream.Write(_questContextId);

            return stream;
        }
    }
}
