using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class ZoneManager(
    IWorldManager worldManager,
    IConflictZoneRuntimeStore runtimeStore = null) : Singleton<ZoneManager>, IZoneManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, uint> _zoneIdToKey;
    private Dictionary<uint, Zone> _zones;
    private Dictionary<uint, ZoneGroup> _groups;
    private Dictionary<ushort, ZoneConflict> _conflicts;
    private Dictionary<uint, ZoneGroupBannedTag> _groupBannedTags;
    private Dictionary<uint, ZoneClimateElem> _climateElem;
    private readonly IConflictZoneRuntimeStore _runtimeStore = runtimeStore;

    public event Action<ushort, ZoneConflictType, ZoneConflictType> ZoneConflictStateChanged;

    // Null-safe: Zone hosts can announce ZoneLoaded before this manager's Load() completes.
    public ZoneConflict[] GetConflicts() => _conflicts?.Values.ToArray() ?? [];

    /// <summary>
    /// Arms the wall-clock cycle for every conflict zone that has <c>conflict_zone_realtime_schedules</c>
    /// rows. Zones without a schedule keep the participation-driven cycle and stay in Tension until a
    /// kill escalation starts them. Call once at boot, after game data is loaded and the task manager
    /// is running.
    /// </summary>
    public void StartConflictCycles()
    {
        var scheduled = 0;
        var now = DateTime.Now;
        IReadOnlyDictionary<ushort, ConflictZoneRuntimeState> saved = new Dictionary<ushort, ConflictZoneRuntimeState>();
        if (_runtimeStore != null)
            saved = _runtimeStore.LoadAll();
        foreach (var conflict in _conflicts.Values)
        {
            var schedule = ConflictZoneGameData.Instance.GetSchedule(conflict.ZoneGroupId);
            if (schedule.Count > 0)
            {
                conflict.BindSchedule(schedule, now);
                scheduled++;
            }
            else
            {
                var starts = conflict.DailyWarStarts
                    .Where(x => ConflictZoneScheduleRules.DecodeDailyWarStart(x).HasValue)
                    .ToArray();
                if (starts.Length > 0)
                {
                    if (conflict.BindDailyWarWindows(starts, now))
                        scheduled++;
                    else if (saved.TryGetValue(conflict.ZoneGroupId, out var state))
                        conflict.RestoreRuntimeState(state, now.ToUniversalTime());
                }
                else if (saved.TryGetValue(conflict.ZoneGroupId, out var state))
                {
                    conflict.RestoreRuntimeState(state, now.ToUniversalTime());
                }
            }
        }

        Logger.Info("Started {0} scheduled conflict-zone cycles ({1} conflict zones loaded)", scheduled, _conflicts.Count);
    }

    /// <summary>
    /// Records an NPC death for conflict-zone participation. The kill only counts when the NPC
    /// template is listed for that zone group in <c>conflict_zone_npc_kills</c>.
    /// </summary>
    public void RegisterNpcKill(Npc npc)
    {
        var zoneGroupId = GetZoneGroupIdForPosition(npc?.Transform);
        if (zoneGroupId is not { } group)
            return;

        var conflict = _conflicts.GetValueOrDefault(group);
        if (conflict == null || !ConflictZoneGameData.Instance.IsParticipatingNpc(group, npc.TemplateId))
            return;

        conflict.AddNpcKill();
        Logger.Debug("Conflict zone {0}: counted NPC kill tpl={1} (npcKills={2}, state={3})",
            group, npc.TemplateId, conflict.NpcKillCount, conflict.CurrentZoneState);
    }

    /// <summary>
    /// Records a finished quest for conflict-zone participation. The completion only counts when the
    /// quest is listed for that zone group in <c>conflict_zone_quest_completions</c>.
    /// </summary>
    public void RegisterQuestCompletion(Character character, uint questId)
    {
        var zoneGroupId = GetZoneGroupIdForPosition(character?.Transform);
        if (zoneGroupId is not { } group)
            return;

        var conflict = _conflicts.GetValueOrDefault(group);
        if (conflict == null || !ConflictZoneGameData.Instance.IsParticipatingQuest(group, questId))
            return;

        conflict.AddQuestCompletion();
        Logger.Debug("Conflict zone {0}: counted quest completion {1} (quests={2}, state={3})",
            group, questId, conflict.QuestCompletionCount, conflict.CurrentZoneState);
    }

    private ushort? GetZoneGroupIdForPosition(Transform transform)
    {
        if (transform == null)
            return null;

        var zone = GetZoneByKey(transform.ZoneId);
        return zone == null ? null : (ushort)zone.GroupId;
    }

    public Zone GetZoneById(uint zoneId)
    {
        return _zoneIdToKey.TryGetValue(zoneId, out var value) ? _zones[value] : null;
    }

    public Zone GetZoneByKey(uint zoneKey)
    {
        return _zones.TryGetValue(zoneKey, out var zone) ? zone : null;
    }

    public ZoneGroup GetZoneGroupById(uint zoneId)
    {
        return _groups.TryGetValue(zoneId, out var group) ? group : null;
    }

    public List<uint> GetZoneKeysInZoneGroupById(uint zoneGroupId)
    {
        var res = new List<uint>();
        foreach (var z in _zones)
            if (z.Value.GroupId == zoneGroupId)
                res.Add(z.Value.ZoneKey);
        return res;
    }

    public uint GetTargetIdByZoneId(uint zoneId)
    {
        var zone = GetZoneByKey(zoneId);
        if (zone == null) return 0;
        var zoneGroup = GetZoneGroupById(zone.GroupId);
        return zoneGroup?.TargetId ?? 0;
    }

    public void Load()
    {
        _zoneIdToKey = [];
        _zones = [];
        _groups = [];
        _conflicts = [];
        _groupBannedTags = [];
        _climateElem = [];
        Logger.Info("Loading ZoneManager...");
        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM zones";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Zone
                        {
                            Id = reader.GetUInt32("id"), Name = (string)reader.GetValue("name"), ZoneKey = reader.GetUInt32("zone_key"),
                            GroupId = reader.GetUInt32("group_id", 0),
                            Closed = reader.GetBoolean("closed", true),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0),
                            ZoneClimateId = reader.GetUInt32("zone_climate_id", 0)
                        };
                        _zoneIdToKey.Add(template.Id, template.ZoneKey);
                        _zones.Add(template.ZoneKey, template);
                    }
                }
            }

            Logger.Info("Loaded {0} zones", _zones.Count);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM zone_groups";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ZoneGroup
                        {
                            Id = reader.GetUInt32("id"), Name = (string)reader.GetValue("name"), X = reader.GetFloat("x"),
                            Y = reader.GetFloat("y"),
                            Width = reader.GetFloat("w"),
                            Hight = reader.GetFloat("h"),
                            TargetId = reader.GetUInt32("target_id"),
                            FactionChatRegionId = reader.GetUInt32("faction_chat_region_id"),
                            PirateDesperado = reader.GetBoolean("pirate_desperado", true),
                            FishingSeaLootPackId = reader.GetUInt32("fishing_sea_loot_pack_id", 0),
                            FishingLandLootPackId = reader.GetUInt32("fishing_land_loot_pack_id", 0),
                            // 1.2 added BuffId
                            BuffId = reader.GetUInt32("buff_id", 0)
                        };
                        _groups.Add(template.Id, template);
                    }
                }
            }

            Logger.Info("Loaded {0} groups", _groups.Count);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM conflict_zones";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var zoneGroupId = reader.GetUInt16("zone_group_id");
                        if (_groups.ContainsKey(zoneGroupId))
                        {
                            var template = new ZoneConflict(
                                _groups[zoneGroupId],
                                OnZoneConflictStateChanged,
                                _runtimeStore == null ? null : _runtimeStore.Save)
                            {
                                ZoneGroupId = zoneGroupId
                            };

                            for (var i = 0; i < 5; i++)
                            {
                                template.NumKills[i] = reader.GetInt32($"num_kills_{i}");
                                template.NumNpcKills[i] = reader.GetInt32($"num_npc_kills_{i}", 0);
                                template.NumQuestCompletions[i] = reader.GetInt32($"num_quest_completions_{i}", 0);
                            }

                            var noKillMinutes = new int[ConflictZoneNoKillDecayMetadata.TroubleStateCount];
                            for (var i = 0; i < noKillMinutes.Length; i++)
                                noKillMinutes[i] = reader.GetInt32($"no_kill_min_{i}");
                            template.BindNoKillDecayMetadata(
                                new ConflictZoneNoKillDecayMetadata(zoneGroupId, noKillMinutes));

                            for (var i = 0; i < template.DailyWarStarts.Length; i++)
                            {
                                template.DailyWarStarts[i] = new ConflictZoneDailyWarStart(
                                    reader.GetInt32($"war_st_hour_{i}", -1),
                                    reader.GetInt32($"war_st_min_{i}", 0));
                            }

                            template.ConflictMin = reader.GetInt32("conflict_min");
                            template.WarMin = reader.GetInt32("war_min");
                            template.PeaceMin = reader.GetInt32("peace_min");

                            template.PeaceProtectedFactionId = reader.GetUInt32("peace_protected_faction_id", 0);
                            template.NuiaReturnPointId = reader.GetUInt32("nuia_return_point_id", 0);
                            template.HariharaReturnPointId = reader.GetUInt32("harihara_return_point_id", 0);
                            template.WarTowerDefId = reader.GetUInt32("war_tower_def_id", 0);
                            template.PeaceTowerDefId = reader.GetUInt32("peace_tower_def_id", 0);
                            template.Closed = reader.GetBoolean("closed", true);

                            _groups[zoneGroupId].Conflict = template;
                            _conflicts.Add(zoneGroupId, template);
                        }
                        else
                            Logger.Warn("ZoneGroupId: {0} doesn't exist for conflict", zoneGroupId);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM zone_group_banned_tags";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ZoneGroupBannedTag
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneGroupId = reader.GetUInt32("zone_group_id"),
                            TagId = reader.GetUInt32("tag_id")
                        };
                        template.BannedPeriods = reader.GetUInt32("banned_periods");
                        _groupBannedTags.Add(template.Id, template);
                    }
                }
            }

            Logger.Info("Loaded {0} group banned tags", _groupBannedTags.Count);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM zone_climate_elems";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ZoneClimateElem
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneClimateId = reader.GetUInt32("zone_climate_id"),
                            ClimateId = (Climate)reader.GetUInt32("climate_id")
                        };
                        _climateElem.Add(template.Id, template);
                    }
                }
            }

            Logger.Info("Loaded {0} climate elems", _climateElem.Count);
        }

        var missingNoKillMetadata = _conflicts.Values
            .Where(conflict => conflict.NoKillDecayMetadata == null)
            .Select(conflict => conflict.ZoneGroupId)
            .ToArray();
        if (missingNoKillMetadata.Length > 0)
        {
            throw new InvalidOperationException(
                $"Conflict zones missing no-kill metadata: {string.Join(',', missingNoKillMetadata)}.");
        }

        var configuredNoKillMetadata = _conflicts.Values.Count(conflict => conflict.NoKillDecayMetadata.IsConfigured);
        Logger.Info(
            "Loaded conflict-zone no-kill metadata: {0} rows, {1} configured; runtime decay remains deferred until semantics are evidenced",
            _conflicts.Count,
            configuredNoKillMetadata);
    }

    private void OnZoneConflictStateChanged(
        ushort zoneGroupId,
        ZoneConflictType previousState,
        ZoneConflictType currentState)
    {
        var listeners = ZoneConflictStateChanged;
        if (listeners == null)
            return;

        foreach (Action<ushort, ZoneConflictType, ZoneConflictType> listener in listeners.GetInvocationList())
        {
            try
            {
                listener(zoneGroupId, previousState, currentState);
            }
            catch (Exception exception)
            {
                Logger.Error(
                    exception,
                    "ZoneGroup {0}: state-change listener failed for {1} -> {2}",
                    zoneGroupId,
                    previousState,
                    currentState);
            }
        }
    }

    public Vector2 GetZoneOriginCell(uint zoneId)
    {
        var world = worldManager.GetWorldTemplateByZoneKey(zoneId);
        if (world?.XmlWorldZones.TryGetValue(zoneId, out var xmlZone) ?? false)
        {
            return new Vector2(xmlZone.OriginX, xmlZone.OriginY);
        }
        return new Vector2();
    }

    /// <summary>
    /// translate the local coordinates to the world coordinates using the original coordinates of the cells for the zone
    /// </summary>
    /// <param name="zoneId">zoneKey</param>
    /// <param name="point">offset inside the zone</param>
    /// <returns></returns>
    public Vector3 ConvertToWorldCoordinates(uint zoneId, Vector3 point)
    {
        var origin = GetZoneOriginCell(zoneId);

        var newX = origin.X * 1024f + point.X;
        var newY = origin.Y * 1024f + point.Y;

        return new Vector3(newX, newY, point.Z);
    }

    /// <summary>
    /// Inverse of <see cref="ConvertToWorldCoordinates"/>. The dedicate keeps spawner and level
    /// geometry in the zone-local space of npc_spawners.g, so anything compared against it
    /// (WZActivateNpcSpawnersInArea) has to be translated back out of world space first.
    /// </summary>
    public Vector3 ConvertToLocalCoordinates(uint zoneId, Vector3 point)
    {
        var origin = GetZoneOriginCell(zoneId);

        return new Vector3(point.X - origin.X * 1024f, point.Y - origin.Y * 1024f, point.Z);
    }

    public List<Climate> GetClimatesByZone(Zone zone)
    {
        var res = new List<Climate>();
        foreach (var zoneClimateElem in _climateElem.Values)
        {
            if (zoneClimateElem.ZoneClimateId == zone.ZoneClimateId)
                res.Add(zoneClimateElem.ClimateId);
        }
        return res;
    }

    /// <summary>
    /// Checks if a doodad is located in a matching climate
    /// </summary>
    /// <param name="doodad"></param>
    /// <returns>Returns true if the doodad can have a growth time bonus, false if out of climate, or no climate defined for the doodad</returns>
    public bool DoodadHasMatchingClimate(Doodad doodad)
    {
        // If no climate defined, then don't give a bonus
        if (doodad.Template == null || doodad.Template.ClimateId == Climate.None || doodad.Template.ClimateId == Climate.Any)
            return false;

        // Get doodad's zone (if missing zoneId (key)
        if (doodad.Transform.ZoneId <= 0)
        {
            // If ZoneId wasn't set yet, calculate it
            var zoneId = worldManager.GetZoneId(doodad.ParentWorld.Template, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y);
            doodad.Transform.ZoneId = zoneId;
        }
        var zone = GetZoneByKey(doodad.Transform.ZoneId);
        if (zone == null)
            return false;

        // Get the climates list for this zone
        var zoneClimates = GetClimatesByZone(zone);

        // Check if it's in there
        return zoneClimates.Contains(doodad.Template.ClimateId);
    }
}
