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

namespace AAEmu.Game.Models.Game.DoodadObj;

public class DoodadSpawner : Spawner<Doodad>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    public float Scale { get; set; }
    public Doodad Last { get; set; }

    private List<Doodad> _spawned;
    private int _scheduledCount;
    private int _spawnCount;

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
    [DefaultValue(1f)]
    public uint Count { get; set; } = 1;
    private bool _permanent { get; set; }
    public List<uint> RelatedIds { get; set; }
    //---
    public uint RespawnDoodadTemplateId { get; set; }

    public DoodadSpawner()
    {
        _permanent = true; // Doodad not on the schedule.
        _spawned = [];
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
        _spawned = [];
        Count = 1;
        Last = new Doodad();
        var character = WorldManager.Instance.GetCharacterByObjId(charId);
        var doodad = DoodadManager.Instance.Create(objId, UnitId, character);

        if (doodad == null)
        {
            Logger.Warn("Doodad {0}, from spawn not exist at db", UnitId);
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
            Logger.Error("Can't spawn doodad {1} from spawn {0}", Id, UnitId);
            return null;
        }

        Last = doodad;
        DoSpawn();// schedule check and spawn
        return doodad;
    }

    /// <summary>
    /// Spawn a doodad (mostly used by respawns)
    /// </summary>
    /// <param name="objId"></param>
    /// <returns></returns>
    public override Doodad Spawn(uint objId) // TODO: clean up each doodad uses the same call
    {
        _permanent = true; // Doodad not on the schedule.
        _spawned = [];
        Count = 1;
        Last = new Doodad();

        if (objId != 0) { return null; }

        var newUnitId = RespawnDoodadTemplateId > 0 ? RespawnDoodadTemplateId : UnitId;
        RespawnDoodadTemplateId = 0; // reset it after 1 spawn

        var doodad = DoodadManager.Instance.Create(objId, newUnitId);
        if (doodad == null)
        {
            Logger.Warn("Doodad Temaplte {0}, used in Spawn() does not exist in db", newUnitId);
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
            Logger.Error("Can't spawn doodad {1} from spawn {0}", Id, newUnitId);
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
        // спавнер присутствует в расписании `game_schedule_doodads`
        // First, let's check if the schedule has such an spawnerId
        var scheduleSpawner = GameScheduleManager.Instance.CheckDoodadInScheduleSpawners((int)UnitId);
        if (scheduleSpawner)
        {
            // спавнер присутствует в расписании `game_schedules`
            // if there is, we'll check the time for the spawning
            var inGameSchedule = GameScheduleManager.Instance.CheckDoodadInGameSchedules(UnitId);
            if (inGameSchedule)
            {
                // период уже начался
                // period has already started
                var alreadyBegun = GameScheduleManager.Instance.PeriodHasAlreadyBegunDoodad((int)UnitId);
                // есть в расписании такой spawner и есть время спавна
                // there is such a spawner in the schedule and there is a spawn time
                if (!alreadyBegun)
                {
                    // есть в расписании, надо запланировать
                    // is on the schedule, needs to be scheduled
                    var cronExpression =
                        GameScheduleManager.Instance.GetDoodadCronRemainingTime((int)doodad.TemplateId, false);
                    if (cronExpression is "" or "0 0 0 0 0 ?")
                    {
                        Logger.Warn($"DoSpawnSchedule: Can't reschedule despawn Doodad templateId={doodad.TemplateId} objId={doodad.ObjId}");
                        Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                    }
                    else
                    {
                        try
                        {
                            TaskManager.Instance.CronSchedule(new DoodadSpawnerDoDespawnTask(doodad), cronExpression);
                            return; // Reschedule when OK
                        }
                        catch (Exception)
                        {
                            Logger.Warn($"DoSpawnSchedule: Can't reschedule despawn Doodad templateId={doodad.TemplateId} objId={doodad.ObjId}");
                            Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                        }
                    }
                    // couldn't find it on the schedule, but it should have been!
                    // no entries found for this unit in Game_Schedule table
                    // All the same, we will be Spawn Doodad, since there was no record in Scheduler
                    // Тем не менее, мы будем спавнить doodad, так как в планировщике не было никаких записей
                }
            }
        }
        #endregion Schedule

        Despawn(doodad);
        if (scheduleSpawner)
        {
            var cronExpression = GameScheduleManager.Instance.GetDoodadCronRemainingTime((int)UnitId, false);
            if (cronExpression is "" or "0 0 0 0 0 ?")
            {
                Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn Doodad templateId={UnitId} objId={Last.ObjId}");
                Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
            }
            else
            {
                try
                {
                    Logger.Debug($"DoDespawn: Doodad TemplateId {doodad.TemplateId}, objId {doodad.ObjId} FuncGroupId {doodad.FuncGroupId}, cronExpression={cronExpression} spawn reschedule next time...");
                    TaskManager.Instance.CronSchedule(new DoodadSpawnerDoSpawnTask(this), cronExpression);
                }
                catch (Exception)
                {
                    Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn Doodad templateId={UnitId} objId={Last.ObjId}");
                    Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                }
            }
        }
    }

    public void DoSpawn()
    {
        #region Schedule
        // спавнер присутствует в расписании `game_schedule_doodads`
        // First, let's check if the schedule has such an spawnerId
        var scheduleSpawner = GameScheduleManager.Instance.CheckDoodadInScheduleSpawners((int)UnitId);
        if (scheduleSpawner)
        {
            // спавнер присутствует в расписании `game_schedules`
            // if there is, we'll check the time for the spawning
            var inGameSchedule = GameScheduleManager.Instance.CheckDoodadInGameSchedules(UnitId);
            if (inGameSchedule)
            {
                _permanent = false; // Doodad on the schedule.
                // период уже начался
                // period has already started
                var alreadyBegun = GameScheduleManager.Instance.PeriodHasAlreadyBegunDoodad((int)UnitId);
                // есть в расписании такой spawner и есть время спавна
                // there is such a spawner in the schedule and there is a spawn time
                if (!alreadyBegun)
                {
                    // есть в расписании, надо запланировать
                    // is on the schedule, needs to be scheduled
                    var cronExpression = GameScheduleManager.Instance.GetDoodadCronRemainingTime((int)UnitId, true);
                    if (cronExpression is "" or "0 0 0 0 0 ?")
                    {
                        Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn Doodad templateId={UnitId} objId={Last.ObjId}");
                        Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                        _permanent = true;
                    }
                    else
                    {
                        try
                        {
                            TaskManager.Instance.CronSchedule(new DoodadSpawnerDoSpawnTask(this), cronExpression);
                            return; // Reschedule when OK
                        }
                        catch (Exception)
                        {
                            Logger.Warn($"DoSpawnSchedule: Can't reschedule spawn Doodad templateId={UnitId} objId={Last.ObjId}");
                            Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                            _permanent = true;
                        }
                    }

                    // couldn't find it on the schedule, but it should have been!
                    // no entries found for this unit in Game_Schedule table
                    // All the same, we will be Spawn Doodad, since there was no record in Scheduler
                    // Тем не менее, мы будем спавнить doodad, так как в планировщике не было никаких записей
                }
            }
        }
        #endregion Schedule

        Last.Spawn(); // initialize Doodad with the initial phase and display it on the terrain

        var world = WorldManager.Instance.GetWorld(Last.Transform.WorldId);
        if (Last.Transform.WorldId > 0)
        {
            // Temporary range for instanced worlds
            var dungeon = IndunManager.Instance.GetDungeonByWorldId(Last.Transform.WorldId);

            if (dungeon is not null)
            {
                //dungeon.RegisterIndunEvents();
                world.Events.OnDoodadSpawn(world, new OnDoodadSpawnArgs { Doodad = Last });
            }
        }

        _spawned.Add(Last);

        if (_scheduledCount > 0)
        {
            _scheduledCount--;
        }
        _spawnCount = _spawned.Count;
        if (_spawnCount < 0)
        {
            _spawnCount = 0;
        }

        if (!_permanent)
        {
            var cronExpression = GameScheduleManager.Instance.GetDoodadCronRemainingTime((int)Last.TemplateId, false);
            if (cronExpression is "" or "0 0 0 0 0 ?")
            {
                Logger.Warn($"DoSpawnSchedule: Can't reschedule despawn Doodad templateId={Last.TemplateId} objId={Last.ObjId}");
                Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
            }
            else
            {
                try
                {
                    TaskManager.Instance.CronSchedule(new DoodadSpawnerDoDespawnTask(Last), cronExpression);
                }
                catch (Exception)
                {
                    Logger.Warn($"DoSpawnSchedule: Can't reschedule despawn Doodad templateId={Last.TemplateId} objId={Last.ObjId}");
                    Logger.Warn($"DoSpawnSchedule: cronExpression {cronExpression}");
                }
            }
            //TaskManager.Instance.Schedule(new DoodadSpawnerDoDespawnTask(Last), TimeSpan.FromSeconds(1));
        }
    }
}
