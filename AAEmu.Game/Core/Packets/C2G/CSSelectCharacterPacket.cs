using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units.Route;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSelectCharacterPacket() : GamePacket(CSOffsets.CSSelectCharacterPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // = 8-byte i64 } then "exit" bool via vtbl+248. Char ids fit in u32, so cast down.
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
            // LastPacketActivityTime is set when the lobby Character is constructed (CSAesXorKey / LoadAccount).
            // Sitting on character-select for >2 minutes then makes the first OnActiveRegionTick after Select
            // treat the player as crashed/inactive and null out ActiveChar — Spawn/NotifyInGame then NRE and
            // the client drops. Reset on select so the inactivity window starts at enter, not at lobby load.
            character.LastPacketActivityTime = DateTime.UtcNow;
            character.ResetMirrorNpcStreaming();
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

            // Opens the in-world data load at context-view state 2 (SELECT_CHARACTER), ahead of the server-driven
            // ChangeState(3→7). First S2C packet after the char-select restrict/congestion checks in a live
            //
            // worldId here is the game-server (shard) id the client connected to via the login server list — the
            // login code calls the same value "WorldId" (GameController: "requesting an invalid WorldId {GsId}").
            // It must echo AppConfiguration.Id (this shard's GameServers[].Id), NOT the internal world-instance id:
            // sending Transform.WorldId (0 for main_world) leaves the client's current-world context unset, so
            // sends 0x02 because that official shard's id is 2; ours is 1.
            Connection.SendPacket(new SCShowCurrentWorldPacket(AppConfiguration.Instance.Id));

            Connection.SendPacket(new SCCharacterStatePacket(character));

            // SCWorldLevelInfo is NOT sent here. The client's world-level manager binds the packet's data to the
            // local player unit, which does not exist until Spawn() runs on NotifyInGame; sending it in the select
            // burst leaves that unit link (*(ClientPlayer+104)+8) null and the GetWorldLevel HUD provider
            // null-derefs on player-frame show. The reference sends it ~4s after NotifyInGame — see
            // CSNotifyInGamePacket.

            Connection.SendPacket(new SCCharacterGamePointsPacket(character));
            Connection.ActiveChar.Inventory.Send();
            // Reference emits prelim equipments here (after inventory contents) to initialize the client equipment
            // view before the player-frame renders.
            Connection.SendPacket(new SCCharacterPrelimEquipmentsPacket());
            Connection.SendPacket(new SCActionSlotsPacket(Connection.ActiveChar.Slots));

            Connection.ActiveChar.Quests.SendInitialState();

            Connection.ActiveChar.Actability.Send();
            Connection.ActiveChar.Mails.SendUnreadMailCount();
            Connection.ActiveChar.Appellations.Send();
            Connection.ActiveChar.Portals.Send();
            Connection.ActiveChar.Friends.Send();
            Connection.ActiveChar.Blocked.Send();

            // 10.0.2.13 world-entry init packets the client requires to populate its player-frame and side panels.
            // The reference server emits each (empty/default) in the select burst; absent, the client dereferences
            // the uninitialized structure when the matching UI window shows and crashes on load.
            Connection.SendPacket(new SCIncreasedFavoritePortalLimitPacket());
            Connection.SendPacket(new SCWorldRestrictOwnerChangePacket(false));
            Connection.SendPacket(new SCPlayerGameDataPacket(character));
            Connection.SendPacket(new SCInstanceVisitCountsPacket(
                IndunManager.Instance.GetVisitCountRecords(character.Id)));
            Connection.SendPacket(new SCBattleFieldRecordsPacket());
            Connection.SendPacket(new SCFavoriteCraftsPacket(character.FavoriteCrafts.GetWireCraftTypes()));
            Connection.SendPacket(new SCCharacterPrivacyStatusUpdatePacket(true, character.PrivacyStatus));
            Connection.SendPacket(new SCUpdateAdditionalSkillPointPacket());

            foreach (var houseBatch in houses.Chunk(SCHouseDataPacket.MaxEntries))
            {
                Connection.SendPacket(new SCHouseDataPacket(houseBatch));
            }

            foreach (var conflict in ZoneManager.Instance.GetConflicts())
            {
                Connection.SendPacket(new SCConflictZoneStatePacket(conflict.ZoneGroupId, conflict.CurrentZoneState, conflict.NextStateTime));
            }

            // 10.0.2.13: SCFactionList (opcode 0x08) was removed; system-faction descriptors are
            // client-side static data. Only the dynamic faction relations (SCFactionRelationList 0x0B)
            // are pushed at world entry.
            FactionManager.Instance.SendRelations(Connection.ActiveChar);
            Connection.SendPacket(new SCFactionPowerScorePacket());
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
