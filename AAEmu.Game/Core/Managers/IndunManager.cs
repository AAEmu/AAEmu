using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using NLog;

namespace AAEmu.Game.Core.Managers;

// ReSharper disable once ClassNeverInstantiated.Global
public class IndunManager(ITickManager tickManager, IWorldManager worldManager, IZoneManager zoneManager, ITeamManager teamManager) : Singleton<IndunManager>, IIndunManager
{
    // ReSharper disable once InconsistentNaming
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, Dictionary<uint, List<DateTime>>> EntryHistory { get; } = []; // <ownerId, <zoneGroupId, entry time>> - dungeon attempts used
    /// <summary>Last time this character created a new copy of a zone group (restore_item_time gate).</summary>
    private Dictionary<uint, Dictionary<uint, DateTime>> CreateHistory { get; } = [];
    /// <summary>How many IVT_RESET tickets this character has bought today per zone group.</summary>
    private Dictionary<uint, Dictionary<uint, int>> ResetPurchaseCount { get; } = [];
    /// <summary>Extra daily enters from IVT_PERMIT tickets (lifetime of process / daily window).</summary>
    private Dictionary<uint, Dictionary<uint, int>> PermitBonusCount { get; } = [];
    // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
    private readonly object _lock = new();

    public void Initialize()
    {
        tickManager.OnTick.Subscribe(IndunInfoTick, TimeSpan.FromSeconds(30), true);
    }

    private void IndunInfoTick(TimeSpan delta)
    {
        var sysInstanceCount = 0;
        var dungeonInstanceCount = 0;
        var worldList = worldManager.GetWorlds().ToList();

        // Count dungeons
        foreach (var worldInstance in worldList)
        {
            if (worldInstance.DungeonInstance != null)
            {
                if (worldInstance.DungeonInstance.IsSystem)
                {
                    sysInstanceCount++;
                }
                else
                {
                    dungeonInstanceCount++;
                }
            }
        }

        if (sysInstanceCount + dungeonInstanceCount <= 0)
            return;
        
        Logger.Info($"Active Instances: {sysInstanceCount} system instance(s), {dungeonInstanceCount} dungeon(s)");

        if (dungeonInstanceCount <= 0)
            return;

        // enumerate dungeon info
        foreach (var worldInstance in worldList)
        {
            if (worldInstance.DungeonInstance != null)
            {
                Logger.Debug($"{worldInstance} - used by {worldInstance.GetCharacterCount()}/{worldInstance.DungeonInstance.PlayersWithAccess.Count} player(s): {worldInstance.ListPlayerNames(10)}");
                if (worldInstance.DungeonInstance.IsExpired)
                {
                    Logger.Warn($"Removing expired solo dungeon {worldInstance}");
                    worldInstance.DungeonInstance.DestroyDungeon();
                }
            }
        }

        InfoAttempt();
    }

    /// <summary>
    /// Checks if the dungeon for a given zone requires a channel select
    /// </summary>
    /// <param name="zoneId"></param>
    /// <returns></returns>
    public bool InstanceHasChannels(uint zoneId)
    {
        var dungeonZone = IndunGameData.Instance.GetDungeonZone(zoneManager.GetZoneById(zoneId).GroupId);
        return dungeonZone.SelectChannel;
    }

    /// <summary>
    /// Requests an instance for the character's team or for the player.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneId"></param>
    /// <param name="channelId"></param>
    /// <param name="dungeon"></param>
    /// <returns></returns>
    public bool RequestSystemInstance(Character character, uint zoneId, uint channelId, out Dungeon dungeon)
    {
        dungeon = null;
        if (character == null)
        {
            Logger.Info("[IndunManager] Player offline.");
            return false;
        }

        var zone = zoneManager.GetZoneById(zoneId);
        if (zone == null)
        {
            Logger.Warn($"Requesting non existing system instance for zone {zoneId}, character {character.Name}");
            return false;
        }

        foreach (var possibleDungeon in GetExistingDungeonsByZoneKey(zone.ZoneKey))
        {
            if (possibleDungeon.World.ChannelId == channelId)
            {
                dungeon = possibleDungeon;
                
                return dungeon.QueuePlayer(character);
            }
        }

        dungeon = CreateSystemInstance(character, zone.ZoneKey, channelId);
        if (dungeon == null)
        {
            Logger.Error($"Failed to create system instance for zoneId {zoneId}, channel: {channelId}, character {character.Name}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Requests an instance for the character's team or for the player.
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneId"></param>
    /// <param name="channelId"></param>
    /// <returns></returns>
    public bool RequestDungeonInstance(Character character, uint zoneId, uint channelId)
    {
        if (character == null)
        {
            Logger.Info($"Player requested a dungeon, but is now offline.");
            return false;
        }
        var team = teamManager.GetTeamByObjId(character.ObjId);
        var zone = zoneManager.GetZoneById(zoneId);

        // Check valid zone/dungeon
        var worldTemplate = worldManager.GetWorldTemplateByZoneKey(zone.ZoneKey);
        if (worldTemplate == null)
        {
            // Non-existing dungeon zone
            return false;
        }

        var targetZone = zoneManager.GetZoneById(zoneId);
        if (targetZone == null)
        {
            // Key does not match any zone
            return false;
        }
        
        var dungeonZone = IndunGameData.Instance.GetDungeonZone(targetZone.GroupId);
        if (dungeonZone == null)
        {
            // Not a dungeon
            return false;
        }

        var possibleTargetInstances = GetExistingDungeonsByZoneKey(targetZone.ZoneKey);

        // Rejoin a copy this player already paid for — do not apply the daily cap again.
        foreach (var possibleTargetInstance in possibleTargetInstances)
        {
            if (possibleTargetInstance.EnterRequests.Contains(character))
                return possibleTargetInstance.QueuePlayer(character);
            if (possibleTargetInstance.World.HasCharacter(character.Id))
            {
                possibleTargetInstance.AddPlayer(character);
                return true;
            }
            if (possibleTargetInstance.HasChargedEntry(character.Id))
            {
                if (IsDungeonFull(possibleTargetInstance.World.GetCharacterCount(), possibleTargetInstance._indunZone.MaxPlayers))
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota);
                    return false;
                }

                return possibleTargetInstance.QueuePlayer(character);
            }
        }

        // Check level (or other stat) requirements
        if (!VerifyDungeonEnterRequirements(dungeonZone, character, team))
        {
            return false;
        }

        // First visit: party access list (not yet charged) or a new copy.
        foreach (var possibleTargetInstance in possibleTargetInstances)
        {
            if (possibleTargetInstance.PlayersWithAccess.Contains(character.Id))
            {
                if (IsDungeonFull(possibleTargetInstance.World.GetCharacterCount(), possibleTargetInstance._indunZone.MaxPlayers))
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota);
                    return false;
                }

                return possibleTargetInstance.QueuePlayer(character);
            }
        }

        // 2 - First check Party required dungeons is available
        if (dungeonZone.PartyOnly) // Only if dungeon requires party
        {
            foreach (var possibleTargetInstance in possibleTargetInstances)
            {
                if (!possibleTargetInstance.PlayerInSameTeam(character))
                    continue;
                
                // Join your team's dungeon (if enough room)
                if (possibleTargetInstance.IsFull)
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota); // Too many users are currently in the dungeon
                    return false;
                }
                
                return possibleTargetInstance.QueuePlayer(character);
            }
        }

        // 3 - Check if non-party/raid leader is a member of the requested dungeon, if so, join their instance
        if (team != null)
        {
            // 3a - Create a list of players to check with party leader as first entry
            // The rest is the same order as the team order
            var checkPlayersList = new List<Character>();
            foreach (var teamMember in team.Members)
            {
                if (teamMember == null || teamMember.Character == null)
                    continue;
                if (teamMember.Character.Id == team.OwnerId)
                {
                    checkPlayersList.Insert(0, teamMember.Character);
                }
                else
                {
                    checkPlayersList.Add(teamMember.Character);
                }
            }

            // 3b - Enumerate the sorted team member list to check if we have a matching dungeon to enter
            foreach (var playerCharacter in checkPlayersList)
            {
                foreach (var possibleTargetInstance in possibleTargetInstances)
                {
                    if (!possibleTargetInstance.PlayersWithAccess.Contains(playerCharacter.Id))
                        continue;
                
                    // Join your team's dungeon (if enough room)
                    // TODO: not sure if we should toss a error here, or continue searching for others
                    if (possibleTargetInstance.IsFull)
                    {
                        character.SendErrorMessage(ErrorMessageType.InstanceQuota); // Too many users are currently in the dungeon
                        return false;
                    }

                    return possibleTargetInstance.QueuePlayer(character);
                }
            }
        }

        // 4 - If none of the above applies, actually create a new dungeon
        Logger.Info($"Creating a new dungeon for player {character.Name} ({character.Id}), zone: {dungeonZone}, channel: {channelId}");
        if (!CreateDungeonInstance(dungeonZone, character, channelId, out _))
        {
            Logger.Error($"Failed to create a new dungeon for player {character.Name} ({character.Id}), zone: {dungeonZone}, channel: {channelId}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a list of all currently active dungeons that have a given zone
    /// </summary>
    /// <param name="zoneKey">Required Zone Key for the dungeons</param>
    /// <returns></returns>
    private List<Dungeon> GetExistingDungeonsByZoneKey(uint zoneKey)
    {
        var res = new List<Dungeon>();
        foreach (var worldInstance in worldManager.GetWorlds())
        {
            if (worldInstance.DungeonInstance == null)
                continue;
            if (worldInstance.Template.ZoneKeys.Contains(zoneKey))
                res.Add(worldInstance.DungeonInstance);
        }
        return res;
    }

    /// <summary>
    /// Check if the player has the level, items and other requirements to be allowed to enter the given dungeon zone
    /// </summary>
    /// <param name="dungeonZone"></param>
    /// <param name="character"></param>
    /// <param name="team"></param>
    /// <returns></returns>
    private bool VerifyDungeonEnterRequirements(IndunZone dungeonZone, Character character, Team team)
    {
        // Check access count
        if (!CheckEntryAttemptCount(character.Id, dungeonZone.ZoneGroupId, dungeonZone, false))
        {
            
            character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
            return false;
        }

        // Check Level requirement
        if (character.Level < dungeonZone.LevelMin)
        {
            Logger.Warn($"Requesting instance level too low ({character.Level} < {dungeonZone.LevelMin}), characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }
        if (character.Level > dungeonZone.LevelMax)
        {
            Logger.Warn($"Requesting instance level too high ({character.Level} > {dungeonZone.LevelMax}), characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.InstanceLevel);
            return false;
        }
        
        // Check party status
        if (dungeonZone.PartyOnly && team == null)
        {
            Logger.Warn($"Requesting instance team required, characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.NeedParty);
            return false;
        }
        
        // 10.0.2.13: indun_zones.item_id removed; the item-requirement check was dead (ItemId always 0)

        return true;
    }

    /// <summary>
    /// Returns true when the dungeon already holds its maximum allowed number of players.
    /// Kept as a single decision point so the manager and <see cref="Dungeon.IsFull"/> stay in sync.
    /// </summary>
    private static bool IsDungeonFull(int characterCount, uint maxPlayers) => characterCount >= maxPlayers;

    /// <summary>
    /// Creates a new player created dungeon instance
    /// </summary>
    /// <param name="dungeonZone"></param>
    /// <param name="character"></param>
    /// <param name="channelId"></param>
    /// <param name="dungeon"></param>
    /// <returns></returns>
    /// <summary>
    /// Builds an instance copy for a matched group without putting anyone in it yet, and grants its
    /// members access so their later enter joins this copy instead of creating another one.
    /// </summary>
    /// <returns>The copy being built, or null when one could not be started.</returns>
    public Dungeon PrepareMatchInstance(uint zoneId, Character owner, IReadOnlyList<uint> memberCharacterIds)
    {
        if (owner == null || memberCharacterIds == null || memberCharacterIds.Count == 0)
            return null;

        var targetZone = zoneManager.GetZoneById(zoneId);
        if (targetZone == null)
            return null;

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(targetZone.GroupId);
        if (dungeonZone == null)
            return null;

        if (worldManager.GetWorlds().Length > AppConfiguration.Instance.World.MaxInstances)
        {
            Logger.Warn($"Preparing a match instance would exceed the allowed amount, zoneGroupId: {dungeonZone.ZoneGroupId}");
            return null;
        }

        var worldTemplate = worldManager.GetWorldTemplateByZoneKey(targetZone.ZoneKey);
        var templateName = worldTemplate?.Name;
        var warmWorld = !string.IsNullOrWhiteSpace(templateName)
            ? WorldIntegration.TryClaimWarmDungeonWorld?.Invoke(templateName, owner.Id)
            : null;

        var dungeon = warmWorld != null
            ? new Dungeon(dungeonZone, owner, 0, null, warmWorld)
            : new Dungeon(dungeonZone, owner, 0, null);

        foreach (var memberId in memberCharacterIds)
            dungeon.PlayersWithAccess.Add(memberId);

        Logger.Info(
            $"Preparing match instance zoneGroupId: {dungeonZone.ZoneGroupId}, world: {dungeon.World?.Id}, members: {memberCharacterIds.Count}, warm: {warmWorld != null}");
        return dungeon;
    }

    private bool CreateDungeonInstance(IndunZone dungeonZone, Character character, uint channelId, out Dungeon dungeon)
    {
        dungeon = null;

        // Check if we have capacity
        if (worldManager.GetWorlds().Length > AppConfiguration.Instance.World.MaxInstances)
        {
            Logger.Warn($"Requesting a new instance would exceeds the allowed ammount, characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");
            character.SendErrorMessage(ErrorMessageType.NoServerInstanceResource);
            return false;
        }

        var team = teamManager.GetTeamByObjId(character.ObjId);
        Logger.Info($"Requesting instance, characterId: {character.Id}, zoneGroupId: {dungeonZone.ZoneGroupId}");

        // Check requirements such as level, item, etc
        if (!VerifyDungeonEnterRequirements(dungeonZone, character, team))
        {
            return false;
        }

        if (!CanCreateAfterRestoreCooldown(character.Id, dungeonZone))
        {
            character.SendErrorMessage(ErrorMessageType.TryLaterInstance);
            return false;
        }

        // Prefer a warm ZoneHost copy when configured for this world template.
        var zoneKeys = zoneManager.GetZoneKeysInZoneGroupById(dungeonZone.ZoneGroupId);
        var worldTemplate = zoneKeys.Count > 0
            ? worldManager.GetWorldTemplateByZoneKey(zoneKeys[0])
            : null;
        var templateName = worldTemplate?.Name;
        var bindOwnerId = team?.Id ?? character.Id;
        var warmWorld = !string.IsNullOrWhiteSpace(templateName)
            ? WorldIntegration.TryClaimWarmDungeonWorld?.Invoke(templateName, bindOwnerId)
            : null;

        dungeon = warmWorld != null
            ? new Dungeon(dungeonZone, character, channelId, team, warmWorld)
            : new Dungeon(dungeonZone, character, channelId, team);

        // Add creator to queue while dungeon is loading
        if (!dungeon.QueuePlayer(character))
            return false;

        RecordCreateTime(character.Id, dungeonZone.ZoneGroupId);
        return true;
    }

    /// <summary>
    /// Creates and returns a system instance with a given channel
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zoneKey"></param>
    /// <param name="channelId"></param>
    /// <param name="overrideInstanceId"></param>
    /// <param name="fixedInstanceId"></param>
    /// <returns></returns>
    public Dungeon CreateSystemInstance(Character character, uint zoneKey, uint channelId, bool overrideInstanceId = false, uint fixedInstanceId = 0)
    {
        Logger.Info($"Requesting system instance, zoneKey: {zoneKey}, character: {character?.Name ?? "[SYSTEM]"}, channel: {channelId}, override InstanceId: {(overrideInstanceId ? fixedInstanceId.ToString() : "NO")}");

        var team = character != null ? teamManager.GetTeamByObjId(character.ObjId) : null;

        var dungeonZone = IndunGameData.Instance.GetDungeonZone(zoneManager.GetZoneByKey(zoneKey).GroupId);
        if (dungeonZone == null)
        {
            Logger.Error($"Requesting invalid system instance: , zoneKey: {zoneKey}, character: {character?.Name ?? "[SYSTEM]"}, channel: {channelId}, override InstanceId: {(overrideInstanceId ? fixedInstanceId.ToString() : "NO")}");
            return null;
        }
        
        // Check for duplicate system instances
        foreach (var worldInstance in worldManager.GetWorlds())
        {
            if (worldInstance.ChannelId == channelId &&
                worldInstance.DungeonInstance?.GetZoneGroupId == dungeonZone.ZoneGroupId)
            {
                // Check requirements such as level, item, etc
                if (character != null && VerifyDungeonEnterRequirements(dungeonZone, character, team))
                {
                    worldInstance.DungeonInstance.QueuePlayer(character);
                }
                return worldInstance.DungeonInstance;
            }
        }

        // Create new system instance
        var dungeon = new Dungeon(dungeonZone, character, channelId, team, overrideInstanceId, fixedInstanceId)
        {
            IsSystem = true
        };

        // Check if zones match
        if (dungeonZone.ZoneGroupId != zoneManager.GetZoneByKey(zoneKey)?.GroupId)
        {
            Logger.Info("[IndunManager] system dungeon request on different area.");
            character?.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
            return null;
        }

        // Check requirements such as level, item, etc
        if (character != null && VerifyDungeonEnterRequirements(dungeon._indunZone, character, team))
        {
            dungeon.QueuePlayer(character);
        }

        return dungeon;
    }

    /// <summary>
    /// Player requesting to remove dungeon with a given zone
    /// </summary>
    /// <param name="character"></param>
    /// <param name="zone"></param>
    /// <returns></returns>
    public bool RequestDeletion(Character character, Zone zone)
    {
        if (character == null)
        {
            return false;
        }
        if (zone == null)
        {
            character.SendErrorMessage(ErrorMessageType.AlreadyUnboundInstance);
            return false;
        }

        var removedCount = 0;
        var dungeons = GetExistingDungeonsByZoneKey(zone.ZoneKey);
        foreach (var dungeon in dungeons)
        {
            if (dungeon.IsSystem)
                continue;

            if (Dungeon.ShouldRefuseResetWhileInside(dungeon.World?.HasCharacter(character.Id) == true))
            {
                character.SendErrorMessage(ErrorMessageType.ProhibitedInInstance);
                return false;
            }

            if (!dungeon.PlayersWithAccess.Contains(character.Id))
                continue;

            // Remove player's own access flag
            dungeon.PlayersWithAccess.Remove(character.Id);
            removedCount++;

            // Portal reset (初期화) dismisses this bind so a fresh copy can be created
            // immediately — restore_item_time only gates creates while a prior create is still "held".
            ClearCreateCooldown(character.Id, dungeon.GetZoneGroupId);

            // If nobody has access anymore, remove the dungeon
            if (Dungeon.ShouldDestroyAfterLastAccessRemoved(dungeon.PlayersWithAccess.Count))
            {
                dungeon.DestroyDungeon();
            }
        }

        if (removedCount <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.AlreadyUnboundInstance);
        }
        else
        {
            character.SendErrorMessage(ErrorMessageType.DismissIndunSuccessed);
        }
        return true;
    }

    /// <summary>
    /// Player requesting to leave the dungeon/instance 
    /// </summary>
    /// <param name="character"></param>
    /// <returns></returns>
    public bool RequestLeaveInstance(Character character)
    {
        if (character == null)
            return false;
        
        // Remove from all possible different types of dungeons
        // System dungeons (mirage/library)
        foreach (var worldInstance in worldManager.GetWorlds().Where(w => w.HasCharacter(character.Id)))
        {
            
            character.Events.OnDungeonLeave(worldInstance, new OnDungeonLeaveArgs { Player = character });
            // dungeon.LeaveSysInstance(character); // Already called in the OnDungeonLeave event
            return true;
        }

        // No instance found that needs exiting
        return false;
    }

    public void DoIndunActions(uint startActionId, WorldInstance worldInstance)
    {
        while (true)
        {
            var action = IndunGameData.Instance.GetIndunActionById(startActionId);
            action.Execute(worldInstance);
            Logger.Warn($"DoIndunActions: world={worldInstance.Id}, action.Id={action.Id}, action.NextActionId={action.NextActionId}");
            if (action.NextActionId > 0)
            {
                startActionId = action.NextActionId;
                continue;
            }

            break;
        }
    }

    public bool CheckEntryAttemptCount(uint characterId, uint zoneGroupId, IndunZone indunZone, bool addAsNewEnty)
    {
        lock (_lock)
        {
            if (!EntryHistory.ContainsKey(characterId))
                EntryHistory.Add(characterId, []);

            var zoneAndEntries = EntryHistory.GetValueOrDefault(characterId);

            if (!zoneAndEntries.ContainsKey(zoneGroupId))
                zoneAndEntries.Add(zoneGroupId, []);

            var entriesList = zoneAndEntries.GetValueOrDefault(zoneGroupId);
            var now = ServerCalendar.UtcNow;
            var usedToday = IndunEntryRules.CountEntriesInDailyWindow(entriesList, now);
            var permitBonus = GetPermitBonusUnlocked(characterId, zoneGroupId);
            var permitted = IndunEntryRules.EffectivePermittedCount(indunZone.EnterCount, permitBonus);

            if (usedToday >= permitted)
            {
                Logger.Warn(
                    $"Requesting instance too many daily entries ({usedToday} / {permitted}), characterId: {characterId}, zoneGroupId: {zoneGroupId}");
                return false;
            }

            if (addAsNewEnty)
            {
                entriesList.Add(now);
                Logger.Warn($"Added entry for player {characterId} in zone {zoneGroupId}, Count is now {entriesList.Count}");
            }

            return true;
        }
    }

    /// <summary>
    /// Blocks a new copy when <see cref="IndunZone.RestoreItemTime"/> has not elapsed since the last create.
    /// </summary>
    private bool CanCreateAfterRestoreCooldown(uint characterId, IndunZone indunZone)
    {
        lock (_lock)
        {
            if (!CreateHistory.TryGetValue(characterId, out var byZone))
                return true;
            if (!byZone.TryGetValue(indunZone.ZoneGroupId, out var lastCreate))
                return true;

            var now = ServerCalendar.UtcNow;
            if (!IndunEntryRules.IsCreateOnCooldown(lastCreate, now, indunZone.RestoreItemTime))
                return true;

            Logger.Warn(
                $"Instance create on restore cooldown ({indunZone.RestoreItemTime}s), characterId: {characterId}, zoneGroupId: {indunZone.ZoneGroupId}");
            return false;
        }
    }

    private void RecordCreateTime(uint characterId, uint zoneGroupId)
    {
        lock (_lock)
        {
            if (!CreateHistory.ContainsKey(characterId))
                CreateHistory.Add(characterId, []);
            CreateHistory[characterId][zoneGroupId] = ServerCalendar.UtcNow;
        }
    }

    /// <summary>
    /// Portal reset (G / 초기화) unbinds the copy — clear <c>restore_item_time</c> so F can create again.
    /// </summary>
    private void ClearCreateCooldown(uint characterId, uint zoneGroupId)
    {
        lock (_lock)
        {
            if (!CreateHistory.TryGetValue(characterId, out var byZone))
                return;
            if (!byZone.Remove(zoneGroupId))
                return;
            Logger.Info(
                "Cleared instance create cooldown after reset, characterId: {0}, zoneGroupId: {1}",
                characterId, zoneGroupId);
        }
    }

    /// <summary>Per-dungeon visit rows for <see cref="AAEmu.Game.Core.Packets.G2C.SCInstanceVisitCountsPacket"/>.</summary>
    public List<InstanceVisitCountRecord> GetVisitCountRecords(uint characterId)
    {
        var now = ServerCalendar.UtcNow;
        var records = new List<InstanceVisitCountRecord>();
        lock (_lock)
        {
            EntryHistory.TryGetValue(characterId, out var zoneAndEntries);
            foreach (var zone in IndunGameData.Instance.GetAllDungeonZones())
            {
                if (zone.InstanceCatalogId == 0 && zone.EnterCount >= 1000)
                    continue;

                var used = 0;
                if (zoneAndEntries != null && zoneAndEntries.TryGetValue(zone.ZoneGroupId, out var entries))
                    used = IndunEntryRules.CountEntriesInDailyWindow(entries, now);

                var resetCount = GetResetPurchasesUnlocked(characterId, zone.ZoneGroupId);
                var permitBonus = GetPermitBonusUnlocked(characterId, zone.ZoneGroupId);
                records.Add(new InstanceVisitCountRecord(
                    ZoneGroupId: (int)zone.ZoneGroupId,
                    InstanceCatalogId: zone.InstanceCatalogId,
                    UsedCount: used,
                    ResetCount: resetCount,
                    PermittedCount: IndunEntryRules.EffectivePermittedCount(zone.EnterCount, permitBonus)));
            }
        }

        return records;
    }

    /// <summary>
    /// CS AddInstanceVisitCount — consume RESET/PERMIT ticket and push SCInstanceVisitCountChange.
    /// </summary>
    public bool TryAddInstanceVisitCount(Character character, sbyte visitType, int typeValue, short typeValue2)
    {
        if (character == null)
            return false;

        var zone = ResolveZoneForVisitTicket((uint)typeValue, typeValue2);
        if (zone == null)
        {
            Logger.Warn(
                "AddInstanceVisitCount: no IndunZone for type={0} type2={1} character={2}",
                typeValue, typeValue2, character.Id);
            character.SendErrorMessage(ErrorMessageType.InternalError);
            return false;
        }

        return visitType switch
        {
            IndunEntryRules.VisitTypeReset => TryBuyResetTicket(character, zone),
            IndunEntryRules.VisitTypePermit => TryBuyPermitTicket(character, zone),
            _ => FailUnknownVisitType(character, visitType)
        };
    }

    private static bool FailUnknownVisitType(Character character, sbyte visitType)
    {
        Logger.Warn("AddInstanceVisitCount: unknown visitType={0} character={1}", visitType, character.Id);
        character.SendErrorMessage(ErrorMessageType.InternalError);
        return false;
    }

    private IndunZone ResolveZoneForVisitTicket(uint catalogOrZero, short typeValue2)
    {
        if (catalogOrZero != 0)
            return IndunGameData.Instance.GetDungeonZoneByCatalogId(catalogOrZero);

        // Client secondary key when type dword is 0: zone_group_id as u16.
        if (typeValue2 > 0)
            return IndunGameData.Instance.GetDungeonZone((uint)typeValue2);

        return null;
    }

    private bool TryBuyResetTicket(Character character, IndunZone zone)
    {
        if (zone.ResetItemId == 0)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        int resetCount;
        int cost;
        lock (_lock)
        {
            resetCount = GetResetPurchasesUnlocked(character.Id, zone.ZoneGroupId);
            if (!IndunEntryRules.CanBuyReset(resetCount, zone.ResetLimit))
            {
                character.SendErrorMessage(ErrorMessageType.InstanceVisitLimit);
                return false;
            }

            cost = IndunEntryRules.ResetTicketCost(resetCount, zone.ResetItemIncreaseScale);
        }

        var consumed = character.Inventory.Bag.ConsumeItem(
            Models.Game.Items.Actions.ItemTaskType.ConsumeIndunTicket,
            zone.ResetItemId,
            cost,
            null);
        if (consumed < cost)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        InstanceVisitCountRecord row;
        lock (_lock)
        {
            if (!ResetPurchaseCount.ContainsKey(character.Id))
                ResetPurchaseCount[character.Id] = [];
            ResetPurchaseCount[character.Id][zone.ZoneGroupId] = resetCount + 1;

            // RESET clears today's used entries for this zone group (visit count refresh).
            if (EntryHistory.TryGetValue(character.Id, out var byZone) &&
                byZone.TryGetValue(zone.ZoneGroupId, out var entries))
            {
                var dayStart = IndunEntryRules.DailyWindowStartUtc(ServerCalendar.UtcNow);
                entries.RemoveAll(t =>
                {
                    var utc = t.Kind switch
                    {
                        DateTimeKind.Utc => t,
                        DateTimeKind.Local => t.ToUniversalTime(),
                        _ => DateTime.SpecifyKind(t, DateTimeKind.Utc)
                    };
                    return utc >= dayStart;
                });
            }

            row = BuildVisitRowUnlocked(character.Id, zone, ServerCalendar.UtcNow);
        }

        character.SendPacket(new Core.Packets.G2C.SCInstanceVisitCountChangePacket(row));
        Logger.Info(
            "AddInstanceVisitCount RESET character={0} zoneGroup={1} cost={2} resetCount={3}",
            character.Id, zone.ZoneGroupId, cost, row.ResetCount);
        return true;
    }

    private bool TryBuyPermitTicket(Character character, IndunZone zone)
    {
        if (zone.PermitEnterCountItemId == 0)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        var consumed = character.Inventory.Bag.ConsumeItem(
            Models.Game.Items.Actions.ItemTaskType.ConsumeIndunTicket,
            zone.PermitEnterCountItemId,
            1,
            null);
        if (consumed < 1)
        {
            character.SendErrorMessage(ErrorMessageType.NotEnoughItem);
            return false;
        }

        InstanceVisitCountRecord row;
        lock (_lock)
        {
            if (!PermitBonusCount.ContainsKey(character.Id))
                PermitBonusCount[character.Id] = [];
            PermitBonusCount[character.Id].TryGetValue(zone.ZoneGroupId, out var bonus);
            PermitBonusCount[character.Id][zone.ZoneGroupId] = bonus + 1;
            row = BuildVisitRowUnlocked(character.Id, zone, ServerCalendar.UtcNow);
        }

        character.SendPacket(new Core.Packets.G2C.SCInstanceVisitCountChangePacket(row));
        Logger.Info(
            "AddInstanceVisitCount PERMIT character={0} zoneGroup={1} permitted={2}",
            character.Id, zone.ZoneGroupId, row.PermittedCount);
        return true;
    }

    private InstanceVisitCountRecord BuildVisitRowUnlocked(uint characterId, IndunZone zone, DateTime now)
    {
        var used = 0;
        if (EntryHistory.TryGetValue(characterId, out var zoneAndEntries) &&
            zoneAndEntries.TryGetValue(zone.ZoneGroupId, out var entries))
            used = IndunEntryRules.CountEntriesInDailyWindow(entries, now);

        return new InstanceVisitCountRecord(
            ZoneGroupId: (int)zone.ZoneGroupId,
            InstanceCatalogId: zone.InstanceCatalogId,
            UsedCount: used,
            ResetCount: GetResetPurchasesUnlocked(characterId, zone.ZoneGroupId),
            PermittedCount: IndunEntryRules.EffectivePermittedCount(
                zone.EnterCount, GetPermitBonusUnlocked(characterId, zone.ZoneGroupId)));
    }

    private int GetResetPurchasesUnlocked(uint characterId, uint zoneGroupId)
    {
        if (!ResetPurchaseCount.TryGetValue(characterId, out var byZone))
            return 0;
        return byZone.GetValueOrDefault(zoneGroupId);
    }

    private int GetPermitBonusUnlocked(uint characterId, uint zoneGroupId)
    {
        if (!PermitBonusCount.TryGetValue(characterId, out var byZone))
            return 0;
        return byZone.GetValueOrDefault(zoneGroupId);
    }

    private void InfoAttempt()
    {
        lock (_lock)
        {
            if (EntryHistory is { Count: > 0 })
            {
                foreach (var (characterId, zoneAndEntries) in EntryHistory)
                {
                    foreach (var (zoneGroupId, entriesList) in zoneAndEntries)
                    {
                        Logger.Debug($"For player={characterId} ({worldManager.GetCharacterById(characterId)?.Name}): {entriesList.Count} entries into dungeon zone group {zoneGroupId} ({zoneManager.GetZoneGroupById(zoneGroupId)?.Name})");
                    }
                }
            }
        }
    }
}
