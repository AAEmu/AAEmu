using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World.Zones;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSelectCharacterPacket : GamePacket
    {
        public CSSelectCharacterPacket() : base(0x025, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();
            var gm = stream.ReadBoolean();
            stream.ReadByte();

            if (DbLoggerCategory.Database.Connection.Characters.ContainsKey(characterId))
            {
                var character = DbLoggerCategory.Database.Connection.Characters[characterId];
                character.Load();
                character.Connection = DbLoggerCategory.Database.Connection;
                var houses = DbLoggerCategory.Database.Connection.Houses.Values.Where(x => x.OwnerId == character.Id);

                DbLoggerCategory.Database.Connection.ActiveChar = character;
                if (Models.Game.Char.Character._usedCharacterObjIds.TryGetValue(character.Id, out uint oldObjId))
                {
                    DbLoggerCategory.Database.Connection.ActiveChar.ObjId = oldObjId;
                }
                else
                {
                    DbLoggerCategory.Database.Connection.ActiveChar.ObjId = ObjectIdManager.Instance.GetNextId();
                    Models.Game.Char.Character._usedCharacterObjIds.TryAdd(character.Id, character.ObjId);
                }

                DbLoggerCategory.Database.Connection.ActiveChar.Simulation = new Simulation(character);

                DbLoggerCategory.Database.Connection.ActiveChar.Simulation = new Simulation(character);

                DbLoggerCategory.Database.Connection.SendPacket(new SCCharacterStatePacket(character));
                DbLoggerCategory.Database.Connection.SendPacket(new SCCharacterGamePointsPacket(character));
                DbLoggerCategory.Database.Connection.ActiveChar.Inventory.Send();
                DbLoggerCategory.Database.Connection.SendPacket(new SCActionSlotsPacket(DbLoggerCategory.Database.Connection.ActiveChar.Slots));

                DbLoggerCategory.Database.Connection.ActiveChar.Quests.Send();
                DbLoggerCategory.Database.Connection.ActiveChar.Quests.SendCompleted();

                DbLoggerCategory.Database.Connection.ActiveChar.Actability.Send();
                DbLoggerCategory.Database.Connection.ActiveChar.Appellations.Send();
                DbLoggerCategory.Database.Connection.ActiveChar.Portals.Send();
                DbLoggerCategory.Database.Connection.ActiveChar.Friends.Send();
                DbLoggerCategory.Database.Connection.ActiveChar.Blocked.Send();

                foreach (var house in houses)
                {
                    DbLoggerCategory.Database.Connection.SendPacket(new SCMyHousePacket(house));
                }

                foreach (var conflict in ZoneManager.Instance.GetConflicts())
                {
                    DbLoggerCategory.Database.Connection.SendPacket(new SCConflictZoneStatePacket(conflict.ZoneGroupId, conflict.CurrentZoneState, conflict.NextStateTime));
                }

                FactionManager.Instance.SendFactions(DbLoggerCategory.Database.Connection.ActiveChar);
                FactionManager.Instance.SendRelations(DbLoggerCategory.Database.Connection.ActiveChar);
                ExpeditionManager.Instance.SendExpeditions(DbLoggerCategory.Database.Connection.ActiveChar);

                if (DbLoggerCategory.Database.Connection.ActiveChar.Expedition != null)
                {
                    ExpeditionManager.Instance.SendExpeditionInfo(DbLoggerCategory.Database.Connection.ActiveChar);
                }

                DbLoggerCategory.Database.Connection.ActiveChar.SendOption(1);
                DbLoggerCategory.Database.Connection.ActiveChar.SendOption(2);
                DbLoggerCategory.Database.Connection.ActiveChar.SendOption(5);
            }
            else
            {
                // TODO ...
            }
        }
    }
}
