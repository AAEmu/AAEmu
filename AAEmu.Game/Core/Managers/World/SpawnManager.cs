using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Exceptions;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
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
    private HashSet<GameObject> _respawns = [];
    private HashSet<GameObject> _despawns = [];

    private Dictionary<uint, List<NpcSpawner>> _npcSpawners = []; // (idx, List<NpcSpawner>)
    private Dictionary<uint, List<NpcSpawner>> _npcEventSpawners = []; // (idx, List<NpcSpawner>)
    private Dictionary<uint, DoodadSpawner> _doodadSpawners = [];
    private Dictionary<uint, TransferSpawner> _transferSpawners = [];
    private Dictionary<uint, GimmickSpawner> _gimmickSpawners = [];
    private Dictionary<uint, SlaveSpawner> _slaveSpawners = [];
    private List<Doodad> _playerDoodads = [];

    private uint _nextId = 1u;
    private uint _fakeSpawnerId = 9000001u;


    private int _currentSpawnerIndex; // Index of the current spawner
    private List<NpcSpawner> _currentSpawners = []; // List of spawners for the current world instance

    /// <summary>
    /// Handle spawner ticks
    /// </summary>
    /// <param name="delta"></param>
    private void Update(TimeSpan delta)
    {
        // If the spawner list is empty, initialize it
        if (_currentSpawners.Count == 0)
        {
            _currentSpawners = _npcSpawners.Values.SelectMany(x => x).ToList();
        }

        var stopwatch = Stopwatch.StartNew();

        var c = 0;
        var startIndex = _currentSpawnerIndex;
        // Continue executing the loop until the time expires
        for (; _currentSpawnerIndex < _currentSpawners.Count; _currentSpawnerIndex++)
        {
            var spawner = _currentSpawners[_currentSpawnerIndex];

            if (spawner.Template == null)
            {
                Logger.Warn($"Templates not found for Npc templateId {spawner.SpawnerId}:{spawner.UnitId} in world {World}");
            }
            else
            {
                var innerStopwatch = Stopwatch.StartNew();
                try
                {
                    spawner.Update();
                }
                finally
                {
                    innerStopwatch.Stop();
                    // Logger.Trace($"Update for spawner {spawner.SpawnerId}:{spawner.UnitId} took {innerStopwatch.ElapsedMilliseconds} ms.");
                }
            }

            c++;
            // If the execution time has exceeded the allowed threshold, abort the loop
            if (stopwatch.Elapsed > TimeSpan.FromMilliseconds(50)) // Threshold 50 ms
            {
                Logger.Debug($"Updated {c}/{_currentSpawners.Count} spawners idx={startIndex}->{_currentSpawnerIndex}. Update loop interrupted due to time limit. Elapsed time: {stopwatch.ElapsedMilliseconds} ms.");
                break;
            }
        }

        // Logger.Info($"idx={startIndex} -> {_currentSpawnerIndex} / {_currentSpawners.Count}. Update loop finished: {stopwatch.ElapsedMilliseconds} ms.");

        // If the loop is complete, reset the index and the list
        if (_currentSpawnerIndex >= _currentSpawners.Count)
        {
            _currentSpawnerIndex = 0;
            _currentSpawners.Clear();
        }
    }

    /// <summary>
    /// Adds an NPC spawner to the manager.
    /// </summary>
    public void AddNpcSpawner(NpcSpawner npcSpawner)
    {
        if (npcSpawner.NpcSpawnerIds is [0])
            npcSpawner.NpcSpawnerIds = [];

        // check for manually entered NpcSpawnerId
        if (npcSpawner.NpcSpawnerIds.Count == 0)
        {
            var npcSpawnerIds = NpcGameData.Instance.GetSpawnerIds(npcSpawner.UnitId);
            var spawners = new List<NpcSpawner>();
            if (npcSpawnerIds == null)
            {
                Logger.Trace($"SpawnerIds for Npc={npcSpawner.UnitId} doesn't exist");
                Logger.Trace($"Generate Spawner for Npc={npcSpawner.UnitId}...");
                var id = _fakeSpawnerId;
                npcSpawner.ParentWorld = World;
                npcSpawner.NpcSpawnerIds.Add(id);
                npcSpawner.Id = id;
                var tmpTemplate = NpcGameData.Instance.GetNpcSpawnerTemplate(1); // id=1 Test Warrior
                npcSpawner.Template = Helpers.Clone(tmpTemplate);
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
                _fakeSpawnerId++;
            }
            else
            {
                foreach (var id in npcSpawnerIds)
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

            _npcSpawners.TryAdd(_nextId, spawners);
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
            _npcEventSpawners.TryAdd(_nextId, spawners);
            _nextId++;
        }
    }

    /// <summary>
    /// Spawn all Npcs for this world template into this instance
    /// </summary>
    private void SpawnAllNpcs()
    {
        Logger.Info($"Spawning {_npcSpawners.Count} NPC spawners in world {World}");
        var count = 0;
        foreach (var spawners in _npcSpawners.Values)
        {
            foreach (var spawner in spawners)
            {
                if (spawner.Template == null)
                {
                    Logger.Warn($"Templates not found for Npc templateId {spawner.UnitId} in world {World}");
                }
                else
                {
                    spawner.Update();
                    count++;
                    if (count % 5000 == 0)
                    {
                        Logger.Debug($"{count} NPC spawners spawned in world {World}");
                    }
                }
            }
        }
        Logger.Info($"{count} NPC spawners spawned in world {World}");

        // Controls all spawners in the instance, updating their state and invoking spavin methods.
        TickManager.Instance.OnTick.Subscribe(Update, TimeSpan.FromSeconds(1));
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
                    //npc.Spawner.Despawn(npc);
                    npc.Spawner.DecreaseCount(npc);
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

        lock (_respawns) _respawns = [];
        lock (_despawns) _despawns = [];
        _npcSpawners = [];
        _npcEventSpawners = [];
        _doodadSpawners = [];
        _transferSpawners = [];
        _gimmickSpawners = [];
        _slaveSpawners = [];
        _playerDoodads = [];

        Logger.Info($"Loading spawn data for {World} ...");
        var worldPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", World.Template.Name);

        // Load NPC Spawns
        Logger.Debug($"Loading NPC spawn data for {World} ...");
        _ = LoadNpcSpawns(worldPath);

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
            Logger.Info($"Loading persistent doodads for {World}");
            var doodadsSpawned = 0;

            // Load furniture
            doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.Housing);
            // Load plants/packs and everything else that was placed into the world by players
            doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.System);
            doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.Character);
            Logger.Info($"{doodadsSpawned} doodads loaded in {World}.");
        }

        // Start timers
        var respawnThread = new Thread(CheckRespawns) { Name = $"RespawnThread_{World.Id}_{World.Template.Id}" };
        respawnThread.Start();

        _loaded = true;
    }

    private bool LoadNpcSpawns(string worldPath)
    {
        string[] npcFiles;
        try
        {
            npcFiles = Directory.GetFiles(worldPath, "npc_spawns*.json");
        }
        catch (Exception)
        {
            return false;
        }
        npcFiles = ReverseSpawnFiles(npcFiles);
        foreach (var jsonFileName in npcFiles)
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
            if (JsonHelper.TryDeserializeObject(contents, out List<NpcSpawner> npcSpawnersFromFile, out _))
            {
                var entry = 0;
                foreach (var npcSpawnerFromFile in npcSpawnersFromFile)
                {
                    entry++;
                    npcSpawnerFromFile.ParentWorld = World;

                    // Check for duplication by UnitId and Position
                    if (_npcSpawners.Values.SelectMany(spawners => spawners)
                        .Any(spawner => spawner.UnitId == npcSpawnerFromFile.UnitId &&
                                        Math.Abs(spawner.Position.X - npcSpawnerFromFile.Position.X) < 2f &&
                                        Math.Abs(spawner.Position.Y - npcSpawnerFromFile.Position.Y) < 2f
                                        ))
                    {
                        Logger.Trace($"Duplicate NPC spawner found in {jsonFileName} (UnitId: {npcSpawnerFromFile.UnitId}, Position: {npcSpawnerFromFile.Position})");
                        continue;
                    }
                    if (!NpcManager.Instance.Exist(npcSpawnerFromFile.UnitId))
                    {
                        Logger.Trace($"Npc Template {npcSpawnerFromFile.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                        continue;
                    }
                    npcSpawnerFromFile.Position.WorldId = World.Id;
                    npcSpawnerFromFile.Position.ZoneId = WorldManager.Instance.GetZoneId(World.Template, npcSpawnerFromFile.Position.X, npcSpawnerFromFile.Position.Y);
                    npcSpawnerFromFile.Position.Yaw = npcSpawnerFromFile.Position.Yaw.DegToRad();
                    npcSpawnerFromFile.Position.Pitch = npcSpawnerFromFile.Position.Pitch.DegToRad();
                    npcSpawnerFromFile.Position.Roll = npcSpawnerFromFile.Position.Roll.DegToRad();
                    AddNpcSpawner(npcSpawnerFromFile);
                }
            }
            else
            {
                throw new GameException($"SpawnManager: Parse {jsonFileName} file");
            }
        }
        return true;
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
        _doodadSpawners = new Dictionary<uint, DoodadSpawner>();
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
                    if (_doodadSpawners.Values
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
                    if (_doodadSpawners.TryAdd(_nextId, spawner))
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
        _transferSpawners = new Dictionary<uint, TransferSpawner>();
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
                    
                    if (!TransferManager.Instance.Exist(spawner.UnitId))
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
                    if (_transferSpawners.TryAdd(_nextId, spawner))
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

    private bool LoadGimmickSpawns(string worldPath)
    {
        _gimmickSpawners = new Dictionary<uint, GimmickSpawner>();
        string[] gimmickFiles;
        try
        {
            gimmickFiles = Directory.GetFiles(worldPath, "gimmick_spawns*.json");
        }
        catch (Exception)
        {
            return false;
        }
        foreach (var jsonFileName in gimmickFiles)
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

            if (JsonHelper.TryDeserializeObject(contents, out List<GimmickSpawner> spawners, out var gimmickException))
            {
                var entry = 0;
                foreach (var spawner in spawners)
                {
                    entry++;
                    spawner.ParentWorld = World;
                    if (spawner.UnitId != 0 && !GimmickGameData.Instance.Exist(spawner.UnitId))
                    {
                        Logger.Error($"Gimmick Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                        continue;
                    }

                    spawner.Id = _nextId;
                    spawner.Position.WorldId = World.Id;
                    spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(World.Template, spawner.Position.X, spawner.Position.Y);
                    if (_gimmickSpawners.TryAdd(_nextId, spawner))
                    {
                        _nextId++;
                    }
                }
            }
            else
            {
                throw gimmickException;// new GameException($"SpawnManager: Parse {jsonFileName} file");
            }
        }
        return true;
    }

    private bool LoadSlaveSpawns(string worldPath)
    {
        _slaveSpawners = new Dictionary<uint, SlaveSpawner>();
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
                    if (_slaveSpawners.TryAdd(_nextId, spawner))
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
        return _playerDoodads.Where(d => d.OwnerId == charId).ToList();
    }

    public List<Doodad> GetAllPlayerDoodads()
    {
        return _playerDoodads;
    }

    public void RemovePlayerDoodad(Doodad doodad)
    {
        _playerDoodads.Remove(doodad);
    }

    public void AddPlayerDoodad(Doodad doodad)
    {
        _playerDoodads.Add(doodad);
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

            command.CommandText = "SELECT * FROM doodads WHERE owner_type = @OwnerType";
            if (ownerToSpawnId >= 0)
                command.CommandText += " AND house_id = @OwnerId";
            command.CommandText += " ORDER BY `plant_time`";
            command.Parameters.AddWithValue("OwnerType", (byte)ownerTypeToSpawn);
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
                    if (parentDoodad > 0)
                    {
                        // var pDoodad = WorldManager.Instance.GetDoodadByDbId(parentDoodad);
                        var pDoodad = _playerDoodads.FirstOrDefault(d => d.DbId == parentDoodad);
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

                    if ((houseId > 0) && (doodad.ParentObjId <= 0))
                    {
                        var owningHouse = HousingManager.Instance.GetHouseById(doodad.OwnerDbId);
                        if (owningHouse == null)
                        {
                            Logger.Warn($"Unable to place doodad {dbId} can't find it's owning house {houseId}");
                        }
                        else
                        {
                            doodad.Transform.Parent = owningHouse.Transform;
                            doodad.ParentObj = owningHouse;
                            doodad.ParentObjId = owningHouse.ObjId;
                        }
                    }

                    if (useParentObject != null)
                    {
                        doodad.ParentObj = useParentObject;
                        doodad.ParentObjId = useParentObject.ObjId;
                        doodad.Transform.Parent = useParentObject.Transform;
                    }

                    doodad.Transform.Local.SetPosition(x, y, z);
                    doodad.Transform.Local.SetRotation(roll, pitch, yaw);

                    // Attach ItemContainer to coffer if needed
                    if (doodad is DoodadCoffer coffer)
                    {
                        if (itemContainerId > 0)
                        {
                            var itemContainer = ItemManager.Instance.GetItemContainerByDbId(itemContainerId);
                            if (itemContainer is CofferContainer cofferContainer)
                                coffer.ItemContainer = cofferContainer;
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

                    if ((ownerTypeToSpawn == DoodadOwnerType.Slave) && (useParentObject is Slave parentSlave))
                    {
                        parentSlave.AttachedDoodads.Add(doodad);
                    }

                    doodad.InitDoodad();

                    _playerDoodads.Add(doodad);
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

    public void SpawnAll()
    {
        Logger.Info("Spawning NPCs...");
        Task.Run(SpawnAllNpcs);

        Logger.Info("Spawning Doodads...");
        Task.Run(() =>
        {
            Logger.Info($"Spawning {_doodadSpawners.Count} Doodads in world {World}");
            var count = 0;
            foreach (var spawner in _doodadSpawners.Values)
            {
                spawner.Spawn(0);
                count++;
                if (count % 5000 == 0)
                {
                    Logger.Debug($"In world {World} Doodads spawned: {count}...");
                }
            }

            Logger.Info($"In world {World} Doodads spawned: {count}");

            // you have to wait for all the doodads to spawn before trying to initialize the fish schools
            FishSchoolManager.Instance.Load(World);
        });

        Logger.Info("Spawning Transfers...");
        Task.Run(() =>
        {
            Logger.Info($"Spawning {_transferSpawners.Count} Transfers in world {World}");
            var count = 0;
            foreach (var spawner in _transferSpawners.Values)
            {
                spawner.SpawnAll();
                count++;
                if (count % 25 == 0)
                {
                    Logger.Debug($"In world {World} Transfers spawned: {count}...");
                }
            }

            Logger.Info($"In world {World} Transfers spawned: {count}");
        });

        Logger.Info("Spawning Gimmicks...");
        Task.Run(() =>
        {
            Logger.Info($"Spawning {_gimmickSpawners.Count} Gimmicks in world {World}");
            var count = 0;
            foreach (var spawner in _gimmickSpawners.Values)
            {
                spawner.Spawn(0);
                count++;
                if (count % 25 == 0)
                {
                    Logger.Debug($"In world {World} Gimmicks spawned: {count}...");
                }
            }

            Logger.Info($"In world {World} Gimmicks spawned: {count}");
        });

        Logger.Info("Spawning Slaves...");
        Task.Run(() =>
        {
            Logger.Info($"Spawning {_slaveSpawners.Count} Slaves in world {World}");
            var count = 0;
            foreach (var spawner in _slaveSpawners.Values)
            {
                spawner.World = World;
                spawner.Spawn(0);
                count++;
                if (count % 25 == 0)
                {
                    Logger.Debug($"In world {World} Slaves spawned: {count}...");
                }
            }

            Logger.Info($"In world {World} slaves spawned: {count}");
        });

        Logger.Info("Spawning Player Doodads asynchronously...");
        Task.Run(() =>
        {
            if (_playerDoodads.Count > 0)
                Logger.Info($"Spawning {_playerDoodads.Count} Player Doodads");
            var count = 0;
            foreach (var doodad in _playerDoodads)
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
            Logger.Info($"In world {World} player doodads spawned: {count}");
        });
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
        lock (_respawns)
        {
            _respawns.Add(obj);
        }
    }

    private void RemoveRespawn(GameObject obj)
    {
        lock (_respawns)
        {
            _respawns.Remove(obj);
        }
    }

    public void AddDespawn(GameObject obj)
    {
        lock (_despawns)
        {
            _despawns.Add(obj);
        }
    }

    private void RemoveDespawn(GameObject obj)
    {
        lock (_despawns)
        {
            _despawns.Remove(obj);
        }
    }

    private HashSet<GameObject> GetRespawnsReady()
    {
        HashSet<GameObject> temp;
        lock (_respawns)
        {
            temp = [.. _respawns];
        }

        var res = new HashSet<GameObject>();
        foreach (var npc in temp.Where(npc => npc.Respawn <= DateTime.UtcNow))
            res.Add(npc);

        return res;
    }

    private HashSet<GameObject> GetDespawnsReady()
    {
        HashSet<GameObject> temp;
        lock (_despawns)
        {
            temp = [.. _despawns];
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
                    if (obj is Npc npc)
                        npc.Spawner.Respawn(npc);
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


    public List<NpcSpawner> GetNpcSpawner(uint spawnerId)
    {
        var ret = new List<NpcSpawner>();

        foreach (var (_, spawners) in _npcEventSpawners)
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
            var spawner = new NpcSpawner();
            spawner.ParentWorld = World;
            var npcSpawnersIds = NpcGameData.Instance.GetSpawnerIds(unitId);
            if (npcSpawnersIds == null)
            {
                spawner.UnitId = unitId;
                spawner.Id = ObjectIdManager.Instance.GetNextId();
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
        _npcEventSpawners.TryGetValue(from, out var value);
        return _npcEventSpawners.TryAdd(to, value);
    }

    public bool RemoveNpcEventSpawners(byte from)
    {
        return _npcEventSpawners.Remove(from, out _);
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
        return _doodadSpawners.Values.Where(ds => chestTemplateIds.Contains(ds.RespawnDoodadTemplateId) || chestTemplateIds.Contains(ds.UnitId)).ToList();
    }

    public void DeleteAllSpawners()
    {
        // First remove all owned spawns and disable the spawner
        // Npc
        foreach (var npcSpawners in _npcSpawners.Values.SelectMany(x => x).ToList())
        {
            foreach (var npc in npcSpawners.SpawnedNpcs.Values.SelectMany(n => n).ToList())
            {
                npc.UnregisterNpcEvents();
                npcSpawners.Despawn(npc);
            }
            npcSpawners.SpawnedNpcs.Clear();
            npcSpawners.ParentWorld = null;
        }
        _npcSpawners.Clear();

        // Doodad
        foreach (var doodadSpawner in _doodadSpawners.Values.ToList())
        {
            foreach (var doodad in doodadSpawner._spawned.ToList())
            {
                doodadSpawner.Despawn(doodad);
            }
            doodadSpawner._spawned.Clear();
            doodadSpawner.ParentWorld = null;
        }
        _doodadSpawners.Clear();
        
        // Gimmick
        _gimmickSpawners.Clear();
        foreach (var (_ , gimmick) in World.GimmickManager._activeGimmicks.ToList())
        {
            gimmick.Delete();
        }
    }
}
