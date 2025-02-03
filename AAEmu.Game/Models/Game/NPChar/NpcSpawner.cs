using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;
using AAEmu.Game.Utils;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawner : Spawner<Npc>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private int _scheduledCount;
    private int _spawnCount;
    private bool IsSpawnScheduled;
    //private bool IsNotFoundInScheduler;
    private readonly object _spawnLock = new(); // Lock for thread safety

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    [DefaultValue(1f)]
    public uint Count { get; set; } = 1;

    public List<uint> NpcSpawnerIds { get; set; } = [];
    public NpcSpawnerTemplate Template { get; set; }
    public List<NpcSpawnerNpc> SpawnableNpcs { get; set; } = []; // List of NPCs that can be spawned
    public ConcurrentDictionary<uint, List<Npc>> SpawnedNpcs { get; set; } = new(); // <SpawnerId, List of spawned NPCs>
    private DateTime _lastSpawnTime = DateTime.MinValue;
    private readonly Dictionary<int, SpawnerPlayerCountCache> _playerCountCache = new();
    private readonly Dictionary<int, SpawnerPlayerInRadiusCache> _playerInRadiusCache = new();

    public NpcSpawner()
    {
        IsSpawnScheduled = false;
    }

    /// <summary>
    /// Initializes the list of SpawnableNpcs based on Template.Npcs.
    /// </summary>
    internal void InitializeSpawnableNpcs(NpcSpawnerTemplate template)
    {
        if (template?.Npcs == null)
        {
            Logger.Warn("Template or template.Npcs is null. SpawnableNpcs will not be initialized.");
            return;
        }

        SpawnableNpcs = [.. template.Npcs];
    }

    /// <summary>
    /// Updates the state of the spawner.
    /// </summary>
    public void Update()
    {
        if (CanDespawn())
        {
            if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcs))
            {
                DoDespawn(npcs);
                return;
            }
        }
        if (!CanSpawn())
            return;

        if (_lastSpawnTime != DateTime.MinValue && (DateTime.UtcNow - _lastSpawnTime).TotalSeconds < Template.SpawnDelayMin)
            return;

        DoSpawn();
    }

    /// <summary>
    /// Checks if NPCs can be spawned.
    /// </summary>
    private bool CanSpawn()
    {
        if (Template == null)
        {
            Logger.Warn("Template is null. Cannot determine if NPC can be spawned.");
            return false;
        }

        // Checks if there is a NPC that is a corpse so it doesn`t respawn immediatly after being killed
        if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcs))
        {
            if (IsCorpse(npcs))
            {
                return false;
            }
        }

        //if (Template.NpcSpawnerCategoryId != NpcSpawnerCategory.Autocreated)
        {
            // Checks if this spawner is suitable based on the number of mobs in the spawn and the number of nearby players
            var spawnerId = GetOptimalSpawnerForPlayers();
            if (spawnerId != 0 && spawnerId is not null && SpawnerId != spawnerId)
                return false;
        }

        // Checks if the spawner is in an active state
        if (!Template.ActivationState)
            return false;

        // Checks if spawning is allowed by the schedule
        if (!IsSpawningScheduleEnabled())
            return false;

        // Checks if a player is within the spawn radius
        if (!IsPlayerInSpawnRadius())
            return false;

        if (Template.NpcSpawnerCategoryId != NpcSpawnerCategory.Autocreated)
        {
            // Checks if a player is within the spawn radius
            if (AreOtherNpcsInSpawnZone())
                return false;
        }

        // Checks if SuspendSpawnCount is exceeded
        if (Template.SuspendSpawnCount > 0 && _spawnCount >= Template.SuspendSpawnCount)
            return false;

        // Checks if the maximum number of NPCs has been reached
        if (_spawnCount >= Template.MaxPopulation)
            return false;

        // Checks if the minimum number of NPCs has been reached
        if (_spawnCount > Template.MinPopulation)
            return false;

        return true;
    }

    /// <summary>
    /// Checks if NPCs can be despawned.
    /// </summary>
    private bool CanDespawn()
    {
        if (IsDespawningScheduleEnabled(SpawnerId))
            return true;

        return !IsPlayerInSpawnRadius();
    }

    /// <summary>
    /// Checks if there is a NPC that is a corpse
    /// </summary>
    private bool IsCorpse(List<Npc> npcs)
    {
        return npcs.Any(npc => npc.IsDead);
    }

    /// <summary>
    /// Chooses an NPC to spawn based on SpawnableNpcs.
    /// </summary>
    private Npc ChooseNpcToSpawn()
    {
        if (SpawnableNpcs == null || SpawnableNpcs.Count == 0)
        {
            Logger.Warn("No spawnable NPCs available.");
            return null;
        }

        var totalWeight = SpawnableNpcs.Sum(n => n.Weight);
        var randomValue = Rand.Next(0, (int)totalWeight);

        foreach (var npcTemplate in SpawnableNpcs)
        {
            if (randomValue < npcTemplate.Weight)
            {
                var npc = NpcManager.Instance.Create(0, npcTemplate.MemberId);
                if (npc != null)
                {
                    return npc;
                }
                Logger.Error($"Failed to create NPC from template {npcTemplate.MemberId}.");
            }
            randomValue -= (int)npcTemplate.Weight;
        }

        Logger.Warn("No NPC was chosen to spawn.");
        return null;
    }

    /// <summary>
    /// Checks if an NPC is within the spawn radius.
    /// </summary>
    private bool IsNpcInSpawnRadius(Npc npc)
    {
        if (npc == null)
            return false;

        if (Template.TestRadiusNpc == 0)
            return true;

        var distance = MathUtil.CalculateDistance(npc.Transform.World.Position, new Vector3(Position.X, Position.Y, Position.Z));
        return distance <= Template.TestRadiusNpc * 3;
    }

    /// <summary>
    /// Checks if a player is within the spawn radius.
    /// </summary>
    private bool IsPlayerInSpawnRadius()
    {
        // Проверяем, нужно ли вообще проверять радиус
        if (Template.TestRadiusPc == 0)
            return true;

        // Проверяем, есть ли кэш для текущего SpawnerId
        if (_playerInRadiusCache.TryGetValue((int)SpawnerId, out var cache))
        {
            // Если с момента последнего обновления прошло меньше 60 секунд, возвращаем кэшированное значение
            if ((DateTime.UtcNow - cache.LastUpdate).TotalSeconds < 60)
            {
                return cache.IsPlayerInRadius;
            }
        }

        // Если кэш устарел или отсутствует, выполняем проверку
        var players = WorldManager.Instance.GetAllCharacters();
        foreach (var player in players)
        {
            var distance = MathUtil.CalculateDistance(player.Transform.World.Position, new Vector3(Position.X, Position.Y, Position.Z));
            if (distance <= Template.TestRadiusPc * 3)
            {
                // Обновляем кэш
                _playerInRadiusCache[(int)SpawnerId] = new SpawnerPlayerInRadiusCache
                {
                    IsPlayerInRadius = true,
                    LastUpdate = DateTime.UtcNow
                };
                return true;
            }
        }

        // Обновляем кэш (игроков в радиусе нет)
        _playerInRadiusCache[(int)SpawnerId] = new SpawnerPlayerInRadiusCache
        {
            IsPlayerInRadius = false,
            LastUpdate = DateTime.UtcNow
        };
        return false;
    }

    // Структура для хранения кэшированных данных
    private struct SpawnerPlayerInRadiusCache
    {
        public bool IsPlayerInRadius { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Returns the number of players within the spawn radius.
    /// </summary>
    /// <param name="template">The spawner template containing the check radius.</param>
    /// <returns>The number of players within the radius.</returns>
    private int GetNumberOfPlayerInSpawnRadius(NpcSpawnerTemplate template)
    {
        // Checks if the template and radius are valid
        if (_playerCountCache.TryGetValue((int)SpawnerId, out var cache))
        {
            // Если прошло меньше 60 секунд с момента последнего обновления, возвращаем кэшированное значение
            if ((DateTime.UtcNow - cache.LastUpdate).TotalSeconds < 60)
            {
                return cache.PlayerCount;
            }
        }

        // Проверяем, что шаблон и радиус валидны
        if (template == null || template.TestRadiusNpc <= 0)
            return 0;

        var playerCount = 0;

        // Gets the spawn position (e.g., the position of the first NPC or the center point)
        if (SpawnedNpcs is { Count: > 0 })
        {
            var npcs = SpawnedNpcs.Values.FirstOrDefault();
            if (npcs?.Count > 0)
            {
                // Returns the number of players
                var tmpPlayerCount = WorldManager.GetAround<Character>(npcs[0], template.TestRadiusNpc * 3).Count;
                if (playerCount < tmpPlayerCount)
                    playerCount = tmpPlayerCount;
            }
        }

        // Обновляем кэш для текущего SpawnerId
        _playerCountCache[(int)SpawnerId] = new SpawnerPlayerCountCache
        {
            PlayerCount = playerCount,
            LastUpdate = DateTime.UtcNow
        };

        return playerCount;
    }

    // Структура для хранения кэшированных данных
    private struct SpawnerPlayerCountCache
    {
        public int PlayerCount { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Returns the optimal spawner for spawning NPCs based on the number of players and MinPopulation/MaxPopulation parameters.
    /// </summary>
    /// <returns>
    /// The ID of the selected spawner or <c>null</c> if no suitable spawner is found.
    /// </returns>
    private uint? GetOptimalSpawnerForPlayers()
    {
        // If the list of spawners is empty, return null
        if (NpcSpawnerIds == null || NpcSpawnerIds.Count == 0)
            return null;

        // Gets the number of players within the spawn radius
        var playerCount = GetNumberOfPlayerInSpawnRadius(Template);
        if (playerCount == 0)
        {
            return SpawnerId;
        }
        // Iterates through all spawners and selects a suitable one
        foreach (var spawnerId in NpcSpawnerIds)
        {
            // Gets the template for the current spawner
            var spawnerTemplate = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);

            // Checks if the number of players is suitable for this spawner
            if (playerCount >= spawnerTemplate.MinPopulation && playerCount <= spawnerTemplate.MaxPopulation)
            {
                // If the conditions are met, returns this spawner
                return spawnerId;
            }
        }

        // If no suitable spawner is found, returns null
        return null;
    }

    /// <summary>
    /// Checks if there are NPCs in other spawners.
    /// </summary>
    /// <returns>
    /// <c>true</c> if there are NPCs in other spawners; 
    /// <c>false</c> if other spawners are empty.
    /// </returns>
    private bool AreOtherNpcsInSpawnZone()
    {
        // Iterates through all spawners
        foreach (var spawnerId in SpawnedNpcs.Keys)
        {
            // Excludes the current spawner
            if (spawnerId == SpawnerId)
                continue;

            // Checks if there are NPCs in this spawner
            if (SpawnedNpcs.TryGetValue(spawnerId, out var npcs) && npcs?.Count > 0)
            {
                return true; // There are NPCs in another spawner
            }
        }

        // If there are no NPCs in any other spawner, returns false
        return false;
    }

    /// <summary>
    /// Spawns all NPCs associated with this spawner.
    /// </summary>
    public List<Npc> SpawnAll(bool beginning = false)
    {
        if (IsSpawningScheduleEnabled())
            return null;

        DoSpawn();

        if (IsSpawnScheduled)
            IsDespawningScheduleEnabled(SpawnerId);

        return SpawnedNpcs[SpawnerId];
    }

    /// <summary>
    /// Spawns a single NPC with the specified object ID.
    /// </summary>
    public override Npc Spawn(uint objId)
    {
        if (IsSpawningScheduleEnabled())
            return null;

        DoSpawn();

        if (IsSpawnScheduled)
            IsDespawningScheduleEnabled(SpawnerId);

        return SpawnedNpcs[SpawnerId][0];
    }

    /// <summary>
    /// Force spawns a single NPC with the specified object ID.
    /// </summary>
    public override Npc ForceSpawn(uint objId)
    {
        if (SpawnedNpcs.Count == 0)
        {
            InitializeSpawnableNpcs(Template);
        }

        DoSpawn();

        if (IsSpawnScheduled)
            IsDespawningScheduleEnabled(SpawnerId);

        return SpawnedNpcs[SpawnerId][0];
    }

    /// <summary>
    /// Despawns the specified NPC.
    /// </summary>
    public override void Despawn(Npc npc)
    {
        if (npc == null)
        {
            Logger.Warn("Attempted to despawn a null NPC.");
            return;
        }

        try
        {
            lock (_spawnLock)
            {
                // Unregisters NPC events and deletes it
                npc.UnregisterNpcEvents();
                npc.Delete();

                // Releases ObjId if the NPC will not respawn
                if (npc.Respawn == DateTime.MinValue)
                    ObjectIdManager.Instance.ReleaseId(npc.ObjId);

                // Removes the NPC from the SpawnedNpcs list
                if (npc.Spawner != null)
                {
                    var id = npc.Spawner.SpawnerId;
                    if (SpawnedNpcs.TryGetValue(id, out var npcList))
                    {
                        var removed = npcList.Remove(npc);
                        if (!removed)
                        {
                            //Logger.Warn($"NPC {npc.TemplateId} not found in SpawnedNpcs for SpawnerId={id}.");
                        }

                        // If the NPC list is empty, removes the entry from the dictionary
                        if (npcList.Count == 0)
                        {
                            var removedEntry = SpawnedNpcs.TryRemove(id, out _);
                            if (!removedEntry)
                            {
                                //Logger.Warn($"Failed to remove empty SpawnerId={id} from SpawnedNpcs.");
                            }
                            else
                            {
                                //Logger.Debug($"Removed empty SpawnerId={id} from SpawnedNpcs.");
                            }
                        }
                    }
                    else
                    {
                        //Logger.Warn($"SpawnerId={id} not found in SpawnedNpcs.");
                    }
                }
                else
                {
                    //Logger.Warn($"NPC {npc.TemplateId} has no associated Spawner.");
                }

                // Decreases the NPC count
                DecreaseCount(npc);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to despawn NPC {npc.TemplateId}.");
        }
    }

    public void RemoveNpc(uint spawnerId, Npc npc)
    {
        if (SpawnedNpcs.TryGetValue(spawnerId, out var npcList))
        {
            lock (_spawnLock)
            {
                npcList.Remove(npc);

                // If the NPC list is empty, removes the entry from the dictionary
                if (npcList.Count == 0)
                {
                    SpawnedNpcs.TryRemove(spawnerId, out _);
                }
            }
        }
    }

    /// <summary>
    /// Clears the last spawn count.
    /// </summary>
    public void ClearLastSpawnCount()
    {
        Interlocked.Exchange(ref _spawnCount, 0);
    }

    /// <summary>
    /// Decreases the spawn count and handles respawn logic for the specified NPC.
    /// </summary>
    public void DecreaseCount(Npc npc)
    {
        if (npc == null)
        {
            Logger.Warn("Attempted to decrease count for a null NPC.");
            return;
        }

        try
        {
            // Decreases the spawn count
            var newSpawnCount = Interlocked.Decrement(ref _spawnCount);
            //Logger.Trace($"Decreased spawn count for NPC {npc.ObjId}. New count: {newSpawnCount}.");

            // Schedules respawn if necessary
            if (RespawnTime > 0 && newSpawnCount + _scheduledCount < Count)
            {
                npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(npc);
                var newScheduledCount = Interlocked.Increment(ref _scheduledCount);
                //Logger.Trace($"Scheduled respawn for NPC {npc.ObjId} in {RespawnTime} seconds. New scheduled count: {newScheduledCount}.");
            }

            // Sets the despawn time
            npc.Despawn = DateTime.UtcNow.AddSeconds(DespawnTime);

            // Extends the despawn time if there are items in the container
            if (npc.LootingContainer != null && npc.LootingContainer.Items.Count > 0)
            {
                npc.Despawn += TimeSpan.FromSeconds(LootingContainer.LootDespawnExtensionTime);
                //Logger.Trace($"Extended despawn time for NPC {npc.ObjId} due to items in looting container.");
            }

            // Adds the NPC to the despawn list
            SpawnManager.Instance.AddDespawn(npc);
            //Logger.Trace($"Added NPC {npc.ObjId} to despawn list.");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to decrease count for NPC {npc.ObjId}.");
        }
    }

    /// <summary>
    /// Despawns the specified NPC and schedules respawn if necessary.
    /// </summary>
    public void DespawnWithRespawn(Npc npc)
    {
        if (npc == null) return;

        npc.Delete();
        Interlocked.Decrement(ref _spawnCount);

        if (RespawnTime <= 0 || _spawnCount + _scheduledCount >= Count)
            return;

        npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
        SpawnManager.Instance.AddRespawn(npc);
        Interlocked.Increment(ref _scheduledCount);
    }

    /// <summary>
    /// Despawns all NPCs, excluding those in combat.
    /// </summary>
    /// <param name="npcs">The list of NPCs to despawn.</param>
    public void DoDespawn(List<Npc> npcs)
    {
        if (npcs == null)
        {
            Logger.Warn("Attempted to despawn a null list of NPCs.");
            return;
        }

        // Creates a copy of the list for safe iteration
        var npcsToDespawn = npcs.ToList();

        foreach (var npc in npcsToDespawn)
        {
            try
            {
                if (npc == null)
                {
                    Logger.Warn("Attempted to despawn a null NPC.");
                    continue;
                }

                // Despawns the NPC if it is not in combat
                if (!npc.IsInBattle)
                {
                    Despawn(npc);
                    //Logger.Trace($"Despawned NPC {npc.ObjId}.");
                }
                else
                {
                    //Logger.Trace($"Skipped despawn for NPC {npc.ObjId} because it is in battle.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to despawn NPC {npc?.ObjId}.");
            }
        }
    }

    /// <summary>
    /// Spawns NPCs.
    /// </summary>
    public void DoSpawn()
    {
        // Checks if there are NPCs to spawn
        if (SpawnableNpcs == null || SpawnableNpcs.Count == 0)
        {
            Logger.Warn("No spawnable NPCs available.");
            return;
        }

        // List to store spawned NPCs
        var spawnedNpcs = new List<Npc>();

        // Iterates through all NPC templates
        foreach (var npcTemplate in SpawnableNpcs)
        {
            try
            {
                if (npcTemplate == null)
                {
                    Logger.Warn("NPC template is null.");
                    continue;
                }

                // Creates the NPC
                var npc = NpcManager.Instance.Create(0, npcTemplate.MemberId);
                if (npc == null)
                {
                    Logger.Warn($"Failed to create NPC from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");
                    continue;
                }

                // Spawns the NPC
                var spawned = npcTemplate.Spawn(this);
                if (spawned == null || spawned.Count == 0)
                {
                    Logger.Warn($"No NPCs spawned from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");
                    continue;
                }

                // Adds the spawned NPCs to the list
                lock (_spawnLock) // Synchronizes access to the list
                {
                    spawnedNpcs.AddRange(spawned);
                    foreach (var n in spawned)
                    {
                        AddNpcToSpawned(n.Spawner.SpawnerId, n);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to spawn NPC from template {npcTemplate?.SpawnerId}:{npcTemplate?.MemberId}");
            }
        }

        // Checks if any NPCs were spawned
        if (spawnedNpcs.Count == 0)
        {
            Logger.Error($"Can't spawn NPC {SpawnerId}:{UnitId} from index={Template.Id}");
            return;
        }

        // Increases the count of spawned NPCs
        IncrementCount(spawnedNpcs);

        //Logger.Info($"Mobs were spawned from SpawnerId={SpawnerId} in the amount of {spawnedNpcs.Count}");
    }

    /// <summary>
    /// Schedules NPC spawning.
    /// </summary>
    private bool IsSpawningScheduleEnabled()
    {
        if (Template == null)
        {
            Logger.Warn($"Can't spawn npc {SpawnerId}:{UnitId} from index={Id}");
            return false;
        }

        IsSpawnScheduled = false;

        // Checks the spawn time
        if (Template.StartTime > 0.0f || Template.EndTime > 0.0f)
        {
            var curTime = TimeManager.Instance.GetTime;
            var startTime = TimeSpan.FromHours(Template.StartTime);
            var endTime = TimeSpan.FromHours(Template.EndTime);
            var currentTime = TimeSpan.FromHours(curTime);

            if (!IsTimeBetween(currentTime, startTime, endTime))
                return false;

            IsSpawnScheduled = true; // Spawning is allowed by time
            return true;
        }

        // Checks the status in GameScheduleManager
        var status = GameScheduleManager.Instance.GetPeriodStatusNpc((int)Template.Id);
        switch (status)
        {
            case GameScheduleManager.PeriodStatus.NotFound:
                //IsNotFoundInScheduler = true;
                IsSpawnScheduled = false;
                return true; // NPC not found in the schedule, allows spawning
            case GameScheduleManager.PeriodStatus.NotStarted:
                //IsNotFoundInScheduler = false;
                IsSpawnScheduled = false;
                return false;
            case GameScheduleManager.PeriodStatus.InProgress:
                //IsNotFoundInScheduler = false;
                IsSpawnScheduled = true;
                return true;
            case GameScheduleManager.PeriodStatus.Ended:
                //IsNotFoundInScheduler = false;
                IsSpawnScheduled = false;
                return false;
            default:
                //IsNotFoundInScheduler = false;
                IsSpawnScheduled = false;
                return false;
        }
    }

    private static bool IsTimeBetween(TimeSpan currentTime, TimeSpan startTime, TimeSpan endTime)
    {
        if (startTime <= endTime)
            return currentTime >= startTime && currentTime <= endTime;

        return currentTime >= startTime || currentTime <= endTime;
    }

    /// <summary>
    /// Schedules NPC despawning.
    /// </summary>
    private bool IsDespawningScheduleEnabled(uint spawnerId)
    {
        // If there are no NPCs for the specified spawnerId, despawning is allowed (nothing to despawn)
        if (!SpawnedNpcs.TryGetValue(spawnerId, out var npcs))
            return true;

        // Checks each NPC
        foreach (var npc in npcs)
        {
            //IsNotFoundInScheduler = false;

            // Checks the spawn time (if specified)
            if (npc.Spawner.Template.StartTime > 0.0f || npc.Spawner.Template.EndTime > 0.0f)
            {
                var curTime = TimeManager.Instance.GetTime;
                var startTime = TimeSpan.FromHours(npc.Spawner.Template.StartTime);
                var endTime = TimeSpan.FromHours(npc.Spawner.Template.EndTime);
                var currentTime = TimeSpan.FromHours(curTime);

                // If the current time is NOT within the allowed interval, despawning is prohibited
                if (!IsTimeBetween(currentTime, startTime, endTime))
                    return false;

                IsSpawnScheduled = false; // Spawning is allowed by time
            }

            // Checks the status in GameScheduleManager
            var status = GameScheduleManager.Instance.GetPeriodStatusNpc((int)npc.Spawner.Template.Id);
            switch (status)
            {
                case GameScheduleManager.PeriodStatus.NotFound:
                    //IsNotFoundInScheduler = true;
                    return false; // Despawning is prohibited because the NPC is not found in the schedule
                case GameScheduleManager.PeriodStatus.NotStarted:
                case GameScheduleManager.PeriodStatus.Ended:
                    return true; // Despawning is allowed
                case GameScheduleManager.PeriodStatus.InProgress:
                    // Despawning is prohibited because the NPC is in progress
                    return false;
                default:
                    return false; // Unknown status, despawning is prohibited
            }
        }

        // If all checks pass, despawning is allowed
        return true;
    }

    /// <summary>
    /// Spawns NPCs for an event.
    /// </summary>
    public void DoEventSpawn()
    {
        if (Template == null)
        {
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Id);
            return;
        }

        if (_spawnCount >= Template.MaxPopulation)
            return;

        if (Template.SuspendSpawnCount > 0 && _spawnCount > Template.SuspendSpawnCount)
            return;

        var n = new List<Npc>();
        var nsnTask = Template.Npcs.FirstOrDefault(nsn => nsn.MemberId == UnitId);
        if (nsnTask != null)
        {
            n = nsnTask.Spawn(this);
        }

        try
        {
            foreach (var npc in n)
            {
                AddNpcToSpawned(SpawnerId, npc);
            }
        }
        catch (Exception)
        {
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Template.Id);
        }

        if (n.Count == 0)
        {
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Template.Id);
            return;
        }

        IncrementCount(n);
    }

    private void IncrementCount(List<Npc> n)
    {
        lock (_spawnLock)
        {
            if (_scheduledCount > 0)
                Interlocked.Add(ref _scheduledCount, -n.Count);

            if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcList))
            {
                lock (npcList)
                    Interlocked.Exchange(ref _spawnCount, npcList.Count);
            }
            else
                Interlocked.Exchange(ref _spawnCount, 0);

            if (_spawnCount < 0)
                Interlocked.Exchange(ref _spawnCount, 0);
        }
    }

    /// <summary>
    /// Spawns NPCs with an effect.
    /// </summary>
    public void DoSpawnEffect(uint spawnerId, SpawnEffect effect, BaseUnit caster, BaseUnit target)
    {
        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
        if (template?.Npcs == null)
            return;

        var n = new List<Npc>();
        var templateNsnTask2 = template.Npcs.FirstOrDefault(nsn => nsn != null && nsn.MemberId == UnitId);
        if (templateNsnTask2 != null)
        {
            n = templateNsnTask2.Spawn(this);
        }

        try
        {
            if (n == null) return;

            foreach (var npc in n)
            {
                if (npc.Spawner != null)
                {
                    npc.Spawner.RespawnTime = 0;
                }

                if (effect.UseSummonerFaction)
                {
                    npc.Faction = target is Npc ? target.Faction : caster.Faction;
                }

                if (effect.UseSummonerAggroTarget && !effect.UseSummonerFaction)
                {
                    if (target is Npc)
                    {
                        npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)target, 1);
                    }
                    else
                    {
                        npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)caster, 1);
                    }

                    npc.Ai.OnAggroTargetChanged();
                }

                if (effect.LifeTime > 0)
                {
                    TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(effect.LifeTime));
                }
            }
        }
        catch (Exception)
        {
            Logger.Error("Can't spawn npc {0} from spawner {1}", UnitId, template.Id);
            return;
        }

        if (n.Count == 0)
        {
            Logger.Error("Can't spawn npc {0} from spawner {1}", UnitId, template.Id);
            return;
        }

        foreach (var npc in n)
        {
            AddNpcToSpawned(SpawnerId, npc);
        }

        if (_scheduledCount > 0)
        {
            Interlocked.Add(ref _scheduledCount, -n.Count);
        }

        if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcList))
        {
            lock (npcList)
            {
                Interlocked.Exchange(ref _spawnCount, npcList.Count);
            }
        }
        else
        {
            Interlocked.Exchange(ref _spawnCount, 0);
        }

        if (_spawnCount < 0)
        {
            Interlocked.Exchange(ref _spawnCount, 0);
        }
    }

    /// <summary>
    /// Clears the spawn count and all spawned NPCs.
    /// </summary>
    public void ClearSpawnCount()
    {
        lock (_spawnLock)
        {
            SpawnedNpcs[SpawnerId].Clear();
            Interlocked.Exchange(ref _spawnCount, 0);
        }

        //Logger.Trace("Spawn count cleared.");
    }

    private void AddNpcToSpawned(uint key, Npc newNpc)
    {
        if (newNpc == null)
        {
            Logger.Warn("Attempted to add a null NPC to SpawnedNpcs.");
            return;
        }

        SpawnedNpcs.AddOrUpdate(
            key,
            k =>
            {
                var newNpcList = new List<Npc> { newNpc };
                //Logger.Trace($"Created new NPC list for key {k} and added NPC {newNpc.ObjId}.");
                return newNpcList;
            },
            (k, existingNpcList) =>
            {
                lock (existingNpcList)
                {
                    existingNpcList.Add(newNpc);
                    //Logger.Trace($"Added NPC {newNpc.ObjId} to existing list for key {k}.");
                    return existingNpcList;
                }
            }
        );
    }

    public static T Clone<T>(T obj)
    {
        var inst = obj.GetType().GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return (T)inst?.Invoke(obj, null);
    }
}
