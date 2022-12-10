using System;
using System.Collections.Generic;
using System.ComponentModel;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.World;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Models.Game.DoodadObj
{
    public class DoodadSpawner : Spawner<Doodad>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public float Scale { get; set; }
        public Doodad Last { get; set; }

        //---
        private List<Doodad> _spawned;
        private int _scheduledCount;
        private int _spawnCount;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1f)]
        public uint Count { get; set; } = 1;
        private bool _permanent { get; set; }
        public List<uint> RelatedIds { get; set; }
        //---

        public DoodadSpawner()
        {
            _permanent = true; // Doodad not on the schedule.
            _spawned = new List<Doodad>();
            Count = 1;
            Last = new Doodad();
            Scale = 1f;
        }

        public DoodadSpawner(uint id, uint unitId, WorldSpawnPosition position)
        {
            Id = id;
            UnitId = unitId;
            Position = position;
        }

        /// <summary>
        /// Spawn a doodad in the world with a character as owner
        /// </summary>
        /// <param name="objId">instance id of the doodad</param>
        /// <param name="itemId">template id of the doodad</param>
        /// <param name="charId">instance id of the character</param>
        /// <returns>Created doodad reference</returns>
        public override Doodad Spawn(uint objId, ulong itemId, uint charId) //Mostly used for player created spawns
        {
            _permanent = true; // Doodad not on the schedule.
            _spawned = new List<Doodad>();
            Count = 1;
            Last = new Doodad();
            var character = WorldManager.Instance.GetCharacterByObjId(charId);
            var doodad = DoodadManager.Instance.Create(objId, UnitId, character);

            if (doodad == null)
            {
                _log.Warn("Doodad {0}, from spawn not exist at db", UnitId);
                return null;
            }

            doodad.Spawner = this;
            doodad.Transform.ApplyWorldSpawnPosition(Position);
            doodad.QuestGlow = 0u; // TODO: make this OOP
            doodad.ItemId = itemId;

            // TODO for test
            doodad.PlantTime = DateTime.UtcNow;

            if (Scale > 0)
            {
                doodad.SetScale(Scale);
            }

            if (doodad.Transform == null)
            {
                _log.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
                return null;
            }

            Last = doodad;
            DoSpawn();// schedule check and spawn
            return doodad;
        }

        public override Doodad Spawn(uint objId) // TODO: clean up each doodad uses the same call
        {
            _permanent = true; // Doodad not on the schedule.
            _spawned = new List<Doodad>();
            Count = 1;
            Last = new Doodad();

            if (objId != 0) { return null; }

            var doodad = DoodadManager.Instance.Create(objId, UnitId, null);
            if (doodad == null)
            {
                _log.Warn("Doodad {0}, from spawn not exist at db", UnitId);
                return null;
            }

            doodad.Spawner = this;
            doodad.Transform.ApplyWorldSpawnPosition(Position);
            // TODO for test
            doodad.PlantTime = DateTime.UtcNow;
            if (Scale > 0)
            {
                doodad.SetScale(Scale);
            }

            if (doodad.Transform == null)
            {
                _log.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
                return null;
            }

            Last = doodad;
            DoSpawn();// schedule check and spawn
            return doodad;
        }

        public override void Despawn(Doodad doodad)
        {
            doodad.Delete();
            if (doodad.Respawn == DateTime.MinValue)
            {
                ObjectIdManager.Instance.ReleaseId(doodad.ObjId);
            }

            Last = null;
        }

        public void DecreaseCount(Doodad doodad)
        {
            if (RespawnTime > 0)
            {
                doodad.Respawn = DateTime.UtcNow.AddSeconds(RespawnTime);
                SpawnManager.Instance.AddRespawn(doodad);
            }
            else
            {
                Last = null;
            }

            doodad.Delete();
        }

        public void DoDespawn(Doodad doodad)
        {
            #region Schedule
            // First, let's check if the schedule has such an spawnerId
            if (GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)doodad.TemplateId)) // doodad.TemplateId
            {
                // if there is, we'll check the time for the spawning
                if (GameScheduleManager.Instance.CheckSpawnerInGameSchedules((int)doodad.TemplateId))
                {
                    var delay = GameScheduleManager.Instance.GetRemainingTime((int)doodad.TemplateId, false);
                    _log.Debug("DoDespawn: Doodad TemplateId {0}, objId {1} FuncGroupId {2} despawn [1] reschedule next time...", UnitId, Last.ObjId, Last.FuncGroupId);
                    _log.Debug("DoDespawn: delay {0}", delay.ToString());
                    TaskManager.Instance.Schedule(new DoodadSpawnerDoDespawnTask(doodad), delay);
                    return; // Reschedule when OK
                }

                // couldn't find it on the schedule, but it should have been!
                // no entries found for this unit in Game_Schedule table
                return;
            }
            #endregion Schedule

            Despawn(doodad);
            _log.Debug("DoDespawn: Doodad TemplateId {0}, objId {1} FuncGroupId {2} spawn [2] reschedule next time...", UnitId, Last.ObjId, Last.FuncGroupId);
            TaskManager.Instance.Schedule(new DoodadSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(1));
        }

        public void DoSpawn()
        {
            #region Schedule
            // First, let's check if the schedule has such an spawnerId
            if (GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)UnitId))
            {
                // if there is, we'll check the time for the spawning
                if (GameScheduleManager.Instance.CheckSpawnerInGameSchedules((int)UnitId))
                {
                    var delay = GameScheduleManager.Instance.GetRemainingTime((int)UnitId, true);
                    _permanent = false; // Doodad on the schedule.
                    _log.Debug("DoSpawn: Doodad TemplateId {0}, objId {1} FuncGroupId {2} despawn [1] reschedule next time...", UnitId, Last.ObjId, Last.FuncGroupId);
                    _log.Debug("DoSpawn: delay {0}", delay.ToString());
                    TaskManager.Instance.Schedule(new DoodadSpawnerDoSpawnTask(this), delay);
                    return; // Reschedule when OK
                }

                // couldn't find it on the schedule, but it should have been!
                // no entries found for this unit in Game_Schedule table
                //return;
                // All the same, we will be Spawn Doodad, since there was no record in Scheduler
                // Тем не менее, мы будем спавнить doodad, так как в планировщике не было никаких записей
            }
            #endregion Schedule

            Last.Spawn(); // initialize Doodad with the initial phase and display it on the terrain
            _spawned.Add(Last);
            if (!_permanent)
            {
                _log.Debug("DoSpawn: Doodad TemplateId {0}, objId {1} FuncGroupId {2} despawn [2] reschedule next time...", UnitId, Last.ObjId, Last.FuncGroupId);
                TaskManager.Instance.Schedule(new DoodadSpawnerDoDespawnTask(Last), TimeSpan.FromSeconds(1));
            }

            if (_scheduledCount > 0)
            {
                _scheduledCount--;
            }
            _spawnCount = _spawned.Count;
            if (_spawnCount < 0)
            {
                _spawnCount = 0;
            }
        }
    }
}
