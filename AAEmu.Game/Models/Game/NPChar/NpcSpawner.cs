using System;
using System.Collections.Generic;
using System.ComponentModel;

using AAEmu.Commons.Utils;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.NPChar.NPSpawner;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcSpawner : Spawner<Npc>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<Npc> _spawned;
        public Npc _lastSpawn;
        private int _scheduledCount;
        private int _spawnCount;

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1f)]
        public uint Count { get; set; } = 1;
        public List<uint> NpcSpawnerId { get; set; }

        public NpcSpawner()
        {
            _spawned = new List<Npc>();
            Count = 1;
            NpcSpawnerId = new List<uint>();
            Template = new Dictionary<uint, NpcSpawnerTemplate>();
            _lastSpawn = new Npc();
        }

        public List<Npc> SpawnAll()
        {
            var list = new List<Npc>();
            for (var num = _scheduledCount; num < Count; num++)
            {
                var npc = Spawn(0);
                if (npc != null)
                {
                    list.Add(npc);
                }
            }

            return list;
        }

        public override Npc Spawn(uint objId)
        {
            DoSpawn();
            return _lastSpawn;
        }

        public override void Despawn(Npc npc)
        {
            npc.Delete();
            if (npc.Respawn == DateTime.MinValue)
            {
                _spawned.Remove(npc);
                ObjectIdManager.Instance.ReleaseId(npc.ObjId);
                _spawnCount--;
            }

            if (_lastSpawn == null || _lastSpawn.ObjId == npc.ObjId)
            {
                _lastSpawn = _spawned.Count != 0 ? _spawned[^1] : null;
            }
        }

        public void DecreaseCount(Npc npc)
        {
            _spawnCount--;
            _spawned.Remove(npc);
            if (RespawnTime > 0 && _spawnCount + _scheduledCount < Count)
            {
                npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(npc);
                _scheduledCount++;
            }

            npc.Despawn = DateTime.UtcNow.AddSeconds(DespawnTime);
            SpawnManager.Instance.AddDespawn(npc);
        }

        public void DespawnWithRespawn(Npc npc)
        {
            npc.Delete();
            _spawnCount--;
            _spawned.Remove(npc);
            if (RespawnTime > 0 && _spawnCount + _scheduledCount < Count)
            {
                npc.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(npc);
                _scheduledCount++;
            }
        }

        public void DoSpawn()
        {
            foreach (var (spawnerId, template) in Template)
            {
                // TODO Check if delay is OK
                if (template == null)
                {
                    // no spawner for TemplateId
                    _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, spawnerId);
                    continue;
                }

                // Check if population is within bounds
                if (_spawnCount >= template.MaxPopulation)
                {
                    continue;
                }

                #region Schedule
                // Check if Time Of Day matches Template.StartTime or Template.EndTime
                if (template.StartTime != 0.0f || template.EndTime != 0.0f)
                {
                    var curTime = TimeManager.Instance.GetTime();
                    if (curTime < template.StartTime || curTime >= template.EndTime)
                    {
                        continue; // Reschedule when OK
                    }
                }
                // First, let's check if the schedule has such an spawnerId
                if (GameScheduleManager.Instance.GetGameScheduleSpawnersData(spawnerId))
                {
                    // if there is, we'll check the time for the spawning
                    if (!GameScheduleManager.Instance.CheckSpawnerInGameSchedules(spawnerId))
                    {
                        _log.Debug("DoSpawn: Npc TemplateId {0}, NpcSpawnerId {1} not on the schedule...", UnitId, Id);
                        continue; // Reschedule when OK
                    }
                }

                #endregion Schedule

                // Check if we did not go over Suspend Spawn Count
                if (template.SuspendSpawnCount > 0 && _spawnCount > template.SuspendSpawnCount)
                {
                    continue;
                }

                // Select an NPC to spawn based on the spawnerId in npc_spawner_npcs
                var n = new List<Npc>();
                foreach (var nsn in template.Npcs)
                {
                    if (nsn.MemberId != UnitId) { continue; }
                    n = nsn.Spawn(this, template.MaxPopulation);
                    break;
                }

                try
                {
                    foreach (var npc in n)
                    {
                        _spawned.Add(npc);
                    }
                    //_spawned.AddRange(n);
                }
                catch (Exception)
                {
                    _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, spawnerId);
                }

                if (n.Count == 0)
                {
                    _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, spawnerId);
                    continue;
                }
                _lastSpawn = n[^1];
                if (_scheduledCount > 0)
                {
                    _scheduledCount -= n.Count;
                }
                _spawnCount = _spawned.Count;
                if (_spawnCount < 0)
                {
                    _spawnCount = 0;
                }

                // Schedule next spawn
                if (_spawnCount < template.MaxPopulation)
                {
                    TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(Rand.Next(template.SpawnDelayMin, template.SpawnDelayMax)));
                }
            }
        }
    }
}
