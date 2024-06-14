using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

namespace AAEmu.Game.Core.Managers.World;

public class SpawnManager : Singleton<SpawnManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;

    private bool _work = true;
    private object _lock = new();
    private object _lockSpawner = new();
    private HashSet<GameObject> _respawns;
    private HashSet<GameObject> _despawns;

    private Dictionary<byte, Dictionary<uint, List<NpcSpawner>>> _npcSpawners; // worldId (idx, List<NpcSpawner>)
    private Dictionary<byte, Dictionary<uint, List<NpcSpawner>>> _npcEventSpawners; // worldId (idx, List<NpcSpawner>)
    private Dictionary<byte, Dictionary<uint, DoodadSpawner>> _doodadSpawners;
    private Dictionary<byte, Dictionary<uint, TransferSpawner>> _transferSpawners;
    private Dictionary<byte, Dictionary<uint, GimmickSpawner>> _gimmickSpawners;
    private Dictionary<byte, Dictionary<uint, SlaveSpawner>> _slaveSpawners;
    private List<Doodad> _playerDoodads;

    private uint _nextId = 1u;
    private uint _fakeSpawnerId = 9000001u;

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
                Logger.Warn($"SpawnerIds for Npc={npcSpawner.UnitId} doesn't exist");
                Logger.Warn($"Generate Spawner for Npc={npcSpawner.UnitId}...");
                //var fakeSpawner = GetNpcSpawner(npcSpawner.UnitId, npcSpawner.Position);
                //var id = ObjectIdManager.Instance.GetNextId();
                var id = _fakeSpawnerId;
                npcSpawner.NpcSpawnerIds.Add(id);
                npcSpawner.Id = id;
                var tmpTemplate = NpcGameData.Instance.GetNpcSpawnerTemplate(1); // id=1 Test Warrior
                npcSpawner.Template = Helpers.Clone(tmpTemplate);
                npcSpawner.Template.Id = id;

                var tmpNpc = new NpcSpawnerNpc(); //Helpers.Clone(npcSpawner.Template.Npcs[i]); // сделаем копию
                tmpNpc.Position = npcSpawner.Position;
                tmpNpc.MemberId = npcSpawner.UnitId;
                tmpNpc.Id = id;
                tmpNpc.MemberType = "Npc";
                tmpNpc.Weight = 1f;
                tmpNpc.NpcSpawnerTemplateId = id;
                npcSpawner.Template.Npcs = new List<NpcSpawnerNpc> { tmpNpc }; // заменим на копию
                NpcGameData.Instance.AddNpcSpawnerNpc(tmpNpc); // добавим в базу сведения о фейковом Npc
                NpcGameData.Instance.AddMemberAndSpawnerTemplateIds(tmpNpc);

                NpcGameData.Instance.AddNpcSpawner(npcSpawner.Template); // добавим в базу Template по фейковому спавнеру
                _fakeSpawnerId++;
            }
            else
            {
                // TODO добавил список спавнеров // added a list of spawners
                var pattern = $@"\b{Regex.Escape(npcSpawner.UnitId.ToString())}\b";
                var regex = new Regex(pattern);
                foreach (var id in npcSpawnerIds)
                {
                    // в template.Name обычно должно присутствовать templateId для нашего Npc, по нему будем брать нужный spawnerId
                    // in template.Name there should usually be a templateId for our Npc, we will use it to take the required spawnerId 
                    var template = NpcGameData.Instance.GetNpcSpawnerTemplate(id);
                    var containsId = regex.IsMatch(template.Name);
                    if (containsId)
                    {
                        npcSpawner.NpcSpawnerIds.Add(id);
                        npcSpawner.Id = id;
                        npcSpawner.Template = template;
                        foreach (var n in npcSpawner.Template.Npcs)
                        {
                            n.Position = npcSpawner.Position;
                        }
                    }
                }

                if (npcSpawner.Id == 0 && npcSpawnerIds.Count == 1)
                {
                    var id = npcSpawnerIds[0];
                    npcSpawner.NpcSpawnerIds.Add(id);
                    npcSpawner.Id = id;
                    npcSpawner.Template = NpcGameData.Instance.GetNpcSpawnerTemplate(id);
                    foreach (var n in npcSpawner.Template.Npcs)
                    {
                        n.Position = npcSpawner.Position;
                    }
                }
            }
            spawners.Add(npcSpawner);
            _npcSpawners[(byte)npcSpawner.Position.WorldId].Add(_nextId, spawners);
            _nextId++; //we'll renumber
        }
        else
        {
            // Load NPC Spawns for Events
            var spawners = new List<NpcSpawner>();
            foreach (var id in npcSpawner.NpcSpawnerIds)
            {
                npcSpawner.Id = id;
                npcSpawner.Template = new NpcSpawnerTemplate(id, npcSpawner.UnitId);
                foreach (var n in npcSpawner.Template.Npcs)
                {
                    n.Position = npcSpawner.Position;
                }
            }
            spawners.Add(npcSpawner);
            _npcEventSpawners[(byte)npcSpawner.Position.WorldId].Add(_nextId, spawners);
            _nextId++; //we'll renumber
        }
    }

    internal void SpawnAllNpcs(byte worldId)
    {
        Logger.Info($"Spawning {_npcSpawners[worldId].Count} NPC spawners in world {worldId}");
        var count = 0;
        foreach (var spawners in _npcSpawners[worldId].Values)
        {
            foreach (var spawner in spawners)
            {
                if (spawner.Template == null)
                {
                    Logger.Warn($"Templates not found for Npc templateId {spawner.UnitId} in world {worldId}");
                }
                else
                {
                    spawner.SpawnAll(true);
                    count++;
                    if (count % 5000 == 0)
                    {
                        Logger.Info($"{count} NPC spawners spawned in world {worldId}");
                    }
                }
            }
        }
        Logger.Info($"{count} NPC spawners spawned in world {worldId}");
    }

    public void Load()
    {
        if (_loaded)
            return;

        _respawns = new HashSet<GameObject>();
        _despawns = new HashSet<GameObject>();
        _npcSpawners = new Dictionary<byte, Dictionary<uint, List<NpcSpawner>>>();
        _npcEventSpawners = new Dictionary<byte, Dictionary<uint, List<NpcSpawner>>>();
        _doodadSpawners = new Dictionary<byte, Dictionary<uint, DoodadSpawner>>();
        _transferSpawners = new Dictionary<byte, Dictionary<uint, TransferSpawner>>();
        _gimmickSpawners = new Dictionary<byte, Dictionary<uint, GimmickSpawner>>();
        _slaveSpawners = new Dictionary<byte, Dictionary<uint, SlaveSpawner>>();
        _playerDoodads = new List<Doodad>();

        var worlds = WorldManager.Instance.GetWorlds();
        foreach (var world in worlds)
        {
            _npcSpawners.Add((byte)world.Id, new Dictionary<uint, List<NpcSpawner>>());
            _npcEventSpawners.Add((byte)world.Id, new Dictionary<uint, List<NpcSpawner>>());
            _doodadSpawners.Add((byte)world.Id, new Dictionary<uint, DoodadSpawner>());
            _transferSpawners.Add((byte)world.Id, new Dictionary<uint, TransferSpawner>());
            _gimmickSpawners.Add((byte)world.Id, new Dictionary<uint, GimmickSpawner>());
            _slaveSpawners.Add((byte)world.Id, new Dictionary<uint, SlaveSpawner>());
        }

        Logger.Info("Loading spawns...");
        foreach (var world in worlds)
        {
            var doodadSpawners = new Dictionary<uint, DoodadSpawner>();
            var transferSpawners = new Dictionary<uint, TransferSpawner>();
            var gimmickSpawners = new Dictionary<uint, GimmickSpawner>();
            var slaveSpawners = new Dictionary<uint, SlaveSpawner>();
            var worldPath = Path.Combine(FileManager.AppPath, "Data", "Worlds", world.Name);

            // Load NPC Spawns
            var jsonFileName = Path.Combine(worldPath, "npc_spawns.json");

            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
            }
            else
            {
                var contents = FileManager.GetFileContents(jsonFileName);

                if (string.IsNullOrWhiteSpace(contents))
                    Logger.Warn($"File {jsonFileName} is empty.");
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<NpcSpawner> npcSpawnersFromFile, out _))
                    {
                        var entry = 0;
                        foreach (var npcSpawnerFromFile in npcSpawnersFromFile)
                        {
                            entry++;
                            if (!NpcManager.Instance.Exist(npcSpawnerFromFile.UnitId))
                            {
                                Logger.Warn($"Npc Template {npcSpawnerFromFile.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                continue; // TODO ... so mb warn here?
                            }

                            npcSpawnerFromFile.Position.WorldId = world.Id;
                            npcSpawnerFromFile.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, npcSpawnerFromFile.Position.X, npcSpawnerFromFile.Position.Y);
                            // Convert degrees from the file to radians for use
                            npcSpawnerFromFile.Position.Yaw = npcSpawnerFromFile.Position.Yaw.DegToRad();
                            npcSpawnerFromFile.Position.Pitch = npcSpawnerFromFile.Position.Pitch.DegToRad();
                            npcSpawnerFromFile.Position.Roll = npcSpawnerFromFile.Position.Roll.DegToRad();
                            // проверка наличия введенного вручную идентификатора NpcSpawnerId

                            AddNpcSpawner(npcSpawnerFromFile);
                        }
                    }
                    else
                        throw new GameException($"SpawnManager: Parse {jsonFileName} file");
                }
            }

            // Load Doodad spawns
            jsonFileName = Path.Combine(worldPath, "doodad_spawns.json");

            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
            }
            else
            {
                var contents = FileManager.GetFileContents(jsonFileName);

                if (string.IsNullOrWhiteSpace(contents))
                    Logger.Warn($"File {jsonFileName} is empty.");
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<DoodadSpawner> spawners, out _))
                    {
                        var entry = 0;
                        foreach (var spawner in spawners)
                        {
                            entry++;
                            if (!DoodadManager.Instance.Exist(spawner.UnitId))
                            {
                                Logger.Warn($"Doodad Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                continue; // TODO ... so mb warn here?
                            }
                            spawner.Id = _nextId;
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            // Convert degrees from the file to radians for use
                            spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                            spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                            spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                            if (doodadSpawners.TryAdd(_nextId, spawner))
                            {
                                _nextId++;
                            }
                        }
                    }
                    else
                        throw new GameException($"SpawnManager: Parse {jsonFileName} file");
                }
            }

            // Load Transfers
            jsonFileName = Path.Combine(worldPath, "transfer_spawns.json");

            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
            }
            else
            {
                var contents = FileManager.GetFileContents(jsonFileName);

                if (string.IsNullOrWhiteSpace(contents))
                {
                    Logger.Warn($"File {jsonFileName} doesn't exists or is empty.");
                }
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<TransferSpawner> spawners, out _))
                    {
                        var entry = 0;
                        foreach (var spawner in spawners)
                        {
                            entry++;
                            if (!TransferManager.Instance.Exist(spawner.UnitId))
                            {
                                Logger.Warn($"Transfer Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                continue; // TODO ... so mb warn here?
                            }
                            spawner.Id = _nextId;
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            // Convert degrees from the file to radians for use
                            spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                            spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                            spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                            if (transferSpawners.TryAdd(_nextId, spawner))
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
            }

            // Load Gimmicks
            jsonFileName = Path.Combine(worldPath, "gimmick_spawns.json");

            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
            }
            else
            {
                var contents = FileManager.GetFileContents(jsonFileName);

                if (string.IsNullOrWhiteSpace(contents))
                {
                    Logger.Warn($"File {jsonFileName} doesn't exists or is empty.");
                }
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<GimmickSpawner> spawners, out _))
                    {
                        var entry = 0;
                        foreach (var spawner in spawners)
                        {
                            entry++;
                            if (spawner.UnitId != 0 && !GimmickManager.Instance.Exist(spawner.UnitId))
                            {
                                Logger.Warn($"Gimmick Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                continue; // TODO ... so mb warn here?
                            }
                            spawner.Id = _nextId;
                            //spawner.UnitId = 0; // EntityGuid is used for elevators
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            if (gimmickSpawners.TryAdd(_nextId, spawner))
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
            }

            // Load Slaves
            jsonFileName = Path.Combine(worldPath, "slave_spawns.json");

            if (!File.Exists(jsonFileName))
            {
                Logger.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
            }
            else
            {
                var contents = FileManager.GetFileContents(jsonFileName);

                if (string.IsNullOrWhiteSpace(contents))
                {
                    Logger.Warn($"File {jsonFileName} doesn't exists or is empty.");
                }
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<SlaveSpawner> spawners, out _))
                    {
                        var entry = 0;
                        foreach (var spawner in spawners)
                        {
                            entry++;
                            if (!SlaveManager.Instance.Exist(spawner.UnitId))
                            {
                                Logger.Warn($"Slave Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                continue; // TODO ... so mb warn here?
                            }
                            spawner.Id = _nextId;
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            // Convert degrees from the file to radians for use
                            spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                            spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                            spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                            if (slaveSpawners.TryAdd(_nextId, spawner))
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
            }
            _doodadSpawners[(byte)world.Id] = doodadSpawners;
            _transferSpawners[(byte)world.Id] = transferSpawners;
            _gimmickSpawners[(byte)world.Id] = gimmickSpawners;
            _slaveSpawners[(byte)world.Id] = slaveSpawners;
        }

        Logger.Info("Loading persistent doodads...");

        var doodadsSpawned = 0;
        // Load furniture
        doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.Housing);
        // Load plants/packs and everything else that was placed into the world by players
        doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.System);
        doodadsSpawned += SpawnPersistentDoodads(DoodadOwnerType.Character);
        Logger.Info($"{doodadsSpawned} doodads loaded.");

        var respawnThread = new Thread(CheckRespawns) { Name = "RespawnThread" };
        respawnThread.Start();

        _loaded = true;
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
        if (_playerDoodads.Contains(doodad))
        {
            _playerDoodads.Remove(doodad);
        }
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
                    var houseId = reader.GetUInt32("house_id"); // actually DbId of the parent/owner (house, slave, etc)
                    var parentDoodad = reader.GetUInt32("parent_doodad");
                    var itemTemplateId = reader.GetUInt32("item_template_id");
                    var itemContainerId = reader.GetUInt64("item_container_id");
                    var data = reader.GetInt32("data");
                    var farmType = (FarmType)reader.GetUInt32("farm_type");

                    var doodad = DoodadManager.Instance.Create(0, templateId, null, true);

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
        foreach (var (worldId, worldSpawners) in _npcSpawners)
        {
            Task.Run(() =>
            {
                SpawnAllNpcs(worldId);
            });
        }

        Logger.Info("Spawning Doodads...");
        foreach (var (worldId, worldSpawners) in _doodadSpawners)
        {
            Task.Run(() =>
            {
                Logger.Info($"Spawning {worldSpawners.Count} Doodads in world {worldId}");
                var count = 0;
                foreach (var spawner in worldSpawners.Values)
                {
                    spawner.Spawn(0);
                    count++;
                    if (count % 1000 == 0 && worldId == 0)
                    {
                        Logger.Info($"in world {worldId} Doodads spawned: {count}...");
                    }
                }
                Logger.Info($"in world {worldId} Doodads spawned: {count}");

                // необходимо дождаться спавна всех doodads
                FishSchoolManager.Instance.Load(worldId);
            });
        }

        Logger.Info("Spawning Transfers...");
        foreach (var (worldId, worldSpawners) in _transferSpawners)
        {
            Task.Run(() =>
            {
                Logger.Info($"Spawning {worldSpawners.Count} Transfers in world {worldId}");
                var count = 0;
                foreach (var spawner in worldSpawners.Values)
                {
                    spawner.SpawnAll();
                    count++;
                    if (count % 10 == 0 && worldId == 0)
                    {
                        Logger.Info($"in world {worldId} Transfers spawned: {count}...");
                    }
                }
                Logger.Info($"in world {worldId} Transfers spawned: {count}");
            });
        }

        Logger.Info("Spawning Gimmicks...");
        foreach (var (worldId, worldSpawners) in _gimmickSpawners)
        {
            Task.Run(() =>
            {
                Logger.Info($"Spawning {worldSpawners.Count} Gimmicks in world {worldId}");
                var count = 0;
                foreach (var spawner in worldSpawners.Values)
                {
                    spawner.Spawn(0);
                    count++;
                    if (count % 5 == 0 && worldId == 0)
                    {
                        Logger.Info($"in world {worldId} Gimmicks spawned: {count}...");
                    }
                }
                Logger.Info($"in world {worldId} Gimmicks spawned: {count}");
            });
        }

        Logger.Info("Spawning Slaves...");
        foreach (var (worldId, worldSpawners) in _slaveSpawners)
        {
            Task.Run(() =>
            {
                Logger.Info($"Spawning {worldSpawners.Count} Slaves in world {worldId}");
                var count = 0;
                foreach (var spawner in worldSpawners.Values)
                {
                    spawner.Spawn(0);
                    count++;
                    if (count % 5 == 0 && worldId == 0)
                    {
                        Logger.Info($"in world {worldId} Slaves spawned: {count}...");
                    }
                }
                Logger.Info($"in world {worldId} slaves spawned: {count}");
            });
        }

        Logger.Info("Spawning Player Doodads asynchronously...");
        Task.Run(() =>
        {
            Logger.Info($"Spawning {_playerDoodads.Count} Player Doodads");
            foreach (var doodad in _playerDoodads)
            {
                if (doodad.Spawner == null)
                {
                    doodad.Spawn();
                }
                else
                {
                    if (doodad.Spawner?.Spawn(doodad.ObjId) == null)
                        Logger.Error($"Failed to spawn player doodad DbId:{doodad.DbId}, TemplateId: {doodad.TemplateId}");
                }
            }
        });
    }

    public List<Npc> SpawnAll(uint worldId, uint worldTemplateId)
    {
        var npcList = new List<Npc>();
        if (_npcSpawners.TryGetValue((byte)worldTemplateId, out var npcSpawners))
        {
            //Task.Run(() =>
            //{
            foreach (var spawners in npcSpawners.Values)
            {
                foreach (var spawner in spawners)
                {
                    spawner.Position.WorldId = worldId;
                    spawner.ClearSpawnCount();
                    npcList.Add(spawner.Spawn(0));
                    spawner.Position.WorldId = worldTemplateId;
                }
            }
            //});
        }
        if (_doodadSpawners.TryGetValue((byte)worldTemplateId, out var doodadSpawners))
        {
            //Task.Run(() =>
            //{
            foreach (var spawner in doodadSpawners.Values)
            {
                spawner.Position.WorldId = worldId;
                spawner.Spawn(0);
                spawner.Position.WorldId = worldTemplateId;
            }
            //});
        }
        if (_slaveSpawners.TryGetValue((byte)worldTemplateId, out var slaveSpawners))
        {
            //Task.Run(() =>
            //{
            foreach (var spawner in slaveSpawners.Values)
            {
                spawner.Position.WorldId = worldId;
                spawner.Spawn(0);
                spawner.Position.WorldId = worldTemplateId;
            }
            //});
        }
        if (_gimmickSpawners.TryGetValue((byte)worldTemplateId, out var gimmickSpawners))
        {
            //Task.Run(() =>
            //{
            foreach (var spawner in gimmickSpawners.Values)
            {
                spawner.Position.WorldId = worldId;
                spawner.Spawn(0);
                spawner.Position.WorldId = worldTemplateId;
            }
            //});
        }
        return npcList;
    }

    public void SpawnWithinSourceRange(uint templateId, Unit source, int range = 15)
    {
        Logger.Warn("SpawnWithinRange - world templateId: " + templateId + ", source templateId: " + source.TemplateId + ", objId: " + source.ObjId);
        if (_doodadSpawners.TryGetValue((byte)templateId, out var doodad))
        {
            foreach (var spawner in doodad.Values)
            {
                if (source.Transform.World.Position.X - spawner.Position.X < range)
                {
                    if (source.Transform.World.Position.Y - spawner.Position.Y < range)
                    {
                        spawner.Spawn(0);
                    }
                }
            }
        }
        if (_npcSpawners.TryGetValue((byte)templateId, out var npc))
        {
            foreach (var spawners in npc.Values)
            {
                foreach (var spawner in spawners)
                {
                    if (source.Transform.World.Position.X - spawner.Position.X < range)
                    {
                        if (source.Transform.World.Position.Y - spawner.Position.Y < range)
                        {
                            spawner.Spawn(0);
                        }
                    }
                }
            }
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
        lock (_respawns)
        {
            _respawns.Add(obj);
        }
    }

    public void RemoveRespawn(GameObject obj)
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

    public void RemoveDespawn(GameObject obj)
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
            temp = new HashSet<GameObject>(_respawns);
        }

        var res = new HashSet<GameObject>();
        foreach (var npc in temp)
            if (npc.Respawn <= DateTime.UtcNow)
                res.Add(npc);
        return res;
    }

    private HashSet<GameObject> GetDespawnsReady()
    {
        HashSet<GameObject> temp;
        lock (_despawns)
        {
            temp = new HashSet<GameObject>(_despawns);
        }

        var res = new HashSet<GameObject>();
        foreach (var item in temp)
            if (item.Despawn <= DateTime.UtcNow)
                res.Add(item);
        return res;
    }

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

            var despawns = GetDespawnsReady();
            if (despawns.Count > 0)
            {
                foreach (var obj in despawns)
                {
                    if (obj.Despawn >= DateTime.UtcNow)
                        continue;
                    if (obj is Npc npc && npc.Spawner != null)
                        npc.Spawner.Despawn(npc);
                    else if (obj is Doodad doodad && doodad.Spawner != null)
                        doodad.Spawner.Despawn(doodad);
                    else if (obj is Transfer transfer && transfer.Spawner != null)
                        transfer.Spawner.Despawn(transfer);
                    else if (obj is Gimmick gimmick && gimmick.Spawner != null)
                        gimmick.Spawner.Despawn(gimmick);
                    else if (obj is Slave slave) // slaves don't have a spawner, but this is used for delayed despawn of un-summoned boats
                        slave.Delete();
                    else if (obj is Doodad doodad2)
                        doodad2.Delete();
                    else
                    {
                        ObjectIdManager.Instance.ReleaseId(obj.ObjId);
                        obj.Delete();
                    }
                    RemoveDespawn(obj);
                }
            }

            Thread.Sleep(1000);
        }
    }

    public List<NpcSpawner> GetNpcSpawner(uint spawnerId, byte worldId)
    {
        var ret = new List<NpcSpawner>();
        _npcEventSpawners.TryGetValue(worldId, out var npcEventSpawners);
        if (npcEventSpawners == null)
        {
            return null;
        }

        foreach (var spawners in npcEventSpawners.Values)
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

    public NpcSpawner GetNpcSpawner(uint unitId, BaseUnit unit)
    {
        lock (_lockSpawner)
        {
            var spawner = new NpcSpawner();
            var npcSpawnersIds = NpcGameData.Instance.GetSpawnerIds(unitId);
            if (npcSpawnersIds == null)
            {
                spawner.UnitId = unitId;
                spawner.Id = ObjectIdManager.Instance.GetNextId();
                spawner.NpcSpawnerIds = new List<uint> { spawner.Id };
                spawner.Template = new NpcSpawnerTemplate(spawner.Id);
                spawner.Template.Npcs[0].MemberId = spawner.UnitId;
                spawner.Template.Npcs[0].UnitId = spawner.UnitId;
                spawner.Template.Npcs[0].MemberType = "Npc";
            }
            else
            {
                spawner.UnitId = unitId;
                spawner.Id = npcSpawnersIds[0];
                spawner.NpcSpawnerIds = new List<uint> { spawner.Id };
                spawner.Template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawner.Id);
                if (spawner.Template == null)
                {
                    return null;
                }

                spawner.Template.Npcs = new List<NpcSpawnerNpc>();
                var nsn = NpcGameData.Instance.GetNpcSpawnerNpc(spawner.Id);
                if (nsn == null)
                {
                    return null;
                }

                spawner.Template.Npcs.Add(nsn);
                spawner.Template.Npcs[0].MemberId = spawner.UnitId;
                spawner.Template.Npcs[0].UnitId = spawner.UnitId;
            }

            spawner.Position = new WorldSpawnPosition();
            spawner.Position.WorldId = unit.Transform.WorldId;
            spawner.Position.ZoneId = unit.Transform.ZoneId;
            spawner.Position.X = unit.Transform.World.Position.X;
            spawner.Position.Y = unit.Transform.World.Position.Y;
            spawner.Position.Z = unit.Transform.World.Position.Z;
            spawner.Position.Yaw = unit.Transform.World.Rotation.Z;
            spawner.Position.Pitch = 0;
            spawner.Position.Roll = 0;

            return spawner;
        }
    }
    public NpcSpawner GetNpcSpawner(uint unitId, WorldSpawnPosition position)
    {
        lock (_lockSpawner)
        {
            var spawner = new NpcSpawner();
            var npcSpawnersIds = NpcGameData.Instance.GetSpawnerIds(unitId);
            if (npcSpawnersIds == null)
            {
                spawner.UnitId = unitId;
                spawner.Id = ObjectIdManager.Instance.GetNextId();
                spawner.NpcSpawnerIds = new List<uint> { spawner.Id };
                spawner.Template = new NpcSpawnerTemplate(spawner.Id);
                spawner.Template.Npcs[0].MemberId = spawner.UnitId;
                spawner.Template.Npcs[0].UnitId = spawner.UnitId;
                spawner.Template.Npcs[0].MemberType = "Npc";
            }

            spawner.Position = new WorldSpawnPosition();
            spawner.Position.WorldId = position.WorldId;
            spawner.Position.ZoneId = position.ZoneId;
            spawner.Position.X = position.X;
            spawner.Position.Y = position.Y;
            spawner.Position.Z = position.Z;
            spawner.Position.Yaw = position.Z;
            spawner.Position.Pitch = 0;
            spawner.Position.Roll = 0;

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
}
