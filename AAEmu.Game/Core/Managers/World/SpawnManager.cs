using System;
using System.Collections.Generic;
using System.Threading;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
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

        public void Load()
        {
            _respawns = new HashSet<GameObject>();
            _despawns = new HashSet<GameObject>();
            _npcSpawners = new Dictionary<byte, Dictionary<uint, NpcSpawner>>();
            _doodadSpawners = new Dictionary<byte, Dictionary<uint, DoodadSpawner>>();
            _transferSpawners = new Dictionary<byte, Dictionary<uint, TransferSpawner>>();

            var worlds = WorldManager.Instance.GetWorlds();
            _log.Info("Loading spawns...");
            foreach (var world in worlds)
            {
                var npcSpawners = new Dictionary<uint, NpcSpawner>();
                var doodadSpawners = new Dictionary<uint, DoodadSpawner>();
                var transferSpawners = new Dictionary<uint, TransferSpawner>();

                var contents =
                    FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json");
                if (string.IsNullOrWhiteSpace(contents))
                    _log.Warn(
                        $"File {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json doesn't exists or is empty.");
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<NpcSpawner> spawners, out _))
                        foreach (var spawner in spawners)
                        {
                            if (!NpcManager.Instance.Exist(spawner.UnitId))
                                continue; // TODO ... so mb warn here?
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager
                                .Instance
                                .GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            npcSpawners.Add(spawner.Id, spawner);
                        }
                    else
                        throw new Exception($"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/npc_spawns.json file");
                }

                contents = FileManager.GetFileContents(
                    $"{FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json");
                if (string.IsNullOrWhiteSpace(contents))
                    _log.Warn($"File {FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json doesn't exists or is empty.");
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<DoodadSpawner> spawners, out _))
                        foreach (var spawner in spawners)
                        {
                            if (!DoodadManager.Instance.Exist(spawner.UnitId))
                                continue; // TODO ... so mb warn here?
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager
                                .Instance
                                .GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            doodadSpawners.Add(spawner.Id, spawner);
                        }
                    else
                        throw new Exception($"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/doodad_spawns.json file");
                }

                contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/Worlds/{world.Name}/transfer_spawns.json");
                if (string.IsNullOrWhiteSpace(contents))
                {
                    _log.Warn($"File {FileManager.AppPath}Data/Worlds/{world.Name}/transfer_spawns.json doesn't exists or is empty.");
                }
                else
                {
                    if (JsonHelper.TryDeserializeObject(contents, out List<TransferSpawner> spawners, out _))
                    {
                        foreach (var spawner in spawners)
                        {
                            if (!TransferManager.Instance.Exist(spawner.UnitId))
                                continue;
                            spawner.Position.WorldId = world.Id;
                            spawner.Position.ZoneId = WorldManager
                                .Instance
                                .GetZoneId(world.Id, spawner.Position.X, spawner.Position.Y);
                            transferSpawners.Add(spawner.Id, spawner);
                        }
                    }
                    else
                    {
                        throw new Exception($"SpawnManager: Parse {FileManager.AppPath}Data/Worlds/{world.Name}/transfer_spawns.json file");
                    }
                }

                _npcSpawners.Add((byte)world.Id, npcSpawners);
                _doodadSpawners.Add((byte)world.Id, doodadSpawners);
                _transferSpawners.Add((byte)world.Id, transferSpawners);

            }

            var respawnThread = new Thread(CheckRespawns) { Name = "RespawnThread" };
            respawnThread.Start();
        }

        public void SpawnAll()
        {
            foreach (var (worldId, worldSpawners) in _npcSpawners)
                foreach (var spawner in worldSpawners.Values)
                    spawner.SpawnAll();

            foreach (var (worldId, worldSpawners) in _doodadSpawners)
                foreach (var spawner in worldSpawners.Values)
                    spawner.Spawn(0);

            foreach (var (worldId, worldSpawners) in _transferSpawners)
                foreach (var spawner in worldSpawners.Values)
                    spawner.SpawnAll();
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
                if (npc.Respawn <= DateTime.Now)
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
                if (item.Despawn <= DateTime.Now)
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
                        if (obj.Respawn >= DateTime.Now)
                            continue;
                        if (obj is Npc npc)
                            npc.Spawner.Respawn(npc);
                        if (obj is Doodad doodad)
                            doodad.Spawner.Respawn(doodad);
                        if (obj is Transfer transfer)
                            transfer.Spawner.Respawn(transfer);
                        RemoveRespawn(obj);
                    }
                }

                var despawns = GetDespawnsReady();
                if (despawns.Count > 0)
                {
                    foreach (var obj in despawns)
                    {
                        if (obj.Despawn >= DateTime.Now)
                            continue;
                        if (obj is Npc npc && npc.Spawner != null)
                            npc.Spawner.Despawn(npc);
                        else if (obj is Doodad doodad && doodad.Spawner != null)
                            doodad.Spawner.Despawn(doodad);
                        else if (obj is Transfer transfer && transfer.Spawner != null)
                            transfer.Spawner.Despawn(transfer);
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
