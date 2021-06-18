using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AAEmu.Commons.Utils;
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
                            template.Name = (string) reader.GetValue("name");
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
                                template.Closed = reader.GetBoolean("closed",true);

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
            /// <summary>
        /// translate the local coordinates to the world coordinates using the original coordinates of the cells for the zone
        /// </summary>
        /// <param name="zoneId"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        public (float, float, float) ConvertToWorldCoordinates(uint zoneId, Vector3 point)
        {
            var origin = new Vector2();

            switch (zoneId)
            {
                // from game\world.xml for version 1.2.0.0
                case 129: //  <Zone name="w_gweonid_forest_1" id="129" originX="9" originY="14">
                    origin = new Vector2(9f, 14f);
                    break;
                case 133: //  <Zone name="w_marianople_1" id="133" originX="10" originY="10">
                    origin = new Vector2(10f, 10f);
                    break;
                case 136: //  <Zone name="e_steppe_belt_1" id="136" originX="22" originY="5">
                    origin = new Vector2(22f, 5f);
                    break;
                case 137: //  <Zone name="e_ruins_of_hariharalaya_1" id="137" originX="25" originY="5">
                    origin = new Vector2(25f, 5f);
                    break;
                case 138: //  <Zone name="e_lokas_checkers_1" id="138" originX="23" originY="7">
                    origin = new Vector2(23f, 7f);
                    break;
                case 139: //  <Zone name="e_ynystere_1" id="139" originX="18" originY="11">
                    origin = new Vector2(18f, 11f);
                    break;
                case 140: //  <Zone name="w_garangdol_plains_1" id="140" originX="11" originY="11">
                    origin = new Vector2(11f, 11f);
                    break;
                case 141: //  <Zone name="e_sunrise_peninsula_1" id="141" originX="14" originY="6">
                    origin = new Vector2(14f, 6f);
                    break;
                case 142: //  <Zone name="w_solzreed_1" id="142" originX="12" originY="13">
                    origin = new Vector2(12f, 13f);
                    break;
                case 143: //  <Zone name="w_white_forest_1" id="143" originX="8" originY="11">
                    origin = new Vector2(8f, 11f);
                    break;
                case 144: //  <Zone name="w_lilyut_meadow_1" id="144" originX="11" originY="14">
                    origin = new Vector2(11f, 14f);
                    break;
                case 145: //  <Zone name="w_the_carcass_1" id="145" originX="8" originY="15">
                    origin = new Vector2(8f, 15f);
                    break;
                case 146: //  <Zone name="e_rainbow_field_1" id="146" originX="18" originY="6">
                    origin = new Vector2(18f, 6f);
                    break;
                case 148: //  <Zone name="w_cross_plains_1" id="148" originX="13" originY="10">
                    origin = new Vector2(13f, 10f);
                    break;
                case 149: //  <Zone name="w_two_crowns_1" id="149" originX="11" originY="9">
                    origin = new Vector2(11f, 9f);
                    break;
                case 150: //  <Zone name="w_cradle_of_genesis" id="150" originX="6" originY="11">
                    origin = new Vector2(6f, 11f);
                    break;
                case 151: //  <Zone name="w_golden_plains_1" id="151" originX="6" originY="8">
                    origin = new Vector2(6f, 8f);
                    break;
                case 153: //  <Zone name="e_mahadevi_1" id="153" originX="17" originY="7">
                    origin = new Vector2(17f, 7f);
                    break;
                case 154: //  <Zone name="w_bronze_rock_1" id="154" originX="5" originY="11">
                    origin = new Vector2(5f, 11f);
                    break;
                case 155: //  <Zone name="e_hasla_1" id="155" originX="27" originY="6">
                    origin = new Vector2(27f, 6f);
                    break;
                case 156: //  <Zone name="e_falcony_plateau_1" id="156" originX="21" originY="7">
                    origin = new Vector2(21f, 7f);
                    break;
                case 157: //  <Zone name="e_sunny_wilderness_1" id="157" originX="18" originY="4">
                    origin = new Vector2(18f, 4f);
                    break;
                case 158: //  <Zone name="e_tiger_spine_mountains_1" id="158" originX="20" originY="7">
                    origin = new Vector2(20f, 7f);
                    break;
                case 159: //  <Zone name="e_ancient_forest" id="159" originX="21" originY="10">
                    origin = new Vector2(21f, 10f);
                    break;
                case 160: //  <Zone name="e_singing_land_1" id="160" originX="20" originY="9">
                    origin = new Vector2(20f, 9f);
                    break;
                case 161: //  <Zone name="w_hell_swamp_1" id="161" originX="6" originY="7">
                    origin = new Vector2(6f, 7f);
                    break;
                case 162: //  <Zone name="w_long_sand_1" id="162" originX="7" originY="7">
                    origin = new Vector2(7f, 7f);
                    break;
                case 164: //  <Zone name="w_barren_land" id="164" originX="8" originY="13">
                    origin = new Vector2(8f, 13f);
                    break;
                case 172: //  <Zone name="s_lost_island" id="172" originX="13" originY="8">
                    origin = new Vector2(13f, 8f);
                    break;
                case 173: //  <Zone name="s_lostway_sea" id="173" originX="15" originY="9">
                    origin = new Vector2(15f, 9f);
                    break;
                case 178: //  <Zone name="w_solzreed_2" id="178" originX="14" originY="13">
                    origin = new Vector2(14f, 13f);
                    break;
                case 179: //  <Zone name="w_solzreed_3" id="179" originX="14" originY="13">
                    origin = new Vector2(14f, 13f);
                    break;
                case 180: //  <Zone name="s_silent_sea_7" id="180" originX="15" originY="13">
                    origin = new Vector2(15f, 13f);
                    break;
                case 181: //  <Zone name="w_gweonid_forest_2" id="181" originX="9" originY="14">
                    origin = new Vector2(9f, 14f);
                    break;
                case 182: //  <Zone name="w_gweonid_forest_3" id="182" originX="10" originY="14">
                    origin = new Vector2(10f, 14f);
                    break;
                case 183: //  <Zone name="w_marianople_2" id="183" originX="10" originY="11">
                    origin = new Vector2(10f, 11f);
                    break;
                case 184: //  <Zone name="e_falcony_plateau_2" id="184" originX="23" originY="7">
                    origin = new Vector2(23f, 7f);
                    break;
                case 185: //  <Zone name="w_garangdol_plains_2" id="185" originX="11" originY="12">
                    origin = new Vector2(11f, 12f);
                    break;
                case 186: //  <Zone name="w_two_crowns_2" id="186" originX="12" originY="9">
                    origin = new Vector2(12f, 9f);
                    break;
                case 187: //  <Zone name="e_rainbow_field_2" id="187" originX="18" originY="6">
                    origin = new Vector2(18f, 6f);
                    break;
                case 188: //  <Zone name="e_rainbow_field_3" id="188" originX="19" originY="6">
                    origin = new Vector2(19f, 6f);
                    break;
                case 189: //  <Zone name="e_rainbow_field_4" id="189" originX="20" originY="6">
                    origin = new Vector2(20f, 6f);
                    break;
                case 190: //  <Zone name="e_sunny_wilderness_2" id="190" originX="21" originY="5">
                    origin = new Vector2(21f, 5f);
                    break;
                case 191: //  <Zone name="e_sunrise_peninsula_2" id="191" originX="15" originY="8">
                    origin = new Vector2(15f, 8f);
                    break;
                case 192: //  <Zone name="w_bronze_rock_2" id="192" originX="5" originY="13">
                    origin = new Vector2(5f, 13f);
                    break;
                case 193: //  <Zone name="w_bronze_rock_3" id="193" originX="6" originY="13">
                    origin = new Vector2(6f, 13f);
                    break;
                case 194: //  <Zone name="e_singing_land_2" id="194" originX="19" originY="9">
                    origin = new Vector2(19f, 9f);
                    break;
                case 195: //  <Zone name="w_lilyut_meadow_2" id="195" originX="11" originY="14">
                    origin = new Vector2(11f, 14f);
                    break;
                case 196: //  <Zone name="e_mahadevi_2" id="196" originX="18" originY="7">
                    origin = new Vector2(18f, 7f);
                    break;
                case 197: //  <Zone name="e_mahadevi_3" id="197" originX="19" originY="7">
                    origin = new Vector2(19f, 7f);
                    break;
                case 200: //  <Zone name="o_temp_d" id="200" originX="19" originY="29">
                    origin = new Vector2(19f, 29f);
                    break;
                case 201: //  <Zone name="o_temp_c" id="201" originX="14" originY="29">
                    origin = new Vector2(14f, 29f);
                    break;
                case 202: //  <Zone name="o_temp_b" id="202" originX="19" originY="24">
                    origin = new Vector2(19f, 24f);
                    break;
                case 203: //  <Zone name="o_temp_a" id="203" originX="14" originY="24">
                    origin = new Vector2(14f, 24f);
                    break;
                case 204: //  <Zone name="o_salpimari" id="204" originX="18" originY="22">
                    origin = new Vector2(18f, 22f);
                    break;
                case 205: //  <Zone name="o_nuimari" id="205" originX="20" originY="22">
                    origin = new Vector2(20f, 22f);
                    break;
                case 206: //  <Zone name="w_golden_plains_2" id="206" originX="7" originY="9">
                    origin = new Vector2(7f, 9f);
                    break;
                case 207: //  <Zone name="w_golden_plains_3" id="207" originX="9" originY="9">
                    origin = new Vector2(9f, 9f);
                    break;
                case 209: //  <Zone name="w_dark_side_of_the_moon" id="209" originX="11" originY="16">
                    origin = new Vector2(11f, 16f);
                    break;
                case 210: //  <Zone name="s_silent_sea_1" id="210" originX="13" originY="15">
                    origin = new Vector2(13f, 15f);
                    break;
                case 211: //  <Zone name="s_silent_sea_2" id="211" originX="18" originY="13">
                    origin = new Vector2(18f, 13f);
                    break;
                case 212: //  <Zone name="s_silent_sea_3" id="212" originX="13" originY="20">
                    origin = new Vector2(13f, 20f);
                    break;
                case 213: //  <Zone name="s_silent_sea_4" id="213" originX="17" originY="18">
                    origin = new Vector2(17f, 18f);
                    break;
                case 214: //  <Zone name="s_silent_sea_5" id="214" originX="22" originY="15">
                    origin = new Vector2(22f, 15f);
                    break;
                case 215: //  <Zone name="s_silent_sea_6" id="215" originX="22" originY="20">
                    origin = new Vector2(22f, 20f);
                    break;
                case 216: //  <Zone name="e_una_basin" id="216" originX="21" originY="12">
                    origin = new Vector2(21f, 12f);
                    break;
                case 217: //  <Zone name="s_nightmare_coast" id="217" originX="22" originY="13">
                    origin = new Vector2(22f, 13f);
                    break;
                case 218: //  <Zone name="s_golden_sea_1" id="218" originX="9" originY="5">
                    origin = new Vector2(9f, 5f);
                    break;
                case 219: //  <Zone name="s_golden_sea_2" id="219" originX="12" originY="5">
                    origin = new Vector2(12f, 5f);
                    break;
                case 221: //  <Zone name="s_crescent_sea" id="221" originX="12" originY="12">
                    origin = new Vector2(12f, 12f);
                    break;
                case 225: //  <Zone name="lock_south_sunrise_peninsula" id="225" originX="15" originY="5">
                    origin = new Vector2(15f, 5f);
                    break;
                case 226: //  <Zone name="lock_golden_sea" id="226" originX="9" originY="5">
                    origin = new Vector2(9f, 5f);
                    break;
                case 227: //  <Zone name="lock_left_side_of_silent_sea" id="227" originX="13" originY="21">
                    origin = new Vector2(13f, 21f);
                    break;
                case 228: //  <Zone name="lock_right_side_of_silent_sea_1" id="228" originX="22" originY="18">
                    origin = new Vector2(22f, 18f);
                    break;
                case 229: //  <Zone name="lock_right_side_of_silent_sea_2" id="229" originX="26" originY="13">
                    origin = new Vector2(26f, 13f);
                    break;
                case 230: //  <Zone name="w_golden_moss_covered_forest" id="230" originX="6" originY="10">
                    origin = new Vector2(6f, 10f);
                    break;
                case 231: //  <Zone name="e_land_of_return_1" id="231" originX="23" originY="11">
                    origin = new Vector2(23f, 11f);
                    break;
                case 232: //  <Zone name="o_temp_e" id="232" originX="18" originY="22">
                    origin = new Vector2(18f, 22f);
                    break;
                case 233: //  <Zone name="o_seonyeokmari" id="233" originX="18" originY="24">
                    origin = new Vector2(18f, 24f);
                    break;
                case 234: //  <Zone name="o_rest_land" id="234" originX="20" originY="24">
                    origin = new Vector2(20f, 24f);
                    break;
                case 242: //  <Zone name="e_ruins_of_hariharalaya_2" id="242" originX="27" originY="5">
                    origin = new Vector2(27f, 5f);
                    break;
                case 243: //  <Zone name="e_ruins_of_hariharalaya_3" id="243" originX="25" originY="6">
                    origin = new Vector2(25f, 6f);
                    break;
                case 244: //  <Zone name="w_white_forest_2" id="244" originX="9" originY="11">
                    origin = new Vector2(9f, 11f);
                    break;
                case 245: //  <Zone name="w_long_sand_2" id="245" originX="8" originY="7">
                    origin = new Vector2(8f, 7f);
                    break;
                case 246: //  <Zone name="e_lokas_checkers_2" id="246" originX="23" originY="9">
                    origin = new Vector2(23f, 9f);
                    break;
                case 247: //  <Zone name="e_steppe_belt_2" id="247" originX="24" originY="5">
                    origin = new Vector2(24f, 5f);
                    break;
                case 248: //  <Zone name="w_hell_swamp_2" id="248" originX="6" originY="8">
                    origin = new Vector2(6f, 8f);
                    break;
                case 249: //  <Zone name="w_tornado_mea" id="249" originX="4" originY="5">
                    origin = new Vector2(4f, 5f);
                    break;
                case 250: //  <Zone name="w_twist_coast" id="250" originX="7" originY="3">
                    origin = new Vector2(7f, 3f);
                    break;
                case 251: //  <Zone name="e_sylvina_region" id="251" originX="15" originY="4">
                    origin = new Vector2(15f, 4f);
                    break;
                case 252: //  <Zone name="e_fossils_desert" id="252" originX="22" originY="3">
                    origin = new Vector2(22f, 3f);
                    break;
                case 253: //  <Zone name="e_laveda" id="253" originX="25" originY="2">
                    origin = new Vector2(25f, 2f);
                    break;
                case 254: //  <Zone name="e_black_desert" id="254" originX="28" originY="3">
                    origin = new Vector2(28f, 3f);
                    break;
                case 256: //  <Zone name="e_singing_land_3" id="256" originX="20" originY="9">
                    origin = new Vector2(20f, 9f);
                    break;
                case 257: //  <Zone name="w_cross_plains_2" id="257" originX="13" originY="10">
                    origin = new Vector2(13f, 10f);
                    break;
                case 258: //  <Zone name="e_tiger_spine_mountains_2" id="258" originX="21" originY="7">
                    origin = new Vector2(21f, 7f);
                    break;
                case 259: //  <Zone name="e_ynystere_2" id="259" originX="20" originY="11">
                    origin = new Vector2(20f, 11f);
                    break;
                case 261: //  <Zone name="e_white_island" id="261" originX="24" originY="14">
                    origin = new Vector2(24f, 14f);
                    break;
                case 266: //  <Zone name="w_mirror_kingdom_1" id="266" originX="6" originY="16">
                    origin = new Vector2(6f, 16f);
                    break;
                case 267: //  <Zone name="w_frozen_top_1" id="267" originX="4" originY="13">
                    origin = new Vector2(4f, 13f);
                    break;
                case 268: //  <Zone name="w_firefly_pen_1" id="268" originX="9" originY="17">
                    origin = new Vector2(9f, 17f);
                    break;
                case 269: //  <Zone name="w_hanuimaru_1" id="269" originX="4" originY="7">
                    origin = new Vector2(4f, 7f);
                    break;
                case 270: //  <Zone name="e_lokaloka_mountains_1" id="270" originX="25" originY="8">
                    origin = new Vector2(25f, 8f);
                    break;
                case 272: //  <Zone name="e_hasla_2" id="272" originX="27" originY="8">
                    origin = new Vector2(27f, 8f);
                    break;
                case 273: //  <Zone name="w_the_carcass_2" id="273" originX="10" originY="16">
                    origin = new Vector2(10f, 16f);
                    break;
                case 274: //  <Zone name="e_hasla_3" id="274" originX="29" originY="6">
                    origin = new Vector2(29f, 6f);
                    break;
                case 275: //  <Zone name="o_land_of_sunlights" id="275" originX="20" originY="24">
                    origin = new Vector2(20f, 24f);
                    break;
                case 276: //  <Zone name="o_abyss_gate" id="276" originX="21" originY="23">
                    origin = new Vector2(21f, 23f);
                    break;
                case 277: //  <Zone name="s_lonely_sea_1" id="277" originX="29" originY="6">
                    origin = new Vector2(29f, 6f);
                    break;
                case 281: //  <Zone name="o_ruins_of_gold" id="281" originX="16" originY="26">
                    origin = new Vector2(16f, 26f);
                    break;
                case 282: //  <Zone name="o_shining_shore_1" id="282" originX="17" originY="25">
                    origin = new Vector2(17f, 25f);
                    break;
                case 283: //  <Zone name="s_freedom_island" id="283" originX="19" originY="15">
                    origin = new Vector2(19f, 15f);
                    break;
                case 284: //  <Zone name="s_pirate_island" id="284" originX="13" originY="21">
                    origin = new Vector2(13f, 21f);
                    break;
                case 286: //  <Zone name="e_sunny_wilderness_3" id="286" originX="18" originY="4">
                    origin = new Vector2(18f, 4f);
                    break;
                case 287: //  <Zone name="e_sunny_wilderness_4" id="287" originX="18" originY="5">
                    origin = new Vector2(18f, 5f);
                    break;
                case 288: //  <Zone name="o_the_great_reeds" id="288" originX="17" originY="27">
                    origin = new Vector2(17f, 27f);
                    break;
                case 289: //  <Zone name="s_silent_sea_8" id="289" originX="18" originY="24">
                    origin = new Vector2(18f, 24f);
                    break;
                case 293: //  <Zone name="o_library_1" id="293" originX="2" originY="28">
                    origin = new Vector2(2f, 28f);
                    break;
                case 294: //  <Zone name="o_library_2" id="294" originX="2" originY="26">
                    origin = new Vector2(2f, 26f);
                    break;
                case 295: //  <Zone name="o_library_3" id="295" originX="2" originY="24">
                    origin = new Vector2(2f, 24f);
                    break;
                case 301: //  <Zone name="o_shining_shore_2" id="301" originX="18" originY="25">
                    origin = new Vector2(18f, 25f);
                    break;
                case 307: //  <Zone name="o_dew_plains" id="307" originX="22" originY="26">
                    origin = new Vector2(22f, 26f);
                    break;
                // for version 2.0.1.7
                //case 308: //  <Zone name="s_broken_mirrors_sea_1" id="308" originX="4" originY="16">
                //    origin = new Vector2(4f, 16f);
                //    break;
                //case 309: //  <Zone name="s_broken_mirrors_sea_2" id="309" originX="4" originY="19">
                //    origin = new Vector2(4f, 19f);
                //    break;
                //case 310: //  <Zone name="o_whale_song_bay" id="310" originX="14" originY="26">
                //    origin = new Vector2(14f, 26f);
                //    break;
                //case 311: //  <Zone name="lock_left_side_of_broken_mirrors_sea" id="311" originX="4" originY="16">
                //    origin = new Vector2(4f, 16f);
                //    break;
                //case 312: //  <Zone name="o_epherium_1" id="312" originX="22" originY="24">
                //    origin = new Vector2(22f, 24f);
                //    break;
                //case 314: //  <Zone name="o_epherium_2" id="314" originX="22" originY="25">
                //    origin = new Vector2(22f, 25f);
                //    break;
                // for version 3.0.3.0
                //case 328: //  <Zone name="w_cradle_of_genesis_2" id="328" originX="6" originY="12">
                //    origin = new Vector2(6f, 12f);
                //    break;
                //case 329: //  <Zone name="s_boiling_sea_1" id="329" originX="12" originY="1">
                //    origin = new Vector2(12f, 1f);
                //    break;
                //case 330: //  <Zone name="s_boiling_sea_2" id="330" originX="17" originY="1">
                //    origin = new Vector2(17f, 1f);
                //    break;
                //case 331: //  <Zone name="s_boiling_sea_3" id="331" originX="21" originY="1">
                //    origin = new Vector2(21f, 1f);
                //    break;
                //case 332: // <Zone name="s_boiling_sea_4" id="332" originX="17" originY="2">
                //    origin = new Vector2(17f, 2f);
                //    break;
                //case 333: //  <Zone name="w_hanuimaru_2" id="333" originX="4" originY="9">
                //    origin = new Vector2(4f, 9f);
                //    break;
                //case 334: //  <Zone name="w_hanuimaru_3" id="334" originX="5" originY="8">
                //    origin = new Vector2(5f, 8f);
                //    break;
                //case 335: //  <Zone name="s_lonely_sea_2" id="335" originX="26" originY="9">
                //    origin = new Vector2(26f, 9f);
                //    break;
                //case 336: //  <Zone name="s_west_sea_1" id="336" originX="2" originY="7">
                //    origin = new Vector2(2f, 7f);
                //    break;
                //case 337: //  <Zone name="o_room_of_queen_1" id="337" originX="2" originY="30">
                //    origin = new Vector2(2f, 30f);
                //    break;
                //case 339: //  <Zone name="s_boiling_sea_5" id="339" originX="14" originY="4">
                //    origin = new Vector2(14f, 4f);
                //    break;
                //case 340: // <Zone name="e_lokaloka_mountains_2" id="340" originX="25" originY="8">
                //    origin = new Vector2(25f, 8f);
                //    break;
                //case 341: // <Zone name="o_room_of_queen_2" id="341" originX="3" originY="30">
                //    origin = new Vector2(3f, 30f);
                //    break;
                //case 342: //  <Zone name="o_room_of_queen_3" id="342" originX="2" originY="30">
                //    origin = new Vector2(2f, 30f);
                //    break;
            }

            var newX = origin.X * 1024 + point.X;
            var newY = origin.Y * 1024 + point.Y;

            return (newX, newY, point.Z);
        }
}
}
