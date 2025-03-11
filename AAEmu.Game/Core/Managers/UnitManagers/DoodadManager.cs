using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Details;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using MySql.Data.MySqlClient;

using NLog;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator

namespace AAEmu.Game.Core.Managers.UnitManagers;

// ReSharper disable once ClassNeverInstantiated.Global
public class DoodadManager : Singleton<DoodadManager>
{
    private Dictionary<uint, DoodadFuncGroups> _allFuncGroups;

    // Details data
    private Dictionary<uint, DoodadFuncConsumeChangerItem> _doodadFuncConsumeChangerItem;
    private Dictionary<uint, List<DoodadFunc>> _funcsByGroups;
    private Dictionary<uint, DoodadFunc> _funcsById;
    private Dictionary<string, Dictionary<uint, DoodadFuncTemplate>> _funcTemplates;
    private bool _loaded;
    private Dictionary<uint, List<DoodadPhaseFunc>> _phaseFuncs;
    private Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>> _phaseFuncTemplates;

    private Dictionary<uint, DoodadTemplate> _templates;

    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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
        if (_loaded)
        {
            return;
        }

        _templates = new Dictionary<uint, DoodadTemplate>();
        _allFuncGroups = new Dictionary<uint, DoodadFuncGroups>();
        _funcsByGroups = new Dictionary<uint, List<DoodadFunc>>();
        _funcsById = new Dictionary<uint, DoodadFunc>();
        _phaseFuncs = new Dictionary<uint, List<DoodadPhaseFunc>>();
        _funcTemplates = new Dictionary<string, Dictionary<uint, DoodadFuncTemplate>>();
        _phaseFuncTemplates = new Dictionary<string, Dictionary<uint, DoodadPhaseFuncTemplate>>();
        foreach (var type in Helpers.GetTypesInNamespace(Assembly.GetAssembly(GetType()), "AAEmu.Game.Models.Game.DoodadObj.Funcs"))
        {
            if (type.BaseType == typeof(DoodadFuncTemplate))
            {
                _funcTemplates.Add(type.Name, new Dictionary<uint, DoodadFuncTemplate>());
            }
            else if (type.BaseType == typeof(DoodadPhaseFuncTemplate))
            {
                _phaseFuncTemplates.Add(type.Name, new Dictionary<uint, DoodadPhaseFuncTemplate>());
            }
        }

        _doodadFuncConsumeChangerItem = new Dictionary<uint, DoodadFuncConsumeChangerItem>();

        using (var connection2 = SQLite.CreateConnection("Data", "compact.server.table.sqlite3"))
        using (var connection = SQLite.CreateConnection())
        {
            #region doodad_funcs

            Logger.Info("Loading doodad functions ...");

            // doodad_func_groups
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT * FROM doodad_func_groups ORDER BY doodad_almighty_id, doodad_func_group_kind_id";
                command.Prepare();
                using (var sqliteDataReaderChild = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReaderChild))
                {
                    while (reader.Read())
                    {
                        var funcGroups = new DoodadFuncGroups
                        {
                            Id = reader.GetUInt32("id"),
                            Almighty = reader.GetUInt32("doodad_almighty_id"),
                            GroupKindId = (DoodadFuncGroups.DoodadFuncGroupKind)reader.GetUInt32("doodad_func_group_kind_id"),
                            SoundId = reader.GetUInt32("sound_id", 0),
                            Model = reader.GetString("model", "")
                        };

                        var template = GetTemplate(funcGroups.Almighty);
                        template?.FuncGroups.Add(funcGroups);
                    }
                }
            }

            // doodad_funcs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_funcs ORDER BY doodad_func_group_id, actual_func_id";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFunc
                        {
                            FuncKey = reader.GetUInt32("id"),
                            GroupId = reader.GetUInt32("doodad_func_group_id"),
                            FuncId = reader.GetUInt32("actual_func_id"),
                            FuncType = reader.GetString("actual_func_type"),
                            NextPhase = reader.GetInt32("next_phase", -1), // TODO next_phase = 0?
                            SoundId = reader.IsDBNull("sound_id") ? 0 : reader.GetUInt32("sound_id"),
                            SkillId = reader.GetUInt32("func_skill_id", 0),
                            PermId = reader.GetUInt32("perm_id"),
                            Count = reader.GetInt32("act_count", 0)
                        };
                        List<DoodadFunc> tempListGroups;
                        if (_funcsByGroups.TryGetValue(func.GroupId, out var funcsByGroup))
                        {
                            tempListGroups = funcsByGroup;
                        }
                        else
                        {
                            tempListGroups = new List<DoodadFunc>();
                            _funcsByGroups.Add(func.GroupId, tempListGroups);
                        }

                        tempListGroups.Add(func);
                        _funcsById.Add(func.FuncKey, func);
                    }
                }
            }

            // doodad_phase_funcs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_phase_funcs ORDER BY doodad_func_group_id, actual_func_id";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadPhaseFunc
                        {
                            GroupId = reader.GetUInt32("doodad_func_group_id"),
                            FuncId = reader.GetUInt32("actual_func_id"),
                            FuncType = reader.GetString("actual_func_type")
                        };
                        List<DoodadPhaseFunc> list;
                        if (_phaseFuncs.TryGetValue(func.GroupId, out var phaseFunc))
                        {
                            list = phaseFunc;
                        }
                        else
                        {
                            list = new List<DoodadPhaseFunc>();
                            _phaseFuncs.Add(func.GroupId, list);
                        }

                        list.Add(func);
                    }
                }
            }

            // doodad_func_animates
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_animates";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncAnimate
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name"),
                            PlayOnce = reader.GetBoolean("play_once", true)
                        };
                        _phaseFuncTemplates["DoodadFuncAnimate"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_area_triggers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_area_triggers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncAreaTrigger
                        {
                            Id = reader.GetUInt32("id"),
                            NpcId = reader.GetUInt32("npc_id", 0),
                            IsEnter = reader.GetBoolean("is_enter", true)
                        };
                        _funcTemplates["DoodadFuncAreaTrigger"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_attachments
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
                        func.AttachPointId = (AttachPointKind)reader.GetByte("attach_point_id");
                        func.Space = reader.GetInt32("space");
                        func.BondKindId = (BondKind)reader.GetByte("bond_kind_id");
                        func.AnimActionId = reader.GetUInt32("anim_action_id"); // (������������ � ������ SCBondDoodadPacket) ���� ��������� � ������ 3+
                        _funcTemplates["DoodadFuncAttachment"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_bindings
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_bindings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBinding
                        {
                            Id = reader.GetUInt32("id"),
                            DistrictId = reader.GetUInt32("district_id")
                        };
                        _funcTemplates["DoodadFuncBinding"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_bubbles
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_bubbles";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBubble
                        {
                            Id = reader.GetUInt32("id"),
                            BubbleId = reader.GetUInt32("bubble_id")
                        };
                        _funcTemplates["DoodadFuncBubble"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_buffs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBuff
                        {
                            Id = reader.GetUInt32("id"),
                            BuffId = reader.GetUInt32("buff_id"),
                            Radius = reader.GetFloat("radius"),
                            Count = reader.GetInt32("count"),
                            PermId = reader.GetUInt32("perm_id"),
                            RelationshipId = reader.GetUInt32("relationship_id")
                        };
                        _funcTemplates["DoodadFuncBuff"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_build_condition_infos
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_build_condition_infos";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBuildConditionInfo();
                        func.Id = reader.GetUInt32("id");
                        func.IsDevote = reader.GetBoolean("isDevote", true);
                        func.IsEnd = reader.GetBoolean("isEnd", true);
                        _phaseFuncTemplates["DoodadFuncBuildConditionInfo"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_build_condition_ui_opens
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_build_condition_ui_opens";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBuildConditionUiOpen();
                        func.Id = reader.GetUInt32("id");
                        _funcTemplates["DoodadFuncBuildConditionUiOpen"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_change_other_doodad_phases
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_change_other_doodad_phases";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncChangeOtherDoodadPhase();
                        func.Id = reader.GetUInt32("id");
                        func.NextPhase = reader.GetInt32("next_phase");
                        func.TargetDoodadId = reader.GetUInt32("target_doodad_id");
                        func.TargetPhase = reader.GetInt32("target_phase");
                        _phaseFuncTemplates["DoodadFuncChangeOtherDoodadPhase"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_devotes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_devotes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncDevote();
                        func.Id = reader.GetUInt32("id");
                        func.Count = reader.GetInt32("count");
                        func.ItemCount = reader.GetInt32("item_count");
                        func.ItemId = reader.GetUInt32("item_id");
                        _funcTemplates["DoodadFuncDevote"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_react_devotes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_react_devotes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncReactDevote();
                        func.Id = reader.GetUInt32("id");
                        func.Count = reader.GetUInt32("count");
                        func.NextPhase = reader.GetInt32("next_phase");
                        func.SkillId = reader.GetUInt32("skill_id");
                        _phaseFuncTemplates["DoodadFuncReactDevote"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_butchers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_butchers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncButcher
                        {
                            Id = reader.GetUInt32("id"),
                            CorpseModel = reader.GetString("corpse_model")
                        };
                        _funcTemplates["DoodadFuncButcher"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_buy_fish_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_buy_fish_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBuyFishItem
                        {
                            Id = reader.GetUInt32("id"),
                            DoodadFuncBuyFishId = reader.GetUInt32("doodad_func_buy_fish_id"),
                            ItemId = reader.GetUInt32("item_id")
                        };
                        _phaseFuncTemplates["DoodadFuncBuyFishItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_buy_fish_models
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
                        //func.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
                        _phaseFuncTemplates["DoodadFuncBuyFishModel"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_buy_fishes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_buy_fishes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncBuyFish
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id", 0)
                        };
                        _funcTemplates["DoodadFuncBuyFish"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_catches
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_catches";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCatch
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCatch"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_cereal_harvests
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_cereal_harvests";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCerealHarvest
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCerealHarvest"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_cleanup_logic_links
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_cleanup_logic_links";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCleanupLogicLink
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCleanupLogicLink"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_climate_reacts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_climate_reacts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncClimateReact
                        {
                            Id = reader.GetUInt32("id"),
                            NextPhase = reader.GetInt32("next_phase", -1)
                        };
                        _phaseFuncTemplates["DoodadFuncClimateReact"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_climbs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_climbs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncClimb
                        {
                            Id = reader.GetUInt32("id"),
                            ClimbTypeId = reader.GetUInt32("climb_type_id"),
                            AllowHorizontalMultiHanger = reader.GetBoolean("allow_horizontal_multi_hanger", true)
                        };
                        _funcTemplates["DoodadFuncClimb"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_clouts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_clouts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncClout
                        {
                            Id = reader.GetUInt32("id"),
                            Duration = reader.GetInt32("duration"),
                            Tick = reader.GetInt32("tick"),
                            TargetRelation = (SkillTargetRelation)reader.GetUInt32("target_relation_id"),
                            BuffId = reader.GetUInt32("buff_id", 0),
                            ProjectileId = reader.GetUInt32("projectile_id", 0),
                            ShowToFriendlyOnly = reader.GetBoolean("show_to_friendly_only", true),
                            NextPhase = reader.GetInt32("next_phase", -1),
                            AoeShapeId = reader.GetUInt32("aoe_shape_id"),
                            TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0),
                            TargetNoBuffTagId = reader.GetUInt32("target_no_buff_tag_id", 0),
                            UseOriginSource = reader.GetBoolean("use_origin_source", true),
                            Effects = new List<uint>()
                        };
                        _phaseFuncTemplates["DoodadFuncClout"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_clout_effects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_clout_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var funcCloutId = reader.GetUInt32("doodad_func_clout_id");
                        if (_phaseFuncTemplates.ContainsKey("DoodadFuncClout") && _phaseFuncTemplates["DoodadFuncClout"].TryGetValue(funcCloutId, out var funcObject) &&
                            funcObject is DoodadFuncClout func)
                        {
                            func.Effects.Add(reader.GetUInt32("effect_id"));
                        }
                        else
                        {
                            // Обработка случая, когда DoodadFuncClout не найден
                            Logger.Warn($"DoodadFuncClout with id {funcCloutId} not found.");
                        }
                    }
                }
            }

            // doodad_func_coffer_perms
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_coffer_perms";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCofferPerm
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCofferPerm"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_coffers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_coffers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCoffer
                        {
                            Id = reader.GetUInt32("id"),
                            Capacity = reader.GetInt32("capacity")
                        };
                        _phaseFuncTemplates["DoodadFuncCoffer"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_conditional_uses
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_conditional_uses";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConditionalUse
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id", 0),
                            FakeSkillId = reader.GetUInt32("fake_skill_id", 0),
                            QuestId = reader.GetUInt32("quest_id", 0),
                            QuestTriggerPhase = reader.GetUInt32("quest_trigger_phase", 0),
                            ItemId = reader.GetUInt32("item_id", 0),
                            ItemTriggerPhase = reader.GetUInt32("item_trigger_phase", 0)
                        };
                        _funcTemplates["DoodadFuncConditionalUse"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_consume_changer_items
            // This is not actually a phase, but rather a collection of items that is available for doodad_func_consume_changers
            // TODO there is no such table in 5070 AAFree
            //using (var command = connection.CreateCommand())
            //{
            //    command.CommandText = "SELECT * FROM doodad_func_consume_changer_items";
            //    command.Prepare();
            //    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            //    {
            //        while (reader.Read())
            //        {
            //            var entry = new DoodadFuncConsumeChangerItem
            //            {
            //                Id = reader.GetUInt32("id"),
            //                DoodadFuncConsumeChangerId = reader.GetUInt32("doodad_func_consume_changer_id"),
            //                ItemId = reader.GetUInt32("item_id")
            //            };
            //            _doodadFuncConsumeChangerItem.TryAdd(entry.Id, entry);
            //        }
            //    }
            //}

            //// doodad_func_consume_changer_model_items
            // TODO there is no such table in 5070 AAFree
            //using (var command = connection.CreateCommand())
            //{
            //    command.CommandText = "SELECT * FROM doodad_func_consume_changer_model_items";
            //    command.Prepare();
            //    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            //    {
            //        while (reader.Read())
            //        {
            //            var func = new DoodadFuncConsumeChangerModelItem
            //            {
            //                Id = reader.GetUInt32("id"),
            //                DoodadFuncConsumeChangerModelId = reader.GetUInt32("doodad_func_consume_changer_model_id"),
            //                ItemId = reader.GetUInt32("item_id")
            //            };
            //            _phaseFuncTemplates["DoodadFuncConsumeChangerModelItem"].Add(func.Id, func);
            //        }
            //    }
            //}

            // doodad_func_consume_changer_models
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
                        //func.Name = reader.GetString("name"); // there is no such field in the database for version 3.0.3.0
                        _phaseFuncTemplates["DoodadFuncConsumeChangerModel"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_consume_changers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_consume_changers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConsumeChanger
                        {
                            Id = reader.GetUInt32("id"),
                            SlotId = reader.GetUInt32("slot_id"),
                            Count = reader.GetInt32("count")
                        };
                        _funcTemplates["DoodadFuncConsumeChanger"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_consume_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_consume_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConsumeItem
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Count = reader.GetInt32("count")
                        };
                        _phaseFuncTemplates["DoodadFuncConsumeItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_convert_fish_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_convert_fish_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConvertFishItem
                        {
                            Id = reader.GetUInt32("id"),
                            DoodadFuncConvertFishId = reader.GetUInt32("doodad_func_convert_fish_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            LootPackId = reader.GetUInt32("loot_pack_id")
                        };
                        _phaseFuncTemplates["DoodadFuncConvertFishItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_convert_fishes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_convert_fishes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConvertFish
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncConvertFish"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_acts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_acts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftAct
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCraftAct"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_cancels
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_cancels";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftCancel
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCraftCancel"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_directs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_directs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftDirect
                        {
                            Id = reader.GetUInt32("id"),
                            NextPhase = reader.GetInt32("next_phase", -1)
                        };
                        _phaseFuncTemplates["DoodadFuncCraftDirect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_get_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_get_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftGetItem
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCraftGetItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_infos
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_infos";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftInfo
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCraftInfo"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_packs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_packs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftPack
                        {
                            Id = reader.GetUInt32("id"),
                            CraftPackId = reader.GetUInt32("craft_pack_id")
                        };
                        _funcTemplates["DoodadFuncCraftPack"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_start_crafts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_start_crafts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftStartCraft
                        {
                            Id = reader.GetUInt32("id"),
                            DoodadFuncCraftStartId = reader.GetUInt32("doodad_func_craft_start_id"),
                            CraftId = reader.GetUInt32("craft_id")
                        };
                        _phaseFuncTemplates["DoodadFuncCraftStartCraft"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_craft_starts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_craft_starts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCraftStart
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCraftStart"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_crop_harvests
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_crop_harvests";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCropHarvest
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCropHarvest"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_crystal_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_crystal_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCrystalCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCrystalCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_cutdownings
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_cutdownings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCutdowning
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCutdowning"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_cutdowns
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_cutdowns";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncCutdown
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncCutdown"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_dairy_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_dairy_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncDairyCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncDairyCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_declare_sieges
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_declare_sieges";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncDeclareSiege
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _phaseFuncTemplates["DoodadFuncDeclareSiege"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_digs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_digs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncDig
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncDig"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_dig_terrains
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_dig_terrains";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncDigTerrain
                        {
                            Id = reader.GetUInt32("id"),
                            Radius = reader.GetInt32("radius"),
                            Life = reader.GetInt32("life")
                        };
                        _funcTemplates["DoodadFuncDigTerrain"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_dyeingredient_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_dyeingredient_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncDyeingredientCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncDyeingredientCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_enter_instances
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_enter_instances";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncEnterInstance
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneId = reader.GetUInt32("zone_id"),
                            ItemId = reader.GetUInt32("item_id", 0)
                        };
                        _funcTemplates["DoodadFuncEnterInstance"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_enter_sys_instances
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_enter_sys_instances";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncEnterSysInstance
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneId = reader.GetUInt32("zone_id"),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0)
                        };
                        _funcTemplates["DoodadFuncEnterSysInstance"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_evidence_item_loots
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_evidence_item_loots";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncEvidenceItemLoot
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            CrimeValue = reader.GetInt32("crime_value"),
                            CrimeKindId = reader.GetUInt32("crime_kind_id")
                        };
                        _funcTemplates["DoodadFuncEvidenceItemLoot"].Add(func.Id, func);
                    }
                }
            }

            // TODO: doodad_func_exchange_items( id INT, doodad_func_exchange_id INT, item_id INT, loot_pack_id INT )

            // doodad_func_exchanges
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_exchanges";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncExchange
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _phaseFuncTemplates["DoodadFuncExchange"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_exit_induns
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_exit_induns";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncExitIndun
                        {
                            Id = reader.GetUInt32("id"),
                            ReturnPointId = reader.GetUInt32("return_point_id", 0)
                        };
                        _funcTemplates["DoodadFuncExitIndun"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_fake_uses
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_fake_uses";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncFakeUse
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id", 0),
                            FakeSkillId = reader.GetUInt32("fake_skill_id", 0),
                            TargetParent = reader.GetBoolean("target_parent", true)
                        };
                        _funcTemplates["DoodadFuncFakeUse"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_feeds
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_feeds";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncFeed
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Count = reader.GetInt32("count")
                        };
                        _funcTemplates["DoodadFuncFeed"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_fiber_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_fiber_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncFiberCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncFiberCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_finals
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_finals";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncFinal
                        {
                            Id = reader.GetUInt32("id"),
                            After = reader.GetInt32("after", 0),
                            Respawn = reader.GetBoolean("respawn", true),
                            MinTime = reader.GetInt32("min_time", 0),
                            MaxTime = reader.GetInt32("max_time", 0),
                            ShowTip = reader.GetBoolean("show_tip", true),
                            ShowEndTime = reader.GetBoolean("show_end_time", true),
                            Tip = reader.GetString("tip")
                        };
                        _phaseFuncTemplates["DoodadFuncFinal"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_fish_schools
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_fish_schools";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncFishSchool
                        {
                            Id = reader.GetUInt32("id"),
                            NpcSpawnerId = reader.GetUInt32("npc_spawner_id")
                        };
                        _phaseFuncTemplates["DoodadFuncFishSchool"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_fruit_picks
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_fruit_picks";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncFruitPick
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncFruitPick"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_gass_extracts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_gass_extracts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncGassExtract
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncGassExtract"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_growths
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_growths";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncGrowth
                        {
                            Id = reader.GetUInt32("id"),
                            Delay = reader.GetInt32("delay"),
                            StartScale = reader.GetInt32("start_scale"),
                            EndScale = reader.GetInt32("end_scale"),
                            NextPhase = reader.GetInt32("next_phase", -1)
                        };
                        // TODO: Remove testing stuff
                        // if (func.Delay > 0)
                        //     func.Delay = Math.Max(1, func.Delay / 1000);

                        _phaseFuncTemplates["DoodadFuncGrowth"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_harvests
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_harvests";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncHarvest
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncHarvest"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_house_farms
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_house_farms";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncHouseFarm
                        {
                            Id = reader.GetUInt32("id"),
                            ItemCategoryId = reader.GetUInt32("item_category_id")
                        };
                        _phaseFuncTemplates["DoodadFuncHouseFarm"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_housing_areas
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_housing_areas";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncHousingArea
                        {
                            Id = reader.GetUInt32("id"),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id"),
                            Radius = reader.GetInt32("radius")
                        };
                        _funcTemplates["DoodadFuncHousingArea"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_hungers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_hungers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncHunger
                        {
                            Id = (uint)reader.GetInt32("id"),
                            HungryTerm = reader.GetInt32("hungry_term"),
                            FullStep = reader.GetInt32("full_step"),
                            PhaseChangeLimit = reader.GetInt32("phase_change_limit"),
                            NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1
                        };
                        _phaseFuncTemplates["DoodadFuncHunger"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_insert_counters
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_insert_counters";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncInsertCounter
                        {
                            Id = reader.GetUInt32("id"),
                            Count = reader.GetInt32("count"),
                            ItemId = reader.GetUInt32("item_id"),
                            ItemCount = reader.GetInt32("item_count")
                        };
                        _funcTemplates["DoodadFuncInsertCounter"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_Logics
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_logics";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncLogic
                        {
                            Id = reader.GetUInt32("id"),
                            OperationId = reader.GetUInt32("operation_id"),
                            DelayId = reader.GetUInt32("delay_id")
                        };
                        _phaseFuncTemplates["DoodadFuncLogic"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_logic_family_providers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_logic_family_providers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncLogicFamilyProvider
                        {
                            Id = reader.GetUInt32("id"),
                            FamilyId = reader.GetUInt32("family_id")
                        };
                        _phaseFuncTemplates["DoodadFuncLogicFamilyProvider"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_logic_family_subscribers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_logic_family_subscribers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncLogicFamilySubscriber
                        {
                            Id = reader.GetUInt32("id"),
                            FamilyId = reader.GetUInt32("family_id")
                        };
                        _phaseFuncTemplates["DoodadFuncLogicFamilySubscriber"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_loot_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_loot_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncLootItem
                        {
                            Id = reader.GetUInt32("id"),
                            WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            CountMin = reader.GetInt32("count_min"),
                            CountMax = reader.GetInt32("count_max"),
                            Percent = reader.GetInt32("percent"),
                            RemainTime = reader.GetInt32("remain_time"),
                            GroupId = reader.GetUInt32("group_id")
                        };
                        _funcTemplates["DoodadFuncLootItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_loot_packs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_loot_packs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncLootPack
                        {
                            Id = reader.GetUInt32("id"),
                            LootPackId = reader.GetUInt32("loot_pack_id")
                        };
                        _funcTemplates["DoodadFuncLootPack"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_machine_parts_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_machine_parts_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncMachinePartsCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncMachinePartsCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_medicalingredient_mines
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_medicalingredient_mines";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncMedicalingredientMine
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncMedicalingredientMine"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_mows
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_mows";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncMow
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncMow"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_mark_pos_to_maps
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_mark_pos_to_maps";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviMarkPosToMap
                        {
                            Id = reader.GetUInt32("id"),
                            X = reader.GetInt32("x"),
                            Y = reader.GetInt32("y"),
                            Z = reader.GetInt32("z")
                        };
                        _funcTemplates["DoodadFuncNaviMarkPosToMap"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_namings
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_namings";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviNaming
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncNaviNaming"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_open_bounties
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_open_bounties";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviOpenBounty
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncNaviOpenBounty"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_open_mailboxes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_open_mailboxes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviOpenMailbox
                        {
                            Id = reader.GetUInt32("id"),
                            Duration = reader.GetInt32("duration")
                        };
                        _funcTemplates["DoodadFuncNaviOpenMailbox"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_open_portals
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_open_portals";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviOpenPortal
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncNaviOpenPortal"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_remove_timers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_remove_timers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviRemoveTimer
                        {
                            Id = reader.GetUInt32("id"),
                            After = reader.GetInt32("after")
                        };
                        _phaseFuncTemplates["DoodadFuncNaviRemoveTimer"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_removes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_removes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviRemove
                        {
                            Id = reader.GetUInt32("id"),
                            ReqLaborPower = reader.GetInt32("req_lp")
                        };
                        _funcTemplates["DoodadFuncNaviRemove"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_navi_teleports
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_navi_teleports";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncNaviTeleport
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncNaviTeleport"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_open_farm_infos
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_open_farm_infos";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncOpenFarmInfo
                        {
                            Id = reader.GetUInt32("id"),
                            FarmId = reader.GetUInt32("farm_id")
                        };
                        _funcTemplates["DoodadFuncOpenFarmInfo"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_open_papers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_open_papers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncOpenPaper
                        {
                            Id = reader.GetUInt32("id"),
                            BookPageId = reader.GetUInt32("book_page_id", 0),
                            BookId = reader.GetUInt32("book_id", 0)
                        };
                        _funcTemplates["DoodadFuncOpenPaper"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_ore_mines
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_ore_mines";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncOreMine
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncOreMine"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_parent_infos
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_parent_infos";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncParentInfo
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncParentInfo"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_parrots
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_parrots";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncParrot
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _phaseFuncTemplates["DoodadFuncParrot"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_plant_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_plant_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPlantCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncPlantCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_play_flow_graphs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_play_flow_graphs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPlayFlowGraph
                        {
                            Id = reader.GetUInt32("id"),
                            EventOnPhaseChangeId = reader.GetUInt32("event_on_phase_change_id"),
                            EventOnVisibleId = reader.GetUInt32("event_on_visible_id")
                        };
                        _phaseFuncTemplates["DoodadFuncPlayFlowGraph"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_pulse_triggers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_pulse_triggers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPulseTrigger
                        {
                            Id = reader.GetUInt32("id"),
                            Flag = reader.GetBoolean("flag", true),
                            NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1
                        };
                        _phaseFuncTemplates["DoodadFuncPulseTrigger"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_pulses
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_pulses";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPulse
                        {
                            Id = reader.GetUInt32("id"),
                            Flag = reader.GetBoolean("flag", true)
                        };
                        _phaseFuncTemplates["DoodadFuncPulse"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_purchases
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_purchases";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPurchase
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id", 0),
                            Count = reader.GetInt32("count"),
                            CoinItemId = reader.GetUInt32("coin_item_id", 0),
                            CoinCount = reader.GetInt32("coin_count", 0),
                            CurrencyId = reader.GetUInt32("currency_id")
                        };
                        _funcTemplates["DoodadFuncPurchase"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_puzzle_ins
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_puzzle_ins";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPuzzleIn
                        {
                            Id = reader.GetUInt32("id"),
                            GroupId = reader.GetUInt32("group_id"),
                            Ratio = reader.GetFloat("ratio")
                        };
                        _phaseFuncTemplates["DoodadFuncPuzzleIn"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_puzzle_outs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_puzzle_outs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPuzzleOut
                        {
                            Id = reader.GetUInt32("id"),
                            GroupId = reader.GetUInt32("group_id"),
                            Ratio = reader.GetFloat("ratio"),
                            Anim = reader.GetString("anim"),
                            ProjectileId = reader.GetUInt32("projectile_id", 0),
                            ProjectileDelay = reader.GetInt32("projectile_delay"),
                            LootPackId = reader.GetUInt32("loot_pack_id", 0),
                            Delay = reader.GetInt32("delay"),
                            NextPhase = reader.GetInt32("next_phase", -1)
                        };
                        _phaseFuncTemplates["DoodadFuncPuzzleOut"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_puzzle_rolls
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_puzzle_rolls";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPuzzleRoll
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Count = reader.GetInt32("count")
                        };
                        _funcTemplates["DoodadFuncPuzzleRoll"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_quests
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_quests";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncQuest
                        {
                            Id = reader.GetUInt32("id"),
                            QuestKindId = reader.GetUInt32("quest_kind_id"),
                            QuestId = reader.GetUInt32("quest_id")
                        };
                        _funcTemplates["DoodadFuncQuest"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_ratio_changes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_ratio_changes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRatioChange
                        {
                            Id = reader.GetUInt32("id"),
                            Ratio = reader.GetInt32("ratio"),
                            NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1
                        };
                        _phaseFuncTemplates["DoodadFuncRatioChange"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_ratio_respawns
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_ratio_respawns";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRatioRespawn
                        {
                            Id = reader.GetUInt32("id"),
                            Ratio = reader.GetInt32("ratio"),
                            SpawnDoodadId = reader.GetUInt32("spawn_doodad_id")
                        };
                        _phaseFuncTemplates["DoodadFuncRatioRespawn"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_recover_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_recover_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRecoverItem
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncRecoverItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_remove_instances
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_remove_instances";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRemoveInstance
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneId = reader.GetUInt32("zone_id")
                        };
                        _funcTemplates["DoodadFuncRemoveInstance"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_remove_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_remove_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRemoveItem
                        {
                            Id = reader.GetUInt32("id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Count = reader.GetInt32("count")
                        };
                        _funcTemplates["DoodadFuncRemoveItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_renew_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_renew_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRenewItem
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id")
                        };
                        _funcTemplates["DoodadFuncRenewItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_require_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_require_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRequireItem
                        {
                            Id = reader.GetUInt32("id"),
                            WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id"),
                            ItemId = reader.GetUInt32("item_id")
                        };
                        _phaseFuncTemplates["DoodadFuncRequireItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_require_quests
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_require_quests";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRequireQuest
                        {
                            Id = reader.GetUInt32("id"),
                            WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id"),
                            QuestId = reader.GetUInt32("quest_id")
                        };
                        _phaseFuncTemplates["DoodadFuncRequireQuest"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_respawns
            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_respawns";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRespawn
                        {
                            Id = reader.GetUInt32("id"),
                            MinTime = reader.GetInt32("min_time"),
                            MaxTime = reader.GetInt32("max_time")
                        };
                        _phaseFuncTemplates["DoodadFuncRespawn"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_rock_mines
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_rock_mines";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncRockMine
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncRockMine"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_seed_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_seed_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSeedCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncSeedCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_shears
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_shears";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncShear
                        {
                            Id = reader.GetUInt32("id"),
                            ShearTypeId = reader.GetUInt32("shear_type_id"),
                            ShearTerm = reader.GetInt32("shear_term")
                        };
                        _funcTemplates["DoodadFuncShear"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_siege_periods
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_siege_periods";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSiegePeriod
                        {
                            Id = reader.GetUInt32("id"),
                            SiegePeriodId = reader.GetUInt32("siege_period_id"),
                            NextPhase = reader.GetInt32("next_phase", -1),
                            Defense = reader.GetBoolean("defense", true)
                        };
                        _phaseFuncTemplates["DoodadFuncSiegePeriod"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_signs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_signs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSign
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name"),
                            PickNum = reader.GetInt32("pick_num")
                        };
                        _phaseFuncTemplates["DoodadFuncSign"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_skill_hits
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_skill_hits";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSkillHit
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id")
                        };
                        _funcTemplates["DoodadFuncSkillHit"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_skin_offs
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_skin_offs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSkinOff
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncSkinOff"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_soil_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_soil_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSoilCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncSoilCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_spawn_gimmicks
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_spawn_gimmicks";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSpawnGimmick
                        {
                            Id = reader.GetUInt32("id"),
                            GimmickId = reader.GetUInt32("gimmick_id"),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id"),
                            Scale = reader.GetFloat("scale"),
                            OffsetX = reader.GetFloat("offset_x"),
                            OffsetY = reader.GetFloat("offset_y"),
                            OffsetZ = reader.GetFloat("offset_z"),
                            VelocityX = reader.GetFloat("velocity_x"),
                            VelocityY = reader.GetFloat("velocity_y"),
                            VelocityZ = reader.GetFloat("velocity_z"),
                            AngleX = reader.GetFloat("angle_x"),
                            AngleY = reader.GetFloat("angle_y"),
                            AngleZ = reader.GetFloat("angle_z"),
                            NextPhase = reader.GetInt32("next_phase", -1)
                        };
                        _phaseFuncTemplates["DoodadFuncSpawnGimmick"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_spawn_mgmts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_spawn_mgmts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSpawnMgmt
                        {
                            Id = reader.GetUInt32("id"),
                            GroupId = reader.GetUInt32("group_id"),
                            Spawn = reader.GetBoolean("spawn", true),
                            ZoneId = reader.GetUInt32("zone_id")
                        };
                        _phaseFuncTemplates["DoodadFuncSpawnMgmt"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_spice_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_spice_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncSpiceCollect { Id = reader.GetUInt32("id") };
                        _funcTemplates["DoodadFuncSpiceCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_stamp_makers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_stamp_makers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncStampMaker
                        {
                            Id = reader.GetUInt32("id"),
                            ConsumeMoney = reader.GetInt32("consume_money"),
                            ItemId = reader.GetUInt32("item_id"),
                            ConsumeItemId = reader.GetUInt32("consume_item_id"),
                            ConsumeCount = reader.GetInt32("consume_count")
                        };
                        _funcTemplates["DoodadFuncStampMaker"].Add(func.Id, func);
                    }
                }
            }

            // TODO 1.2
            // doodad_func_store_uis
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_store_uis";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncStoreUi
                        {
                            Id = reader.GetUInt32("id"),
                            MerchantPackId = reader.GetUInt32("merchant_pack_id")
                        };
                        _funcTemplates["DoodadFuncStoreUi"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_timers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_timers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncTimer
                        {
                            Id = reader.GetUInt32("id"),
                            Delay = reader.GetInt32("delay"),
                            NextPhase = reader.GetInt32("next_phase", -1),
                            KeepRequester = reader.GetBoolean("keep_requester", true),
                            ShowTip = reader.GetBoolean("show_tip", true),
                            ShowEndTime = reader.GetBoolean("show_end_time", true),
                            Tip = reader.GetString("tip")
                        };
                        // TODO: Remove testing stuff
                        // if (func.Delay > 0)
                        //     func.Delay = Math.Max(1, func.Delay / 1000);

                        _phaseFuncTemplates["DoodadFuncTimer"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_tods
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_tods";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncTod
                        {
                            Id = reader.GetUInt32("id"),
                            Tod = reader.GetUInt32("tod"),
                            NextPhase = reader.GetInt32("next_phase", -1) >= 0 ? reader.GetInt32("next_phase") : -1
                        };

                        // Calculate the ToD value as a float in hours
                        var tod = func.Tod;
                        // Correction for typos in the compact DB
                        while (tod >= 2400)
                            tod /= 10;
                        // Conversion from int to float
                        var hh = tod / 100;
                        var mm = tod % 100;
                        if (mm >= 60)
                            Logger.Warn($"DoodadFuncToD has invalid value for minutes, Id {func.Id}, ToD {func.Tod}");
                        mm %= 60;
                        func.TodAsHours = (hh * 1f) + (mm / 60f);

                        _phaseFuncTemplates["DoodadFuncTod"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_tree_byproducts_collects
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_tree_byproducts_collects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncTreeByProductsCollect
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncTreeByProductsCollect"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_ucc_imprints
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_ucc_imprints";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncUccImprint
                        {
                            Id = reader.GetUInt32("id")
                        };
                        _funcTemplates["DoodadFuncUccImprint"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_uses
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_uses";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncUse
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id", 0)
                        };
                        _funcTemplates["DoodadFuncUse"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_water_volumes
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_water_volumes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncWaterVolume
                        {
                            Id = reader.GetUInt32("id"),
                            LevelChange = reader.GetFloat("levelChange"),
                            Duration = reader.GetFloat("duration")
                        };
                        _phaseFuncTemplates["DoodadFuncWaterVolume"].Add(func.Id, func);
                    }
                }
            }

            // TODO 1.2
            // doodad_func_zone_reacts
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_zone_reacts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncZoneReact
                        {
                            Id = reader.GetUInt32("id"),
                            ZoneGroupId = reader.GetUInt32("zone_group_id"),
                            NextPhase = reader.GetInt32("next_phase", -1)
                        };

                        _phaseFuncTemplates["DoodadFuncZoneReact"].Add(func.Id, func);
                    }
                }
            }

            Logger.Info("Finished loading doodad functions ...");

            #endregion

            #region doodads_and_func_groups

            Logger.Info("Loading doodad templates...");

            // First load all doodad_func_groups
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_groups";
                command.Prepare();
                using (var sqliteDataReaderChild = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReaderChild))
                {
                    while (reader.Read())
                    {
                        var funcGroups = new DoodadFuncGroups
                        {
                            Id = reader.GetUInt32("id"),
                            Almighty = reader.GetUInt32("doodad_almighty_id"),
                            GroupKindId =
                                (DoodadFuncGroups.DoodadFuncGroupKind)reader.GetUInt32("doodad_func_group_kind_id"),
                            SoundId = reader.GetUInt32("sound_id", 0)
                        };

                        if (!_allFuncGroups.TryAdd(funcGroups.Id, funcGroups))
                        {
                            Logger.Fatal($"Failed to add FuncGroups: {funcGroups.Id}");
                        }
                    }
                }
            }

            // Then Load actual doodads
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from doodad_almighties";
                command.Prepare();
                using (var sqliteDataReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteDataReader))
                {
                    while (reader.Read())
                    {
                        var templateId = reader.GetUInt32("id");

                        var cofferCapacity = IsCofferTemplate(templateId);

                        var template = cofferCapacity > 0
                            ? new DoodadCofferTemplate() { Capacity = cofferCapacity }
                            : new DoodadTemplate();

                        template.Id = templateId;
                        template.OnceOneMan = reader.GetBoolean("once_one_man", true);
                        template.OnceOneInteraction = reader.GetBoolean("once_one_interaction", true);
                        template.MgmtSpawn = reader.GetBoolean("mgmt_spawn", true);
                        template.Percent = reader.GetInt32("percent", 0);
                        template.MinTime = reader.GetInt32("min_time", 0);
                        template.MaxTime = reader.GetInt32("max_time", 0);
                        template.ModelKindId = reader.GetUInt32("model_kind_id");
                        template.UseCreatorFaction = reader.GetBoolean("use_creator_faction", true);
                        template.ForceTodTopPriority = reader.GetBoolean("force_tod_top_priority", true);
                        //template.MilestoneId = reader.GetUInt32("milestone_id", 0); // there is no such field in the database for version 3.0.3.0
                        template.GroupId = reader.GetUInt32("group_id");
                        template.UseTargetDecal = reader.GetBoolean("use_target_decal", true);
                        template.UseTargetSilhouette = reader.GetBoolean("use_target_silhouette", true);
                        template.UseTargetHighlight = reader.GetBoolean("use_target_highlight", true);
                        template.TargetDecalSize = reader.GetFloat("target_decal_size", 0);
                        template.SimRadius = reader.GetInt32("sim_radius", 0);
                        template.CollideShip = reader.GetBoolean("collide_ship", true);
                        template.CollideVehicle = reader.GetBoolean("collide_vehicle", true);
                        template.ClimateId = (Climate)reader.GetUInt32("climate_id", 0);
                        template.SaveIndun = reader.GetBoolean("save_indun", true);
                        template.ForceUpAction = reader.GetBoolean("force_up_action", true);
                        template.Parentable = reader.GetBoolean("parentable", true);
                        template.Childable = reader.GetBoolean("childable", true);
                        template.FactionId = (FactionsEnum)reader.GetUInt32("faction_id");
                        template.GrowthTime = reader.GetInt32("growth_time", 0);
                        template.DespawnOnCollision = reader.GetBoolean("despawn_on_collision", true);
                        template.NoCollision = reader.GetBoolean("no_collision", true);
                        template.RestrictZoneId = reader.IsDBNull("restrict_zone_id")
                            ? 0
                            : reader.GetUInt32("restrict_zone_id");

                        _templates.Add(template.Id, template);
                    }
                }
            }

            // Bind FuncGroups to Template
            foreach (var (_, funcGroups) in _allFuncGroups)
            {
                var template = GetTemplate(funcGroups.Almighty);
                template?.FuncGroups.Add(funcGroups);
            }

            Logger.Info($"Loaded {_templates.Count} doodad templates");

            #endregion
        }

        CreateTemplateCaches();
        _loaded = true;
    }

    /// <summary>
    /// Creates and cache various values that would otherwise consume too much time to be calculating all the time at runtime
    /// </summary>
    private void CreateTemplateCaches()
    {
        // For all doodad templates
        foreach (var template in _templates.Values)
        {
            // Cache Total Growth Times for doodads that have them
            template.TotalDoodadGrowthTime = 0;
            foreach (var funcGroup in template.FuncGroups)
            {
                var funcGroups = Instance.GetFuncsForGroup(funcGroup.Id);
                foreach (var doodadFunc in funcGroups)
                {
                    var thisFuncTemplate = Instance.GetPhaseFuncTemplate(doodadFunc.FuncId, doodadFunc.FuncType);
                    if (thisFuncTemplate is DoodadFuncGrowth growthFunc)
                    {
                        template.TotalDoodadGrowthTime += growthFunc.Delay;
                    }
                }
            }

            if (template.TotalDoodadGrowthTime <= 0)
            {
                template.TotalDoodadGrowthTime = template.GrowthTime;
            }
        }
    }

    /// <summary>
    /// Checks if a DoodadTemplateId has a doodad_func_coffer attached to it
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns>Returns the Coffer Capacity if true, otherwise returns -1</returns>
    private int IsCofferTemplate(uint templateId)
    {
        if (templateId == 0)
        {
            return -1;
        }

        // Check if template is a Coffer
        foreach (var (_, funcGroup) in _allFuncGroups)
        {
            if (funcGroup.Almighty != templateId)
            {
                continue;
            }

            if (!_funcsByGroups.TryGetValue(funcGroup.Id, out var funcList))
            {
                continue;
            }

            foreach (var func in funcList)
            {
                if (!_phaseFuncTemplates.TryGetValue(func.FuncType, out var phaseFuncTemplates))
                {
                    continue;
                }

                if (!phaseFuncTemplates.TryGetValue(func.FuncId, out var phaseFuncTemplate))
                {
                    continue;
                }

                if (phaseFuncTemplate is DoodadFuncCoffer funcCoffer)
                {
                    return funcCoffer.Capacity;
                }
            }
        }

        return -1;
    }

    public Doodad Create(uint bcId, uint templateId, GameObject ownerObject = null, bool skipPhaseInitialization = false)
    {
        if (!_templates.TryGetValue(templateId, out var template))
        {
            return null;
        }

        Doodad doodad = null;

        // Check if template is a Coffer
        if (template is DoodadCofferTemplate doodadCofferTemplate)
        {
            doodad = new DoodadCoffer { Capacity = doodadCofferTemplate.Capacity };
        }

        doodad ??= new Doodad();

        doodad.ObjId = bcId > 0 ? bcId : ObjectIdManager.Instance.GetNextId();
        doodad.TemplateId = template.Id; // copy the templateId
        doodad.Template = template;
        doodad.OwnerObjId = ownerObject?.ObjId ?? 0;
        doodad.PlantTime = DateTime.UtcNow;
        doodad.OwnerType = DoodadOwnerType.System;
        doodad.FuncGroupId = doodad.GetFuncGroupId();
        // doodad.GrowthTime = doodad.PlantTime.AddMilliseconds(doodad.Template.TotalDoodadGrowthTime);

        switch (ownerObject)
        {
            case Character character:
                doodad.OwnerId = character.Id;
                doodad.OwnerType = DoodadOwnerType.Character;
                break;
            case House house:
                doodad.OwnerObjId = 0;
                doodad.ParentObjId = house.ObjId;
                doodad.OwnerId = house.OwnerId;
                doodad.OwnerType = DoodadOwnerType.Housing;
                doodad.OwnerDbId = house.Id;
                break;
            case Transfer transfer:
                doodad.OwnerId = 0;
                doodad.ParentObjId = transfer.ObjId;
                doodad.OwnerType = DoodadOwnerType.System;
                break;
        }

        if (!skipPhaseInitialization)
        {
            Task.Run(() => doodad.InitDoodad());
        }

        //Logger.Debug($"Create: TemplateId {doodad.TemplateId}, ObjId {doodad.ObjId}, FuncGroupId {doodad.FuncGroupId}");

        return doodad;
    }

    public DoodadFunc GetFunc(uint funcId)
    {
        return _funcsById.GetValueOrDefault(funcId);
    }

    public DoodadFunc GetFunc(uint funcGroupId, uint skillId)
    {
        if (!_funcsByGroups.TryGetValue(funcGroupId, out var funcsInGroup))
        {
            return null;
        }

        foreach (var func in funcsInGroup)
        {
            if (func.SkillId == skillId)
            {
                return func;
            }

            var funcTemplate = GetFuncTemplate(func.FuncId, func.FuncType);
            // Special handler for fake use skill id
            if (funcTemplate is DoodadFuncFakeUse { FakeSkillId: > 0 } fakeUseTemplate && fakeUseTemplate.FakeSkillId == skillId)
            {
                return func;
            }

            // Special handler for use (func) skill id
            if (funcTemplate is DoodadFuncUse { SkillId: > 0 } useTemplate && useTemplate.SkillId == skillId)
            {
                return func;
            }
        }

        // First we skip functions with NextPhase = -1
        foreach (var func in funcsInGroup)
        {
            if (func.SkillId == 0 && func.NextPhase != -1)
            {
                return func;
            }
        }

        // Then we search with NextPhase = -1
        foreach (var func in funcsInGroup)
        {
            if (func.SkillId == 0)
            {
                return func;
            }
        }

        return null;
    }

    public List<DoodadFunc> GetFuncsForGroup(uint funcGroupId)
    {
        return _funcsByGroups.TryGetValue(funcGroupId, out var group) ? group : new List<DoodadFunc>();
    }

    public List<DoodadPhaseFunc> GetPhaseFunc(uint funcGroupId)
    {
        return _phaseFuncs.TryGetValue(funcGroupId, out var func) ? func : new List<DoodadPhaseFunc>();
    }

    public DoodadFuncTemplate GetFuncTemplate(uint funcId, string funcType)
    {
        if (!_funcTemplates.TryGetValue(funcType, out var funcs))
        {
            return null;
        }

        return funcs.GetValueOrDefault(funcId);
    }

    public DoodadPhaseFuncTemplate GetPhaseFuncTemplate(uint funcId, string funcType)
    {
        if (!_phaseFuncTemplates.TryGetValue(funcType, out var funcs))
        {
            return null;
        }

        return funcs.GetValueOrDefault(funcId);
    }

    /// <summary>
    /// GetDoodadFuncGroups - Get a group of functions for a given TemplateId
    /// </summary>
    /// <param name="doodadTemplateId"></param>
    /// <returns>List of DoodadFuncGroups</returns>
    public List<DoodadFuncGroups> GetDoodadFuncGroups(uint doodadTemplateId)
    {
        var listDoodadFuncGroups = new List<DoodadFuncGroups>();

        if (_templates.TryGetValue(doodadTemplateId, out var template))
        {
            listDoodadFuncGroups.AddRange(template.FuncGroups);
        }

        return listDoodadFuncGroups;
    }

    public List<uint> GetDoodadFuncGroupsId(uint doodadTemplateId)
    {
        var listId = new List<uint>();

        var listDoodadFuncGroups = new List<DoodadFuncGroups>();

        if (!_templates.TryGetValue(doodadTemplateId, out var template))
        {
            return listId;
        }

        listDoodadFuncGroups.AddRange(template.FuncGroups);
        foreach (var item in listDoodadFuncGroups)
        {
            listId.Add(item.Id);
        }

        return listId;
    }

    /// <summary>
    /// GetDoodadFuncs - Get all features
    /// </summary>
    /// <param name="doodadFuncGroupId"></param>
    /// <returns>List of DoodadFunc</returns>
    public List<DoodadFunc> GetDoodadFuncs(uint doodadFuncGroupId)
    {
        return _funcsByGroups.TryGetValue(doodadFuncGroupId, out var funcs) ? funcs : new List<DoodadFunc>();
    }

    /// <summary>
    /// GetDoodadPhaseFuncs - Get all phase functions
    /// </summary>
    /// <param name="funcGroupId"></param>
    /// <returns>DoodadFunc[]</returns>
    public List<DoodadPhaseFunc> GetDoodadPhaseFuncs(uint funcGroupId)
    {
        return _phaseFuncs.TryGetValue(funcGroupId, out var funcs) ? funcs : new List<DoodadPhaseFunc>();
    }

    /// <summary>
    /// Saves and creates a doodad
    /// </summary>
    public static Doodad CreatePlayerDoodad(Character character, uint id, float x, float y, float z, float zRot, float scale, ulong itemId, FarmType farmType = FarmType.Invalid)
    {
        Logger.Warn($"{character.Name} is placing a doodad {id} at position {x} {y} {z}");

        var targetHouse = HousingManager.Instance.GetHouseAtLocation(x, y);

        // Create doodad
        var doodad = Instance.Create(0, id, character, true);
        doodad.IsPersistent = true;
        doodad.Transform = character.Transform.CloneDetached(doodad);
        doodad.Transform.Local.SetPosition(x, y, z);
        doodad.Transform.Local.SetZRotation(zRot);
        doodad.ItemId = itemId;
        doodad.PlantTime = DateTime.UtcNow;
        doodad.FarmType = farmType;
        if (targetHouse != null)
        {
            doodad.OwnerDbId = targetHouse.Id;
            doodad.AttachPoint = AttachPointKind.None;
            doodad.OwnerType = DoodadOwnerType.Housing;
            doodad.ParentObj = targetHouse;
            doodad.ParentObjId = targetHouse.ObjId;
            doodad.Transform.Parent = targetHouse.Transform;
        }
        else
        {
            doodad.OwnerDbId = 0;
        }

        if (scale > 0f)
        {
            doodad.SetScale(scale);
        }

        // Consume item
        var items = ItemManager.Instance.GetItemIdsFromDoodad(id);
        var preferredItem = character.Inventory.Bag.GetItemByItemId(itemId);

        if (preferredItem == null)
        {
            Logger.Error($"Unable to create doodad because source item (Id: {itemId}) does not exist in {character.Name}'s bag inventory.");
            doodad.Delete();
            return null;
        }

        doodad.ItemTemplateId = preferredItem.TemplateId;
        if (preferredItem.Template.MaxCount > 1)
        {
            doodad.ItemId = 0; // If it's a stackable item, don't store the actual itemId, but only it's templateId
        }

        if (doodad is DoodadCoffer coffer)
        {
            coffer.InitializeCoffer(character.Id);
        }

        foreach (var item in items)
        {
            character.ItemUse(preferredItem);
            character.Inventory.ConsumeItem(new[] { SlotType.Bag }, ItemTaskType.DoodadCreate, item, 1,
                preferredItem);
        }

        doodad.InitDoodad();
        doodad.Spawn();
        doodad.Save();
        SpawnManager.Instance.AddPlayerDoodad(doodad);

        return doodad;
    }

    public static bool OpenCofferDoodad(Character character, uint objId)
    {
        var doodad = WorldManager.Instance.GetDoodad(objId);
        if (doodad is not DoodadCoffer coffer)
        {
            return false;
        }

        // Somebody already using this ?
        if (coffer.OpenedBy != null)
        {
            return false;
        }

        // TODO: Check permissions

        coffer.OpenedBy = character;

        if (character == null)
        {
            return true;
        }

        byte firstSlot = 0;
        while (firstSlot < coffer.Capacity)
        {
            character.SendPacket(new SCCofferContentsUpdatePacket(coffer, firstSlot));
            firstSlot += SCCofferContentsUpdatePacket.MaxSlotsToSend;
        }

        return true;
    }

    public static bool CloseCofferDoodad(Character character, uint objId)
    {
        var doodad = WorldManager.Instance.GetDoodad(objId);
        if (doodad is not DoodadCoffer coffer)
        {
            return false;
        }

        // Used for GM commands
        if (character is null)
        {
            coffer.OpenedBy = null;
            return true;
        }

        // Only the person who opened it, can close it
        if (coffer.OpenedBy is not null && coffer.OpenedBy.Id != character.Id)
        {
            return false;
        }

        coffer.OpenedBy = null;

        return true;
    }

    public static bool ChangeDoodadData(Character player, Doodad doodad, int data)
    {
        // TODO: Can non-coffer doodads that use this packet only be changed by their owner ?
        if (doodad.OwnerId != player.Id)
        {
            return false;
        }

        // For Coffers validate if select option is applicable
        if (doodad is DoodadCoffer)
        {
            switch (data)
            {
                case (int)HousingPermission.Family when player.Family <= 0:
                    player.SendErrorMessage(ErrorMessageType.FamilyNotExist); // Not sure
                    return false;
                case (int)HousingPermission.Guild when player.Expedition is not { Id: > 0 }:
                    player.SendErrorMessage(ErrorMessageType.OnlyExpeditionMember); // Not sure
                    return false;
            }
        }

        doodad.Data = data;

        doodad.BroadcastPacket(new SCDoodadChangedPacket(doodad.ObjId, doodad.Data), false);

        return true;
    }

    public List<uint> GetDoodadFuncConsumeChangerItemList(uint doodadFuncConsumeChangerId)
    {
        return _doodadFuncConsumeChangerItem.Values
            .Where(d => d.DoodadFuncConsumeChangerId == doodadFuncConsumeChangerId).Select(entry => entry.ItemId)
            .ToList();
    }

    /// <summary>
    /// Deletes a persistent doodad directly from DB (do not use on spawned doodads)
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="transaction"></param>
    /// <param name="dbId">Doodad DB Id</param>
    public void DeleteDoodadById(MySqlConnection connection, MySqlTransaction transaction, uint dbId)
    {
        // First grab the doodad data from the DB to check if there are items attached
        ulong attachedItemId = 0u;
        ulong attachedContainer = 0u;
        using (var command = connection.CreateCommand())
        {
            if (transaction != null)
                command.Transaction = transaction;

            // First grab item related data
            command.CommandText = "SELECT * FROM doodads WHERE id = @id LIMIT 1";
            command.Parameters.AddWithValue("@id", dbId);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    attachedItemId = reader.GetUInt32("item_id");
                    attachedContainer = reader.GetUInt32("item_container_id");
                }
            }

            // Actually delete the doodad from DB
            command.CommandText = "DELETE FROM doodads WHERE id = @id";
            // command.Parameters.AddWithValue("@id", dbId); // recycled from above
            command.Prepare();
            if (command.ExecuteNonQuery() <= 0)
            {
                Logger.Error($"Failed to delete doodad from DB Id: {dbId}");
                return;
            }
        }
        DoodadIdManager.Instance.ReleaseId(dbId); // Free up the Id

        // Handle attached items
        if (attachedItemId > 0)
        {
            var item = ItemManager.Instance.GetItemByItemId(attachedItemId);
            if (item != null)
            {
                item._holdingContainer = null;
                ItemManager.Instance.ReleaseId(item.Id);
            }
        }

        // Delete attached container
        if (attachedContainer > 0)
        {
            var container = ItemManager.Instance.GetItemContainerByDbId(attachedContainer);
            if (container != null)
                ItemManager.Instance.DeleteItemContainer(container);
        }
    }
}
