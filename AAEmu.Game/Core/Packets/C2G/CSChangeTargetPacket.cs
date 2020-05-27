using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSChangeTargetPacket : GamePacket
    {
        public CSChangeTargetPacket() : base(0x02c, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var targetId = stream.ReadBc();
            DbLoggerCategory.Database.Connection
                    .ActiveChar
                    .CurrentTarget = targetId > 0 ? WorldManager.Instance.GetUnit(targetId) : null;

            DbLoggerCategory.Database.Connection
                .ActiveChar
                .BroadcastPacket(
                    new SCTargetChangedPacket(DbLoggerCategory.Database.Connection.ActiveChar.ObjId,
                        DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget?.ObjId ?? 0), true);

            if (DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget == null)
                return;
            if (DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget is Npc npc)
                DbLoggerCategory.Database.Connection.ActiveChar.SendMessage("ObjId: {0}, TemplateId: {1}", targetId, npc.TemplateId);
            else if (DbLoggerCategory.Database.Connection.ActiveChar.CurrentTarget is Character character)
                DbLoggerCategory.Database.Connection.ActiveChar.SendMessage("ObjId: {0}, CharacterId: {1}", targetId, character.Id);
        }
    }
}
