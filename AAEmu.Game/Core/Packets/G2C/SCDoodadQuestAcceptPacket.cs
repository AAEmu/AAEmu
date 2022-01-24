using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDoodadQuestAcceptPacket : GamePacket
    {
        private readonly uint _doodadObjId;
        private readonly uint _questContextId;

        public SCDoodadQuestAcceptPacket(uint doodadObjId, uint questContextId) : base(SCOffsets.SCDoodadQuestAcceptPacket, 1)
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
