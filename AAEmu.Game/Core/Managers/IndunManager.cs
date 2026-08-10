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

        return dungeon.QueuePlayer(character);
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

        // Check level (or other stat) requirements
        if (!VerifyDungeonEnterRequirements(dungeonZone, character, team))
        {
            return false;
        }

        // 1 - Check if player is already a member of an active dungeon in this zone and re-enter it if they are
        var possibleTargetInstances = GetExistingDungeonsByZoneKey(targetZone.ZoneKey);
        foreach (var possibleTargetInstance in possibleTargetInstances)
        {
            // If queued for this dungeon, let them wait
            if (possibleTargetInstance.EnterRequests.Contains(character))
            {
                // Already queued, please wait
                character.SendErrorMessage(ErrorMessageType.TryLaterInstance); // probably not a good error for this
                return true;
            }
            // If they were already in there, add them again (probably after disconnect)
            if (possibleTargetInstance.World.HasCharacter(character.Id))
            {
                possibleTargetInstance.AddPlayer(character);
                return true;
            }
            // If they were already had access before, add them to queue (again)
            if (possibleTargetInstance.PlayersWithAccess.Contains(character.Id))
            {
                // Return queue if not full yet
                if (IsDungeonFull(possibleTargetInstance.World.GetCharacterCount(), possibleTargetInstance._indunZone.MaxPlayers))
                {
                    character.SendErrorMessage(ErrorMessageType.InstanceQuota); // Too many users are currently in the dungeon
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
        if (!CreateDungeonInstance(dungeonZone, character, channelId, out var dungeon))
        {
            Logger.Error($"Failed to create a new dungeon for player {character.Name} ({character.Id}), zone: {dungeonZone}, channel: {channelId}");
            return false;
        }

        return dungeon.QueuePlayer(character);
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

        // Create the actual dungeon
        dungeon = new Dungeon(dungeonZone, character, channelId, team);

        // Add creator to queue while dungeon is loading
        return dungeon.QueuePlayer(character);
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

            if (!dungeon.PlayersWithAccess.Contains(character.Id))
                continue;

            // Remove player's own access flag
            dungeon.PlayersWithAccess.Remove(character.Id);
            removedCount++;

            // If nobody has access anymore, remove the dungeon
            if (dungeon.PlayersWithAccess.Count == 0)
            {
                dungeon.DestroyDungeon();
            }
        }

        if (removedCount <= 0)
        {
            character.SendErrorMessage(ErrorMessageType.AlreadyUnboundInstance);
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

            // Can customize this to your start of the day to make this daily entry count
            var thresholdTime = DateTime.UtcNow.AddHours(-4);
            var lastEntriesQuery = entriesList.Where(x => x >= thresholdTime).ToList();

            if (lastEntriesQuery.Count >= indunZone.EnterCount)
            {
                // Max entries reached
                Logger.Warn($"Requesting instance too many daily entries ({lastEntriesQuery.Count} / {indunZone.EnterCount}), characterId: {characterId}, zoneGroupId: {zoneGroupId}");
                return false;
            }

            // Add current entry attempt if requested
            if (addAsNewEnty)
            {
                entriesList.Add(DateTime.UtcNow);
                Logger.Warn($"Added entry for player {characterId} in zone {zoneGroupId}, Count is now {entriesList.Count}");
            }

            return true; // true - you could also go to the dungeon, false - used up free attempts, used up extra attempts
        }
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
