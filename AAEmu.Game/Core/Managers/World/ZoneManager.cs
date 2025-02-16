using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class ZoneManager : Singleton<ZoneManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, uint> _zoneIdToKey;
    private Dictionary<uint, Zone> _zones;
    private Dictionary<uint, ZoneGroup> _groups;
    private Dictionary<ushort, ZoneConflict> _conflicts;
    private Dictionary<uint, ZoneGroupBannedTag> _groupBannedTags;
    private Dictionary<uint, ZoneClimateElem> _climateElem;

    public ZoneConflict[] GetConflicts() => _conflicts.Values.ToArray();

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
        _zoneIdToKey = new Dictionary<uint, uint>();
        _zones = new Dictionary<uint, Zone>();
        _groups = new Dictionary<uint, ZoneGroup>();
        _conflicts = new Dictionary<ushort, ZoneConflict>();
        _groupBannedTags = new Dictionary<uint, ZoneGroupBannedTag>();
        _climateElem = new Dictionary<uint, ZoneClimateElem>();
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
                        var template = new Zone();
                        template.Id = reader.GetUInt32("id");
                        template.Name = (string)reader.GetValue("name");
                        template.ZoneKey = reader.GetUInt32("zone_key");
                        template.GroupId = reader.GetUInt32("group_id", 0);
                        template.Closed = reader.GetBoolean("closed", true);
                        template.FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0);
                        template.ZoneClimateId = reader.GetUInt32("zone_climate_id", 0);
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
                        var template = new ZoneGroup();
                        template.Id = reader.GetUInt32("id");
                        template.Name = (string)reader.GetValue("name");
                        template.X = reader.GetFloat("x");
                        template.Y = reader.GetFloat("y");
                        template.Width = reader.GetFloat("w");
                        template.Hight = reader.GetFloat("h");
                        template.TargetId = reader.GetUInt32("target_id");
                        template.FactionChatRegionId = reader.GetUInt32("faction_chat_region_id");
                        template.PirateDesperado = reader.GetBoolean("pirate_desperado", true);
                        template.FishingSeaLootPackId = reader.GetUInt32("fishing_sea_loot_pack_id", 0);
                        template.FishingLandLootPackId = reader.GetUInt32("fishing_land_loot_pack_id", 0);
                        // 1.2 added BuffId
                        template.BuffId = reader.GetUInt32("buff_id", 0);
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
                        ushort zoneGroupId; // AAFree добавили остров zoneGroupId = 9000001;
                        try
                        {
                            zoneGroupId = reader.GetUInt16("zone_group_id");
                        }
                        catch (Exception)
                        {
                            continue;
                        }

                        if (_groups.ContainsKey(zoneGroupId))
                        {
                            var template = new ZoneConflict(_groups[zoneGroupId]);
                            template.ZoneGroupId = zoneGroupId;

                            for (var i = 0; i < 5; i++)
                            {
                                template.NumKills[i] = reader.GetInt32($"num_kills_{i}");
                                template.NoKillMin[i] = reader.GetInt32($"no_kill_min_{i}");
                            }

                            template.ConflictMin = reader.GetInt32("conflict_min");
                            template.WarMin = reader.GetInt32("war_min");
                            template.PeaceMin = reader.GetInt32("peace_min");

                            template.PeaceProtectedFactionId = reader.GetUInt32("peace_protected_faction_id", 0);
                            template.NuiaReturnPointId = reader.GetUInt32("nuia_return_point_id", 0);
                            template.HariharaReturnPointId = reader.GetUInt32("harihara_return_point_id", 0);
                            template.WarTowerDefId = reader.GetUInt32("war_tower_def_id", 0);
                            // TODO 1.2 // template.PeaceTowerDefId = reader.GetUInt32("peace_tower_def_id", 0);
                            template.Closed = reader.GetBoolean("closed", true);

                            _groups[zoneGroupId].Conflict = template;
                            _conflicts.Add(zoneGroupId, template);

                            // Only do intial setup when the zone isn't closed
                            if (!template.Closed)
                                template.SetState(ZoneConflictType
                                    .Conflict); // Set to Conflict for testing, normally it should start at Tension
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
                        var template = new ZoneGroupBannedTag();
                        template.Id = reader.GetUInt32("id");
                        template.ZoneGroupId = reader.GetUInt32("zone_group_id");
                        template.TagId = reader.GetUInt32("tag_id");
                        // TODO 1.2 // template.BannedPeriodsId = reader.GetUInt32("banned_periods_id");
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
                        var template = new ZoneClimateElem();
                        template.Id = reader.GetUInt32("id");
                        template.ZoneClimateId = reader.GetUInt32("zone_climate_id");
                        template.ClimateId = (Climate)reader.GetUInt32("climate_id");
                        _climateElem.Add(template.Id, template);
                    }
                }
            }

            Logger.Info("Loaded {0} climate elems", _climateElem.Count);
        }
    }

    public static Vector2 GetZoneOriginCell(uint zoneId)
    {
        var world = WorldManager.Instance.GetWorldByZone(zoneId);
        if (world != null && world.XmlWorldZones.TryGetValue(zoneId, out var xmlZone))
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
    public static Vector3 ConvertToWorldCoordinates(uint zoneId, Vector3 point)
    {
        var origin = GetZoneOriginCell(zoneId);

        var newX = origin.X * 1024f + point.X;
        var newY = origin.Y * 1024f + point.Y;

        return new Vector3(newX, newY, point.Z);
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
    public static bool DoodadHasMatchingClimate(Doodad doodad)
    {
        // If no climate defined, then don't give a bonus
        if (doodad.Template.ClimateId == Climate.Any || doodad.Template.ClimateId == Climate.Any)
            return false;

        // Get doodad's zone (if missing zoneId (key)
        if (doodad.Transform.ZoneId <= 0)
        {
            // If ZoneId wasn't set yet, calculate it
            var zoneId = WorldManager.Instance.GetZoneId(doodad.Transform.WorldId, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y);
            doodad.Transform.ZoneId = zoneId;
        }
        var zone = ZoneManager.Instance.GetZoneByKey(doodad.Transform.ZoneId);
        if (zone == null)
        {
            return false;
        }
        // Get the climates list for this zone
        var zoneClimates = ZoneManager.Instance.GetClimatesByZone(zone);

        // Check if it's in there
        return zoneClimates.Contains(doodad.Template.ClimateId);
    }

    public uint GetZoneIdByKey(uint zoneKey)
    {
        _zones.TryGetValue(zoneKey, out var zone);
        return zone?.GroupId ?? 0;
    }
}
