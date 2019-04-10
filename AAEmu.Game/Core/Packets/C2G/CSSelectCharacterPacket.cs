﻿using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.World.Zones;

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

            if (Connection.Characters.ContainsKey(characterId))
            {
                var character = Connection.Characters[characterId];
                character.Load();
                character.Connection = Connection;
                var houses = Connection.Houses.Values.Where(x => x.OwnerId == character.Id);

                Connection.ActiveChar = character;
                Connection.ActiveChar.ObjId = ObjectIdManager.Instance.GetNextId();

                Connection.SendPacket(new SCCharacterStatePacket(character));
                Connection.SendPacket(new SCCharacterGamePointsPacket(character));
                Connection.ActiveChar.Inventory.Send();
                Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));

                Connection.ActiveChar.Quests.Send();
                Connection.ActiveChar.Quests.SendCompleted();

                Connection.ActiveChar.Actability.Send();
                Connection.ActiveChar.Appellations.Send();
                Connection.ActiveChar.Portals.Send();
                Connection.ActiveChar.Friends.Send();

                Connection.ActiveChar.Portals.Send();
                Connection.ActiveChar.Friends.Send();
                Connection.ActiveChar.Blocked.Send();

                foreach (var house in houses)
                    Connection.SendPacket(new SCMyHousePacket(house));

                foreach (var conflict in ZoneManager.Instance.GetConflicts())
                {
                    Connection.SendPacket(
                        new SCConflictZoneStatePacket(
                            conflict.ZoneGroupId,
                            ZoneConflictType.Trouble0,
                            conflict.NoKillMin[0] > 0
                                ? DateTime.Now.AddMinutes(conflict.NoKillMin[0])
                                : DateTime.MinValue
                        )
                    );
                }

                FactionManager.Instance.SendFactions(Connection.ActiveChar);
                FactionManager.Instance.SendRelations(Connection.ActiveChar);
                ExpeditionManager.Instance.SendExpeditions(Connection.ActiveChar);

                if (Connection.ActiveChar.Expedition != null)
                {
                    ExpeditionManager.Instance.SendExpeditionInfo(Connection.ActiveChar);
                }

                Connection.ActiveChar.SendOption(1);
                Connection.ActiveChar.SendOption(2);
                Connection.ActiveChar.SendOption(5);
            }
            else
            {
                // TODO ...
            }
        }
    }
}
