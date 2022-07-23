﻿using System;
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
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class SpawnManager : Singleton<SpawnManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private bool _work = true;
        private object _lock = new object();
        private HashSet<GameObject> _respawns;
        private HashSet<GameObject> _despawns;

        private Dictionary<byte, Dictionary<uint, NpcSpawner>> _npcSpawners;
        private Dictionary<byte, Dictionary<uint, DoodadSpawner>> _doodadSpawners;
        private Dictionary<byte, Dictionary<uint, TransferSpawner>> _transferSpawners;
        private Dictionary<byte, Dictionary<uint, GimmickSpawner>> _gimmickSpawners;
        private List<Doodad> _playerDoodads;

        public void Load()
        {
            _respawns = new HashSet<GameObject>();
            _despawns = new HashSet<GameObject>();
            _npcSpawners = new Dictionary<byte, Dictionary<uint, NpcSpawner>>();
            _doodadSpawners = new Dictionary<byte, Dictionary<uint, DoodadSpawner>>();
            _transferSpawners = new Dictionary<byte, Dictionary<uint, TransferSpawner>>();
            _gimmickSpawners = new Dictionary<byte, Dictionary<uint, GimmickSpawner>>();
            _playerDoodads = new List<Doodad>();

            var worlds = WorldManager.Instance.GetWorlds();
            _log.Info("Loading spawns...");

            foreach (var world in worlds)
            {
                var npcSpawners = new Dictionary<uint, NpcSpawner>();
                var doodadSpawners = new Dictionary<uint, DoodadSpawner>();
                var transferSpawners = new Dictionary<uint, TransferSpawner>();
                var gimmickSpawners = new Dictionary<uint, GimmickSpawner>();
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
                        if (JsonHelper.TryDeserializeObject(contents, out List<NpcSpawner> spawners, out _))
                            foreach (var spawner in spawners)
                            {
                                if (!NpcManager.Instance.Exist(spawner.UnitId))
                                    continue; // TODO ... so mb warn here?
                                
                                if (npcSpawners.ContainsKey(spawner.Id))
                                {
                                    _log.Warn($"Duplicate Npc Spawn Id: {spawner.Id}");
                                    continue;
                                }
                                
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId =
                                    WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                // Convert degrees from the file to radians for use
                                spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                                spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                                spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                                npcSpawners.Add(spawner.Id, spawner);
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
                            var C = 0;
                            foreach (var spawner in spawners)
                            {
                                C++;
                                if (!DoodadManager.Instance.Exist(spawner.UnitId))
                                {
                                    _log.Warn($"Doodad Template {spawner.UnitId} (file entry {C}) doesn't exist - {jsonFileName}");
                                    continue; // TODO ... so mb warn here?
                                }

                                if (doodadSpawners.ContainsKey(spawner.Id))
                                {
                                    _log.Warn($"Duplicate Doodad Spawn Id: {spawner.Id}");
                                    continue;
                                }
                                
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId = WorldManager
                                    .Instance
                                    .GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                // Convert degrees from the file to radians for use
                                spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                                spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                                spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                                doodadSpawners.Add(spawner.Id, spawner);
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
                            foreach (var spawner in spawners)
                            {
                                if (!TransferManager.Instance.Exist(spawner.UnitId))
                                    continue;
                                
                                if (transferSpawners.ContainsKey(spawner.Id))
                                {
                                    _log.Warn($"Duplicate Transfer Spawn Id: {spawner.Id}");
                                    continue;
                                }
                                
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId = WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                // Convert degrees from the file to radians for use
                                spawner.Position.Yaw = spawner.Position.Yaw.DegToRad();
                                spawner.Position.Pitch = spawner.Position.Pitch.DegToRad();
                                spawner.Position.Roll = spawner.Position.Roll.DegToRad();
                                transferSpawners.Add(spawner.Id, spawner);
                            }
                        }
                        else
                        {
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                        }
                    }
                }

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
                            foreach (var spawner in spawners)
                            {
                                if (!GimmickManager.Instance.Exist(spawner.UnitId))
                                    continue;
                                
                                if (gimmickSpawners.ContainsKey(spawner.Id))
                                {
                                    _log.Warn($"Duplicate Gimmick Spawn Id: {spawner.Id}");
                                    continue;
                                }
                                
                                spawner.Position.WorldId = world.Id;
                                spawner.Position.ZoneId =
                                    WorldManager.Instance.GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                                gimmickSpawners.Add(spawner.Id, spawner);
                            }
                        }
                        else
                        {
                            throw new Exception($"SpawnManager: Parse {jsonFileName} file");
                        }
                    }

                }

                _npcSpawners.Add((byte)world.Id, npcSpawners);
                _doodadSpawners.Add((byte)world.Id, doodadSpawners);
                _transferSpawners.Add((byte)world.Id, transferSpawners);
                _gimmickSpawners.Add((byte)world.Id, gimmickSpawners);
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
                            
                            doodad.DbId = dbId;
                            doodad.CurrentPhaseId = phaseId;
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
        }

        public void SpawnAll()
        {
            foreach (var spawner in _npcSpawners.Values.SelectMany(worldSpawners => worldSpawners.Values))
                spawner.SpawnAll();

            foreach (var spawner in _doodadSpawners.Values.SelectMany(worldSpawners => worldSpawners.Values))
                spawner.Spawn(0);

            foreach (var spawner in _transferSpawners.Values.SelectMany(worldSpawners => worldSpawners.Values))
                spawner.SpawnAll();

            foreach (var spawner in _gimmickSpawners.Values.SelectMany(worldSpawners => worldSpawners.Values))
                spawner.Spawn(0);

            Task.Run(() =>
            {
                foreach (var doodad in _playerDoodads)
                {
                    // doodad.DoPhase(null, 0);
                    doodad.Spawn();
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
                    }
                }

                Thread.Sleep(1000);
            }
        }
    }
}
