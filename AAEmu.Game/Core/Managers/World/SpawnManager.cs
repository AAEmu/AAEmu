using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.GameData;
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

namespace AAEmu.Game.Core.Managers.World
{
    public class SpawnManager : Singleton<SpawnManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _loaded = false;

        private bool _work = true;
        private object _lock = new object();
        private object _lockSpawner = new object();
        private HashSet<GameObject> _respawns;
        private HashSet<GameObject> _despawns;

        private Dictionary<byte, Dictionary<uint, NpcSpawner>> _npcSpawners;
        private Dictionary<byte, Dictionary<uint, NpcSpawner>> _npcEventSpawners;
        private Dictionary<byte, Dictionary<uint, DoodadSpawner>> _doodadSpawners;
        private Dictionary<byte, Dictionary<uint, TransferSpawner>> _transferSpawners;
        private Dictionary<byte, Dictionary<uint, GimmickSpawner>> _gimmickSpawners;
        private Dictionary<byte, Dictionary<uint, SlaveSpawner>> _slaveSpawners;
        private List<Doodad> _playerDoodads;

        private uint _nextId = 1u; // for spawner.Id

        public void AddNpcSpawner(NpcSpawner npcSpawner)
        {
            // check for manually entered NpcSpawnerId
            if (npcSpawner.NpcSpawnerIds.Count == 0)
            {
                var npcSpawnerIds = NpcGameData.Instance.GetSpawnerIds(npcSpawner.UnitId);
                // !!! npcSpawnerIds.Count всегда = 1 !!!
                if (!_npcSpawners[(byte)npcSpawner.Position.WorldId].ContainsKey(_nextId))
                {
                    npcSpawner.NpcSpawnerIds.Add(npcSpawnerIds[0]);
                    npcSpawner.Id = _nextId;
                    npcSpawner.Template = NpcGameData.Instance.GetNpcSpawnerTemplate(npcSpawnerIds[0]);
                    _npcSpawners[(byte)npcSpawner.Position.WorldId].Add(_nextId, npcSpawner);
                    _nextId++; //we'll renumber
                }
            }
            else
            {
                // Load NPC Spawns for Events
                npcSpawner.Id = _nextId;
                if (npcSpawner.Template.Id != npcSpawner.NpcSpawnerIds[0])
                {
                    npcSpawner.Template = new NpcSpawnerTemplate(npcSpawner.NpcSpawnerIds[0]);
                }
                _npcEventSpawners[(byte)npcSpawner.Position.WorldId].Add(_nextId, npcSpawner);
                _nextId++; //we'll renumber
            }
        }

        internal void SpawnAllNpcs(byte worldId)
        {
            _log.Info("Spawning {0} NPC spawners in world {1}", _npcSpawners[worldId].Count, worldId);
            var count = 0;
            foreach (var spawner in _npcSpawners[worldId].Values)
            {
                if (spawner.Template == null)
                {
                    _log.Warn("Templates not found for Npc templateId {0} in world {1}", spawner.UnitId, worldId);
                }
                else
                {
                    spawner.SpawnAll();
                    count++;
                    if (count % 5000 == 0 && worldId == 0)
                    {
                        _log.Info("{0} NPC spawners spawned...", count);
                    }
                }
            }
            _log.Info("{0} NPC spawners spawned...", count);
        }

        public void Load()
        {
            if (_loaded)
                return;

            _respawns = new HashSet<GameObject>();
            _despawns = new HashSet<GameObject>();
            _npcSpawners = new Dictionary<byte, Dictionary<uint, NpcSpawner>>();
            _npcEventSpawners = new Dictionary<byte, Dictionary<uint, NpcSpawner>>();
            _doodadSpawners = new Dictionary<byte, Dictionary<uint, DoodadSpawner>>();
            _transferSpawners = new Dictionary<byte, Dictionary<uint, TransferSpawner>>();
            _gimmickSpawners = new Dictionary<byte, Dictionary<uint, GimmickSpawner>>();
            _slaveSpawners = new Dictionary<byte, Dictionary<uint, SlaveSpawner>>();
            _playerDoodads = new List<Doodad>();

            var worlds = WorldManager.Instance.GetWorlds();
            foreach (var world in worlds)
            {
                _npcSpawners.Add((byte)world.Id, new Dictionary<uint, NpcSpawner>());
                _npcEventSpawners.Add((byte)world.Id, new Dictionary<uint, NpcSpawner>());
                _doodadSpawners.Add((byte)world.Id, new Dictionary<uint, DoodadSpawner>());
                _transferSpawners.Add((byte)world.Id, new Dictionary<uint, TransferSpawner>());
                _gimmickSpawners.Add((byte)world.Id, new Dictionary<uint, GimmickSpawner>());
                _slaveSpawners.Add((byte)world.Id, new Dictionary<uint, SlaveSpawner>());
            }

            _log.Info("Loading spawns...");
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
                    _log.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
                }
                else
                {
                    var contents = FileManager.GetFileContents(jsonFileName);

                    if (string.IsNullOrWhiteSpace(contents))
                        _log.Warn($"File {jsonFileName} is empty.");
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
                                    _log.Warn($"Npc Template {npcSpawnerFromFile.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
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
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                    }
                }

                // Load Doodad spawns
                jsonFileName = Path.Combine(worldPath, "doodad_spawns.json");

                if (!File.Exists(jsonFileName))
                {
                    _log.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
                }
                else
                {
                    var contents = FileManager.GetFileContents(jsonFileName);

                    if (string.IsNullOrWhiteSpace(contents))
                        _log.Warn($"File {jsonFileName} is empty.");
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
                                    _log.Warn($"Doodad Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                    continue; // TODO ... so mb warn here?
                                }
                                spawner.Id = _nextId;
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                // Convert degrees from the file to radians for use
                                spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                                spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                                spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                                if (!doodadSpawners.ContainsKey(_nextId))
                                {
                                    doodadSpawners.Add(_nextId, spawner);
                                    _nextId++;
                                }
                            }
                        }
                        else
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                    }
                }

                // Load Transfers
                jsonFileName = Path.Combine(worldPath, "transfer_spawns.json");

                if (!File.Exists(jsonFileName))
                {
                    _log.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
                }
                else
                {
                    var contents = FileManager.GetFileContents(jsonFileName);

                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        _log.Warn($"File {jsonFileName} doesn't exists or is empty.");
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
                                    _log.Warn($"Transfer Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                    continue; // TODO ... so mb warn here?
                                }
                                spawner.Id = _nextId;
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                // Convert degrees from the file to radians for use
                                spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                                spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                                spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                                if (!transferSpawners.ContainsKey(_nextId))
                                {
                                    transferSpawners.Add(_nextId, spawner);
                                    _nextId++;
                                }
                            }
                        }
                        else
                        {
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                        }
                    }
                }

                // Load Gimmicks
                jsonFileName = Path.Combine(worldPath, "gimmick_spawns.json");

                if (!File.Exists(jsonFileName))
                {
                    _log.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
                }
                else
                {
                    var contents = FileManager.GetFileContents(jsonFileName);

                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        _log.Warn($"File {jsonFileName} doesn't exists or is empty.");
                    }
                    else
                    {
                        if (JsonHelper.TryDeserializeObject(contents, out List<GimmickSpawner> spawners, out _))
                        {
                            var entry = 0;
                            foreach (var spawner in spawners)
                            {
                                entry++;
                                if (!GimmickManager.Instance.Exist(spawner.UnitId))
                                {
                                    _log.Warn($"Gimmick Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                    continue; // TODO ... so mb warn here?
                                }
                                spawner.Id = _nextId;
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                if (!gimmickSpawners.ContainsKey(_nextId))
                                {
                                    gimmickSpawners.Add(_nextId, spawner);
                                    _nextId++;
                                }
                            }
                        }
                        else
                        {
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                        }
                    }

                }

                // Load Slaves
                jsonFileName = Path.Combine(worldPath, "slave_spawns.json");

                if (!File.Exists(jsonFileName))
                {
                    _log.Info($"World  {world.Name}  is missing  {Path.GetFileName(jsonFileName)}");
                }
                else
                {
                    var contents = FileManager.GetFileContents(jsonFileName);

                    if (string.IsNullOrWhiteSpace(contents))
                    {
                        _log.Warn($"File {jsonFileName} doesn't exists or is empty.");
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
                                    _log.Warn($"Slave Template {spawner.UnitId} (file entry {entry}) doesn't exist - {jsonFileName}");
                                    continue; // TODO ... so mb warn here?
                                }
                                spawner.Id = _nextId;
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                if (!slaveSpawners.ContainsKey(_nextId))
                                {
                                    slaveSpawners.Add(_nextId, spawner);
                                    _nextId++;
                                }
                            }
                        }
                        else
                        {
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                        }
                    }

                }
                _doodadSpawners[(byte)world.Id] = doodadSpawners;
                _transferSpawners[(byte)world.Id] = transferSpawners;
                _gimmickSpawners[(byte)world.Id] = gimmickSpawners;
                _slaveSpawners[(byte)world.Id] = slaveSpawners;
            }

            _log.Info("Loading persistent doodads...");
            List<Doodad> newCoffers = new List<Doodad>();
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    // Sorting required to make make sure parenting doesn't produce invalid parents (normally)
                    command.CommandText = "SELECT * FROM doodads ORDER BY `plant_time` ASC";
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
                            var plantTime = reader.GetDateTime("plant_time");
                            var growthTime = reader.GetDateTime("growth_time");
                            // var phaseTime = reader.GetDateTime("phase_time"); // Not used
                            var ownerId = reader.GetUInt32("owner_id");
                            var ownerType = (DoodadOwnerType)reader.GetByte("owner_type");
                            var itemId = reader.GetUInt64("item_id");
                            var houseId = reader.GetUInt32("house_id");
                            var parentDoodad = reader.GetUInt32("parent_doodad");
                            var itemTemplateId = reader.GetUInt32("item_template_id");
                            var itemContainerId = reader.GetUInt64("item_container_id");
                            var data = reader.GetInt32("data");

                            var doodad = DoodadManager.Instance.Create(0, templateId);

                            doodad.Spawner = new DoodadSpawner();
                            doodad.Spawner.UnitId = templateId;
                            doodad.DbId = dbId;
                            doodad.FuncGroupId = phaseId;
                            doodad.OwnerId = ownerId;
                            doodad.OwnerType = ownerType;
                            doodad.AttachPoint = AttachPointKind.None;
                            doodad.PlantTime = plantTime;
                            doodad.GrowthTime = growthTime;
                            doodad.ItemId = itemId;
                            doodad.DbHouseId = houseId;
                            // Try to grab info from the actual item if it still exists
                            var sourceItem = ItemManager.Instance.GetItemByItemId(itemId);
                            doodad.ItemTemplateId = sourceItem?.TemplateId ?? itemTemplateId;
                            // Grab Ucc from it's old source item
                            doodad.UccId = sourceItem?.UccId ?? 0;
                            doodad.SetData(data); // Directly assigning to Data property would trigger a .Save()

                            doodad.Transform.Local.SetPosition(x, y, z);
                            doodad.Transform.Local.SetRotation(reader.GetFloat("roll"), reader.GetFloat("pitch"), reader.GetFloat("yaw"));

                            // Apparently this is only a reference value, so might not actually need to parent it
                            if (parentDoodad > 0)
                            {
                                // var pDoodad = WorldManager.Instance.GetDoodadByDbId(parentDoodad);
                                var pDoodad = _playerDoodads.FirstOrDefault(d => d.DbId == parentDoodad);
                                if (pDoodad == null)
                                {
                                    _log.Warn($"Unable to place doodad {dbId} can't find it's parent doodad {parentDoodad}");
                                }
                                else
                                {
                                    //doodad.Transform.Parent = pDoodad.Transform;
                                    //doodad.ParentObj = pDoodad;
                                    //doodad.ParentObjId = pDoodad.ObjId;
                                }
                            }

                            if (houseId > 0)
                            {
                                var owningHouse = HousingManager.Instance.GetHouseById(doodad.DbHouseId);
                                if (owningHouse == null)
                                {
                                    _log.Warn($"Unable to place doodad {dbId} can't find it's owning house {houseId}");
                                }
                                else
                                {
                                    doodad.Transform.Parent = owningHouse.Transform;
                                    doodad.ParentObj = owningHouse;
                                    doodad.ParentObjId = owningHouse.ObjId;
                                }
                            }

                            // Attach ItemContainer to coffer if needed
                            if (doodad is DoodadCoffer coffer)
                            {
                                if (itemContainerId > 0)
                                {
                                    var itemContainer = ItemManager.Instance.GetItemContainerByDbId(itemContainerId);
                                    if (itemContainer is CofferContainer cofferContainer)
                                        coffer.ItemContainer = cofferContainer;
                                    else
                                        _log.Error($"Unable to attach ItemContainer {itemContainerId} to DoodadCoffer, objId: {doodad.ObjId}, DbId: {doodad.DbId}");
                                }
                                else
                                {
                                    _log.Warn($"DoodadCoffer has no persistent ItemContainer assigned to it, creating new one, objId: {doodad.ObjId}, DbId: {doodad.DbId}");
                                    coffer.InitializeCoffer(ownerId);
                                    newCoffers.Add(coffer); // Mark for saving again later when we're done with this loop
                                }
                            }

                            _playerDoodads.Add(doodad);
                        }
                    }
                }
            }

            var respawnThread = new Thread(CheckRespawns) { Name = "RespawnThread" };
            respawnThread.Start();

            // Save Coffer Doodads that had a new ItemContainer created for them (should only happen on first run if there were already coffers placed)
            foreach (var coffer in newCoffers)
                coffer.Save();

            _loaded = true;
        }

        public void SpawnAll()
        {
            _log.Info("Spawning NPCs...");
            foreach (var (worldId, worldSpawners) in _npcSpawners)
            {
                Task.Run(() =>
                {
                    SpawnAllNpcs(worldId);
                });
            }

            _log.Info("Spawning Doodads...");
            foreach (var (worldId, worldSpawners) in _doodadSpawners)
            {
                Task.Run(() =>
                {
                    _log.Info("Spawning {0} Doodads in world {1}", worldSpawners.Count, worldId);
                    var count = 0;
                    foreach (var spawner in worldSpawners.Values)
                    {
                        spawner.Spawn(0);
                        count++;
                        if (count % 1000 == 0 && worldId == 0)
                        {
                            _log.Info("{0} Doodads spawned", count);
                        }
                    }
                    _log.Info("{0} Doodads spawned", count);
                });
            }

            _log.Info("Spawning Transfers...");
            foreach (var (worldId, worldSpawners) in _transferSpawners)
            {
                Task.Run(() =>
                {
                    _log.Info("Spawning {0} Transfers in world {1}", worldSpawners.Count, worldId);
                    var count = 0;
                    foreach (var spawner in worldSpawners.Values)
                    {
                        spawner.SpawnAll();
                        count++;
                        if (count % 10 == 0 && worldId == 0)
                        {
                            _log.Info("{0} Transfers spawned...", count);
                        }
                    }
                    _log.Info("{0} Transfers spawned...", count);
                });
            }

            _log.Info("Spawning Gimmicks...");
            foreach (var (worldId, worldSpawners) in _gimmickSpawners)
            {
                Task.Run(() =>
                {
                    _log.Info("Spawning {0} Gimmicks in world {1}", worldSpawners.Count, worldId);
                    var count = 0;
                    foreach (var spawner in worldSpawners.Values)
                    {
                        spawner.Spawn(0);
                        count++;
                        if (count % 5 == 0 && worldId == 0)
                        {
                            _log.Info("{0} Gimmicks spawned...", count);
                        }
                    }
                    _log.Info("{0} Gimmicks spawned...", count);
                });
            }

            _log.Info("Spawning Slaves...");
            foreach (var (worldId, worldSpawners) in _slaveSpawners)
            {
                Task.Run(() =>
                {
                    _log.Info("Spawning {0} Slaves in world {1}", worldSpawners.Count, worldId);
                    var count = 0;
                    foreach (var spawner in worldSpawners.Values)
                    {
                        spawner.Spawn(0);
                        count++;
                        if (count % 5 == 0 && worldId == 0)
                        {
                            _log.Info("{0} Slaves spawned...", count);
                        }
                    }
                    _log.Info("{0} Slaves spawned...", count);
                });
            }

            _log.Info("Spawning Player Doodads asynchronously...");
            Task.Run(() =>
            {
                foreach (var doodad in _playerDoodads)
                {
                    if (doodad.Spawner.Spawn(doodad.ObjId) == null)
                    {
                        doodad.Spawn();
                    }
                }
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

            foreach (var spawner in npcEventSpawners.Values.Where(spawner => spawner.NpcSpawnerIds[0] == spawnerId))
            {
                spawner.Template.Npcs[^1].MemberId = spawner.UnitId;
                spawner.Template.Npcs[^1].UnitId = spawner.UnitId;
                spawner.Template.Npcs[^1].MemberType = "Npc";
                ret.Add(spawner);
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
                    if (spawner.Template.Npcs == null)
                    {
                        return null;
                    }

                    spawner.Template.Npcs[0].MemberId = spawner.UnitId;
                    spawner.Template.Npcs[0].UnitId = spawner.UnitId;
                }

                spawner.Position = new WorldSpawnPosition();
                spawner.Position.WorldId = unit.Transform.WorldId;
                spawner.Position.ZoneId = unit.Transform.ZoneId;
                spawner.Position.X = unit.Transform.World.Position.X;
                spawner.Position.Y = unit.Transform.World.Position.Y;
                spawner.Position.Z = unit.Transform.World.Position.Z;
                spawner.Position.Yaw = 0;
                spawner.Position.Pitch = 0;
                spawner.Position.Roll = 0;

                return spawner;
            }
        }
    }
}
