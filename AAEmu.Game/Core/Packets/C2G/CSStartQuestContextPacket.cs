using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartQuestContextPacket : GamePacket
    {
        public CSStartQuestContextPacket() : base(0x0d5, 1) //TODO 1.0 opcode: 0x0d1
        {
        }

        public override void Read(PacketStream stream)
        {
            var questId = stream.ReadUInt32();
            var objId = stream.ReadBc();
            var objId2 = stream.ReadBc();
            var type = stream.ReadUInt32();

            if (objId > 0 &&
                DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget != null &&
                DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget.ObjId != objId)
                return;
            DbLoggerCategory.Database.Connection.ActiveChar.Quests.Add(questId);
        }
    }
}
