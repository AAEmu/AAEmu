using System.Numerics;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils;

using NLog;
// ReSharper disable ChangeFieldTypeToSystemThreadingLock

namespace AAEmu.Game.Core.Managers.World;

public class SpawnManager(WorldInstance parentWorld)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;

    /// <summary>
    /// WorldInstance that owns this spawn manager
    /// </summary>
    private WorldInstance World { get; } = parentWorld;

    private bool _work = true;
    private readonly object _lock = new();
    private readonly object _lockSpawner = new();
    private HashSet<GameObject> Respawns { get; } = [];
    private HashSet<GameObject> Despawns { get; } = [];

    private Dictionary<uint, List<NpcSpawner>> NpcSpawners { get; } = []; // (idx, List<NpcSpawner>)
    private Dictionary<uint, List<NpcSpawner>> NpcEventSpawners { get; } = []; // (idx, List<NpcSpawner>)
    private Dictionary<uint, DoodadSpawner> DoodadSpawners { get; } = [];
    private Dictionary<uint, TransferSpawner> TransferSpawners { get; } = [];
    private Dictionary<uint, GimmickSpawner> GimmickSpawners { get; } = [];
    private Dictionary<uint, SlaveSpawner> SlaveSpawners { get; } = [];
    private List<Doodad> PlayerDoodads { get; } = [];

    private uint _nextId = 1u;
    // Shared across all SpawnManager instances — all write into the global NpcGameData singleton
    private static uint s_fakeSpawnerId = 9000001u;

    public List<Task> SpawnTasks { get; init; } = [];

    /// <summary>
    /// Adds an NPC spawner to the manager.
    /// </summary>
    public void AddNpcSpawner(NpcSpawner npcSpawner)
    {
        lock (NpcSpawners)
        {
            if (npcSpawner.NpcSpawnerIds is [0])
                npcSpawner.NpcSpawnerIds = [];

            // check for manually entered NpcSpawnerId
            if (npcSpawner.NpcSpawnerIds.Count == 0)
            {
                var gameDataNpcSpawnerIds = NpcGameData.Instance.GetSpawnerIds(npcSpawner.UnitId);
                var spawners = new List<NpcSpawner>();
                if (gameDataNpcSpawnerIds == null || gameDataNpcSpawnerIds.Count == 0)
                {
                    // No pre-defined spawner found, create a new one
                    Logger.Trace($"SpawnerIds for Npc={npcSpawner.UnitId} doesn't exist");
                    Logger.Trace($"Generate Spawner for Npc={npcSpawner.UnitId}...");
                    var id = s_fakeSpawnerId;
                    npcSpawner.ParentWorld = World;
                    npcSpawner.NpcSpawnerIds.Add(id);
                    npcSpawner.Id = id;
                    // 10.0.2.13 compact.sqlite3: npc_spawners is empty — no template id=1 to clone.
                    var tmpTemplate = NpcGameData.Instance.GetNpcSpawnerTemplate(1);
                    npcSpawner.Template = tmpTemplate != null
                        ? Helpers.Clone(tmpTemplate)
                        : new NpcSpawnerTemplate();
                    npcSpawner.Template.Id = id;

                    var tmpNpc = new NpcSpawnerNpc
                    {
                        Position = npcSpawner.Position,
                        MemberId = npcSpawner.UnitId,
                        Id = id,
                        MemberType = "Npc",
                        Weight = 1f,
                        NpcSpawnerTemplateId = id
                    };
                    npcSpawner.Template.Npcs = [tmpNpc];
                    NpcGameData.Instance.AddNpcSpawnerNpc(tmpNpc);
                    NpcGameData.Instance.AddMemberAndSpawnerTemplateIds(tmpNpc);
                    NpcGameData.Instance.AddNpcSpawner(npcSpawner.Template);
                    s_fakeSpawnerId++;
                }
                else
                {
                    // There were spawners found in the game data that define this NPC
                    foreach (var id in gameDataNpcSpawnerIds)
                    {
                        var spawner = NpcSpawner.Clone(npcSpawner);
                        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(id);
                        spawner.ParentWorld = World;
                        spawner.InitializeSpawnableNpcs(template);
                        spawner.NpcSpawnerIds.Add(id);
                        spawner.Id = _nextId;
                        spawner.SpawnerId = id;
                        spawner.Template = template;
                        foreach (var n in spawner.Template.Npcs)
                        {
                            n.Position = spawner.Position;
                        }

                        spawners.Add(spawner);
                        _nextId++;
                    }
                }

                NpcSpawners.TryAdd(_nextId, spawners);
            }
            else
            {
                // Load NPC Spawns for Events
                var spawners = new List<NpcSpawner>();
                foreach (var id in npcSpawner.NpcSpawnerIds)
                {
                    npcSpawner.Id = id;
                    npcSpawner.Template = new NpcSpawnerTemplate(id, npcSpawner.UnitId);
                    npcSpawner.ParentWorld = World;
                    foreach (var n in npcSpawner.Template.Npcs)
                    {
                        n.Position = npcSpawner.Position;
                    }
                }

                spawners.Add(npcSpawner);
                NpcEventSpawners.TryAdd(_nextId, spawners);
                _nextId++;
            }
        }
    }

    /// <summary>
    /// Despawns all Npcs and Doodads in this instance
    /// </summary>
    /// <returns></returns>
    public int DeSpawnAll()
    {
        var res = 0;
        // NPCs
        foreach (var npc in World.GetAllNpcs().ToList())
            try
            {
                if (npc.Spawner != null)
                {
                    npc.Spawner.RespawnTime = 9999999;
                    npc.Spawner.DoDespawn(npc);
                }
                else
                {
                    npc.Delete();
                }

                res++;
            }
            catch
            {
                //
            }

        // Doodads
        foreach (var doodad in World.GetAllDoodads().ToList())
            try
            {
                if (doodad.Spawner != null)
                {
                    doodad.Spawner.RespawnTime = 9999999;
                    doodad.Spawner.Despawn(doodad);
                }
                else
                {
                    doodad.IsPersistent = false; // Don't force additional deletes by detaching the doodad from the save system
                    doodad.Delete();
                }

                res++;
            }
            catch
            {
                //
            }

        foreach (var mate in World.GetAllMates().ToList())
            try
            {
                mate.Delete();
                res++;
            }
            catch
            {
                //
            }

        foreach (var slave in World.GetAllSlaves().ToList())
            try
            {
                slave.Delete();
                res++;
            }
            catch
            {
                //
            }

        return res;
    }

    /// <summary>
    /// Load spawn data and spawns persistent objects
    /// </summary>
    public void Load()
    {
        if (_loaded)
            return;

        lock (Respawns) Respawns.Clear();
        lock (Despawns) Despawns.Clear();
        NpcSpawners.Clear();
        NpcEventSpawners.Clear();
        DoodadSpawners.Clear();
        TransferSpawners.Clear();
        GimmickSpawners.Clear();
        SlaveSpawners.Clear();
        PlayerDoodads.Clear();

        Logger.Info($"Loading spawn data for {World} ...");
        var worldPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", World.Template.Name);

        // NPC placements are not read here: the Zone loads npc_spawners.g itself and announces
        // every NPC it creates over ZWSpawnNpc, which World mirrors. Parsing the same placements a
        // second time only produced a duplicate, unmanaged copy of the world's NPCs.

        // Load Doodad spawns
        Logger.Debug($"Loading Doodad spawn data for {World} ...");
        _ = LoadDoodadSpawns(worldPath);

        // Load Transfers
        Logger.Debug($"Loading Transfer spawn data for {World} ...");
        _ = LoadTransferSpawns(worldPath);

        // Load Gimmicks
        Logger.Debug($"Loading Gimmick spawn data for {World} ...");
        _ = LoadGimmickSpawns(worldPath);

        // Load Slaves
        Logger.Debug($"Loading Slave spawn data for {World} ...");
        _ = LoadSlaveSpawns(worldPath);

        // Spawn persistent doodads (main_world only)
        if (World.Template.Id == WorldManager.DefaultWorldTemplateId)
        {
            // Load player housing data
            Logger.Info($"Loading player housing for {World}");
            HousingManager.Instance.LoadPlayerHousing(World);
            HousingManager.Instance.SpawnAll(); // Houses need to be spawned before doodads

            if (AppConfiguration.Instance.World.SpawnDoodads)
            {
                Logger.Info($"Loading persistent doodads for {World}");
                var doodadsSpawned = 0;

                // Load furniture and bound doodads
                doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.Housing);
                // Reconcile bound doodads: spawn any missing from DB, remove duplicates
                if (AppConfiguration.Instance.World.UsePersistentHouseDoodads)
                    HousingManager.Instance.ReconcileBoundDoodads();
                // Load plants/packs and everything else that was placed into the world by players
                doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.System);
                doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.Character);
                Logger.Info($"{doodadsSpawned} doodads loaded in {World}.");
            }
        }

        // Start timers
        var respawnThread = new Thread(CheckRespawns) { Name = $"RespawnThread_{World.Id}_{World.Template.Id}" };
        respawnThread.Start();

        _loaded = true;
    }

    private static string[] ReverseSpawnFiles(string[] spawnFiles)
    {
        if (spawnFiles is not { Length: not 0 })
        {
            return [];
        }

        var reversedFiles = new string[spawnFiles.Length];

        for (var i = 0; i < spawnFiles.Length; i++)
        {
            reversedFiles[i] = spawnFiles[spawnFiles.Length - 1 - i];
        }

        return reversedFiles;
    }

    private bool LoadDoodadSpawns(string worldPath)
    {
        DoodadSpawners.Clear();
        string[] doodadFiles;
        try
        {
            doodadFiles = Directory.GetFiles(worldPath, "doodad_spawns*.json");
        }
        catch (Exception)
        {
            return false;
        }
        doodadFiles = ReverseSpawnFiles(doodadFiles);
        foreach (var jsonFileName in doodadFiles)
        {
            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World {World} is missing {Path.GetFileName(jsonFileName)}");
                continue;
            }
            var contents = FileManager.GetFileContents(jsonFileName);
            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {jsonFileName} is empty.");
                continue;
            }
            if (JsonHelper.TryDeserializeObject(contents, out List<DoodadSpawner> spawners, out _))
            {
                var entry = 0;
                foreach (var spawner in spawners)
                {
                    entry++;
                    spawner.ParentWorld = World;

                    // Check for duplication by UnitId and Position
                    if (DoodadSpawners.Values
                        .Any(existingSpawner => existingSpawner.UnitId == spawner.UnitId &&
                                                Math.Abs(existingSpawner.Position.X - spawner.Position.X) < 0.01f &&
                                                Math.Abs(existingSpawner.Position.Y - spawner.Position.Y) < 0.01f &&
                                                Math.Abs(existingSpawner.Position.Z - spawner.Position.Z) < 0.01f
                                                ))
                    {
                        Logger.Trace($"Duplicate Doodad spawner found in {jsonFileName} (UnitId: {spawner.UnitId}, Position: {spawner.Position})");
                        continue;
                    }
                    if (!DoodadManager.Instance.Exist(spawner.UnitId))
                    {
                        Logger.Trace($"Doodad Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                        continue;
                    }
                    spawner.Id = _nextId;
                    spawner.Position.WorldId = World.Id;
                    spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(World.Template, spawner.Position.X, spawner.Position.Y);
                    spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                    spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                    spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                    if (DoodadSpawners.TryAdd(_nextId, spawner))
                    {
                        _nextId++;
                    }
                }
            }
            else
            {
                throw new GameException($"SpawnManager: Parse {jsonFileName} file");
            }
        }

        return true;
    }

    private bool LoadTransferSpawns(string worldPath)
    {
        TransferSpawners.Clear();
        string[] transferFiles;
        try
        {
            transferFiles = Directory.GetFiles(worldPath, "transfer_spawns*.json");
        }
        catch (Exception)
        {
            return false;
        }
        foreach (var jsonFileName in transferFiles)
        {
            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World {World} is missing {Path.GetFileName(jsonFileName)}");
                continue;
            }

            var contents = FileManager.GetFileContents(jsonFileName);

            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {jsonFileName} doesn't exists or is empty.");
                continue;
            }

            if (JsonHelper.TryDeserializeObject(contents, out List<TransferSpawner> spawners, out _))
            {
                var entry = 0;
                foreach (var spawner in spawners)
                {
                    entry++;
                    spawner.ParentWorld = World;
                    
                    if (!TransferGameData.Instance.Exist(spawner.UnitId))
                    {
                        Logger.Warn($"Transfer Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                        continue;
                    }

                    spawner.Id = _nextId;
                    spawner.Position.WorldId = World.Id;
                    spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(World.Template, spawner.Position.X, spawner.Position.Y);
                    spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                    spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                    spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                    if (TransferSpawners.TryAdd(_nextId, spawner))
                    {
                        _nextId++;
                    }
                }
            }
            else
            {
                throw new GameException($"SpawnManager: Parse {jsonFileName} file");
            }
        }
        return true;
    }

    /// <summary>
    /// Builds the world's gimmick spawners from the client level data.
    /// </summary>
    /// <remarks>
    /// Sourced from <c>entities.xml</c> rather than a spawn file: a lift carries template id 0 and
    /// binds to the client purely through its <c>EntityGuid</c>, so placement kept by hand drifts
    /// silently the moment the client's level is rebuilt and every movement update is then
    /// discarded client-side.
    /// </remarks>
    private bool LoadGimmickSpawns(string worldPath)
    {
        GimmickSpawners.Clear();

        var spawners = GimmickClientSpawnLoader.Load(World);
        var entry = 0;
        foreach (var spawner in spawners)
        {
            entry++;
            spawner.ParentWorld = World;
            if (spawner.UnitId != 0 && !GimmickGameData.Instance.Exist(spawner.UnitId))
            {
                Logger.Error($"Gimmick Template {spawner.UnitId} (entry {entry}) doesn't exist in world {World}");
                continue;
            }

            spawner.Id = _nextId;
            spawner.Position.WorldId = World.Id;
            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(World.Template, spawner.Position.X, spawner.Position.Y);
            if (GimmickSpawners.TryAdd(_nextId, spawner))
            {
                _nextId++;
            }
        }

        Logger.Info($"Loaded {GimmickSpawners.Count} gimmick spawners for world {World} from client level data");
        return true;
    }

    private bool LoadSlaveSpawns(string worldPath)
    {
        SlaveSpawners.Clear();
        string[] slaveFiles;
        try
        {
            slaveFiles = Directory.GetFiles(worldPath, "slave_spawns*.json");
        }
        catch (Exception)
        {
            return false;
        }
        foreach (var jsonFileName in slaveFiles)
        {
            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World {World} is missing {Path.GetFileName(jsonFileName)}");
                continue;
            }

            var contents = FileManager.GetFileContents(jsonFileName);

            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {jsonFileName} doesn't exists or is empty.");
                continue;
            }

            if (JsonHelper.TryDeserializeObject(contents, out List<SlaveSpawner> spawners, out _))
            {
                var entry = 0;
                foreach (var spawner in spawners)
                {
                    entry++;
                    if (!SlaveGameData.Instance.Exist(spawner.UnitId))
                    {
                        Logger.Warn($"Slave Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                        continue;
                    }

                    spawner.Id = _nextId;
                    spawner.World = World;
                    spawner.Position.WorldId = World.Id;
                    spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(World.Template, spawner.Position.X, spawner.Position.Y);
                    spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                    spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                    spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                    if (SlaveSpawners.TryAdd(_nextId, spawner))
                    {
                        _nextId++;
                    }
                }
            }
            else
            {
                throw new GameException($"SpawnManager: Parse {jsonFileName} file");
            }
        }
        return true;
    }

    public List<Doodad> GetPlayerDoodads(uint charId)
    {
        return PlayerDoodads.Where(d => d.OwnerId == charId).ToList();
    }

    public List<Doodad> GetAllPlayerDoodads()
    {
        return PlayerDoodads;
    }

    public void RemovePlayerDoodad(Doodad doodad)
    {
        PlayerDoodads.Remove(doodad);
    }

    public void AddPlayerDoodad(Doodad doodad)
    {
        PlayerDoodads.Add(doodad);
    }

    /// <summary>
    /// Load Persistent Doodads from the DataBase
    /// </summary>
    /// <param name="ownerTypeToSpawn">Only spawn doodads that have this ownerType</param>
    /// <param name="ownerToSpawnId">Only spawn doodads with a specific ownerId, -1 for all doodads of the given ownerType</param>
    /// <param name="useParentObject">If not null, force-set the Parent object of the loaded data</param>
    /// <param name="doSpawn">If true, also sends a Spawn() command after loading the doodad</param>
    /// <returns></returns>
    public int SpawnPersistentDoodads(DoodadOwnerType ownerTypeToSpawn, int ownerToSpawnId = -1, GameObject useParentObject = null, bool doSpawn = false)
    {
        var spawnCount = 0;
        var newCoffers = new List<Doodad>();
        using var connection = MySQL.CreateConnection();
        using (var command = connection.CreateCommand())
        {
            // Sorting required to make sure parenting doesn't produce invalid parents (normally)

            command.CommandText = "SELECT d.*, c.faction_id AS creator_faction_id FROM doodads d " +
                                  "LEFT JOIN characters c ON c.id = d.owner_id " +
                                  "AND d.owner_type IN (@CharacterOwnerType, @HousingOwnerType) " +
                                  "WHERE d.owner_type = @OwnerType";
            if (ownerToSpawnId >= 0)
                command.CommandText += " AND house_id = @OwnerId";
            command.CommandText += " ORDER BY d.plant_time";
            command.Parameters.AddWithValue("OwnerType", (byte)ownerTypeToSpawn);
            command.Parameters.AddWithValue("CharacterOwnerType", (byte)DoodadOwnerType.Character);
            command.Parameters.AddWithValue("HousingOwnerType", (byte)DoodadOwnerType.Housing);
            if (ownerToSpawnId >= 0)
                command.Parameters.AddWithValue("OwnerId", ownerToSpawnId);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var templateId = reader.GetUInt32("template_id");
                    var dbId = reader.GetUInt32("id");
                    var phaseId = reader.GetUInt32("current_phase_id");
                    var x = reader.GetFloat("x");
                    var y = reader.GetFloat("y");
                    var z = reader.GetFloat("z");
                    var roll = reader.GetFloat("roll");
                    var pitch = reader.GetFloat("pitch");
                    var yaw = reader.GetFloat("yaw");
                    var scale = reader.GetFloat("scale");
                    var plantTime = reader.GetDateTime("plant_time");
                    var growthTime = reader.GetDateTime("growth_time");
                    var phaseTime = reader.GetDateTime("phase_time");
                    var ownerId = reader.GetUInt32("owner_id");
                    var ownerType = (DoodadOwnerType)reader.GetByte("owner_type");
                    var attachPoint = (AttachPointKind)reader.GetUInt32("attach_point");
                    var itemId = reader.GetUInt64("item_id");
                    var houseId = reader.GetUInt32("house_id"); // actually DbId of the parent/owner (house, slave, etc.)
                    var parentDoodad = reader.GetUInt32("parent_doodad");
                    var itemTemplateId = reader.GetUInt32("item_template_id");
                    var itemContainerId = reader.GetUInt64("item_container_id");
                    var data = reader.GetInt32("data");
                    var farmType = (FarmType)reader.GetUInt32("farm_type");
                    var creatorFactionId = reader.IsDBNull(reader.GetOrdinal("creator_faction_id"))
                        ? FactionsEnum.Invalid
                        : (FactionsEnum)reader.GetUInt32("creator_faction_id");

                    var doodad = DoodadManager.Instance.Create(World, 0, templateId, null, true);

                    //doodad.Spawner = new DoodadSpawner();
                    //doodad.Spawner.UnitId = templateId;
                    doodad.IsPersistent = true;
                    doodad.DbId = dbId;
                    doodad.FuncGroupId = phaseId;
                    doodad.OwnerId = ownerId;
                    doodad.OwnerType = ownerType;
                    doodad.AttachPoint = attachPoint;
                    doodad.PlantTime = plantTime;
                    doodad.GrowthTime = growthTime;
                    doodad.OverridePhaseTime = phaseTime;
                    doodad.PhaseTime = phaseTime;
                    doodad.ItemId = itemId;
                    doodad.OwnerDbId = houseId;
                    doodad.SetScale(scale != 0f ? scale : 1f);
                    // Try to grab info from the actual item if it still exists
                    var sourceItem = ItemManager.Instance.GetItemByItemId(itemId);
                    doodad.ItemTemplateId = sourceItem?.TemplateId ?? itemTemplateId;
                    // Grab Ucc from its old source item
                    doodad.UccId = sourceItem?.UccId ?? 0;
                    doodad.SetData(data); // Directly assigning to Data property would trigger a .Save()
                    doodad.FarmType = farmType;

                    // Apparently this is only a reference value, so might not actually need to parent it
                    BaseUnit creator = null;
                    House owningHouse = null;
                    if (parentDoodad > 0)
                    {
                        // var pDoodad = WorldManager.Instance.GetDoodadByDbId(parentDoodad);
                        var pDoodad = PlayerDoodads.FirstOrDefault(d => d.DbId == parentDoodad);
                        if (pDoodad == null)
                        {
                            Logger.Warn($"Unable to place doodad {dbId} can't find it's parent doodad {parentDoodad}");
                        }
                        else
                        {
                            doodad.Transform.Parent = pDoodad.Transform;
                            doodad.ParentObj = pDoodad;
                            doodad.ParentObjId = pDoodad.ObjId;
                        }
                    }

                    if (houseId > 0 && doodad.ParentObjId <= 0)
                    {
                        var resolvedHouse = HousingManager.Instance.GetHouseById(doodad.OwnerDbId);
                        if (resolvedHouse == null)
                        {
                            Logger.Warn($"Unable to place doodad {dbId} can't find it's owning house {houseId}");
                        }
                        else
                        {
                            doodad.Transform.Parent = resolvedHouse.Transform;
                            doodad.ParentObj = resolvedHouse;
                            doodad.ParentObjId = resolvedHouse.ObjId;
                            owningHouse = resolvedHouse;

                            // If persistent house doodads are enabled and this doodad matches a binding
                            // from the house template, register it in AttachedDoodads so
                            // House.Spawn/Show/Hide/Delete handle it correctly.
                            if (AppConfiguration.Instance.World.UsePersistentHouseDoodads)
                            {
                                var isBoundDoodad = resolvedHouse.Template?.HousingBindingDoodad != null &&
                                    resolvedHouse.Template.HousingBindingDoodad.Any(b =>
                                        b.DoodadId == templateId && b.AttachPointId == attachPoint);
                                if (isBoundDoodad)
                                    resolvedHouse.AttachedDoodads.Add(doodad);
                            }
                        }
                    }

                    if (useParentObject != null)
                    {
                        doodad.ParentObj = useParentObject;
                        doodad.ParentObjId = useParentObject.ObjId;
                        doodad.Transform.Parent = useParentObject.Transform;
                        creator = useParentObject as BaseUnit;
                        owningHouse = useParentObject as House ?? owningHouse;
                    }

                    DoodadManager.Instance.RefreshFaction(doodad, creator, owningHouse, creatorFactionId);

                    doodad.Transform.Local.SetPosition(x, y, z);
                    doodad.Transform.Local.SetRotation(roll, pitch, yaw);

                    // Attach ItemContainer to coffer if needed
                    if (doodad is DoodadCoffer coffer)
                    {
                        if (itemContainerId > 0)
                        {
                            var itemContainer = ItemManager.Instance.GetItemContainerByDbId(itemContainerId);
                            if (itemContainer is CofferContainer cofferContainer)
                                coffer.ConfigureItemContainer(cofferContainer);
                            else
                                Logger.Error($"Unable to attach ItemContainer {itemContainerId} to DoodadCoffer, objId: {doodad.ObjId}, DbId: {doodad.DbId}");
                        }
                        else
                        {
                            Logger.Warn($"DoodadCoffer has no persistent ItemContainer assigned to it, creating new one, objId: {doodad.ObjId}, DbId: {doodad.DbId}");
                            coffer.InitializeCoffer(ownerId);
                            newCoffers.Add(coffer); // Mark for saving again later when we're done with this loop
                        }
                    }

                    if (ownerTypeToSpawn == DoodadOwnerType.Slave && useParentObject is Slave parentSlave)
                    {
                        parentSlave.AttachedDoodads.Add(doodad);
                    }

                    doodad.InitDoodad();

                    PlayerDoodads.Add(doodad);
                    spawnCount++;

                    if (doSpawn)
                        doodad.Spawn();
                }
            }
        }
        // Save Coffer Doodads that had a new ItemContainer created for them (should only happen on first run if there were already coffers placed)
        foreach (var coffer in newCoffers)
            coffer.Save();

        return spawnCount;
    }

    /// <summary>
    /// Spawns everything this world owns. NPCs are absent by design: the Zone creates them and
    /// World mirrors what it announces.
    /// </summary>
    public void SpawnAll()
    {
        if (AppConfiguration.Instance.World.SpawnDoodads)
        {
            Logger.Info("Spawning Doodads...");
            SpawnTasks.Add(Task.Run(() =>
            {
                var spawnStartTime = DateTime.UtcNow;
                Logger.Info($"Spawning {DoodadSpawners.Count} Doodads in world {World}");
                var count = 0;
                foreach (var spawner in DoodadSpawners.Values)
                {
                    // Zone takes physics ownership of each doodad World authors.
                    var doodad = spawner.Spawn(0);
                    if (doodad != null)
                        WorldIntegration.RelayCreateDoodadToZone?.Invoke(doodad);
                    count++;
                    if (count % 5000 == 0)
                        Logger.Debug($"In world {World} Doodads spawned: {count}...");
                }

                Logger.Info($"In world {World} Doodads spawned: {count} in {DateTime.UtcNow.Subtract(spawnStartTime)} ({GameService.TimeSinceStart} since server start)");

                // you have to wait for all the doodads to spawn before trying to initialize the fish schools
                FishSchoolManager.Instance.Load(World);
            }));
        }
        else
        {
            Logger.Info("Doodad spawning disabled by configuration (World.SpawnDoodads)");
        }

        if (AppConfiguration.Instance.World.SpawnTransfers)
        {
            Logger.Info("Spawning Transfers...");
            SpawnTasks.Add(Task.Run(() =>
            {
                var spawnStartTime = DateTime.UtcNow;
                Logger.Info($"Spawning {TransferSpawners.Count} Transfers in world {World}");
                var count = 0;
                foreach (var spawner in TransferSpawners.Values)
                {
                    spawner.SpawnAll();
                    count++;
                    if (count % 25 == 0)
                        Logger.Debug($"In world {World} Transfers spawned: {count}...");
                }

                Logger.Info($"In world {World} Transfers spawned: {count} in {DateTime.UtcNow.Subtract(spawnStartTime)} ({GameService.TimeSinceStart} since server start)");
            }));
        }
        else
        {
            Logger.Info("Transfer spawning disabled by configuration (World.SpawnTransfers)");
        }

        if (AppConfiguration.Instance.World.SpawnGimmicks)
        {
            Logger.Info("Spawning Gimmicks...");
            SpawnTasks.Add(Task.Run(() =>
            {
                var spawnStartTime = DateTime.UtcNow;
                Logger.Info($"Spawning {GimmickSpawners.Count} Gimmicks in world {World}");
                var count = 0;
                foreach (var spawner in GimmickSpawners.Values)
                {
                    try
                    {
                        var gimmick = spawner.Spawn(0);
                        if (gimmick == null)
                            continue;

                        var pos = gimmick.Transform.World.Position;
                        var zoneId = gimmick.Transform.ZoneId;
                        WorldIntegration.RelayGimmickCreatedToZone?.Invoke(
                            gimmick.ObjId, gimmick.TemplateId, zoneId,
                            gimmick.ModelPath ?? gimmick.Template?.ModelPath ?? "",
                            pos.X, pos.Y, pos.Z, gimmick.Scale);
                        count++;
                        if (count % 25 == 0)
                            Logger.Debug($"In world {World} Gimmicks spawned: {count}...");
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Gimmick spawn failed in world {0}", World);
                    }
                }

                Logger.Info($"In world {World} Gimmicks spawned: {count} in {DateTime.UtcNow.Subtract(spawnStartTime)} ({GameService.TimeSinceStart} since server start)");
            }));
        }
        else
        {
            Logger.Info("Gimmick spawning disabled by configuration (World.SpawnGimmicks)");
        }

        if (AppConfiguration.Instance.World.SpawnSlaves)
        {
            Logger.Info("Spawning Slaves...");
            SpawnTasks.Add(Task.Run(() =>
            {
                var spawnStartTime = DateTime.UtcNow;
                Logger.Info($"Spawning {SlaveSpawners.Count} Slaves in world {World}");
                var count = 0;
                foreach (var spawner in SlaveSpawners.Values)
                {
                    spawner.World = World;
                    spawner.Spawn(0);
                    count++;
                    if (count % 25 == 0)
                        Logger.Debug($"In world {World} Slaves spawned: {count}...");
                }

                Logger.Info($"In world {World} slaves spawned: {count} in {DateTime.UtcNow.Subtract(spawnStartTime)} ({GameService.TimeSinceStart} since server start)");
            }));
        }
        else
        {
            Logger.Info("Slave spawning disabled by configuration (World.SpawnSlaves)");
        }

        if (AppConfiguration.Instance.World.SpawnDoodads)
        {
            Logger.Info("Spawning Player Doodads asynchronously...");
            SpawnTasks.Add(Task.Run(() =>
            {
                var spawnStartTime = DateTime.UtcNow;
                if (PlayerDoodads.Count > 0)
                    Logger.Info($"Spawning {PlayerDoodads.Count} Player Doodads");
                var count = 0;
                foreach (var doodad in PlayerDoodads)
                {
                    if (doodad.Spawner == null)
                    {
                        doodad.Spawn();
                        count++;
                        if (count % 25 == 0)
                        {
                            Logger.Debug($"In world {World} player doodads spawned: {count}...");
                        }
                    }
                    else
                    {
                        if (doodad.Spawner?.Spawn(doodad.ObjId) == null)
                            Logger.Error($"Failed to spawn player doodad DbId:{doodad.DbId}, TemplateId: {doodad.TemplateId}");
                    }
                }
                Logger.Info($"In world {World} player doodads spawned: {count} in {DateTime.UtcNow.Subtract(spawnStartTime)} ({GameService.TimeSinceStart} since server start)");
            }));
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _work = false;
        }
    }

    public void AddRespawn(GameObject obj)
    {
        lock (Respawns)
        {
            Respawns.Add(obj);
        }
    }

    private void RemoveRespawn(GameObject obj)
    {
        lock (Respawns)
        {
            Respawns.Remove(obj);
        }
    }

    public void AddDespawn(GameObject obj)
    {
        lock (Despawns)
        {
            Despawns.Add(obj);
        }
    }

    private void RemoveDespawn(GameObject obj)
    {
        lock (Despawns)
        {
            Despawns.Remove(obj);
        }
    }

    private HashSet<GameObject> GetRespawnsReady()
    {
        HashSet<GameObject> temp;
        lock (Respawns)
        {
            temp = [.. Respawns];
        }

        var res = new HashSet<GameObject>();
        foreach (var npc in temp.Where(npc => npc.Respawn <= DateTime.UtcNow))
            res.Add(npc);

        return res;
    }

    private HashSet<GameObject> GetDespawnsReady()
    {
        HashSet<GameObject> temp;
        lock (Despawns)
        {
            temp = [.. Despawns];
        }

        var res = new HashSet<GameObject>();
        foreach (var item in temp.Where(item => item.Despawn <= DateTime.UtcNow))
            res.Add(item);

        return res;
    }

    /// <summary>
    /// Handles timed re-spawning and de-spawning tick
    /// </summary>
    private void CheckRespawns()
    {
        while (_work)
        {
            var respawns = GetRespawnsReady();
            if (respawns.Count > 0)
            {
                foreach (var obj in respawns)
                {
                    if (obj.Respawn >= DateTime.UtcNow)
                        continue;
                    // Mirrored NPCs respawn on the Zone's own timer; only spawners World created
                    // itself (GM commands, skill effects, dungeons) are rearmed here.
                    if (obj is Npc { Spawner: not null } npc)
                        npc.Spawner.SetSpawnScheduled(false); // in the Update() method, enable spawn

                    if (obj is Doodad doodad)
                        doodad.Spawner.Respawn(doodad);
                    if (obj is Transfer transfer)
                        transfer.Spawner.Respawn(transfer);
                    if (obj is Gimmick gimmick)
                        gimmick.Spawner.Respawn(gimmick);
                    RemoveRespawn(obj);
                }
            }

            var deSpawns = GetDespawnsReady();
            if (deSpawns.Count > 0)
            {
                foreach (var obj in deSpawns)
                {
                    if (obj.Despawn >= DateTime.UtcNow)
                        continue;
                    if (obj is Npc { Spawner: not null } npc)
                        npc.Spawner.Despawn(npc);
                    else if (obj is Npc { IsZoneMirror: true } mirrorNpc)
                    {
                        // Zone mirror bcIds come from ObjectIdManager (unit pool under max_unit).
                        WorldIntegration.RelayNpcStartDespawnToZone?.Invoke(mirrorNpc.ObjId);
                        WorldIntegration.RelayUnitRemovedToZone?.Invoke(mirrorNpc.ObjId);
                        mirrorNpc.Delete();
                        ObjectIdManager.Instance.ReleaseId(mirrorNpc.ObjId);
                        RemoveDespawn(obj);
                        continue;
                    }
                    else if (obj is Doodad { Spawner: not null } doodadWithSpawner)
                        doodadWithSpawner.Spawner.Despawn(doodadWithSpawner);
                    else if (obj is Transfer { Spawner: not null } transfer)
                        transfer.Spawner.Despawn(transfer);
                    else if (obj is Gimmick { Spawner: not null } gimmick)
                        gimmick.Spawner.Despawn(gimmick);
                    else if (obj is Slave slave) // slaves don't have a spawner, but this is used for delayed despawn of un-summoned boats
                        slave.Delete();
                    else if (obj is Doodad doodadWithNoSpawner)
                        doodadWithNoSpawner.Delete();
                    else
                        obj.Delete();

                    if (obj is Doodad or Gimmick)
                        NonUnitObjectIdManager.Instance.ReleaseId(obj.ObjId);
                    else
                        ObjectIdManager.Instance.ReleaseId(obj.ObjId);
                    RemoveDespawn(obj);
                }
            }

            // Check if any Npcs with loot need to be made public
            var makePublic = World.GetNpcsToMakePublicLooting();
            if (makePublic.Count > 0)
            {
                foreach (var npc in makePublic)
                {
                    npc.LootingContainer.MakeLootPublic();
                }
            }

            Thread.Sleep(1000);
        }
    }
    
    /// <summary>
    /// Gets all Spawners.
    /// </summary>
    public Dictionary<uint, List<NpcSpawner>> GetAllSpawners()
    {
        Dictionary<uint, List<NpcSpawner>> temp;
        lock (NpcSpawners)
        {
            temp = NpcSpawners.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToList()
            );
        }

        return temp;
    }

    public List<NpcSpawner> GetNpcSpawner(uint spawnerId)
    {
        var ret = new List<NpcSpawner>();

        foreach (var (_, spawners) in NpcEventSpawners)
        {
            foreach (var spawner in spawners)
            {
                if (spawner.Id != spawnerId) { continue; }
                spawner.Template.Npcs[^1].MemberId = spawner.UnitId;
                spawner.Template.Npcs[^1].UnitId = spawner.UnitId;
                spawner.Template.Npcs[^1].MemberType = "Npc";
                ret.Add(spawner);
            }
        }

        return ret;
    }
    
    /// <summary>
    /// Creates a new Npc spawner at unit location
    /// </summary>
    /// <param name="unitId"></param>
    /// <param name="unit"></param>
    /// <returns></returns>
    public NpcSpawner GetNpcSpawner(uint unitId, BaseUnit unit)
    {
        lock (_lockSpawner)
        {
            var spawner = new NpcSpawner { ParentWorld = World };
            var npcSpawnersIds = NpcGameData.Instance.GetSpawnerIds(unitId);
            if (npcSpawnersIds == null)
            {
                spawner.UnitId = unitId;
                spawner.Id = NonUnitObjectIdManager.Instance.GetNextId();
                spawner.NpcSpawnerIds = [spawner.Id];
                spawner.Template = new NpcSpawnerTemplate(spawner.Id);
                spawner.Template.Npcs[0].MemberId = spawner.UnitId;
                spawner.Template.Npcs[0].UnitId = spawner.UnitId;
                spawner.Template.Npcs[0].MemberType = "Npc";
            }
            else
            {
                spawner.UnitId = unitId;
                spawner.Id = npcSpawnersIds[0];
                spawner.NpcSpawnerIds = [spawner.Id];
                spawner.Template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawner.Id);
                if (spawner.Template == null)
                {
                    return null;
                }

                spawner.Template.Npcs = [];
                var nsn = NpcGameData.Instance.GetNpcSpawnerNpc(spawner.Id);
                if (nsn == null)
                {
                    return null;
                }

                spawner.Template.Npcs.Add(nsn);
                spawner.Template.Npcs[0].MemberId = spawner.UnitId;
                spawner.Template.Npcs[0].UnitId = spawner.UnitId;
            }

            spawner.Position = new WorldSpawnPosition
            {
                WorldId = unit.Transform.WorldId,
                ZoneId = unit.Transform.ZoneId,
                X = unit.Transform.World.Position.X,
                Y = unit.Transform.World.Position.Y,
                Z = unit.Transform.World.Position.Z,
                Yaw = unit.Transform.World.Rotation.Z,
                Pitch = 0,
                Roll = 0
            };

            return spawner;
        }
    }

    public bool CloneNpcEventSpawners(byte from, byte to)
    {
        NpcEventSpawners.TryGetValue(from, out var value);
        return NpcEventSpawners.TryAdd(to, value);
    }

    public bool RemoveNpcEventSpawners(byte from)
    {
        return NpcEventSpawners.Remove(from, out _);
    }

    /// <summary>
    /// Gets a list of all Treasure Chests in the world that can be dug up
    /// </summary>
    /// <returns></returns>
    public List<DoodadSpawner> GetTreasureChestDoodadSpawners()
    {
        var chestTemplateIds = DoodadManager.Instance.GetTreasureChestTemplateIds();
        if (chestTemplateIds == null)
            return [];
        return DoodadSpawners.Values.Where(ds => chestTemplateIds.Contains(ds.RespawnDoodadTemplateId) || chestTemplateIds.Contains(ds.UnitId)).ToList();
    }

    public void DeleteAllSpawners()
    {
        // First remove all owned spawns and disable the spawner
        // Npc
        foreach (var npcSpawners in NpcSpawners.Values.SelectMany(x => x).ToList())
        {
            foreach (var npc in npcSpawners.SpawnedNpcs.Values.SelectMany(n => n).ToList())
            {
                npc.UnregisterNpcEvents();
                npcSpawners.Despawn(npc);
            }
            npcSpawners.SpawnedNpcs.Clear();
            npcSpawners.ParentWorld = null;
        }
        NpcSpawners.Clear();

        // Doodad
        foreach (var doodadSpawner in DoodadSpawners.Values.ToList())
        {
            foreach (var doodad in doodadSpawner._spawned.ToList())
            {
                doodadSpawner.Despawn(doodad);
            }
            doodadSpawner._spawned.Clear();
            doodadSpawner.ParentWorld = null;
        }
        DoodadSpawners.Clear();
        
        // Gimmick
        GimmickSpawners.Clear();
        foreach (var (_ , gimmick) in World.GimmickManager._activeGimmicks.ToList())
        {
            gimmick.Delete();
        }
    }
}
