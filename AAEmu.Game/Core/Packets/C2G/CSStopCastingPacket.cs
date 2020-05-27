using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStopCastingPacket : GamePacket
    {
        public CSStopCastingPacket() : base(0x054, 1)
        {
        }

        public override async void Read(PacketStream stream)
        {
            var tl = stream.ReadUInt16(); // sid
            stream.ReadUInt16(); // tl; pid
            var objId = stream.ReadBc();

            if (DbLoggerCategory.Database.Connection.ActiveChar.ObjId != objId || DbLoggerCategory.Database.Connection.ActiveChar.SkillTask == null ||
                DbLoggerCategory.Database.Connection.ActiveChar.SkillTask.Skill.TlId != tl)
                return;
            await DbLoggerCategory.Database.Connection.ActiveChar.SkillTask.Cancel();
            DbLoggerCategory.Database.Connection.ActiveChar.SkillTask.Skill.Stop(DbLoggerCategory.Database.Connection.ActiveChar);
        }
    }
}
