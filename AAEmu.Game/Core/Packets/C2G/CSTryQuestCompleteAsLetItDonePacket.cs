using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSTryQuestCompleteAsLetItDonePacket : GamePacket
    {
        private uint id;
        private uint objId;
        private int selected;
        public CSTryQuestCompleteAsLetItDonePacket() : base(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            id = stream.ReadUInt32();
            objId = stream.ReadBc();
            selected = stream.ReadInt32();

            _log.Warn("TryQuestCompleteAsLetItDone, Id: {0}, ObjId: {1}, Selected: {2}", id, objId, selected);

            if (
                objId > 0
                && Connection.ActiveChar.CurrentTarget != null
                && Connection.ActiveChar.CurrentTarget.ObjId != objId
               )
                return;
            Connection.ActiveChar.Quests.Complete(id, selected);
        }
    }
}

