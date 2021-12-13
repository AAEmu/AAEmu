using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCompleteQuestContextPacket : GamePacket
    {
        private uint _questContextId;
        private uint _npcObjId;
        private uint _doodadObjId;
        private int _selected;

        public CSCompleteQuestContextPacket() : base(CSOffsets.CSCompleteQuestContextPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            _questContextId = stream.ReadUInt32(); // questId
            _npcObjId = stream.ReadBc();           // npcObjId
            _doodadObjId = stream.ReadBc();        // doodadObjId
            _selected = stream.ReadInt32();
        }
        public override async Task Execute()
        {
            if (
                _npcObjId > 0
                && Connection.ActiveChar.CurrentTarget != null
                && Connection.ActiveChar.CurrentTarget.ObjId != _npcObjId
               )
                return;
            Connection.ActiveChar.Quests.Complete(_questContextId, _selected);
        }
    }
}
