using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSQuestStartWithPacket : GamePacket
    {
        public CSQuestStartWithPacket() : base(CSOffsets.CSQuestStartWithPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // TODO find unk
            var unkId = stream.ReadUInt32();
            var unkId2 = stream.ReadUInt32();
            var unkId3 = stream.ReadUInt32();
            _log.Warn("QuestStartWith, UnkId: {0}, UnkId2: {1}, UnkId3: {2}", unkId, unkId2, unkId3);
        }
    }
}
