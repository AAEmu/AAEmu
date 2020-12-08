using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCompleteQuestContextPacket : GamePacket
    {
        public CSCompleteQuestContextPacket() : base(CSOffsets.CSCompleteQuestContextPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var questId = stream.ReadUInt32();
            var objId = stream.ReadBc();
            var bc = stream.ReadBc();
            var selected = stream.ReadInt32();

            if (objId > 0 &&
                Connection.ActiveChar.CurrentTarget != null &&
                Connection.ActiveChar.CurrentTarget.ObjId != objId)
                return;
            Connection.ActiveChar.Quests.Complete(questId, selected);
        }
    }
}
