using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCompletedQuestsPacket : GamePacket
    {
        private readonly CompletedQuest[] _quests;

        public SCCompletedQuestsPacket(CompletedQuest[] quests) : base(SCOffsets.SCCompletedQuestsPacket, 1)
        {
            _quests = quests;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_quests.Length); // TODO max 200
            foreach (var quest in _quests)
            {
                var body = new byte[8];
                quest.Body.CopyTo(body, 0);

                stream.Write(quest.Id); // idx
                stream.Write(body); // body
            }
            return stream;
        }
    }
}
