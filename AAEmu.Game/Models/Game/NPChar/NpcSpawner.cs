using System.Collections.Concurrent;
using System.ComponentModel;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawner : Spawner<Npc>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
    public WorldInstance ParentWorld { get; set; }

    private int _scheduledCount;
    // Вычисляемое свойство, возвращающее текущее количество NPC из SpawnedNpcs для данного SpawnerId.
    private int CurrentSpawnCount => SpawnedNpcs.TryGetValue(SpawnerId, out var list) ? list.Count : 0;
    private bool IsSpawnScheduled;
    private bool IsDespawnScheduled;
    private bool RespawnDenied;
    private static readonly object _spawnLock = new(); // Lock for thread safety

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
        IsDespawnScheduled = false;
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
    /// Manages NPC spawning and despawning with thread-safe operations.
    /// Priority rules:
    /// 1. Despawning is checked first and has higher priority.
    /// 2. If despawning isn't triggered, it attempts to spawn NPCs (if conditions are met and the cooldown has passed).
    /// 3. Logs all actions, including cases where no operation is performed.
    /// Thread safety is enforced via locks.
    /// </summary>
    public void Update()
    {
        try
        {
            lock (_spawnLock)
            {
                var didAction = false;

                if (CanDespawnNpcs())
                {
                    //Logger.Debug($"[SpawnerId={SpawnerId}, UnitId={UnitId}] Despawning NPCs...");
                    DespawnNpcs();
                    didAction = true;
                }
                else if (!IsPlayerInSpawnRadius() && CurrentSpawnCount > 0)
                {
                    //Logger.Debug($"[SpawnerId={SpawnerId}, UnitId={UnitId}] Despawning NPCs...");
                    DespawnNpcsNow();
                    didAction = true;
                }

                if (!didAction && CanSpawnNpcs())
                {
                    //Logger.Debug($"[SpawnerId={SpawnerId}, UnitId={UnitId}] Spawning NPCs...");
                    DoSpawn();
                    didAction = true;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Error occurred during NpcSpawner update. [SpawnerId={SpawnerId}, UnitId={UnitId}]");
        }
    }

    /// <summary>
    /// Determines whether the NPCs can be despawned based on schedule and current presence.
    /// </summary>
    public bool CanDespawnNpcs()
    {
        if (!IsDespawningScheduleEnabled(SpawnerId))
        {
            //Logger.Debug($"[Despawn] Schedule does not allow despawning for SpawnerId={SpawnerId}.");
            return false;
        }

        return true;
    }

    private void DespawnNpcs()
    {
        if (IsDespawnScheduled)
        {
            Logger.Debug($"[Despawn] is already at the stage of removal for SpawnerId={SpawnerId}.");
            return; // Group is already at the stage of removal
        }

        if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcs))
            DoDespawns(npcs);
    }

    public void DespawnNpcsNow()
    {
        if (IsDespawnScheduled)
        {
            Logger.Debug($"[Despawn] is already at the stage of removal for SpawnerId={SpawnerId}.");
            return; // Group is already at the stage of removal
        }

        if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcs))
            DoDespawnsNow(npcs);
    }

    /// <summary>
    /// Determines whether the NPCs can be spawned based on schedule and current presence.
    /// </summary>
    /// <returns></returns>
    private bool CanSpawnNpcs()
    {
        if (!CanSpawn())
            return false;

        if (IsSpawnDelayNotElapsed())
        {
            //Logger.Debug($"[SpawnerId={SpawnerId}, UnitId={UnitId}] Spawn delayed — waiting for cooldown.");
            return false;
        }

        return true;
    }

    private bool IsSpawnDelayNotElapsed()
    {
        if (_lastSpawnTime == DateTime.MinValue)
            return false;

        var elapsedSeconds = (DateTime.UtcNow - _lastSpawnTime).TotalSeconds;
        return elapsedSeconds < Template.SpawnDelayMin;
    }

    /// <summary>
    /// Checks if the NPC can be spawned based on various world, player, and schedule conditions.
    /// </summary>
    private bool CanSpawn()
    {
        if (Template == null)
        {
            Logger.Warn($"[Spawn [SpawnerId={SpawnerId}, UnitId={UnitId}] Template is null. Cannot determine if NPC can be spawned.");
            return false;
        }

        if (HasCorpse())
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] Cannot spawn NPC — corpse still present.");
            return false;
        }

        if (IsDespawnScheduled)
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] Despawn is scheduled. Spawning is blocked.");
            return false;
        }

        if (IsSpawnScheduled)
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] Spawn is scheduled. Spawning is blocked.");
            return false;
        }

        if (!IsOptimalSpawner())
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] This is not the optimal spawner.");
            return false;
        }

        if (!IsSpawningScheduleEnabled())
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] Spawning schedule is not enabled.");
            return false;
        }

        if (!CheckSpawnCountCanSpawn())
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] Spawn count conditions not met.");
            return false;
        }

        //Logger.Debug($"[Spawn SpawnerId={SpawnerId}, UnitId={UnitId}] All spawn conditions met. NPC can be spawned.");
        return true;
    }

    /// <summary>
    /// Returns true if this is the optimal spawner
    /// </summary>
    public bool IsOptimalSpawner()
    {
        var optimalId = SelectSpawnerId();
        var result = SpawnerId == optimalId;
        if (!result)
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}] Not optimal (best is {optimalId})");
        }
        return result;
    }

    /// <summary>
    /// Selects the appropriate SpawnerId for an NPC based on the following conditions:
    /// 1. If the NPC has a schedule, selects a spawner with a suitable time window.
    /// 2. If the NPC has no schedule, selects an AutoCreated spawner with a single NPC.
    /// 3. If there is only one spawner for the NPC, selects that spawner.
    /// </summary>
    /// <returns>The selected SpawnerId, or null if no suitable spawner is found.</returns>
    private uint? SelectSpawnerId()
    {
        // Condition 3: If there is only one spawner, select it
        // Condition 2: Check for an AutoCreated spawner without a scheduled NPC
        // Condition 1: Check for a spawner with a suitable schedule

        // Condition 3: If there is only one spawner, select it
        if (NpcSpawnerIds.Count == 1)
        {
            //Logger.Info($"Selected the only available SpawnerId={SpawnerId}.");
            return SpawnerId;
        }

        // Condition 2: Check for an AutoCreated spawner without a scheduled NPC
        if (Template.NpcSpawnerCategoryId == NpcSpawnerCategory.Autocreated && !HasScheduledSpawner())
        {
            //Logger.Info($"Selected AutoCreated SpawnerId={spawnerId} without a scheduled NPC.");
            return SpawnerId;
        }

        if (IsThereSpawningSchedule())
        {
            //Logger.Info($"Selected SpawnerId={spawnerId} based on schedule.");
            return SpawnerId;
        }

        //Logger.Warn("No suitable SpawnerId found for this NPC.");
        return null;
    }

    public bool IsThereSpawningSchedule()
    {
        var scheduleStatus = GameScheduleManager.Instance.GetPeriodStatusNpc((int)SpawnerId);
        switch (scheduleStatus)
        {
            case GameScheduleManager.PeriodStatus.NotFound:
                //Logger.Debug($"[Spawn] No schedule found for NPC {npcId}. Falling back to time window.");
                break; // Переход к проверке времени

            case GameScheduleManager.PeriodStatus.InProgress:
            case GameScheduleManager.PeriodStatus.NotStarted:
            case GameScheduleManager.PeriodStatus.Ended:
                //Logger.Debug($"[Spawn] Расписание у NPC {npcId} имеется.");
                return true;

            default:
                Logger.Warn($"[Spawn] Unknown schedule status '{scheduleStatus}' for NPC {SpawnerId}.");
                return false;
        }

        // If there is no schedule, we check if the time of appearance is set
        if (HasSpawningTime())
        {
            //Logger.Debug($"[Spawn] NPC {npcId} is within spawn time window — spawning enabled.");
            return true;
        }

        //Logger.Debug($"[Spawn] NPC {npcId} not in spawn time window.");
        return false;
    }

    private bool HasSpawningTime()
    {
        if (Template.StartTime > 0.0f || Template.EndTime > 0.0f)
        {
            //Logger.Debug($"[TimeCheck] NPC {Template.Id} checking time window: now={currentTime}, start={startTime}, end={endTime}, inside={result}");
            return true;
        }

        //Logger.Debug($"[TimeCheck] NPC {Template.Id} has no time window defined.");
        return false;
    }

    /// <summary>
    /// Determines whether this spawner is part of a valid scheduled group with defined start and end times.
    /// </summary>
    private bool HasScheduledSpawner()
    {
        if (NpcSpawnerIds == null || NpcSpawnerIds.Count == 0)
        {
            //Logger.Debug($"[Spawn SpawnerId={SpawnerId}] No NpcSpawnerIds defined.");
            return true;
        }

        var result = false; // Default to false
        foreach (var spawnerId in NpcSpawnerIds)
        {
            if (spawnerId == 0)
                continue;

            var spawnerTemplate = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
            if (spawnerTemplate == null)
                continue;

            if (SpawnerId != spawnerId)
            {
                if (spawnerTemplate is { StartTime: > 0.0f, EndTime: > 0.0f } || CheckGameScheduleStatus())
                {
                    //Logger.Debug($"[Spawn SpawnerId={SpawnerId}] имеет другой спавнер SpawnerId={spawnerId} с расписанием спавна.");
                    result = true;
                }
            }
            if (SpawnerId == spawnerId)
            {
                if (spawnerTemplate is { StartTime: > 0.0f, EndTime: > 0.0f } || CheckGameScheduleStatus())
                {
                    //Logger.Debug($"[Spawn SpawnerId={SpawnerId}] имеет расписание спавна.");
                    return true;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Checks if this NPC is allowed to spawn according to the current game schedule.
    /// Updates IsSpawnScheduled flag and returns true if spawning is allowed.
    /// </summary>
    private bool CheckGameScheduleStatus()
    {
        var npcId = (int)Template.Id;
        var status = GameScheduleManager.Instance.GetPeriodStatusNpc(npcId);

        switch (status)
        {
            case GameScheduleManager.PeriodStatus.NotStarted:
            case GameScheduleManager.PeriodStatus.Ended:
            case GameScheduleManager.PeriodStatus.InProgress:
                //Logger.Debug($"[Schedule] NPC TemplateId={npcId} имеет расписание спавна.");
                return true;

            case GameScheduleManager.PeriodStatus.NotFound:
            default:
                //Logger.Debug($"[Schedule] Unknown schedule status '{status}' for NPC TemplateId={npcId}. Не имеет расписание спавна.");
                return false;
        }
    }

    private bool CheckSpawnCountCanSpawn()
    {
        var minPopulation = Template.MinPopulation;
        var maxPopulation = Template.MaxPopulation;
        if (minPopulation == 0)
            minPopulation = 1;

        var playerCount = GetNumberOfPlayerInSpawnRadius(Template);
        if (playerCount == 0)
            playerCount = 1;

        if (playerCount < minPopulation)
            maxPopulation = (uint)playerCount;

        if (playerCount >= minPopulation && playerCount <= maxPopulation)
        {
            maxPopulation = (uint)playerCount;
        }

        // Используем вычисляемое свойство CurrentSpawnCount вместо _spawnCount
        if (Template.SuspendSpawnCount > 0 && CurrentSpawnCount + AreOtherNpcsInSpawnZone().Item2 >= Template.SuspendSpawnCount)
        {
            //Logger.Debug($"Spawn count ({CurrentSpawnCount}:{AreOtherNpcsInSpawnZone().Item2}) for SpawnerId: {UnitId}:{SpawnerId} reached suspend limit ({Template.SuspendSpawnCount}).");
            return false;
        }

        if (CurrentSpawnCount + AreOtherNpcsInSpawnZone().Item2 >= maxPopulation)
        {
            //Logger.Debug($"Spawn count ({CurrentSpawnCount}:{AreOtherNpcsInSpawnZone().Item2}) for SpawnerId: {UnitId}:{SpawnerId} reached maximum limit ({Template.MaxPopulation}).");
            return false;
        }

        return true;
    }

    private bool HasCorpse()
    {
        if (SpawnedNpcs.TryGetValue(SpawnerId, out var npcs))
        {
            if (IsCorpse(npcs))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns true if any of the given NPCs are corpses (dead and persistent).
    /// </summary>
    private bool IsCorpse(List<Npc> npcs)
    {
        if (npcs == null || npcs.Count == 0)
            return false;

        foreach (var npc in npcs)
        {
            if (npc.IsDead) // или другой критерий мертвого NPC
            {
                //Logger.Debug($"[Spawn SpawnerId={SpawnerId}] Found corpse NPC: ObjId={npc.ObjId}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if a player is within the spawn radius.
    /// </summary>
    public bool IsPlayerInSpawnRadius()
    {
        var testRadiusPc = Template.TestRadiusPc == 0 ? Template.TestRadiusNpc : Template.TestRadiusPc;
        var testRadius = testRadiusPc * 50f * testRadiusPc * 50f;
        // Проверяем, есть ли кэш для текущего SpawnerId
        if (_playerInRadiusCache.TryGetValue((int)SpawnerId, out var cache))
        {
            // Если с момента последнего обновления прошло меньше 10 секунд, возвращаем кэшированное значение
            if ((DateTime.UtcNow - cache.LastUpdate).TotalSeconds < 10)
            {
                return cache.IsPlayerInRadius;
            }
        }

        // Если кэш устарел или отсутствует, выполняем проверку
        var players = WorldManager.Instance.GetAllCharacters();
        foreach (var player in players)
        {
            var distance = Vector3.DistanceSquared(player.Transform.World.Position, new Vector3(Position.X, Position.Y, Position.Z));
            if (distance <= testRadius)
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
        // Проверяем, есть ли уже кэш для этого SpawnerId
        if (_playerCountCache.TryGetValue((int)SpawnerId, out var cache))
        {
            // Если прошло меньше 10 секунд с момента последнего обновления, возвращаем кэшированное значение
            if ((DateTime.UtcNow - cache.LastUpdate).TotalSeconds < 10)
            {
                return cache.PlayerCount;
            }
        }

        // Проверяем, что шаблон и радиус валидны
        if (template == null || template.TestRadiusNpc <= 0)
            return 0;

        var playerCount = 0;

        // Получаем позицию спавна (например, позицию первого NPC или центральную точку)
        if (SpawnedNpcs is { Count: > 0 })
        {
            var npcs = SpawnedNpcs.Values.FirstOrDefault();
            if (npcs?.Count > 0)
            {
                // Получаем количество игроков в радиусе
                var tmpPlayerCount = WorldManager.GetAround<Character>(npcs[0], template.TestRadiusNpc * 50).Count;
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

    // Структура для хранения кэшированных данных
    private struct SpawnerNpcsInZoneCache
    {
        public int Count { get; set; }
        public bool AreNpcsInZone { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    // Словарь для хранения кэша
    private readonly Dictionary<int, SpawnerNpcsInZoneCache> _npcsInZoneCache = new();

    /// <summary>
    /// Checks if there are NPCs in other spawners.
    /// </summary>
    /// <returns>
    /// <c>true</c> if there are NPCs in other spawners; 
    /// <c>false</c> if other spawners are empty.
    /// </returns>
    private (bool, int) AreOtherNpcsInSpawnZone()
    {
        var count = 0;

        // Проверяем, есть ли уже кэш для этого SpawnerId
        if (_npcsInZoneCache.TryGetValue((int)SpawnerId, out var cache))
        {
            // Если прошло меньше 60 секунд с момента последнего обновления, возвращаем кэшированное значение
            if ((DateTime.UtcNow - cache.LastUpdate).TotalSeconds < 10)
            {
                //Logger.Debug($"Using cached value for SpawnerId: {UnitId}:{SpawnerId}. AreOtherNpcsInSpawnZone: {cache.AreNpcsInZone}");
                return (cache.AreNpcsInZone, cache.Count);
            }
        }

        var areOtherNpcsInZone = false;

        // Итерируем по всем спавнерам
        foreach (var spawnerId in SpawnedNpcs.Keys)
        {
            // Исключаем текущий спавнер
            if (spawnerId == SpawnerId)
                continue;

            // Проверяем, есть ли NPC в этом спавнере
            if (SpawnedNpcs.TryGetValue(spawnerId, out var npcs) && npcs?.Count > 0)
            {
                //Logger.Debug($"spawn count={_spawnCount + _scheduledCount} for SpawnerId: {UnitId}:{SpawnerId}");
                count += CurrentSpawnCount + _scheduledCount;
                areOtherNpcsInZone = npcs.Count > 0; // В другом спавнере есть NPC
            }
        }

        // Обновляем кэш для текущего SpawnerId
        _npcsInZoneCache[(int)SpawnerId] = new SpawnerNpcsInZoneCache
        {
            Count = count,
            AreNpcsInZone = areOtherNpcsInZone,
            LastUpdate = DateTime.UtcNow
        };

        //Logger.Debug($"Updated cache for SpawnerId: {UnitId}:{SpawnerId}. AreOtherNpcsInSpawnZone: {areOtherNpcsInZone}");
        return (areOtherNpcsInZone, count);
    }

    /// <summary>
    /// Spawns all NPCs associated with this spawner.
    /// </summary>
    public void SpawnAll(bool beginning = false)
    {
        if (IsSpawningScheduleEnabled())
            return;

        DoSpawn();

        if (IsSpawnScheduled)
            IsDespawningScheduleEnabled(SpawnerId);
    }

    /// <summary>
    /// Spawns a single NPC with the specified object ID.
    /// </summary>
    public override Npc Spawn(uint objId)
    {
        DoSpawn();

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
                RemoveNpcFromSpawnedList(npc);
                UnregisterAndDeleteNpc(npc);
                IsDespawnScheduled = false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to despawn NPC {npc.TemplateId}.");
        }
    }

    private static void UnregisterAndDeleteNpc(Npc npc)
    {
        npc.UnregisterNpcEvents();
        npc.Delete();
    }

    private void RemoveNpcFromSpawnedList(Npc npc)
    {
        if (npc.Spawner == null)
        {
            Logger.Warn($"NPC {npc.TemplateId} has no associated Spawner.");
            return;
        }

        var id = npc.Spawner.SpawnerId;
        lock (_spawnLock)
        {
            if (SpawnedNpcs.TryGetValue(id, out var npcList))
            {
                lock (npcList)
                {
                    var removed = npcList.Remove(npc);
                    if (!removed)
                    {
                        Logger.Warn($"NPC {npc.TemplateId} not found in SpawnedNpcs for SpawnerId={id}.");
                    }

                    if (npcList.Count == 0)
                    {
                        var removedEntry = SpawnedNpcs.TryRemove(id, out _);
                        if (!removedEntry)
                        {
                            Logger.Warn($"Failed to remove empty SpawnerId={id} from SpawnedNpcs.");
                        }
                    }
                }
            }
            else
            {
                Logger.Warn($"SpawnerId={id} not found in SpawnedNpcs.");
            }
        }
    }

    /// <summary>
    /// Decreases the spawn count and handles respawn logic for the specified NPC.
    /// </summary>
    internal void DoDespawn(Npc npc)
    {
        try
        {
            lock (_spawnLock)
            {
                // Если условия позволяют, планируем респаун
                if (RespawnTime > 0 && AreOtherNpcsInSpawnZone().Item2 + _scheduledCount < Template.MaxPopulation)
                {
                    // Планируем респаун и обновляем _scheduledCount
                    IncrementCount(true);
                    //Logger.Info($"Scheduled respawn for NPC {UnitId}:{SpawnerId}:{npc.ObjId} in {RespawnTime} seconds.");
                    npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                    npc.ParentWorld.SpawnManager.AddRespawn(npc);
                }
                else
                {
                    IncrementCount(false);
                    //Logger.Info($"Despawning NPC {UnitId}:{SpawnerId}:{npc.ObjId} without scheduling respawn.");
                }

                // Sets the despawn time
                npc.Despawn = DateTime.UtcNow.AddSeconds(DespawnTime);
                //Logger.Info($"Scheduled despawn for NPC {UnitId}:{SpawnerId}:{npc.ObjId} in {DespawnTime} seconds.");

                // Extends the despawn time if there are items in the container
                if (npc.LootingContainer != null && npc.LootingContainer.Items.Count > 0)
                {
                    npc.Despawn += TimeSpan.FromSeconds(LootingContainer.LootDespawnExtensionTime);
                    //Logger.Info($"Extended despawn time in {LootingContainer.LootDespawnExtensionTime} seconds for NPC {UnitId}:{SpawnerId}:{npc.ObjId} due to items in looting container.");
                }

                // Adds the NPC to the despawn list
                npc.ParentWorld.SpawnManager.AddDespawn(npc);
                //Logger.Info($"Added NPC {UnitId}:{SpawnerId}:{npc.ObjId} to despawn list, scheduledCount={_scheduledCount}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to process despawn for NPC {UnitId}:{SpawnerId}:{npc.ObjId}.");
        }
    }

    private void DoDespawnNow(Npc npc)
    {
        try
        {
            lock (_spawnLock)
            {
                // Если планируется немедленный деспаун
                if (AreOtherNpcsInSpawnZone().Item2 + _scheduledCount < Template.MaxPopulation)
                {
                    IncrementCount(true);
                    Logger.Info($"Immediate despawn scheduled for NPC {UnitId}:{SpawnerId}:{npc.ObjId}.");
                }
                npc.ParentWorld.SpawnManager.AddDespawn(npc);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to immediately despawn NPC {UnitId}:{SpawnerId}:{npc.ObjId}.");
        }
    }

    /// <summary>
    /// Despawns the specified NPC and schedules respawn if necessary.
    /// </summary>
    public void DespawnWithRespawn(Npc npc)
    {
        if (npc == null)
            return;

        npc.Delete();

        // Schedules respawn if necessary
        if (RespawnTime > 0 && AreOtherNpcsInSpawnZone().Item2 < Template.MaxPopulation)
        {
            npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
            npc.ParentWorld.SpawnManager.AddRespawn(npc);
            // Логика изменения _scheduledCount (если требуется) остаётся неизменной
            var newScheduledCount = Interlocked.Increment(ref _scheduledCount);
            if (_scheduledCount < 0)
            {
                Interlocked.Exchange(ref _scheduledCount, 0);
            }
            //Logger.Info($"Scheduled respawn for NPC {UnitId}:{SpawnerId}:{npc.ObjId} in {RespawnTime} seconds. New scheduled count: {newScheduledCount}.");
        }
    }

    /// <summary>
    /// Despawns all NPCs, excluding those in combat.
    /// </summary>
    /// <param name="npcs">The list of NPCs to despawn.</param>
    public void DoDespawns(List<Npc> npcs)
    {
        if (npcs == null)
        {
            Logger.Warn("Attempted to despawn a null list of NPCs.");
            return;
        }

        lock (_spawnLock)
        {
            // Установка флага деспауна
            IsDespawnScheduled = true;

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
                    // будем деспавнить Npc в любом случае
                    // we'll despawn the Npc anyway
                    // Despawns the NPC if it is not in combat
                    //if (!npc.IsInBattle)
                    //{
                    DoDespawn(npc);
                    //Logger.Debug($"Despawned NPC {npc.ObjId}.");
                    //}
                    //else
                    //{
                    //    Logger.Debug($"Skipped despawn for NPC {npc.ObjId} because it is in battle.");
                    //}
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to despawn NPC {UnitId}:{SpawnerId}:{npc?.ObjId}.");
                }
            }
            // Сброс флага после завершения деспауна
            IsDespawnScheduled = false;
        }
    }

    public void DoDespawnsNow(List<Npc> npcs)
    {
        if (npcs == null)
        {
            Logger.Warn("Attempted to despawn a null list of NPCs.");
            return;
        }

        lock (_spawnLock)
        {
            // Установка флага деспауна
            IsDespawnScheduled = true;

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
                    DoDespawnNow(npc);
                    Logger.Debug($"Despawned NPC {npc.TemplateId}:{npc.ObjId}.");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to despawn NPC {UnitId}:{SpawnerId}:{npc?.ObjId}.");
                }
            }
            // Сброс флага после завершения деспауна
            IsDespawnScheduled = false;
        }
    }

    public void SetSpawnScheduled(bool value)
    {
        IsSpawnScheduled = value;
    }

    /// <summary>
    /// Spawns NPCs.
    /// </summary>
    public void DoSpawn()
    {
        // Check if template exists
        if (Template == null)
        {
            Logger.Error($"[Spawn] Can't spawn npc {UnitId} from spawnerId {Id} - Template is null");
            return;
        }
        Logger.Debug($"[Spawn] Starting spawn process for SpawnerId={SpawnerId}, UnitId={UnitId}, Template={Template.Id}");

        // Check population limits
        if (CurrentSpawnCount >= Template.MaxPopulation)
        {
            //Logger.Debug($"[Spawn] SpawnerId={SpawnerId} reached max population ({CurrentSpawnCount}/{Template.MaxPopulation})");
            return;
        }

        if (Template.SuspendSpawnCount > 0 && CurrentSpawnCount > Template.SuspendSpawnCount)
        {
            //Logger.Debug($"[Spawn] SpawnerId={SpawnerId} reached suspend limit ({CurrentSpawnCount}/{Template.SuspendSpawnCount})");
            return;
        }

        // Checks if there are NPCs to spawn
        if (SpawnableNpcs == null || SpawnableNpcs.Count == 0)
        {
            Logger.Warn($"[Spawn] No spawnable NPCs available for SpawnerId={SpawnerId}");
            return;
        }

        //Logger.Debug($"[Spawn] Found {SpawnableNpcs.Count} spawnable NPCs for SpawnerId={SpawnerId}");

        // List to store spawned NPCs
        var spawnedNpcs = new List<Npc>();

        // Iterates through all NPC templates
        foreach (var npcTemplate in SpawnableNpcs)
        {
            try
            {
                if (npcTemplate == null)
                {
                    Logger.Warn($"[Spawn] NPC template is null in SpawnerId={SpawnerId}");
                    continue;
                }

                //Logger.Debug($"[Spawn] Attempting to spawn NPC template {npcTemplate.SpawnerId}:{npcTemplate.MemberId} for SpawnerId={SpawnerId}");

                lock (_spawnLock)
                {
                    // Спавним NPC по шаблону
                    var spawned = npcTemplate.Spawn(this);
                    if (spawned == null || spawned.Count == 0)
                    {
                        Logger.Warn($"[Spawn] Failed to spawn NPC from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId} for SpawnerId={SpawnerId}");
                        continue;
                    }

                    //Logger.Debug($"[Spawn] Successfully spawned {spawned.Count} NPCs from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");

                    // Adds the spawned NPCs to the list
                    spawnedNpcs.AddRange(spawned);
                    foreach (var npc in spawned)
                    {
                        AddNpcToSpawned(npc.Spawner.SpawnerId, npc);
                        //Logger.Debug($"[Spawn] Added NPC {npc.ObjId} to SpawnedNpcs for SpawnerId={npc.Spawner.SpawnerId}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[Spawn] Failed to spawn NPC from template {npcTemplate?.SpawnerId}:{npcTemplate?.MemberId} for SpawnerId={SpawnerId}");
            }
        }

        // Checks if any NPCs were spawned
        if (spawnedNpcs.Count == 0)
        {
            Logger.Error($"[Spawn] Failed to spawn any NPCs for {UnitId}:{SpawnerId}");
            return;
        }

        //Logger.Info($"[Spawn] Successfully spawned {spawnedNpcs.Count} NPCs from SpawnerId={UnitId}:{SpawnerId}");

        // Update spawn count
        DecrementCount(spawnedNpcs);
        //Logger.Debug($"[Spawn] Updated spawn count for SpawnerId={SpawnerId}, Current count={CurrentSpawnCount}");
    }

    /// <summary>
    /// Schedules NPC spawning based on schedule status and optional time window.
    /// </summary>
    public bool IsSpawningScheduleEnabled()
    {
        if (Template == null)
        {
            Logger.Warn($"[Spawn] Can't spawn NPC {SpawnerId}:{UnitId} (index={Id}) — template is null.");
            return false;
        }

        IsSpawnScheduled = false;

        var scheduleStatus = GameScheduleManager.Instance.GetPeriodStatusNpc((int)SpawnerId);
        switch (scheduleStatus)
        {
            case GameScheduleManager.PeriodStatus.InProgress:
                //Logger.Debug($"[Spawn] NPC {npcId} has active schedule — spawning enabled.");
                RespawnDenied = true;
                IsSpawnScheduled = true;
                return true;

            case GameScheduleManager.PeriodStatus.NotFound:
                //Logger.Debug($"[Spawn] No schedule found for NPC {npcId}. Falling back to time window.");
                break; // Переход к проверке времени

            case GameScheduleManager.PeriodStatus.NotStarted:
                //Logger.Debug($"[Spawn] Schedule not started for NPC {npcId}.");
                return false;

            case GameScheduleManager.PeriodStatus.Ended:
                //Logger.Debug($"[Spawn] Schedule ended for NPC {npcId}.");
                return false;

            default:
                Logger.Debug($"[Spawn] Unknown schedule status '{scheduleStatus}' for NPC {SpawnerId}.");
                return false;
        }

        // Если расписания нет — проверим, задано ли время появления
        if (IsWithinSpawnTime())
        {
            //Logger.Debug($"[Spawn] NPC {npcId} is within spawn time window — spawning enabled.");
            RespawnDenied = true;
            IsSpawnScheduled = true;
            return true;
        }

        //Logger.Debug($"[Spawn] NPC {npcId} not in spawn time window.");
        return false;
    }

    /// <summary>
    /// Checks if the current time is between NPC spawn start and end time.
    /// </summary>
    private bool IsWithinSpawnTime()
    {
        if (Template.StartTime > 0.0f || Template.EndTime > 0.0f)
        {
            var curTime = TimeManager.Instance.GetTime;
            var startTime = TimeSpan.FromHours(Template.StartTime);
            var endTime = TimeSpan.FromHours(Template.EndTime);
            var currentTime = TimeSpan.FromHours(curTime);

            var result = IsTimeBetween(currentTime, startTime, endTime);
            //Logger.Debug($"[TimeCheck] NPC {Template.Id} checking time window: now={currentTime}, start={startTime}, end={endTime}, inside={result}");
            return result;
        }

        //Logger.Debug($"[TimeCheck] NPC {Template.Id} has no time window defined.");
        return true; // было false, но не совсем корректно, т.к. надо спавнить, если не в расписании
    }

    /// <summary>
    /// Checks if NPCs under the given spawner should remain spawned based on time or schedule.
    /// </summary>
    private bool IsDespawningScheduleEnabled(uint spawnerId)
    {
        if (!SpawnedNpcs.TryGetValue(spawnerId, out var npcs))
            return false;

        foreach (var npc in npcs)
        {
            if (IsWithinDespawnTime(npc))
            {
                //Logger.Debug($"[Despawn] NPC {npc.ObjId} not in allowed time window — stays.");
                return true;
            }

            if (IsNpcInTimeWindow(npc))
            {
                //Logger.Debug($"[Despawn] NPC {npc.ObjId} is within active schedule — stays.");
                return true;
            }
        }

        //Logger.Debug($"[Despawn] All NPCs under Spawner {spawnerId} are outside of time/schedule — despawn allowed.");
        return false;
    }

    private static bool IsWithinDespawnTime(Npc npc)
    {
        var template = npc.Spawner?.Template;
        if (template == null || (template.StartTime <= 0.0f && template.EndTime <= 0.0f))
            return false;

        var curTime = TimeManager.Instance.GetTime;
        var startTime = TimeSpan.FromHours(template.StartTime);
        var endTime = TimeSpan.FromHours(template.EndTime);
        var currentTime = TimeSpan.FromHours(curTime);

        var outside = !IsTimeBetween(currentTime, startTime, endTime);
        //Logger.Debug($"[DespawnTime] NPC {npc.ObjId} time check: now={currentTime}, start={startTime}, end={endTime}, outside={outside}");
        return outside;
    }

    private static bool IsNpcInTimeWindow(Npc npc)
    {
        var status = GameScheduleManager.Instance.GetPeriodStatusNpc((int)npc.Spawner.Template.Id);

        switch (status)
        {
            case GameScheduleManager.PeriodStatus.InProgress:
                return true;

            case GameScheduleManager.PeriodStatus.NotFound:
            case GameScheduleManager.PeriodStatus.NotStarted:
            case GameScheduleManager.PeriodStatus.Ended:
                return false;

            default:
                Logger.Warn($"[Schedule] Unknown schedule status '{status}' for NPC {npc.ObjId}. Assuming not in progress.");
                return false;
        }
    }

    /// <summary>
    /// Checks if the current time is between startTime and endTime, including wrapping over midnight.
    /// </summary>
    public static bool IsTimeBetween(TimeSpan currentTime, TimeSpan startTime, TimeSpan endTime)
    {
        if (startTime <= endTime)
        {
            var result = currentTime >= startTime && currentTime <= endTime;
            //Logger.Debug($"[TimeCheck] {currentTime} inside range {startTime}-{endTime}? {result}");
            return result;
        }

        var resultWrapped = currentTime >= startTime || currentTime <= endTime;
        //Logger.Debug($"[TimeCheck] {currentTime} inside wrapped range {startTime}-{endTime}? {resultWrapped}");
        return resultWrapped;
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

        if (CurrentSpawnCount >= Template.MaxPopulation)
            return;

        if (Template.SuspendSpawnCount > 0 && CurrentSpawnCount > Template.SuspendSpawnCount)
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

        DecrementCount(n);
    }

    private void DecrementCount(List<Npc> n)
    {
        lock (_spawnLock)
        {
            if (_scheduledCount > 0)
                Interlocked.Add(ref _scheduledCount, -n.Count);
        }
    }

    private void IncrementCount(bool respawn = false)
    {
        lock (_spawnLock)
        {
            if (respawn)
            {
                _ = Interlocked.Increment(ref _scheduledCount);
                if (_scheduledCount < 0)
                {
                    Interlocked.Exchange(ref _scheduledCount, 0);
                }
            }
        }
    }

    /// <summary>
    /// Spawns a random NPC, with optional ownerId (used with target_my_npc flag)
    /// </summary>
    public Npc DoRandomSpawn(uint spawnerId, uint ownerId = 0)
    {
        // Get the NPC spawner template
        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
        if (template?.Npcs == null || template.Npcs.Count == 0)
        {
            Logger.Warn($"No NPC templates available for spawner {spawnerId}.");
            return null;
        }
        // Select a random NPC template from the template.Npcs
        var npcTemplate = template.Npcs.RandomElementByWeight(x => x.Weight);
        if (npcTemplate == null)
        {
            Logger.Warn($"Random template returned null on the NPC selection for spawner {spawnerId}.");
            return null;
        }

        try
        {
            // Creates the NPC
            var npc = NpcManager.Instance.Create(ParentWorld, 0, npcTemplate.MemberId);
            if (npc == null)
            {
                Logger.Warn($"Failed to create NPC from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");
                return null;
            }
            // Spawns the NPC
            var spawned = npcTemplate.Spawn(this, ownerId);
            if (spawned == null || spawned.Count == 0)
            {
                Logger.Warn($"No NPCs spawned from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");
                return null;
            }
            // Adds the spawned NPC to the list
            if (spawned.Count > 0)
            {
                var spawnedNpc = spawned.First();
                lock (_spawnLock) // Synchronizes access to the list
                {
                    AddNpcToSpawned(spawnedNpc.Spawner.SpawnerId, spawnedNpc);
                }

                spawnedNpc.Spawn();

                return spawnedNpc;
            }
            Logger.Warn($"Failed to retrieve spawned NPC from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, $"Failed to spawn NPC from template {npcTemplate.SpawnerId}:{npcTemplate.MemberId}");
            return null;
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
                //Logger.Debug($"Created new NPC list for key {k} and added NPC {newNpc.ObjId}.");
                return newNpcList;
            },
            (k, existingNpcList) =>
            {
                lock (existingNpcList)
                {
                    existingNpcList.Add(newNpc);
                    //Logger.Debug($"Added NPC {newNpc.ObjId} to existing list for key {k}.");
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
