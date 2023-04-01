using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCQuestContextUpdatedPacket : GamePacket
    {
        private readonly Quest _quest;
        private readonly uint _componentId;

        public SCQuestContextUpdatedPacket(Quest quest, uint componentId) : base(SCOffsets.SCQuestContextUpdatedPacket, 1)
        {
            _quest = quest;
            _componentId = componentId;
        }

        public override PacketStream Write(PacketStream stream)
        {
            _log.Debug($"[Quest] SCQuestContextUpdatedPacket: quest {_quest.TemplateId}, componentId {_componentId}, EarlyCompletion {_quest.EarlyCompletion}, ExtraCompletion {_quest.ExtraCompletion}, Objectives0 {_quest.Objectives[0]}, Objectives1 {_quest.Objectives[1]}, Objectives2 {_quest.Objectives[2]}, Objectives3 {_quest.Objectives[3]}, Objectives4 {_quest.Objectives[4]}");

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
