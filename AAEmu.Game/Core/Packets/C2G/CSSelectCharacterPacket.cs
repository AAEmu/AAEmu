using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSelectCharacterPacket : GamePacket
    {
        public CSSelectCharacterPacket() : base(0x024, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var characterId = stream.ReadUInt32();
            var gm = stream.ReadBoolean();

            if (Connection.Characters.ContainsKey(characterId))
            {
                var character = Connection.Characters[characterId];
                character.Load();
                character.Connection = Connection;

                Connection.ActiveChar = character;
                Connection.ActiveChar.ObjId = ObjectIdManager.Instance.GetNextId();

                Connection.SendPacket(new SCCharacterStatePacket(character));
                Connection.SendPacket(new SCCharacterGamePointsPacket());
                Connection.ActiveChar.Inventory.Send();
                Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));
                
                Connection.ActiveChar.Quests.Send();
                Connection.ActiveChar.Quests.SendCompleted();

                Connection.SendPacket(new SCActabilityPacket()); // Умения (Крафт, Язык)
                Connection.ActiveChar.Appellations.Send();

                Connection.SendPacket(new SCFriendsPacket());

                foreach (var conflict in ZoneManager.Instance.GetConflicts())
                {
                    Connection.SendPacket(
                        new SCConflictZoneStatePacket(
                            conflict.ZoneGroupId,
                            ZoneConflictType.Trouble0,
                            conflict.NoKillMin[0] > 0 ? DateTime.Now.AddMinutes(conflict.NoKillMin[0]) : DateTime.MinValue
                        )
                    );
                }

                FactionManager.Instance.SendFactions(Connection.ActiveChar);
                FactionManager.Instance.SendRelations(Connection.ActiveChar);

                Connection.ActiveChar.SendOption("quest_notifier_list");
                Connection.ActiveChar.SendOption("roadmap_option");
                Connection.ActiveChar.SendOption("quest_context_state_values");
            }
            else
            {
                // TODO ...
            }
        }
    }
}
