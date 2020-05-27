using System.Collections.Generic;
using System.Drawing;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Items.Actions;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSStartDuelPacket : GamePacket
    {
        public CSStartDuelPacket() : base(0x051, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var challengerId = stream.ReadUInt32();  // ID of the one who challenged us to a duel
            var errorMessage = stream.ReadInt16();  // 0 - accepted the duel, 507 - refused

            _log.Warn("StartDuel, Id: {0}, ErrorMessage: {1}", challengerId, errorMessage);

            if (errorMessage != 0)
            {
                return;
            }

            var challengedObjId = DbLoggerCategory.Database.Connection.ActiveChar.ObjId;
            var challenger = WorldManager.Instance.GetCharacterById(challengerId);
            var challengerObjId = challenger.ObjId;

            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCDuelStartedPacket(challengerObjId, challengedObjId), true);
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCAreaChatBubblePacket(true, DbLoggerCategory.Database.Connection.ActiveChar.ObjId, 543), true);
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCDuelStartCountdownPacket(), true);

            var doodadFlag = new DoodadSpawner();
            const uint unitId = 5014u; // Combat Flag
            doodadFlag.Id = 0;
            doodadFlag.UnitId = unitId;
            doodadFlag.Position = DbLoggerCategory.Database.Connection.ActiveChar.Position.Clone();

            doodadFlag.Position.X = DbLoggerCategory.Database.Connection.ActiveChar.Position.X - (DbLoggerCategory.Database.Connection.ActiveChar.Position.X - challenger.Position.X) / 2;
            doodadFlag.Position.Y = DbLoggerCategory.Database.Connection.ActiveChar.Position.Y - (DbLoggerCategory.Database.Connection.ActiveChar.Position.Y - challenger.Position.Y) / 2;
            doodadFlag.Position.Z = DbLoggerCategory.Database.Connection.ActiveChar.Position.Z - (DbLoggerCategory.Database.Connection.ActiveChar.Position.Z - challenger.Position.Z) / 2;

            doodadFlag.Spawn(0);

            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCDuelStatePacket(challengerObjId, doodadFlag.Last.ObjId), true);
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCDuelStatePacket(challengedObjId, doodadFlag.Last.ObjId), true);
            DbLoggerCategory.Database.Connection.SendPacket(new SCDoodadPhaseChangedPacket(doodadFlag.Last));
            DbLoggerCategory.Database.Connection.SendPacket(new SCCombatEngagedPacket(challengerObjId));
            DbLoggerCategory.Database.Connection.ActiveChar.BroadcastPacket(new SCCombatEngagedPacket(challengedObjId), false);
        }
    }
}
