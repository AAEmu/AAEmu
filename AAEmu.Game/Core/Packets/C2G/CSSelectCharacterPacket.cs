using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSelectCharacterPacket() : GamePacket(CSOffsets.CSSelectCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // SelectCharacterPacket body: "id" group { charId = 8-byte i64 } then "exit" bool.
        // Char ids fit in u32, so cast down.
        var characterId = (uint)stream.ReadUInt64();
        _ = stream.ReadBoolean(); // exit (return-to-character-select flag)

        if (Connection.Characters.TryGetValue(characterId, out var character))
        {
            // Force player into main_world when coming from character select
            character.Transform.InstanceId = WorldManager.DefaultInstanceId;
            // Despawn any old pets this character might have even before loading it
            character.Load();
            character.Connection = Connection;
            var houses = Connection.Houses.Values.Where(x => x.OwnerId == character.Id);
            // Remove old pets from all world instances
            foreach (var worldInstance in WorldManager.Instance.GetWorlds())
            {
                worldInstance.MateManager.RemoveAndDespawnAllActiveOwnedMates(character);
            }

            Connection.ActiveChar = character;
            if (Character.UsedCharacterObjIds.TryGetValue(character.Id, out var oldObjId))
            {
                Connection.ActiveChar.ObjId = oldObjId;
            }
            else
            {
                Connection.ActiveChar.ObjId = ObjectIdManager.Instance.GetNextId();
                Character.UsedCharacterObjIds.TryAdd(character.Id, character.ObjId);
            }
            // Add to server pool
            WorldManager.Instance.TryAddCharacter(character);

            var mySlave = Connection.ActiveChar.ParentWorld.SlaveManager.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
            if (mySlave != null)
            {
                Logger.Warn($"{Connection.ActiveChar.Name}: Abort the task of disabling vehicles");
                mySlave.CancelTokenSource.Cancel();
            }

            Connection.ActiveChar.Simulation = new Simulation(character);

            Connection.SendPacket(new SCCharacterStatePacket(character));
            Connection.SendPacket(new SCCharacterGamePointsPacket(character));
            Connection.ActiveChar.Inventory.Send();
            Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));

            Connection.ActiveChar.Quests.Send();
            Connection.ActiveChar.Quests.SendCompleted();

            Connection.ActiveChar.Actability.Send();
            Connection.ActiveChar.Mails.SendUnreadMailCount();
            Connection.ActiveChar.Appellations.Send();
            Connection.ActiveChar.Portals.Send();
            Connection.ActiveChar.Friends.Send();
            Connection.ActiveChar.Blocked.Send();

            foreach (var house in houses)
            {
                Connection.SendPacket(new SCMyHousePacket(house));
            }

            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                Connection.SendPacket(new SCConflictZoneStatePacket(conflict.ZoneGroupId, conflict.CurrentZoneState, conflict.NextStateTime));
            }

            FactionManager.Instance.SendFactions(Connection.ActiveChar);
            FactionManager.Instance.SendRelations(Connection.ActiveChar);
            ExpeditionManager.Instance.SendExpeditions(Connection.ActiveChar);

            if (Connection.ActiveChar.Expedition != null)
            {
                ExpeditionManager.SendExpeditionInfo(Connection.ActiveChar);
            }

            Connection.ActiveChar.SendOption(1);
            Connection.ActiveChar.SendOption(2);
            Connection.ActiveChar.SendOption(5);

            Connection.ActiveChar.Buffs.AddBuff((uint)BuffConstants.LoggedOn, Connection.ActiveChar);

            // 10.0.2.13: the character_buffs table (per-race/gender default login buffs) was removed.
            // (A v10 replacement would be character_idle_buffs — not yet loaded.)

            // Load persistent buffs from database
            character.Buffs.LoadActiveBuffs(character);
            character.CheckWantedThreshold();
            
            character.UpdateGearBonuses(null, null);
            character.RestoreSavedHpMp();

            character.Breath = character.LungCapacity;

            Connection.ActiveChar.OnZoneChange(0, Connection.ActiveChar.Transform.ZoneId);
        }
        else
        {
            // TODO: Character not found
            Logger.Error($"Character {characterId} not found in list of loaded characters of this account {Connection.AccountId}");
        }
    }
}
