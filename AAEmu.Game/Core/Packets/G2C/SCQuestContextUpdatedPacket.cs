using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCQuestContextUpdatedPacket : GamePacket
    {
        private readonly Quest _quest;
        private readonly uint _componentId;

        public SCQuestContextUpdatedPacket(Quest quest, uint componentId) : base(SCOffsets.SCQuestContextUpdatedPacket, 5)
        {
            _quest = quest;
            _componentId = componentId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_quest);
            stream.Write(_componentId); // componentId
            stream.Write(0); // type
            stream.Write(0); // type
            stream.Write(0); // type
            stream.Write(0); // type
            return stream;
        }
    }
}
