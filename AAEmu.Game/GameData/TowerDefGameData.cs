using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class TowerDefGameData : Singleton<TowerDefGameData>, IGameDataLoader
{
    private Dictionary<uint, TowerDef> _towerDefs;
    private Dictionary<uint, TowerDefProg> _towerDefProgs;

    public void Load(SqliteConnection connection)
    {
        _towerDefs = [];
        _towerDefProgs = [];

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_defs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TowerDef
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name", ""),
                        StartMsg = reader.GetString("start_msg", ""),
                        EndMsg = reader.GetString("end_msg", ""),
                        TitleMsg = reader.GetString("title_msg", ""),
                        TimeOfDay = reader.GetFloat("tod"),
                        FirstWaveAfter = reader.GetFloat("first_wave_after"),
                        TargetNpcSpawnId = reader.GetUInt32("target_npc_spawner_id", 0),
                        KillNpcId = reader.GetUInt32("kill_npc_id", 0),
                        KillNpcCount = reader.GetUInt32("kill_npc_count", 0),
                        ForceEndTime = reader.GetFloat("force_end_time"),
                        TimeOfDayDayInterval = reader.GetUInt32("tod_day_interval"),
                        MilestoneId = reader.GetUInt32("milestone_id", 0),
                        BroadcastToWholeWorld = reader.GetBoolean("broadcast_event_to_whole_seamless_world", true),
                        StartDayOfWeekBit = reader.GetUInt32("start_day_of_week_bit", 0),
                        Progs = []
                    };

                    // start_hour/start_minute is the Sunday slot; start_hourN is day N. A 00:00
                    // pair means the event does not run that day — every row that genuinely wants
                    // a midnight start uses 00:01 (망자 시스템 runs 00:01 on all seven days).
                    for (var day = 0; day < 7; day++)
                    {
                        var suffix = day == 0 ? "" : day.ToString();
                        var hour = reader.GetInt32($"start_hour{suffix}", 0);
                        var minute = reader.GetInt32($"start_minute{suffix}", 0);
                        if (hour == 0 && minute == 0)
                            continue;
                        if (hour is < 0 or > 23 || minute is < 0 or > 59)
                            continue;

                        template.StartTimes[day] = new TimeSpan(hour, minute, 0);
                    }

                    _towerDefs.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_def_progs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var towerDefId = reader.GetUInt32("tower_def_id");
                    if (!_towerDefs.TryGetValue(towerDefId, out var towerDef))
                        return;

                    var template = new TowerDefProg
                    {
                        Id = reader.GetUInt32("id"),
                        TowerDef = towerDef,
                        CondToNextTime = reader.GetFloat("cond_to_next_time"),
                        CondCompByAnd = reader.GetBoolean("cond_comp_by_and", true),
                        KillTargets = [],
                        SpawnTargets = []
                    };

                    towerDef.Progs.Add(template);
                    _towerDefProgs.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_def_prog_spawn_targets";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var towerDefProgId = reader.GetUInt32("tower_def_prog_id");
                    if (!_towerDefProgs.TryGetValue(towerDefProgId, out var towerDefProg))
                        return;

                    var template = new TowerDefProgSpawnTarget
                    {
                        Id = reader.GetUInt32("id"),
                        SpawnTargetId = reader.GetUInt32("spawn_target_id"),
                        SpawnTargetType = reader.GetString("spawn_target_type"),
                        DespawnOnNextStep = reader.GetBoolean("despawn_on_next_step", true),
                        TowerDefProg = towerDefProg
                    };

                    towerDefProg.SpawnTargets.Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_def_prog_kill_targets";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var towerDefProgId = reader.GetUInt32("tower_def_prog_id");
                    if (!_towerDefProgs.TryGetValue(towerDefProgId, out var towerDefProg))
                        return;

                    var template = new TowerDefProgKillTarget
                    {
                        Id = reader.GetUInt32("id"),
                        KillTargetId = reader.GetUInt32("kill_target_id"),
                        KillTargetType = reader.GetString("kill_target_type"),
                        KillCount = reader.GetUInt32("kill_count"),
                        TowerDefProg = towerDefProg
                    };

                    towerDefProg.KillTargets.Add(template);
                }
            }
        }
    }

    public void PostLoad()
    {
    }

    public TowerDef GetTowerDef(uint id) => _towerDefs.GetValueOrDefault(id);

    public IReadOnlyCollection<TowerDef> GetAllTowerDefs() => _towerDefs.Values;

    /// <summary>
    /// The events that carry a wall-clock start slot on at least one weekday — the timed world
    /// events (world bosses, ghost ship, dragon invasions) as opposed to the rows driven purely by
    /// kill counts or quest triggers.
    /// </summary>
    public IEnumerable<TowerDef> GetScheduledTowerDefs()
    {
        foreach (var towerDef in _towerDefs.Values)
        {
            if (towerDef.IsScheduled)
                yield return towerDef;
        }
    }
}
