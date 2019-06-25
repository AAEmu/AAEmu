using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers.UnitManagers
{
    public class DoodadManager : Singleton<DoodadManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, DoodadTemplate> _templates;
        private Dictionary<uint, List<DoodadFunc>> _funcs;
        private Dictionary<uint, List<DoodadFunc>> _phaseFuncs;
        private Dictionary<string, Dictionary<uint, DoodadFuncTemplate>> _funcTemplates;

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        public DoodadTemplate GetTemplate(uint id)
        {
            return Exist(id) ? _templates[id] : null;
        }

        public void Load()
        {
            _templates = new Dictionary<uint, DoodadTemplate>();
            _funcs = new Dictionary<uint, List<DoodadFunc>>();
            _phaseFuncs = new Dictionary<uint, List<DoodadFunc>>();
            _funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
            foreach (var type in Helpers.GetTypesInNamespace("AAEmu.Game.Models.Game.DoodadObj.Funcs"))
                if (type.BaseType == typeof(DoodadFuncTemplate))
                    _funcTemplates.Add(type.Name, new Dictionary<uint, DoodadFuncTemplate>());

            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading doodad templates...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * from doodad_almighties";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var template = new DoodadTemplate();
                            template.Id = reader.GetUInt32("id");
                            template.OnceOneMan = reader.GetBoolean("once_one_man", true);
                            template.OnceOneInteraction = reader.GetBoolean("once_one_interaction", true);
                            template.MgmtSpawn = reader.GetBoolean("mgmt_spawn", true);
                            template.Percent = reader.GetInt32("percent", 0);
                            template.MinTime = reader.GetInt32("min_time", 0);
                            template.MaxTime = reader.GetInt32("max_time", 0);
                            template.ModelKindId = reader.GetUInt32("model_kind_id");
                            template.UseCreatorFaction = reader.GetBoolean("use_creator_faction", true);
                            template.ForceTodTopPriority = reader.GetBoolean("force_tod_top_priority", true);
                            template.MilestoneId = reader.GetUInt32("milestone_id", 0);
                            template.GroupId = reader.GetUInt32("group_id");
                            template.UseTargetDecal = reader.GetBoolean("use_target_decal", true);
                            template.UseTargetSilhouette = reader.GetBoolean("use_target_silhouette", true);
                            template.UseTargetHighlight = reader.GetBoolean("use_target_highlight", true);
                            template.TargetDecalSize = reader.GetFloat("target_decal_size", 0);
                            template.SimRadius = reader.GetInt32("sim_radius", 0);
                            template.CollideShip = reader.GetBoolean("collide_ship", true);
                            template.CollideVehicle = reader.GetBoolean("collide_vehicle", true);
                            template.ClimateId = reader.GetUInt32("climate_id", 0);
                            template.SaveIndun = reader.GetBoolean("save_indun", true);
                            template.ForceUpAction = reader.GetBoolean("force_up_action", true);
                            template.Parentable = reader.GetBoolean("parentable", true);
                            template.Childable = reader.GetBoolean("childable", true);
                            template.FactionId = reader.GetUInt32("faction_id");
                            template.GrowthTime = reader.GetInt32("growth_time", 0);
                            template.DespawnOnCollision = reader.GetBoolean("despawn_on_collision", true);
                            template.NoCollision = reader.GetBoolean("no_collision", true);
                            template.RestrictZoneId = reader.IsDBNull("restrict_zone_id") ? 0 : reader.GetUInt32("restrict_zone_id");

                            using (var commandChild = connection.CreateCommand())
                            {
                                commandChild.CommandText =
                                    "SELECT * FROM doodad_func_groups WHERE doodad_almighty_id = @doodad_almighty_id";
                                commandChild.Prepare();
                                commandChild.Parameters.AddWithValue("doodad_almighty_id", template.Id);
                                using (var sqliteDataReaderChild = commandChild.ExecuteReader())
                                using (var readerChild = new SQLiteWrapperReader(sqliteDataReaderChild))
                                {
                                    while (readerChild.Read())
                                    {
                                        var funcGroups = new DoodadFuncGroups();
                                        funcGroups.Id = readerChild.GetUInt32("id");
                                        funcGroups.GroupKindId = readerChild.GetUInt32("doodad_func_group_kind_id");
                                        funcGroups.SoundId = readerChild.IsDBNull("sound_id") ? 0 : readerChild.GetUInt32("sound_id");

                                        template.FuncGroups.Add(funcGroups);
                                    }
                                }
                            }

                            _templates.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loaded {0} doodad templates", _templates.Count);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_funcs";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFunc();
                            func.GroupId = reader.GetUInt32("doodad_func_group_id");
                            func.FuncId = reader.GetUInt32("actual_func_id");
                            func.FuncType = reader.GetString("actual_func_type");
                            func.NextPhase = reader.GetInt32("next_phase", -1); // TODO next_phase = 0?
                            func.SoundId = reader.IsDBNull("sound_id") ? 0 : reader.GetUInt32("sound_id");
                            func.SkillId = reader.GetUInt32("func_skill_id", 0);
                            func.PermId = reader.GetUInt32("perm_id");
                            func.Count = reader.GetInt32("act_count", 0);
                            List<DoodadFunc> list;
                            if (_funcs.ContainsKey(func.GroupId))
                                list = _funcs[func.GroupId];
                            else
                            {
                                list = new List<DoodadFunc>();
                                _funcs.Add(func.GroupId, list);
                            }

                            list.Add(func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_phase_funcs";
                    command.Prepare();
                    using (var sqliteDataReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFunc();
                            func.GroupId = reader.GetUInt32("doodad_func_group_id");
                            func.FuncId = reader.GetUInt32("actual_func_id");
                            func.FuncType = reader.GetString("actual_func_type");
                            List<DoodadFunc> list;
                            if (_phaseFuncs.ContainsKey(func.GroupId))
                                list = _phaseFuncs[func.GroupId];
                            else
                            {
                                list = new List<DoodadFunc>();
                                _phaseFuncs.Add(func.GroupId, list);
                            }

                            list.Add(func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_animates";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncAnimate();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            func.PlayOnce = reader.GetBoolean("play_once", true);
                            _funcTemplates["DoodadFuncAnimate"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_area_triggers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncAreaTrigger();
                            func.Id = reader.GetUInt32("id");
                            func.NpcId = reader.GetUInt32("npc_id", 0);
                            func.IsEnter = reader.GetBoolean("is_enter", true);
                            _funcTemplates["DoodadFuncAreaTrigger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_attachments";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncAttachment();
                            func.Id = reader.GetUInt32("id");
                            func.AttachPointId = reader.GetByte("attach_point_id");
                            func.Space = reader.GetInt32("space");
                            func.BondKindId = reader.GetByte("bond_kind_id");
                            _funcTemplates["DoodadFuncAttachment"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_bindings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBinding();
                            func.Id = reader.GetUInt32("id");
                            func.DistrictId = reader.GetUInt32("district_id");
                            _funcTemplates["DoodadFuncBinding"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_bubbles";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBubble();
                            func.Id = reader.GetUInt32("id");
                            func.BubbleId = reader.GetUInt32("bubble_id");
                            _funcTemplates["DoodadFuncBubble"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buffs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuff();
                            func.Id = reader.GetUInt32("id");
                            func.BuffId = reader.GetUInt32("buff_id");
                            func.Radius = reader.GetFloat("radius");
                            func.Count = reader.GetInt32("count");
                            func.PermId = reader.GetUInt32("perm_id");
                            func.RelationshipId = reader.GetUInt32("relationship_id");
                            _funcTemplates["DoodadFuncBuff"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_butchers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncButcher();
                            func.Id = reader.GetUInt32("id");
                            func.CorpseModel = reader.GetString("corpse_model");
                            _funcTemplates["DoodadFuncButcher"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buy_fish_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuyFishItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncBuyFishId = reader.GetUInt32("doodad_func_buy_fish_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _funcTemplates["DoodadFuncBuyFishItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buy_fish_models";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuyFishModel();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            _funcTemplates["DoodadFuncBuyFishModel"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_buy_fishes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncBuyFish();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            _funcTemplates["DoodadFuncBuyFish"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_catches";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCatch();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCatch"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cereal_harvests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCerealHarvest();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCerealHarvest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cleanup_logic_links";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCleanupLogicLink();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCleanupLogicLink"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_climate_reacts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncClimateReact();
                            func.Id = reader.GetUInt32("id");
                            func.NextPhase = reader.GetUInt32("next_phase");
                            _funcTemplates["DoodadFuncClimateReact"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_climbs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncClimb();
                            func.Id = reader.GetUInt32("id");
                            func.ClimbTypeId = reader.GetUInt32("climb_type_id");
                            func.AllowHorizontalMultiHanger = reader.GetBoolean("allow_horizontal_multi_hanger", true);
                            _funcTemplates["DoodadFuncClimb"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_clout_effects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCloutEffect();
                            func.Id = reader.GetUInt32("id");
                            func.FuncCloutId = reader.GetUInt32("doodad_func_clout_id");
                            func.EffectId = reader.GetUInt32("effect_id");
                            _funcTemplates["DoodadFuncCloutEffect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_clouts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncClout();
                            func.Id = reader.GetUInt32("id");
                            func.Duration = reader.GetInt32("duration");
                            func.Tick = reader.GetInt32("tick");
                            func.TargetRelationId = reader.GetUInt32("target_relation_id");
                            func.BuffId = reader.GetUInt32("buff_id", 0);
                            func.ProjectileId = reader.GetUInt32("projectile_id", 0);
                            func.ShowToFriendlyOnly = reader.GetBoolean("show_to_friendly_only", true);
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            func.AoeShapeId = reader.GetUInt32("aoe_shape_id");
                            func.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                            func.TargetNoBuffTagId = reader.GetUInt32("target_no_buff_tag_id", 0);
                            func.UseOriginSource = reader.GetBoolean("use_origin_source", true);
                            _funcTemplates["DoodadFuncClout"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_coffer_perms";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCofferPerm();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCofferPerm"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_coffers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCoffer();
                            func.Id = reader.GetUInt32("id");
                            func.Capacity = reader.GetInt32("capacity");
                            _funcTemplates["DoodadFuncCoffer"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_conditional_uses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConditionalUse();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id", 0);
                            func.FakeSkillId = reader.GetUInt32("fake_skill_id", 0);
                            func.QuestId = reader.GetUInt32("quest_id", 0);
                            func.QuestTriggerPhase = reader.GetUInt32("quest_trigger_phase", 0);
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            func.ItemTriggerPhase = reader.GetUInt32("item_trigger_phase", 0);
                            _funcTemplates["DoodadFuncConditionalUse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changer_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChangerItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncConsumeChangerId = reader.GetUInt32("doodad_func_consume_changer_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _funcTemplates["DoodadFuncConsumeChangerItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changer_model_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChangerModelItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncConsumeChangerModelId = reader.GetUInt32("doodad_func_consume_changer_model_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _funcTemplates["DoodadFuncConsumeChangerModelItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changer_models";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChangerModel();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            _funcTemplates["DoodadFuncConsumeChangerModel"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_changers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeChanger();
                            func.Id = reader.GetUInt32("id");
                            func.SlotId = reader.GetUInt32("slot_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncConsumeChanger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_consume_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConsumeItem();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncConsumeItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_convert_fish_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConvertFishItem();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncConvertFishId = reader.GetUInt32("doodad_func_convert_fish_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.LootPackId = reader.GetUInt32("loot_pack_id");
                            _funcTemplates["DoodadFuncConvertFishItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_convert_fishes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncConvertFish();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncConvertFish"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_acts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftAct();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftAct"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_cancels";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftCancel();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftCancel"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_directs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftDirect();
                            func.Id = reader.GetUInt32("id");
                            func.NextPhase = reader.GetUInt32("next_phase");
                            _funcTemplates["DoodadFuncCraftDirect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_get_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftGetItem();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftGetItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_infos";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftInfo();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftInfo"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_packs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftPack();
                            func.Id = reader.GetUInt32("id");
                            func.CraftPackId = reader.GetUInt32("craft_pack_id");
                            _funcTemplates["DoodadFuncCraftPack"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_start_crafts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftStartCraft();
                            func.Id = reader.GetUInt32("id");
                            func.DoodadFuncCraftStartId = reader.GetUInt32("doodad_func_craft_start_id");
                            func.CraftId = reader.GetUInt32("craft_id");
                            _funcTemplates["DoodadFuncCraftStartCraft"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_craft_starts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCraftStart();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCraftStart"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_crop_harvests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCropHarvest();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCropHarvest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_crystal_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCrystalCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCrystalCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cutdownings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCutdowning();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCutdowning"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_cutdowns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncCutdown();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncCutdown"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_dairy_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDairyCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDairyCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_declare_sieges";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDeclareSiege();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDeclareSiege"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_digs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDig();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDig"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_dig_terrains";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDigTerrain();
                            func.Id = reader.GetUInt32("id");
                            func.Radius = reader.GetInt32("radius");
                            func.Life = reader.GetInt32("life");
                            _funcTemplates["DoodadFuncDigTerrain"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_dyeingredient_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncDyeingredientCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncDyeingredientCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_enter_instances";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncEnterInstance();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneId = reader.GetUInt32("zone_id");
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            _funcTemplates["DoodadFuncEnterInstance"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_enter_sys_instances";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncEnterSysInstance();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneId = reader.GetUInt32("zone_id");
                            func.FactionId = reader.GetUInt32("faction_id", 0);
                            _funcTemplates["DoodadFuncEnterSysInstance"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_evidence_item_loots";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncEvidenceItemLoot();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id");
                            func.CrimeValue = reader.GetInt32("crime_value");
                            func.CrimeKindId = reader.GetUInt32("crime_kind_id");
                            _funcTemplates["DoodadFuncEvidenceItemLoot"].Add(func.Id, func);
                        }
                    }
                }

                // TODO doodad_func_exchange_items( id INT, doodad_func_exchange_id INT, item_id INT, loot_pack_id INT )

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_exchanges";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncExchange();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncExchange"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_exit_induns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncExitIndun();
                            func.Id = reader.GetUInt32("id");
                            func.ReturnPointId = reader.GetUInt32("return_point_id", 0);
                            _funcTemplates["DoodadFuncExitIndun"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fake_uses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFakeUse();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id", 0);
                            func.FakeSkillId = reader.GetUInt32("fake_skill_id", 0);
                            func.TargetParent = reader.GetBoolean("target_parent", true);
                            _funcTemplates["DoodadFuncFakeUse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_feeds";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFeed();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncFeed"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fiber_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFiberCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncFiberCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_finals";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFinal();
                            func.Id = reader.GetUInt32("id");
                            func.After = reader.GetInt32("after", 0);
                            func.Respawn = reader.GetBoolean("respawn", true);
                            func.MinTime = reader.GetInt32("min_time", 0);
                            func.MaxTime = reader.GetInt32("max_time", 0);
                            func.ShowTip = reader.GetBoolean("show_tip", true);
                            func.ShowEndTime = reader.GetBoolean("show_end_time", true);
                            func.Tip = reader.GetString("tip");
                            _funcTemplates["DoodadFuncFinal"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fish_schools";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFishSchool();
                            func.Id = reader.GetUInt32("id");
                            func.NpcSpawnerId = reader.GetUInt32("npc_spawner_id");
                            _funcTemplates["DoodadFuncFishSchool"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_fruit_picks";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncFruitPick();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncFruitPick"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_gass_extracts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncGassExtract();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncGassExtract"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_growths";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncGrowth();
                            func.Id = reader.GetUInt32("id");
                            func.Delay = reader.GetInt32("delay");
                            func.StartScale = reader.GetInt32("start_scale");
                            func.EndScale = reader.GetInt32("end_scale");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            _funcTemplates["DoodadFuncGrowth"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_harvests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHarvest();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncHarvest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_house_farms";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHouseFarm();
                            func.Id = reader.GetUInt32("id");
                            func.ItemCategoryId = reader.GetUInt32("item_category_id");
                            _funcTemplates["DoodadFuncHouseFarm"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_housing_areas";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHousingArea();
                            func.Id = reader.GetUInt32("id");
                            func.FactionId = reader.GetUInt32("faction_id");
                            func.Radius = reader.GetInt32("radius");
                            _funcTemplates["DoodadFuncHousingArea"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_hungers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncHunger();
                            func.Id = (uint)reader.GetInt32("id");
                            func.HungryTerm = reader.GetInt32("hungry_term");
                            func.FullStep = reader.GetInt32("full_step");
                            func.PhaseChangeLimit = reader.GetInt32("phase_change_limit");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            _funcTemplates["DoodadFuncHunger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_insert_counters";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncInsertCounter();
                            func.Id = reader.GetUInt32("id");
                            func.Count = reader.GetInt32("count");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.ItemCount = reader.GetInt32("item_count");
                            _funcTemplates["DoodadFuncInsertCounter"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_logics";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLogic();
                            func.Id = reader.GetUInt32("id");
                            func.OperationId = reader.GetUInt32("operation_id");
                            func.DelayId = reader.GetUInt32("delay_id");
                            _funcTemplates["DoodadFuncLogic"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_logic_family_providers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLogicFamilyProvider();
                            func.Id = reader.GetUInt32("id");
                            func.FamilyId = reader.GetUInt32("family_id");
                            _funcTemplates["DoodadFuncLogicFamilyProvider"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_logic_family_subscribers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLogicFamilySubscriber();
                            func.Id = reader.GetUInt32("id");
                            func.FamilyId = reader.GetUInt32("family_id");
                            _funcTemplates["DoodadFuncLogicFamilySubscriber"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_loot_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLootItem();
                            func.Id = reader.GetUInt32("id");
                            func.WorldInteractionId = reader.GetUInt32("wi_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.CountMin = reader.GetInt32("count_min");
                            func.CountMax = reader.GetInt32("count_max");
                            func.Percent = reader.GetInt32("percent");
                            func.RemainTime = reader.GetInt32("remain_time");
                            func.GroupId = reader.GetUInt32("group_id");
                            _funcTemplates["DoodadFuncLootItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_loot_packs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncLootPack();
                            func.Id = reader.GetUInt32("id");
                            func.LootPackId = reader.GetUInt32("loot_pack_id");
                            _funcTemplates["DoodadFuncLootPack"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_machine_parts_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncMachinePartsCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncMachinePartsCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_medicalingredient_mines";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncMedicalingredientMine();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncMedicalingredientMine"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_mows";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncMow();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncMow"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_mark_pos_to_maps";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviMarkPosToMap();
                            func.Id = reader.GetUInt32("id");
                            func.X = reader.GetInt32("x");
                            func.Y = reader.GetInt32("y");
                            func.Z = reader.GetInt32("z");
                            _funcTemplates["DoodadFuncNaviMarkPosToMap"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_namings";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviNaming();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviNaming"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_open_bounties";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviOpenBounty();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviOpenBounty"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_open_mailboxes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviOpenMailbox();
                            func.Id = reader.GetUInt32("id");
                            func.Duration = reader.GetInt32("duration");
                            _funcTemplates["DoodadFuncNaviOpenMailbox"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_open_portals";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviOpenPortal();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviOpenPortal"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_remove_timers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviRemoveTimer();
                            func.Id = reader.GetUInt32("id");
                            func.After = reader.GetInt32("after");
                            _funcTemplates["DoodadFuncNaviRemoveTimer"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_removes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviRemove();
                            func.Id = reader.GetUInt32("id");
                            func.ReqLaborPower = reader.GetInt32("req_lp");
                            _funcTemplates["DoodadFuncNaviRemove"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_navi_teleports";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncNaviTeleport();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncNaviTeleport"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_open_farm_infos";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncOpenFarmInfo();
                            func.Id = reader.GetUInt32("id");
                            func.FarmId = reader.GetUInt32("farm_id");
                            _funcTemplates["DoodadFuncOpenFarmInfo"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_open_papers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncOpenPaper();
                            func.Id = reader.GetUInt32("id");
                            func.BookPageId = reader.GetUInt32("book_page_id", 0);
                            func.BookId = reader.GetUInt32("book_id", 0);
                            _funcTemplates["DoodadFuncOpenPaper"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ore_mines";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncOreMine();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncOreMine"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_parent_infos";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncParentInfo();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncParentInfo"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_parrots";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncParrot();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncParrot"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_plant_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPlantCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncPlantCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_play_flow_graphs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPlayFlowGraph();
                            func.Id = reader.GetUInt32("id");
                            func.EventOnPhaseChangeId = reader.GetUInt32("event_on_phase_change_id");
                            func.EventOnVisibleId = reader.GetUInt32("event_on_visible_id");
                            _funcTemplates["DoodadFuncPlayFlowGraph"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_pulse_triggers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPulseTrigger();
                            func.Id = reader.GetUInt32("id");
                            func.Flag = reader.GetBoolean("flag", true);
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            _funcTemplates["DoodadFuncPulseTrigger"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_pulses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPulse();
                            func.Id = reader.GetUInt32("id");
                            func.Flag = reader.GetBoolean("flag", true);
                            _funcTemplates["DoodadFuncPulse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_purchases";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPurchase();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id", 0);
                            func.Count = reader.GetInt32("count");
                            func.CoinItemId = reader.GetUInt32("coin_item_id", 0);
                            func.CoinCount = reader.GetInt32("coin_count", 0);
                            func.CurrencyId = reader.GetUInt32("currency_id");
                            _funcTemplates["DoodadFuncPurchase"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_puzzle_ins";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPuzzleIn();
                            func.Id = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("group_id");
                            func.Ratio = reader.GetFloat("ratio");
                            _funcTemplates["DoodadFuncPuzzleIn"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_puzzle_outs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPuzzleOut();
                            func.Id = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("group_id");
                            func.Ratio = reader.GetFloat("ratio");
                            func.Anim = reader.GetString("anim");
                            func.ProjectileId = reader.GetUInt32("projectile_id", 0);
                            func.ProjectileDelay = reader.GetInt32("projectile_delay");
                            func.LootPackId = reader.GetUInt32("loot_pack_id", 0);
                            func.Delay = reader.GetInt32("delay");
                            func.NextPhase = reader.GetUInt32("next_phase");
                            _funcTemplates["DoodadFuncPuzzleOut"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_puzzle_rolls";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncPuzzleRoll();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncPuzzleRoll"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_quests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncQuest();
                            func.Id = reader.GetUInt32("id");
                            func.QuestKindId = reader.GetUInt32("quest_kind_id");
                            func.QuestId = reader.GetUInt32("quest_id");
                            _funcTemplates["DoodadFuncQuest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ratio_changes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRatioChange();
                            func.Id = reader.GetUInt32("id");
                            func.Ratio = reader.GetInt32("ratio");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            _funcTemplates["DoodadFuncRatioChange"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ratio_respawns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRatioRespawn();
                            func.Id = reader.GetUInt32("id");
                            func.Ratio = reader.GetInt32("ratio");
                            func.SpawnDoodadId = reader.GetUInt32("spawn_doodad_id");
                            _funcTemplates["DoodadFuncRatioRespawn"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_recover_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRecoverItem();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncRecoverItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_remove_instances";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRemoveInstance();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneId = reader.GetUInt32("zone_id");
                            _funcTemplates["DoodadFuncRemoveInstance"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_remove_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRemoveItem();
                            func.Id = reader.GetUInt32("id");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.Count = reader.GetInt32("count");
                            _funcTemplates["DoodadFuncRemoveItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_renew_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRenewItem();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id");
                            _funcTemplates["DoodadFuncRenewItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_require_items";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRequireItem();
                            func.Id = reader.GetUInt32("id");
                            func.WorldInteractionId = reader.GetUInt32("wi_id");
                            func.ItemId = reader.GetUInt32("item_id");
                            _funcTemplates["DoodadFuncRequireItem"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_require_quests";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRequireQuest();
                            func.Id = reader.GetUInt32("id");
                            func.WorldInteractionId = reader.GetUInt32("wi_id");
                            func.QuestId = reader.GetUInt32("quest_id");
                            _funcTemplates["DoodadFuncRequireQuest"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_respawns";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRespawn();
                            func.Id = reader.GetUInt32("id");
                            func.MinTime = reader.GetInt32("min_time");
                            func.MaxTime = reader.GetInt32("max_time");
                            _funcTemplates["DoodadFuncRespawn"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_rock_mines";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncRockMine();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncRockMine"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_seed_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSeedCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSeedCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_shears";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncShear();
                            func.Id = reader.GetUInt32("id");
                            func.ShearTypeId = reader.GetUInt32("shear_type_id");
                            func.ShearTerm = reader.GetInt32("shear_term");
                            _funcTemplates["DoodadFuncShear"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_siege_periods";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSiegePeriod();
                            func.Id = reader.GetUInt32("id");
                            func.SiegePeriodId = reader.GetUInt32("siege_period_id");
                            func.NextPhase = reader.GetUInt32("next_phase");
                            func.Defense = reader.GetBoolean("defense", true);
                            _funcTemplates["DoodadFuncSiegePeriod"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_signs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSign();
                            func.Id = reader.GetUInt32("id");
                            func.Name = reader.GetString("name");
                            func.PickNum = reader.GetInt32("pick_num");
                            _funcTemplates["DoodadFuncSign"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_skill_hits";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSkillHit();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id");
                            _funcTemplates["DoodadFuncSkillHit"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_skin_offs";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSkinOff();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSkinOff"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_soil_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSoilCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSoilCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_spawn_gimmicks";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSpawnGimmick();
                            func.Id = reader.GetUInt32("id");
                            func.GimmickId = reader.GetUInt32("gimmick_id");
                            func.FactionId = reader.GetUInt32("faction_id");
                            func.Scale = reader.GetFloat("scale");
                            func.OffsetX = reader.GetFloat("offset_x");
                            func.OffsetY = reader.GetFloat("offset_y");
                            func.OffsetZ = reader.GetFloat("offset_z");
                            func.VelocityX = reader.GetFloat("velocity_x");
                            func.VelocityY = reader.GetFloat("velocity_y");
                            func.VelocityZ = reader.GetFloat("velocity_z");
                            func.AngleX = reader.GetFloat("angle_x");
                            func.AngleY = reader.GetFloat("angle_y");
                            func.AngleZ = reader.GetFloat("angle_z");
                            func.NextPhase = reader.GetUInt32("next_phase");
                            _funcTemplates["DoodadFuncSpawnGimmick"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_spawn_mgmts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSpawnMgmt();
                            func.Id = reader.GetUInt32("id");
                            func.GroupId = reader.GetUInt32("group_id");
                            func.Spawn = reader.GetBoolean("spawn", true);
                            func.ZoneId = reader.GetUInt32("zone_id");
                            _funcTemplates["DoodadFuncSpawnMgmt"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_spice_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncSpiceCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncSpiceCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_stamp_makers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncStampMaker();
                            func.Id = reader.GetUInt32("id");
                            func.ConsumeMoney = reader.GetInt32("consume_money");
                            func.ItemId = reader.GetUInt32("item_id");
                            func.ConsumeItemId = reader.GetUInt32("consume_item_id");
                            func.ConsumeCount = reader.GetInt32("consume_count");
                            _funcTemplates["DoodadFuncStampMaker"].Add(func.Id, func);
                        }
                    }
                }

                // TODO 1.2                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_store_uis";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncStoreUi();
                            func.Id = reader.GetUInt32("id");
                            func.MerchantPackId = reader.GetUInt32("merchant_pack_id");
                            _funcTemplates["DoodadFuncStoreUi"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_timers";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncTimer();
                            func.Id = reader.GetUInt32("id");
                            func.Delay = reader.GetInt32("delay");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            func.KeepRequester = reader.GetBoolean("keep_requester", true);
                            func.ShowTip = reader.GetBoolean("show_tip", true);
                            func.ShowEndTime = reader.GetBoolean("show_end_time", true);
                            func.Tip = reader.GetString("tip");
                            _funcTemplates["DoodadFuncTimer"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_tods";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncTod();
                            func.Id = reader.GetUInt32("id");
                            func.Tod = reader.GetInt32("tod");
                            func.NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetUInt32("next_phase") : 0;
                            _funcTemplates["DoodadFuncTod"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_tree_byproducts_collects";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncTreeByProductsCollect();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncTreeByProductsCollect"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_ucc_imprints";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncUccImprint();
                            func.Id = reader.GetUInt32("id");
                            _funcTemplates["DoodadFuncUccImprint"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_uses";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncUse();
                            func.Id = reader.GetUInt32("id");
                            func.SkillId = reader.GetUInt32("skill_id", 0);
                            _funcTemplates["DoodadFuncUse"].Add(func.Id, func);
                        }
                    }
                }

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_water_volumes";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncWaterVolume();
                            func.Id = reader.GetUInt32("id");
                            func.LevelChange = reader.GetFloat("levelChange");
                            func.Duration = reader.GetFloat("duration");
                            _funcTemplates["DoodadFuncWaterVolume"].Add(func.Id, func);
                        }
                    }
                }

                // TODO 1.2                
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM doodad_func_zone_reacts";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var func = new DoodadFuncZoneReact();
                            func.Id = reader.GetUInt32("id");
                            func.ZoneGroupId = reader.GetUInt32("zone_group_id");
                            func.NextPhase = reader.GetUInt32("next_phase");

                            _funcTemplates["DoodadFuncZoneReact"].Add(func.Id, func);
                        }
                    }
                }
            }
        }

        public Doodad Create(uint bcId, uint id, Unit unit = null)
        {
            if (!_templates.ContainsKey(id))
                return null;
            var template = _templates[id];
            var doodad = new Doodad();
            doodad.ObjId = bcId > 0 ? bcId : ObjectIdManager.Instance.GetNextId();
            doodad.TemplateId = template.Id;
            doodad.Template = template;
            doodad.OwnerObjId = unit?.ObjId ?? 0;
            doodad.OwnerId = 0;

            if (unit is Character character)
            {
                doodad.OwnerId = character.Id;
                doodad.OwnerType = DoodadOwnerType.Character;
            }

            if (unit is House house)
            {
                doodad.OwnerObjId = 0;
                doodad.ParentObjId = house.ObjId;
                doodad.OwnerId = house.OwnerId;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.DbId = house.Id;
            }

            doodad.FuncGroupId = doodad.GetGroupId(); // TODO look, using doodadFuncId
            return doodad;
        }

        public DoodadFunc GetFunc(uint funcGroupId, uint skillId)
        {
            if (!_funcs.ContainsKey(funcGroupId))
                return null;
            foreach (var func in _funcs[funcGroupId])
            {
                if (func.SkillId == skillId)
                    return func;
            }

            foreach (var func in _funcs[funcGroupId])
            {
                if (func.SkillId == 0)
                    return func;
            }

            return null;
        }

        public DoodadFunc[] GetPhaseFunc(uint funcGroupId)
        {
            if (_phaseFuncs.ContainsKey(funcGroupId))
                return _phaseFuncs[funcGroupId].ToArray();
            return new DoodadFunc[0];
        }

        public DoodadFuncTemplate GetFuncTemplate(uint funcId, string funcType)
        {
            if (!_funcTemplates.ContainsKey(funcType))
                return null;
            var funcs = _funcTemplates[funcType];
            if (funcs.ContainsKey(funcId))
                return funcs[funcId];
            return null;
        }
    }
}
