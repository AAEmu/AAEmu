using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class TowerDefGameData : Singleton<TowerDefGameData>, IGameDataLoader
{
    public Dictionary<uint, TowerDef> _towerDefs;
    private Dictionary<uint, TowerDefProg> _towerDefProgs;

    public void Load(SqliteConnection connection)
    {
        _towerDefs = new Dictionary<uint, TowerDef>();
        _towerDefProgs = new Dictionary<uint, TowerDefProg>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM tower_defs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new TowerDef()
                    {
                        Id = reader.GetUInt32("id"),
                        StartMsg = LocalizationManager.Instance.Get("tower_defs", "start_msg", reader.GetUInt32("id"),
                            reader.GetString("start_msg")),
                        EndMsg = LocalizationManager.Instance.Get("tower_defs", "end_msg", reader.GetUInt32("id"),
                            reader.GetString("end_msg")),
                        TimeOfDay = reader.GetFloat("tod"),
                        FirstWaveAfter = reader.GetFloat("first_wave_after"),
                        TargetNpcSpawnId = reader.GetUInt32("target_npc_spawner_id", 0),
                        KillNpcId = reader.GetUInt32("kill_npc_id", 0),
                        KillNpcCount = reader.GetUInt32("kill_npc_count", 0),
                        ForceEndTime = reader.GetFloat("force_end_time"),
                        TimeOfDayDayInterval = reader.GetUInt32("tod_day_interval"),
                        Progs = new List<TowerDefProg>()
                    };

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
                        continue;

                    var template = new TowerDefProg()
                    {
                        Id = reader.GetUInt32("id"),
                        Msg = LocalizationManager.Instance.Get("tower_def_progs", "msg", reader.GetUInt32("id"),
                            reader.GetString("msg")),
                        TowerDef = towerDef,
                        CondToNextTime = reader.GetFloat("cond_to_next_time"),
                        CondCompByAnd = reader.GetBoolean("cond_comp_by_and", true),
                        KillTargets = new List<TowerDefProgKillTarget>(),
                        SpawnTargets = new List<TowerDefProgSpawnTarget>()
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
                        continue;

                    var template = new TowerDefProgSpawnTarget()
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
                        continue;

                    var template = new TowerDefProgKillTarget()
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
}
