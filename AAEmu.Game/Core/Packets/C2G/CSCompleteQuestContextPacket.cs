using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSCompleteQuestContextPacket : GamePacket
    {
        public CSCompleteQuestContextPacket() : base(0x0d6, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var questId = stream.ReadUInt32();
            var objId = stream.ReadBc();
            var bc = stream.ReadBc();
            var selected = stream.ReadInt32();

            if (objId > 0 &&
                DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget != null &&
                DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget.ObjId != objId)
                return;
            DbLoggerCategory.Database.Connection.ActiveChar.Quests.Complete(questId, selected);
        }
    }
}
