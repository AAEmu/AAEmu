using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers.World
{
    public class ZoneManager : Singleton<ZoneManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, uint> _zoneIdToKey;
        private Dictionary<uint, Zone> _zones;
        private Dictionary<uint, ZoneGroup> _groups;
        private Dictionary<ushort, ZoneConflict> _conflicts;
        private Dictionary<uint, ZoneGroupBannedTag> _groupBannedTags;
        private Dictionary<uint, ZoneClimateElem> _climateElem;

        public ZoneConflict[] GetConflicts() => _conflicts.Values.ToArray();

        public Zone GetZoneById(uint zoneId)
        {
            return _zoneIdToKey.ContainsKey(zoneId) ? _zones[_zoneIdToKey[zoneId]] : null;
        }

        public Zone GetZoneByKey(uint zoneKey)
        {
            return _zones.ContainsKey(zoneKey) ? _zones[zoneKey] : null;
        }

        public ZoneGroup GetZoneGroupById(uint zoneId)
        {
            return _groups.ContainsKey(zoneId) ? _groups[zoneId] : null;
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

        public (float, float, float) ConvertCoordFromZoneKey(uint zoneId, float x, float y, float z)
        {
            //if (Instance.GetZoneByKey(zoneId) == null)
            //{
            //    return (x, y, z); // если не нашли зону, вернем координаты
            //}
            //var zonegroup = Instance.GetZoneGroupById(Instance.GetZoneByKey(zoneId).GroupId);
            //var newX = zonegroup.X + x;
            //var newY = zonegroup.Y + y;
            //var newZ = z;
            var zone = GetZoneByKey(zoneId);
            if (zone == null)
                return (x, y, z); // если не нашли зону, вернем координаты

            var zoneGroup = GetZoneGroupById(zone.GroupId);
            var newX = zoneGroup.X + x + 2412f; //2411.19535f; // w_solzreed id=5, x=11925, y=13266
            var newY = zoneGroup.Y + y + 46f;
            var newZ = z;
            //var newX = x; //для теста, берём координаты из файла
            //var newY = y;
            //var newZ = z;

            return (newX, newY, newZ);
        }

        /// <summary>
        /// Переведём локальные координаты в мировые в зависимости от zoneId
        /// </summary>
        /// <param name="zoneId"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        public (float, float, float) ConvertCoord0(uint zoneId, float x, float y, float z)
        {
            var zone = GetZoneByKey(zoneId);
            if (zone == null)
                return (x, y, z); // если не нашли зону, вернем координаты

            var zoneGroup = GetZoneGroupById(zone.GroupId);
            var newX = zoneGroup.X + x; // w_solzreed id=5, x=11925.0, y=13266.0
            var newY = zoneGroup.Y + y;

            return (newX, newY, z);
        }

        public (float, float, float) ConvertToWorldCoord(uint zoneId, Vector3 point)
        {
            Vector2 offsets = new Vector2();
            var zone = GetZoneByKey(zoneId);
            if (zone == null)
                return (point.X, point.Y, point.Z); // если не нашли зону, вернем координаты

            var zoneGroup = GetZoneGroupById(zone.GroupId);
            switch (zoneId)
            {
                case 129: // zone_group=1 -
                    offsets = new Vector2(1352.02f, 139.95f);
                    break;
                case 140: // zone_group=3 +++
                    offsets = new Vector2(1329.411429f, -895.9959f); // 49=(1329.046829f, -897.2141f) 65=(1329.411429f, -895.9959f)
                    break;
                case 141: // zone_group=4 -
                    offsets = new Vector2(1845.532429f, -113.2284f);
                    break;
                case 142: // zone_group=5   +                         -
                    offsets = new Vector2(363.0062f, 46.0064f); //387.766f, 36.206f);
                    break;
                case 143: // zone_group=18  +++                    -
                    offsets = new Vector2(495f, -550.9975f); //404.1797f, -508.763f);
                    break;
                case 144: // zone_group=6 -
                    offsets = new Vector2(329.3186f, -271.2882f);
                    break;
                case 146: // zone_group=7 -
                    offsets = new Vector2(1396f, -970f);
                    break;
                case 178: // zone_group=5 +++
                    offsets = new Vector2(2410.951f, 46.011f);
                    break;
                case 179: // zone_group=5 -
                    offsets = new Vector2(2410.951f, 46.011f);
                    break;
                case 181: // zone_group=1 -
                    offsets = new Vector2(1352.02f, 139.95f);
                    break;
                case 182: // zone_group=1 +++
                    offsets = new Vector2(1347.07935f, 154.05155f);
                    break;
                case 185: // zone_group=3 -
                    offsets = new Vector2(1845.532429f, -113.2284f);
                    break;
                case 188: // zone_group=7 -
                    offsets = new Vector2(1396f, -970f);
                    break;
                case 189: // zone_group=7 -
                    offsets = new Vector2(1396f, -970f);
                    break;
                case 191: // zone_group=4 -
                    offsets = new Vector2(1845.532429f, -113.2284f);
                    break;
                case 195: // zone_group=6   +                               -
                    offsets = new Vector2(345.736673f, -277.809482f); //344.929873f, -283.508042f);
                    break;
                case 197: // zone_group=7 -
                    offsets = new Vector2(1396f, -970f);
                    break;
                case 244: // zone_group=18 -
                    offsets = new Vector2(404.1797f, -508.763f);
                    break;
            }

            var newX = zoneGroup.X + point.X + offsets.X;
            var newY = zoneGroup.Y + point.Y + offsets.Y;

            return (newX, newY, point.Z);
        }

        /// <summary>
        /// Переведём локальные координаты в мировые с использованием константы зависимой от zoneId
        /// </summary>
        /// <param name="zoneId"></param>
        /// <param name="point"></param>
        /// <param name="coefficient"></param>
        /// <returns></returns>
        public (float, float, float) ConvertCoord2(uint zoneId, Vector3 point, Vector2 coefficient)
        {
            var zone = GetZoneByKey(zoneId);
            if (zone == null)
                return (point.X, point.Y, point.Z); // если не нашли зону, вернем координаты

            var zoneGroup = GetZoneGroupById(zone.GroupId);

            var newX = coefficient.X + point.X + zoneGroup.X;
            var newY = coefficient.Y + point.Y + zoneGroup.Y;

            return (newX, newY, point.Z);
        }

        /// <summary>
        /// Найти коэффициент для перевода локальных координат в мировые координаты
        /// </summary>
        /// <param name="zoneId"></param>
        /// <param name="point">точка возле остановки с мировыми координатами</param>
        /// <param name="target">нулевая точка начального участка пути из файла</param>
        /// <returns></returns>
        public Vector2 GetCoefficients0(uint zoneId, Vector3 point, Vector3 target)
        {
            var coefficient = new Vector2();
            var zone = GetZoneByKey(zoneId);
            var zoneGroup = GetZoneGroupById(zone.GroupId);
            coefficient.X = point.X - zoneGroup.X - target.X;
            coefficient.Y = point.Y - zoneGroup.Y - target.Y;
            return coefficient;
        }

        /// <summary>
        /// Найти коэффициент для перевода локальных координат в мировые координаты
        /// </summary>
        /// <param name="zoneId"></param>
        /// <param name="target">нулевая точка начального участка пути из файла</param>
        /// <returns></returns>
        public Vector2 GetCoefficients(uint zoneId, Vector3 target)
        {
            var coefficient = new Vector2();
            Vector3 tmp;
            var zone = GetZoneByKey(zoneId);
            var zoneGroup = GetZoneGroupById(zone.GroupId);
            switch (zoneGroup.Id)
            {
                case 5:
                    tmp = new Vector3(15663f, 15020f, 0);
                    coefficient = GetCoefficients0(zoneId, tmp, target);
                    break;
                case 6:
                    tmp = new Vector3(12609f, 15359f, 0);
                    coefficient = GetCoefficients0(zoneGroup.Id, tmp, target);
                    break;
            }
            return coefficient;
        }

        public (float, float, float) ConvertCoordFromZoneKey(uint zoneId, Vector3 position)
        {
            var zone = GetZoneByKey(zoneId);
            if (zone == null)
                return (position.X, position.Y, position.Z); // если не нашли зону, вернем координаты

            var zoneGroup = GetZoneGroupById(zone.GroupId);

            var a = (32768 + position.Z) / 27.712812f + 1f;
            var b = (32768 + position.X) / 32f + 0.5f;
            var c = a * 0.5f + b;
            var d = c - a * 2f;
            var e = (float)(d * 0.33333334d + 0.0000099999997d);

            var newX = e * 2f + a;
            var newY = a * 0.5f;
            var newZ = position.Z;

            return (newX, newY, newZ);
        }

        public void Load()
        {
            _zoneIdToKey = new Dictionary<uint, uint>();
            _zones = new Dictionary<uint, Zone>();
            _groups = new Dictionary<uint, ZoneGroup>();
            _conflicts = new Dictionary<ushort, ZoneConflict>();
            _groupBannedTags = new Dictionary<uint, ZoneGroupBannedTag>();
            _climateElem = new Dictionary<uint, ZoneClimateElem>();
            _log.Info("Loading ZoneManager...");
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
                            template.FactionId = reader.GetUInt32("faction_id", 0);
                            template.ZoneClimateId = reader.GetUInt32("zone_climate_id", 0);
                            _zoneIdToKey.Add(template.Id, template.ZoneKey);
                            _zones.Add(template.ZoneKey, template);
                        }
                    }
                }

                _log.Info("Loaded {0} zones", _zones.Count);

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
                            // TODO 1.2 // template.BuffId = reader.GetUInt32("buff_id", 0);
                            _groups.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loaded {0} groups", _groups.Count);

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
                                    template.SetState(ZoneConflictType.Conflict); // Set to Conflict for testing, normally it should start at Tension
                            }
                            else
                                _log.Warn("ZoneGroupId: {1} doesn't exist for conflict", zoneGroupId);
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

                _log.Info("Loaded {0} group banned tags", _groupBannedTags.Count);
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
                            template.ClimateId = reader.GetUInt32("climate_id");
                            _climateElem.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loaded {0} climate elems", _climateElem.Count);
            }
        }
    }
}
