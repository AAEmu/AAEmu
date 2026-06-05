using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.TowerDefs;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class TowerDefGameData : Singleton<TowerDefGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, TowerDef> _towerDefs;
    private Dictionary<uint, TowerDefProg> _towerDefProgs;

    public TowerDef GetTowerDef(uint id)
    {
        return _towerDefs != null && _towerDefs.TryGetValue(id, out var def) ? def : null;
    }

    public IReadOnlyCollection<TowerDef> GetAllTowerDefs()
    {
        return (IReadOnlyCollection<TowerDef>)_towerDefs?.Values ?? Array.Empty<TowerDef>();
    }

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
                        TimeOfDay = reader.GetFloat("tod"),
                        FirstWaveAfter = reader.GetFloat("first_wave_after"),
                        TargetNpcSpawnId = reader.GetUInt32("target_npc_spawner_id", 0),
                        KillNpcId = reader.GetUInt32("kill_npc_id", 0),
                        KillNpcCount = reader.GetUInt32("kill_npc_count", 0),
                        ForceEndTime = reader.GetFloat("force_end_time"),
                        TimeOfDayDayInterval = reader.GetUInt32("tod_day_interval"),
                        Progs = []
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
                        continue;

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
                        continue;

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
        // Quick sanity log so we can verify the loader works end-to-end after a server restart.
        // Each TowerDef row should now carry its progs + spawn/kill targets.
        var defs = _towerDefs?.Count ?? 0;
        var progs = _towerDefProgs?.Count ?? 0;
        var spawnRows = 0;
        var killRows = 0;
        if (_towerDefProgs != null)
        {
            foreach (var p in _towerDefProgs.Values)
            {
                spawnRows += p.SpawnTargets?.Count ?? 0;
                killRows += p.KillTargets?.Count ?? 0;
            }
        }
        Logger.Info($"TowerDefGameData: {defs} tower_defs / {progs} progs / {spawnRows} spawn-targets / {killRows} kill-targets");

        // Halcyona War (id=18) deep-check: dump its prog layout so we can spot wiring bugs at a glance.
        if (_towerDefs != null && _towerDefs.TryGetValue(18u, out var halcyona))
        {
            Logger.Info($"  Halcyona War (id=18): {halcyona.Progs?.Count ?? 0} progs, force_end={halcyona.ForceEndTime}s, target_npc_spawner={halcyona.TargetNpcSpawnId}");
            if (halcyona.Progs != null)
            {
                foreach (var prog in halcyona.Progs)
                {
                    Logger.Info($"    prog id={prog.Id} cond_to_next={prog.CondToNextTime}s and={prog.CondCompByAnd} spawn={prog.SpawnTargets?.Count ?? 0} kill={prog.KillTargets?.Count ?? 0}");
                }
            }
        }
    }
}
