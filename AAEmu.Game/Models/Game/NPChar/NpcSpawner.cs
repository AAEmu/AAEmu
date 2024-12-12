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

namespace AAEmu.Game.Models.Game.NPChar;

public class NpcSpawner : Spawner<Npc>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private List<Npc> _spawned; // the list of Npc's that have been shown
    private Npc _lastSpawn;      // the last of the displayed Npc
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
        _spawned = [];
        Count = 1;
        NpcSpawnerIds = [];
        Template = null;
        _lastSpawn = null;
    }

    /// <summary>
    /// Show all Npcs
    /// </summary>
    /// <returns></returns>
    public List<Npc> SpawnAll(bool beginning = false)
    {
        if (DoSpawnSchedule(true))
        {
            return null; // if npcs are delayed to show later
        }
        DoSpawn(true, beginning); // show all Npc, first start server

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
        npc.UnregisterNpcEvents();
        npc.Delete();

        /*
        if (npc.Transform.WorldId > 0)
        {
            // Temporary range for instanced worlds
            var dungeon = IndunManager.Instance.GetDungeonByWorldId(npc.Transform.WorldId);

            if (dungeon is not null)
            {
                dungeon.UnregisterNpcEvents(npc);
            }
        }
        */

        if (npc.Respawn == DateTime.MinValue)
        {
            npc.Spawner._spawned.Remove(npc);
            ObjectIdManager.Instance.ReleaseId(npc.ObjId);
            npc.Spawner._spawnCount--;
        }

        if (npc.Spawner._lastSpawn == null || npc.Spawner._lastSpawn.ObjId == npc.ObjId)
        {
            npc.Spawner._lastSpawn = npc.Spawner._spawned.Count != 0 ? npc.Spawner._spawned[^1] : null;
        }
    }

    public void ClearLastSpawnCount()
    {
        _spawnCount = 0;
    }
    public void DecreaseCount(Npc npc)
    {
        npc.Spawner._spawnCount--;
        npc.Spawner._spawned.Remove(npc);
        if (npc.Spawner.RespawnTime > 0 && npc.Spawner._spawnCount + npc.Spawner._scheduledCount < npc.Spawner.Count)
        {
            npc.Respawn = DateTime.UtcNow.AddSeconds(npc.Spawner.RespawnTime);
            SpawnManager.Instance.AddRespawn(npc);
            npc.Spawner._scheduledCount++;
        }

        npc.Despawn = DateTime.UtcNow.AddSeconds(npc.Spawner.DespawnTime);
        SpawnManager.Instance.AddDespawn(npc);
    }

    public void DespawnWithRespawn(Npc npc)
    {
        npc.Delete();
        npc.Spawner._spawnCount--;
        npc.Spawner._spawned.Remove(npc);
        if (npc.Spawner.RespawnTime > 0 && npc.Spawner._spawnCount + npc.Spawner._scheduledCount < npc.Spawner.Count)
        {
            npc.Respawn = DateTime.UtcNow.AddSeconds(npc.Spawner.RespawnTime);
            SpawnManager.Instance.AddRespawn(npc);
            npc.Spawner._scheduledCount++;
        }
    }

    /// <summary>
    /// Do despawn
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="all">to show everyone or not</param>
    public void DoDespawn(Npc npc, bool all = false)
    {
        if (npc == null) { return; }

        if (npc.IsInBattle)
        {
            return;
        }
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
    /// <param name="beginning">if this is the first start of the server</param>
    public void DoSpawn(bool all = false, bool beginning = false)
    {
        // проверим, что взяли все спавнеры
        // check what all spawners took
        var spawnerIds = NpcGameData.Instance.GetSpawnerIds(UnitId);
        var npcSpawnerIds = spawnerIds.Count > NpcSpawnerIds.Count ? spawnerIds : NpcSpawnerIds;

        // Select an NPC to spawn based on the spawnerId in npc_spawner_npcs
        var npcs = new List<Npc>();
        var delnpcs = new List<Npc>();

        foreach (var spawnerId in npcSpawnerIds)
        {
            var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
            if (template == null)
            {
                // Select an NPC to spawn based on the spawnerId in npc_spawner_npcs
                foreach (var nsn in Template.Npcs.Where(nsn => nsn.MemberId == UnitId))
                {
                    npcs = nsn.Spawn(this, all ? Template.MaxPopulation : 1);
                    if (npcs == null) { return; }
                    break;
                }
            }
            else
            {
                // cпавним всегда по возможности NpcSpawnerCategory.Autocreated
                // если это обычный спавн Npc, то пропускаем NpcSpawnerCategory.Normal
                // if it's a normal Npc spawn then skip NpcSpawnerCategory.Normal
                if (template.NpcSpawnerCategoryId == NpcSpawnerCategory.Normal && npcSpawnerIds.Count > 1)
                {
                    continue;
                }

                var suspendSpawnCount = template.SuspendSpawnCount > 0 ? template.SuspendSpawnCount : 1;
                var maxPopulation = template.MaxPopulation;
                var testRadiusPc = template.TestRadiusPc;
                var quantity = suspendSpawnCount;
                //var playerCount = 0u;

                // проверим есть ли рядом игроки
                // see if there are any players around
                //if (_lastSpawn != null)
                //{
                //    playerCount = (uint)WorldManager.GetAround<Character>(_lastSpawn, testRadiusPc).Count;
                //}

                // если рядом игроки, то увеличим количество Npc
                // if there are players around, we'll increase the number of Npc
                //if (playerCount > 1) { quantity = suspendSpawnCount * playerCount; }

                // проверим, что бы количество было не более максимальной популяции
                // check that the number is not more than the maximum population
                if (quantity > maxPopulation) { quantity = maxPopulation; }

                // если не хотим спавнить всех
                // if we don't want to spawn everyone
                if (!all) { quantity = 1; }

                // Check if we did not go over MaxPopulation Spawn Count
                if (_spawnCount > maxPopulation)
                {
                    Logger.Trace($"Let's not spawn Npc templateId {UnitId} from spawnerId {Template.Id} since exceeded MaxPopulation {maxPopulation}");
                    return;
                }

                foreach (var nsn in template.Npcs.Where(nsn => nsn.MemberId == UnitId))
                {
                    npcs = nsn.Spawn(this, quantity, maxPopulation);
                    break;
                }
                if (npcs == null) { continue; }
            }

            if (npcs.Count == 0)
            {
                Logger.Error($"Can't spawn npc {UnitId} from spawnerId {Template.Id}");
                continue;
            }

            delnpcs.AddRange(npcs);

            _spawned.AddRange(npcs);

            if (_scheduledCount > 0)
            {
                _scheduledCount -= npcs.Count;
            }

            _spawnCount += npcs.Count;

            if (_spawnCount < 0)
            {
                _spawnCount = 0;
            }

            _lastSpawn = _spawned[^1];
            //_lastSpawn.Spawner = this;
        }

        if (_isScheduled)
        {
            DoDespawnSchedule(_lastSpawn, all);
        }

        // удалим чуть позже всех лишних Npc, оставим только одного
        var deleteCount = delnpcs.Count - 1;
        if (deleteCount > 1)
        {
            for (var i = 0; i < deleteCount; i++)
            {
                Logger.Trace($"Let's schedule npc removal {UnitId} from spawnerId {Template.Id}");
                DoDespawnSchedule(delnpcs[i], false, 60); // через 1 минуту
            }
        }

        /*
        if (IsNullOrEmpty(FollowPath)) { return; }

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
        */
    }

    /// <summary>
    /// Do schedule spawn Npc
    /// </summary>
    /// <param name="all">to show everyone or not</param>
    private bool DoSpawnSchedule(bool all = false)
    {
        // TODO Check if delay is OK
        if (Template == null)
        {
            // no spawner for TemplateId
            Logger.Warn($"Can't spawn npc {UnitId} from spawnerId {Id}");
            return true;
        }

        // Check if population is within bounds
        if (_spawnCount >= Template.MaxPopulation)
        {
            Logger.Trace($"Let's not spawn Npc templateId {UnitId} from spawnerId {Template.Id} since exceeded MaxPopulation");
            return true;
        }

        #region Schedule
        _isScheduled = false;
        // Check if Time Of Day matches Template.StartTime or Template.EndTime
        if (Template.StartTime > 0.0f | Template.EndTime > 0.0f)
        {
            var curTime = TimeManager.Instance.GetTime;
            if (!TimeSpan.FromHours(curTime).IsBetween(TimeSpan.FromHours(Template.StartTime), TimeSpan.FromHours(Template.EndTime)))
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
                    delay = 5f;
                }
                _isScheduled = true; // Npc is on the schedule
                TaskManager.Instance.Schedule(new NpcSpawnerDoSpawnTask(this), TimeSpan.FromSeconds(delay));
                // Reschedule when OK
                return true;
            }
        }
        else
        {
            // спавнер присутствует в расписании `game_schedule_doodads`
            // First, let's check if the schedule has such an spawnerId
            var scheduleSpawner = GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)Template.Id);
            if (scheduleSpawner)
            {
                // спавнер присутствует в расписании `game_schedules`
                // if there is, we'll check the time for the spawning
                var inGameSchedule = GameScheduleManager.Instance.CheckSpawnerInGameSchedules((int)Template.Id);
                if (inGameSchedule)
                {
                    _isScheduled = true; // Npc is on the schedule
                    // период уже начался
                    // period has already started
                    var alreadyBegun = GameScheduleManager.Instance.PeriodHasAlreadyBegunNpc((int)Template.Id);
                    // есть в расписании такой spawner и есть время спавна
                    // there is such a spawner in the schedule and there is a spawn time
                    if (!alreadyBegun)
                    {
                        // есть в расписании, надо запланировать
                        // is on the schedule, needs to be scheduled
                        var cronExpression = GameScheduleManager.Instance.GetCronRemainingTime((int)Template.Id, true);
                        if (cronExpression is "" or "0 0 0 0 0 ?")
                        {
                            Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn Npc {UnitId} from spawnerId {Template.Id}");
                            Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                            _isScheduled = false;
                            return false;
                        }

                        try
                        {
                            TaskManager.Instance.CronSchedule(new NpcSpawnerDoSpawnTask(this), cronExpression);
                        }
                        catch (Exception)
                        {
                            Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn Npc {UnitId} from spawnerId {Template.Id}");
                            Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                            _isScheduled = false;
                            return false;
                        }

                        return true; // Reschedule when OK
                        // couldn't find it on the schedule, but it should have been!
                        // no entries found for this unit in Game_Schedule table
                    }
                }
            }
        }
        #endregion Schedule

        return false;
    }

    /// <summary>
    /// Do schedule despawn Npc
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="all">to show everyone or not</param>
    /// <param name="timeToDespawn"></param>
    private void DoDespawnSchedule(Npc npc, bool all = false, float timeToDespawn = 0)
    {
        #region Schedule
        // удалим по запросу
        if (timeToDespawn > 0)
        {
            TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(timeToDespawn));
            return; // Reschedule when OK
        }
        // Check if Time Of Day matches Template.StartTime or Template.EndTime

        if (Template.StartTime > 0.0f | Template.EndTime > 0.0f)
        {
            var curTime = TimeManager.Instance.GetTime;
            if (TimeSpan.FromHours(curTime).IsBetween(TimeSpan.FromHours(Template.StartTime), TimeSpan.FromHours(Template.EndTime)))
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
                    delay = 5f;
                }
                TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(delay));

                return; // Reschedule when OK
            }
        }
        else
        {
            // спавнер присутствует в расписании `game_schedule_doodads`
            // First, let's check if the schedule has such an spawnerId
            var scheduleSpawner = GameScheduleManager.Instance.CheckSpawnerInScheduleSpawners((int)Template.Id);
            if (scheduleSpawner)
            {
                // спавнер присутствует в расписании `game_schedules`
                // if there is, we'll check the time for the spawning
                var inGameSchedule = GameScheduleManager.Instance.CheckSpawnerInGameSchedules((int)Template.Id);
                if (inGameSchedule)
                {
                    // период уже начался
                    // period has already started
                    var alreadyBegun = GameScheduleManager.Instance.PeriodHasAlreadyBegunNpc((int)Template.Id);
                    // есть в расписании такой spawner и есть время спавна
                    // there is such a spawner in the schedule and there is a spawn time
                    if (alreadyBegun)
                    {
                        // есть в расписании, надо запланировать
                        // is on the schedule, needs to be scheduled
                        var cronExpression = GameScheduleManager.Instance.GetCronRemainingTime((int)Template.Id, false);
                        if (cronExpression is "" or "0 0 0 0 0 ?")
                        {
                            Logger.Warn($"DoDespawnSchedule: Can't reschedule despawn Npc {UnitId} from spawnerId {Template.Id}");
                            Logger.Warn($"DoDespawnSchedule: cronExpression {cronExpression}");
                            return;
                        }

                        try
                        {
                            TaskManager.Instance.CronSchedule(new NpcSpawnerDoDespawnTask(npc), cronExpression);
                        }
                        catch (Exception)
                        {
                            Logger.Warn($"DoDespawnSchedule: Can't reschedule despawn Npc {UnitId} from spawnerId {Template.Id}");
                            Logger.Warn($"DoDespawnSchedule: cronExpression {cronExpression}");
                            return;
                        }
                        // couldn't find it on the schedule, but it should have been!
                        // no entries found for this unit in Game_Schedule table
                    }
                }

                return; // Reschedule when OK
            }
        }
        #endregion Schedule

        DoDespawn(npc, all);
        DoSpawnSchedule(all);
    }

    public void DoEventSpawn()
    {
        // TODO Check if delay is OK
        if (Template == null)
        {
            // no spawner for TemplateId
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Id);
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
            //Logger.Debug("DoSpawn: Npc TemplateId {0}, spawnerId {1} reschedule next time...", UnitId, Id);
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
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Template.Id);
        }

        if (n.Count == 0)
        {
            Logger.Error("Can't spawn npc {0} from spawnerId {1}", UnitId, Template.Id);
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

    public void DoSpawnEffect(uint spawnerId, SpawnEffect effect, BaseUnit caster, BaseUnit target)
    {
        var template = NpcGameData.Instance.GetNpcSpawnerTemplate(spawnerId);
        if (template.Npcs == null)
        {
            return;
        }

        var n = new List<Npc>();
        foreach (var nsn in template.Npcs)
        {
            if (nsn == null || nsn.MemberId != UnitId)
                continue;

            n = nsn.Spawn(this, template.MaxPopulation);
            break;
        }

        try
        {
            if (n == null) { return; }

            foreach (var npc in n)
            {
                if (npc.Spawner != null)
                {
                    npc.Spawner.RespawnTime = 0; // don't respawn
                }

                if (effect.UseSummonerFaction)
                {
                    npc.Faction = target is Npc ? target.Faction : caster.Faction;
                }

                if (effect.UseSummonerAggroTarget && !effect.UseSummonerFaction)
                {
                    // TODO : Pick random target off of Aggro table ?

                    // Npc attacks the character
                    if (target is Npc)
                    {
                        npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)target, 1);
                    }
                    else
                    {
                        npc.Ai.Owner.AddUnitAggro(AggroKind.Damage, (Unit)caster, 1);
                    }

                    npc.Ai.OnAggroTargetChanged();
                    //npc.Ai.GoToCombat();
                }

                if (effect.LifeTime > 0)
                {
                    TaskManager.Instance.Schedule(new NpcSpawnerDoDespawnTask(npc), TimeSpan.FromSeconds(effect.LifeTime));
                }
            }
        }
        catch (Exception)
        {
            Logger.Error("Can't spawn npc {0} from spawner {1}", UnitId, template.Id);
            return;
        }
        if (n.Count == 0)
        {
            Logger.Error("Can't spawn npc {0} from spawner {1}", UnitId, template.Id);
            return;
        }
        _spawned.AddRange(n);
        if (_scheduledCount > 0)
        {
            _scheduledCount -= n.Count;
        }
        _spawnCount = _spawned.Count;
        if (_spawnCount < 0)
        {
            _spawnCount = 0;
        }
        _lastSpawn = n[^1];
        _lastSpawn.Spawner = this;
    }

    public void ClearSpawnCount()
    {
        _spawnCount = 0;
        _spawned = [];
        _lastSpawn = null;
    }

    public bool CanSpawn()
    {
        return _spawnCount < Template.MaxPopulation;
    }

    public Unit GetLastSpawn()
    {
        return _lastSpawn;
    }
}
