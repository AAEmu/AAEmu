using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

using AAEmu.Commons.Utils;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.World;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Models.Game.NPChar
{
    public class NpcSpawner : Spawner<Npc>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private List<Npc> _spawned; // the list of Npc's that have been shown
        public Npc _lastSpawn;      // the last of the displayed Npc
        private int _scheduledCount; // already scheduled to show Npc
        private int _spawnCount;     // have already shown so many Npc

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        [DefaultValue(1f)]
        public uint Count { get; set; }
        public List<uint> NpcSpawnerIds { get; set; }
        private bool _isScheduled { get; set; }
        public NpcSpawnerTemplate Template { get; set; } // npcSpawnerId(NpcSpawnerTemplateId), template

        public NpcSpawner()
        {
            _isScheduled = false; // Npc isn't on the schedule
            _spawned = new List<Npc>();
            Count = 1;
            NpcSpawnerIds = new List<uint>();
            Template = new NpcSpawnerTemplate();
            _lastSpawn = new Npc();
        }

        /// <summary>
        /// Show all Npcs
        /// </summary>
        /// <returns></returns>
        public List<Npc> SpawnAll()
        {
            if (DoSpawnSchedule(true))
            {
                return null; // if npcs are delayed to show later
            }
            DoSpawn(true); // show all Npc

            return _spawned;
        }

        /// <summary>
        /// Show one Npc
        /// </summary>
        /// <param name="objId"></param>
        /// <returns></returns>
        public override Npc Spawn(uint objId)
        {
            if (DoSpawnSchedule())
            {
                return null; // if npcs are delayed to show later
            }
            DoSpawn(); // show one Npc
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

        /// <summary>
        /// Do despawn
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="all">to show everyone or not</param>
        public void DoDespawn(Npc npc, bool all = false)
        {
            if (all)
            {
                for (var i = 0; i < _spawnCount; i++)
                {
                    Despawn(npc.Spawner._lastSpawn);
                }
            }
            else
            {
                Despawn(npc);
            }
        }

        /// <summary>
        /// Do spawn Npc
        /// </summary>
        /// <param name="all">to show everyone or not</param>
        public void DoSpawn(bool all = false)
        {
            //// Check if we did not go over Suspend Spawn Count
            //if (Template.SuspendSpawnCount > 0 && _spawnCount > Template.SuspendSpawnCount)
            //{
            //    //if (UnitId == 3321)
            //    {
            //        _log.Debug($"DoSpawn: Npc TemplateId {UnitId}, NpcSpawnerId {Id} spawn [3] reschedule next time...");
            //    }
            //    return;
            //}

            // Select an NPC to spawn based on the spawnerId in npc_spawner_npcs
            var npcs = new List<Npc>();
            foreach (var nsn in Template.Npcs)
            {
                if (nsn.MemberId != UnitId) { continue; }
                npcs = nsn.Spawn(this, all ? Template.MaxPopulation : 1);
                break;
            }
            _spawned.AddRange(npcs);
            if (npcs.Count == 0)
            {
                _log.Error($"Can't spawn npc {UnitId} from spawn {Id}, spawner {Template.Id}");
                return;
            }
            if (_scheduledCount > 0)
            {
                _scheduledCount -= npcs.Count;
            }
            _spawnCount = npcs.Count;
            if (_spawnCount < 0)
            {
                _spawnCount = 0;
            }
            _lastSpawn = _spawned[^1];
            for (var i = 0; i < _spawnCount; i++)
            {
                _spawned[i].Spawner = this;
            }

            if (_isScheduled)
            {
                //if (UnitId == 3321) // 13126
                //{
                //    _log.Debug($"DoSpawn: Npc TemplateId {UnitId}, NpcSpawnerId {Id}, MaxPopulation {Template.MaxPopulation} spawned...");
                //}
                DoDespawnSchedule(_lastSpawn, all);
            }

            if (!string.IsNullOrEmpty(FollowPath))
            {
                foreach (var npc in npcs)
                {
                    if (npc.IsInPatrol) { return; }
                    npc.IsInPatrol = true;
                    npc.Simulation.RunningMode = false;
                    npc.Simulation.Cycle = true;
                    npc.Simulation.MoveToPathEnabled = false;
                    npc.Simulation.MoveFileName = FollowPath;
                    npc.Simulation.GoToPath(npc, true);
                }
            }
        }

        /// <summary>
        /// Do schedule spawn Npc
        /// </summary>
        /// <param name="all">to show everyone or not</param>
        public bool DoSpawnSchedule(bool all = false)
        {
            // TODO Check if delay is OK
            if (Template == null)
            {
                // no spawner for TemplateId
                _log.Warn($"Can't spawn npc {UnitId} from spawn {Id}, npcSpawnerId {Id}");
                return true;
            }

            // Check if population is within bounds
            if (_spawnCount >= Template.MaxPopulation)
            {
                _log.Warn($"DoSpawn: Npc TemplateId {UnitId}, NpcSpawnerId {Id} достигли максимальной популяции...");
                return true;
            }

            #region Schedule

            _isScheduled = false;
            // Check if Time Of Day matches Template.StartTime or Template.EndTime
            if (Template.StartTime > 0.0f | Template.EndTime > 0.0f)
            {
                var curTime = TimeManager.Instance.GetTime();
                if (!TimeSpanExtensions.IsBetween(TimeSpan.FromHours(curTime), TimeSpan.FromHours(Template.StartTime), TimeSpan.FromHours(Template.EndTime)))
                {
                    var start = (int)Math.Round(Template.StartTime);
                    if (start == 0) { start = 24; }
                    var delay = start - curTime;
                    if (delay < 0f)
                    {
                        delay = curTime + delay;
                    }
                    delay = delay * 60f * 10f;
                    if (delay < 1f)
                    {
                        //if (UnitId == 3321)
                        //{
                        //    _log.Debug($"DoSpawnSchedule: Npc TemplateId {UnitId}, NpcSpawnerId {Id} spawn time...");
                        //    _log.Debug($"DoSpawnSchedule: delay {delay} sec");
                        //    _log.Debug($"DoSpawnSchedule: curTime {curTime}, StartTime {Template.StartTime}, EndTime {Template.EndTime}");
                        //}
                        delay = 5f;
                    }
                    //if (UnitId == 3321)
                    //{
                    //_log.Warn($"DoSpawnSchedule: Npc TemplateId {UnitId}, NpcSpawnerId {Id}, spawner {Template.Id} spawn reschedule next time...");
                    //_log.Warn($"DoSpawnSchedule: delay {delay} sec");
                    //_log.Warn($"DoSpawnSchedule: curTime {curTime}, StartTime {Template.StartTime}, EndTime {Template.EndTime}");
                    //}
                    _isScheduled = true; // Npc is on the schedule
                    TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(delay));
                    // Reschedule when OK
                    return true;
                }
            }
            // First, let's check if the schedule has such an spawnerId
            else if (GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)Template.Id))
            {
                _isScheduled = true; // Npc is on the schedule
                
                // if there is, we'll check the time for the spawning
                if (GameScheduleManager.Instance.CheckSpawnerInGameSchedules((int)Template.Id))
                {
                    // есть в расписании, надо спавнить сейчас
                    return false;
                }
                else
                {
                    // есть в расписании, надо запланировать
                    var cronExpression = GameScheduleManager.Instance.GetCronRemainingTime((int)Template.Id, true);

                    if (cronExpression == String.Empty || cronExpression == "0 0 0 0 0 ?")
                    {
                        _log.Warn($"DoSpawnSchedule: Can't reschedule spawn npc {UnitId} from spawn {Id}, spawner {Template.Id}");
                        _log.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                        return false;
                    }

                    TaskManager.Instance.CronSchedule(new NpcSpawnerDoSpawnTask(this), cronExpression);

                    ////if (UnitId == 3321)
                    //{
                    //_log.Warn($"DoSpawnSchedule: Npc TemplateId {UnitId}, NpcSpawnerId {Id}, spawner {Template.Id} spawn reschedule next time...");
                    //_log.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                    //}
                    return true; // Reschedule when OK
                }

                // couldn't find it on the schedule, but it should have been!
                // no entries found for this unit in Game_Schedule table
            }

            #endregion Schedule

            // if it's not on the schedule, we'll just spawn it right away.
            //if (UnitId == 3321)
            //{
            //    _log.Debug($"DoSpawnSchedule: Npc TemplateId {UnitId}, NpcTemplate.Id {Id} spawn time...");
            //    var curTime = TimeManager.Instance.GetTime();
            //    _log.Debug($"DoSpawnSchedule: curTime {curTime}, StartTime {Template.StartTime}, EndTime {Template.EndTime}");
            //}
            //DoSpawn(all);
            return false;
        }

        /// <summary>
        /// Do schedule despawn Npc
        /// </summary>
        /// <param name="npc"></param>
        /// <param name="all">to show everyone or not</param>
        private void DoDespawnSchedule(Npc npc, bool all = false)
        {
            #region Schedule

            // Check if Time Of Day matches Template.StartTime or Template.EndTime
            if (Template.StartTime > 0.0f | Template.EndTime > 0.0f)
            {
                var curTime = TimeManager.Instance.GetTime();
                if (TimeSpanExtensions.IsBetween(TimeSpan.FromHours(curTime), TimeSpan.FromHours(Template.StartTime), TimeSpan.FromHours(Template.EndTime)))
                {
                    var end = (int)Math.Round(Template.EndTime);
                    if (end == 0) { end = 24; }
                    var delay = end - curTime;
                    if (delay < 0f)
                    {
                        delay = curTime + delay;
                    }
                    delay = delay * 60f * 10f;
                    if (delay < 1f)
                    {
                        //if (UnitId == 3321)
                        //{
                        //    _log.Debug($"DoDespawnSchedule: Npc TemplateId {UnitId}, NpcTemplate.Id {Id} despawn time...");
                        //    _log.Debug($"DoDespawnSchedule: delay {delay} sec");
                        //    _log.Debug($"DoDespawnSchedule: curTime {curTime}, StartTime {Template.StartTime}, EndTime {Template.EndTime}");
                        //}
                        delay = 5f;
                    }
                    //if (UnitId == 3321)
                    //{
                    //    _log.Debug($"DoDespawnSchedule: Npc TemplateId {UnitId}, NpcTemplate.Id {Id} despawn reschedule next time...");
                    //    _log.Debug($"DoDespawnSchedule: delay {delay} sec");
                    //    _log.Debug($"DoDespawnSchedule: curTime {curTime}, StartTime {Template.StartTime}, EndTime {Template.EndTime}");
                    //}
                    TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(delay));
                    return; // Reschedule when OK
                }
            }
            // First, let's check if the schedule has such an Template.Id
            else if (GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)Template.Id))
            {
                var cronExpression = GameScheduleManager.Instance.GetCronRemainingTime((int)Template.Id, true);

                if (cronExpression == String.Empty || cronExpression == "0 0 0 0 0 ?")
                {
                    _log.Warn($"DoDespawnSchedule: Can't reschedule despawn npc {UnitId} from spawn {Id}, spawner {Template.Id}");
                    _log.Warn($"DoDespawnSchedule: cronExpression {cronExpression}");
                    return;
                }
                TaskManager.Instance.CronSchedule(new NpcSpawnerDoDespawnTask(npc), cronExpression);

                ////if (UnitId == 3321)
                //{
                //_log.Warn($"DoDespawnSchedule: Npc TemplateId {UnitId}, NpcSpawnerId {Id}, spawner {Template.Id} despawn reschedule next time...");
                //_log.Warn($"DoDespawnSchedule: cronExpression {cronExpression}");
                //}
                return; // Reschedule when OK
            }

            #endregion Schedule

            //if (UnitId == 3321)
            //{
            //    _log.Debug($"Despawn: Npc TemplateId {UnitId}, NpcTemplate.Id {Id} despawn time...");
            //    var curTime = TimeManager.Instance.GetTime();
            //    _log.Debug($"DoDespawnSchedule: curTime {curTime}, StartTime {Template.StartTime}, EndTime {Template.EndTime}");
            //}

            DoDespawn(npc, all);
            DoSpawnSchedule(all);
        }

        public void DoEventSpawn()
        {
            // TODO Check if delay is OK
            if (Template == null)
            {
                // no spawner for TemplateId
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, Id);
                return;
            }

            // Check if population is within bounds
            if (_spawnCount >= Template.MaxPopulation)
            {
                return;
            }

            // Check if we did not go over Suspend Spawn Count
            if (Template.SuspendSpawnCount > 0 && _spawnCount > Template.SuspendSpawnCount)
            {
                //_log.Debug("DoSpawn: Npc TemplateId {0}, NpcTemplate.Id {1} spawn [3] reschedule next time...", UnitId, Id);
                //TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(60));
                return;
            }

            // Select an NPC to spawn based on the Template.Id in npc_spawner_npcs
            var n = new List<Npc>();
            foreach (var nsn in Template.Npcs)
            {
                if (nsn.MemberId != UnitId) { continue; }
                n = nsn.Spawn(this);
                break;
            }

            try
            {
                foreach (var npc in n)
                {
                    _spawned.Add(npc);
                }
            }
            catch (Exception)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, Template.Id);
            }

            if (n.Count == 0)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, Template.Id);
                return;
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
        }

        public void DoSpawnEffect(uint spawnerId, SpawnEffect effect, Unit caster, BaseUnit target)
        {
            var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
            if (template.Npcs == null)
            {
                return;
            }

            var n = new List<Npc>();
            foreach (var nsn in template.Npcs.Where(nsn => nsn.MemberId == UnitId))
            {
                n = nsn.Spawn(this, template.MaxPopulation);
                break;
            }

            try
            {
                foreach (var npc in n)
                {
                    _spawned.Add(npc);
                    npc.Spawner.RespawnTime = 0; // don't respawn

                    if (effect.UseSummonerFaction)
                    {
                        npc.Faction = target is Npc ? target.Faction : caster.Faction;
                    }

                    if (effect.UseSummonerAggroTarget)
                    {
                        // TODO : Pick random target off of Aggro table ?

                        // Npc attacks the character
                        if (target is Npc)
                        {
                            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)target, 1);
                        }
                        else
                        {
                            npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, caster, 1);
                        }
                        npc.Ai.OnAggroTargetChanged();
                        npc.Ai.GoToCombat();
                    }

                    if (effect.LifeTime > 0)
                    {
                        TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(effect.LifeTime));
                    }

                    //if (effect.LifeTime > 0)
                    //{
                    //    TaskManager.Instance.Schedule(new DespawnTask(npc), TimeSpan.FromSeconds(effect.LifeTime));
                    //}
                }
            }
            catch (Exception)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, template.Id);
            }

            if (n.Count == 0)
            {
                _log.Error("Can't spawn npc {1} from spawn {0}, spawner {2}", Id, UnitId, template.Id);
                return;
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
        }
    }
}
