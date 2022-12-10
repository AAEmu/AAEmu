using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCQuestRewardedByMailPacket : GamePacket
    {
        private readonly uint[] _questList;

        public SCQuestRewardedByMailPacket(uint[] questList) : base(SCOffsets.SCQuestRewardedByMailPacket, 1)
        {
            _questList = questList;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)_questList.Length);
            foreach (var questId in _questList)
                stream.Write(questId);
            return stream;
        }
    }
}
