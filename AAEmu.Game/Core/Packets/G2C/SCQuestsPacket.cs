using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCQuestsPacket : GamePacket
    {
        private readonly Quest[] _quests;

        public SCQuestsPacket(Quest[] quests) : base(SCOffsets.SCQuestsPacket, 5)
        {
            _quests = quests;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_quests.Length); // count // TODO max 20
            foreach (var quest in _quests)
            {
                stream.Write(quest);
            }

            return stream;
        }
    }
}
