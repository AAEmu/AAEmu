using System;
using System.Collections.Generic;
using System.ComponentModel;

using AAEmu.Commons.Utils;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Observers;
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
        public uint NpcSpawnerId { get; set; }

        public NpcSpawner()
        {
            _spawned = new List<Npc>();
            Count = 1;
        }

        public List<Npc> SpawnAll()
        {
            var list = new List<Npc>();
            Count = Template.MaxPopulation;
            for (var num = _scheduledCount; num < Count; num++)
            {
                var npc = Spawn(0);
                if (npc != null)
                    list.Add(npc);
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
                _lastSpawn = _spawned.Count != 0 ? _spawned[_spawned.Count - 1] : null;
        }

        public void DecreaseCount(Npc npc)
        {
            _spawnCount--;
            _spawned.Remove(npc);
            if (RespawnTime > 0 && (_spawnCount + _scheduledCount) < Count)
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
            // TODO Check if delay is OK
            if (Template == null)
            {
                // no spawner for TemplateId
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, NpcSpawnerId);
                return;
            }

            // Check if population is within bounds
            if (_spawned.Count >= Template.MaxPopulation)
                return;

            #region Schedule
            // Check if Time Of Day matches Template.StartTime or Template.EndTime
            if (Template.StartTime != 0.0f || Template.EndTime != 0.0f)
            {
                var curTime = TimeManager.Instance.GetTime();
                if (curTime < Template.StartTime || curTime >= Template.EndTime)
                    return; // Reschedule when OK
            }

            // First, let's check if the schedule has such an NpcSpawnerId
            if (GameScheduleManager.Instance.GetGameScheduleSpawnersData(NpcSpawnerId))
            {
                // if there is, we'll check the time for the spawning
                if (!GameScheduleManager.Instance.CheckInGameSchedules(NpcSpawnerId))
                {
                    return; // Reschedule when OK
                }
            }
            //else if (Template.NpcSpawnerCategoryId == NpcSpawnerCategory.Normal)
            //{
            //    return; // there's no such npc in the schedule - we won't be spawning
            //}

            #endregion Schedule

            // Check if we did not go over Suspend Spawn Count
            if (Template.SuspendSpawnCount > 0 && _spawnCount > Template.SuspendSpawnCount)
                return;

            // Pick a random NPC to spawn based on weights from the table npc_spawner_npcs
            var nsn = Template.Npcs.RandomElementByWeight(nsn => nsn.Weight);

            var nextPosition = Position.Clone();

            // Spawn the NPC, add NPC to population
            // TODO: Make NpcGroup count as a single part of population?
            var n = nsn.Spawn(nextPosition, Template);
            if (n == null || n.Count == 0)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, NpcSpawnerId);
                return;
            }
            _spawned.AddRange(n);

            // Schedule next spawn
            if (_spawned.Count < Template.MaxPopulation)
                TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(Rand.Next(Template.SpawnDelayMin, Template.SpawnDelayMax)));

            _lastSpawn = n[0];
            if (_scheduledCount > 0)
            {
                _scheduledCount -= n.Count;
            }
            _spawnCount += n.Count;
        }
    }
}
