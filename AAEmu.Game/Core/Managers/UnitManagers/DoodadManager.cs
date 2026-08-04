using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.Creatures;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.CommonFarm.Static;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Details;
using AAEmu.Game.Models.Game.DoodadObj.Funcs;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;
using MySql.Data.MySqlClient;

using NLog;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
// ReSharper disable ForeachCanBeConvertedToQueryUsingAnotherGetEnumerator

namespace AAEmu.Game.Core.Managers.UnitManagers;

// ReSharper disable once ClassNeverInstantiated.Global
public class DoodadManager(INonUnitObjectIdManager objectIdManager, IDoodadIdManager doodadIdManager, IItemManager itemManager, Lazy<IHousingManager> housingManager, ISusManager susManager, IFactionManager factionManager) : Singleton<DoodadManager>, IDoodadManager
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
    /// <summary>prefab_elements.file_path → models.id (PrefabModel) for Zone WZCreateDoodad.</summary>
    private Dictionary<string, uint> _zoneModelPathToId;
    private Dictionary<string, uint> _zoneModelBasenameToId;

    // ReSharper disable once FieldCanBeMadeReadOnly.Local
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static Dictionary<uint, Creature> _creatures = new();

    public static string GetSpawnName(uint id)
    {
        return _creatures.TryGetValue(id, out var creature) ? creature.Title : string.Empty;
    }

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

        _templates = [];
        _allFuncGroups = [];
        _funcsByGroups = [];
        _funcsById = [];
        _phaseFuncs = [];
        _funcTemplates = [];
        _phaseFuncTemplates = [];
        foreach (var type in Helpers.GetTypesInNamespace(Assembly.GetAssembly(GetType()),
                     "AAEmu.Game.Models.Game.DoodadObj.Funcs"))
        {
            if (!type.IsAbstract && typeof(DoodadFuncTemplate).IsAssignableFrom(type))
            {
                _funcTemplates.Add(type.Name, []);
            }
            else if (!type.IsAbstract && typeof(DoodadPhaseFuncTemplate).IsAssignableFrom(type))
            {
                _phaseFuncTemplates.Add(type.Name, []);
            }
        }

        _doodadFuncConsumeChangerItem = [];
        _creatures = Creature.GetAllCreatures();

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
                            GroupKindId =
                                (DoodadFuncGroups.DoodadFuncGroupKind)reader.GetUInt32("doodad_func_group_kind_id"),
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
                            tempListGroups = [];
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
                            list = [];
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
                        var func = new DoodadFuncAttachment
                        {
                            Id = reader.GetUInt32("id"),
                            AttachPointId = (AttachPointKind)reader.GetByte("attach_point_id"),
                            Space = reader.GetInt32("space"),
                            BondKindId = (BondKind)reader.GetByte("bond_kind_id")
                        };
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
                        var func = new DoodadFuncBuyFishModel
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name")
                        };
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
                        var itemId = reader.GetUInt32("item_id", 0);
                        var functionId = reader.GetUInt32("id");
                        var allowedItemIds = _phaseFuncTemplates["DoodadFuncBuyFishItem"].Values
                            .OfType<DoodadFuncBuyFishItem>()
                            .Where(x => x.DoodadFuncBuyFishId == functionId)
                            .Select(x => x.ItemId)
                            .ToHashSet();
                        if (itemId != 0)
                            allowedItemIds.Add(itemId);

                        var func = new DoodadFuncBuyFish
                        {
                            Id = functionId,
                            AllowedItemIds = allowedItemIds
                        };
                        _funcTemplates["DoodadFuncBuyFish"].Add(func.Id, func);
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
                            ClimbTypeId = reader.GetUInt32("climb_type_id")
                            // 10.0.2.13: allow_horizontal_multi_hanger removed
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
                            Effects = []
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
                        var func = (DoodadFuncClout)_phaseFuncTemplates["DoodadFuncClout"][funcCloutId];
                        func.Effects.Add(reader.GetUInt32("effect_id"));
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
                        _funcTemplates[nameof(DoodadFuncCoffer)].Add(func.Id, func);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_private_coffers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncPrivateCoffer
                        {
                            Id = reader.GetUInt32("id"),
                            Capacity = reader.GetInt32("capacity"),
                            IsManikin = reader.GetBoolean("is_manikin")
                        };
                        _funcTemplates[nameof(DoodadFuncPrivateCoffer)].Add(func.Id, func);
                    }
                }
            }

            // Private coffer item restrictions are polymorphic content rows keyed by function id.
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT owner_id, item_category_id FROM coffer_item_categories WHERE owner_type = $ownerType";
                command.Parameters.AddWithValue("$ownerType", nameof(DoodadFuncPrivateCoffer));
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var ownerId = reader.GetUInt32("owner_id");
                        if (_funcTemplates[nameof(DoodadFuncPrivateCoffer)]
                            .TryGetValue(ownerId, out var funcTemplate) && funcTemplate is DoodadFuncPrivateCoffer coffer)
                        {
                            coffer.AllowedItemCategoryIds.Add(reader.GetInt32("item_category_id"));
                        }
                        else
                        {
                            Logger.Warn($"Ignoring item-category restriction for missing private coffer function {ownerId}");
                        }
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
                            ItemTriggerPhase = reader.GetInt32("item_trigger_phase", 0)
                        };
                        _funcTemplates["DoodadFuncConditionalUse"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_consume_changer_items
            // This is not actually a phase, but rather a collection of items that is available for doodad_func_consume_changers
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_consume_changer_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var entry = new DoodadFuncConsumeChangerItem
                        {
                            Id = reader.GetUInt32("id"),
                            DoodadFuncConsumeChangerId = reader.GetUInt32("doodad_func_consume_changer_id"),
                            ItemId = reader.GetUInt32("item_id")
                        };
                        _doodadFuncConsumeChangerItem.TryAdd(entry.Id, entry);
                    }
                }
            }

            // doodad_func_consume_changer_model_items
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_consume_changer_model_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConsumeChangerModelItem
                        {
                            Id = reader.GetUInt32("id"),
                            DoodadFuncConsumeChangerModelId = reader.GetUInt32("doodad_func_consume_changer_model_id"),
                            ItemId = reader.GetUInt32("item_id")
                        };
                        _phaseFuncTemplates["DoodadFuncConsumeChangerModelItem"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_consume_changer_models
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_consume_changer_models";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncConsumeChangerModel
                        {
                            Id = reader.GetUInt32("id"),
                            Name = reader.GetString("name")
                        };
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
                            // 10.0.2.13: slot_id/count removed; source item identified by tag_id
                            TagId = reader.GetUInt32("tag_id", 0)
                        };
                        _funcTemplates["DoodadFuncConsumeChanger"].Add(func.Id, func);
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
                            // loot_pack_id removed in 10.0.2.13 schema (replaced by convert_item_id); LootPackId is unused at runtime
                            LootPackId = 0
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
                            CrimeValue = reader.GetInt16("crime_value"),
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
                        _phaseFuncTemplates["DoodadFuncGrowth"].Add(func.Id, func);
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

            // doodad_func_item_changers — the sowable options on a farm plot, ordered within their phase.
            // DoodadItemChangeEffect selects between them by position, so preserve the DB order.
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_func_item_changers ORDER BY id";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var func = new DoodadFuncItemChanger
                        {
                            Id = reader.GetUInt32("id"),
                            NextPhase = reader.GetInt32("next_phase"),
                            ItemId = reader.GetUInt32("item_id"),
                            ItemCount = reader.GetInt32("item_count"),
                            SkillId = reader.GetUInt32("skill_id")
                        };
                        _phaseFuncTemplates["DoodadFuncItemChanger"].Add(func.Id, func);
                    }
                }
            }

            // doodad_func_logics
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
            using (var command = connection.CreateCommand())
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
                            ConsumeItemId = reader.GetUInt32("consume_item_id", 0),
                            ConsumeCount = reader.GetInt32("consume_count")
                        };
                        _funcTemplates["DoodadFuncStampMaker"].Add(func.Id, func);
                    }
                }
            }

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
                            Tod = reader.GetInt32("tod"),
                            NextPhase = reader.GetInt32("next_phase", -1),
                            TodEnd = reader.GetInt32("tod_end", -1),
                            IsRealtime = reader.GetBoolean("is_realtime", false)
                        };

                        var normalizedTod = func.Tod;
                        while (normalizedTod >= 2400)
                            normalizedTod /= 10;
                        if (normalizedTod % 100 >= 60)
                            Logger.Warn($"DoodadFuncToD has invalid value for minutes, Id {func.Id}, ToD {func.Tod}");
                        func.TodAsHours = DoodadFuncTod.ToHours(func.Tod);

                        _phaseFuncTemplates["DoodadFuncTod"].Add(func.Id, func);
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

                        var template = GetCofferTemplate(templateId) ?? new DoodadTemplate();

                        template.Id = templateId;
                        template.OnceOneMan = reader.GetBoolean("once_one_man", true);
                        template.OnceOneInteraction = reader.GetBoolean("once_one_interaction", true);
                        template.MgmtSpawn = reader.GetBoolean("mgmt_spawn", true);
                        template.Percent = reader.GetInt32("percent", 0);
                        template.MinTime = reader.GetInt32("min_time", 0);
                        template.MaxTime = reader.GetInt32("max_time", 0);
                        template.ModelKindId = reader.GetUInt32("model_kind_id");
                        template.Model = reader.GetString("model", "") ?? "";
                        template.LoadModelFromWorld = reader.GetBoolean("load_model_from_world", false);
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

            LoadZoneModelIdMap(connection);

            #endregion
        }

        CreateTemplateCaches();
        _loaded = true;
    }

    /// <summary>
    /// Zone WZCreateDoodad looks up modelId in the models registry; path comes from PrefabModel
    /// </summary>
    private void LoadZoneModelIdMap(SqliteConnection connection)
    {
        _zoneModelPathToId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        _zoneModelBasenameToId = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT m.id, pe.file_path, pe.state_id
                FROM prefab_elements pe
                JOIN models m ON m.sub_type = 'PrefabModel' AND m.sub_id = pe.prefab_model_id
                """;
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                var modelId = reader.GetUInt32("id");
                var path = (reader.GetString("file_path", "") ?? "").Trim();
                if (path.Length == 0)
                    continue;
                var stateId = reader.GetInt32("state_id", 0);
                _zoneModelPathToId.TryAdd(path, modelId);
                var basename = path;
                var slash = path.LastIndexOf('/');
                if (slash >= 0 && slash + 1 < path.Length)
                    basename = path[(slash + 1)..];
                // Prefer idle/normal state (1) for basename collisions.
                if (stateId == 1 || !_zoneModelBasenameToId.ContainsKey(basename))
                    _zoneModelBasenameToId[basename] = modelId;
            }
        }
        catch (Exception e)
        {
            Logger.Warn(e, "Failed loading prefab_elements→models map for Zone doodad modelId");
        }

        Logger.Info(
            "Zone doodad modelId map: {0} paths, {1} basenames",
            _zoneModelPathToId.Count,
            _zoneModelBasenameToId.Count);
    }

    /// <summary>
    /// Resolve a doodad model URI to models.id for WZCreateDoodad pisc[1].
    /// Exact path first, then basename (e.g. vegetation pinetree_a02.cgf → pine_tree PrefabModel).
    /// </summary>
    public uint ResolveZoneModelId(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || _zoneModelPathToId == null)
            return 0;

        var path = modelPath.Trim();
        foreach (var candidate in ZoneModelPathVariants(path))
        {
            if (_zoneModelPathToId.TryGetValue(candidate, out var id))
                return id;
        }

        var basename = path;
        var slash = path.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < path.Length)
            basename = path[(slash + 1)..];
        if (_zoneModelBasenameToId != null &&
            _zoneModelBasenameToId.TryGetValue(basename, out var byBase))
            return byBase;

        return 0;
    }

    private static IEnumerable<string> ZoneModelPathVariants(string path)
    {
        yield return path;
        // Strip / add game/ segment — DB paths are inconsistent across doodad vs prefab_elements.
        if (path.Contains("://game/", StringComparison.OrdinalIgnoreCase))
            yield return path.Replace("://game/", "://", StringComparison.OrdinalIgnoreCase);
        else if (path.Contains("://objects/", StringComparison.OrdinalIgnoreCase))
            yield return path.Replace("://objects/", "://game/objects/", StringComparison.OrdinalIgnoreCase);
        else if (path.Contains("://Objects/", StringComparison.Ordinal))
            yield return path.Replace("://Objects/", "://Game/objects/", StringComparison.Ordinal);
        if (path.StartsWith("vegetation://", StringComparison.OrdinalIgnoreCase))
        {
            var rest = path["vegetation://".Length..];
            yield return "cgf://" + rest;
            yield return "cgf://game/" + rest;
        }
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
    /// Builds the coffer specialization attached to a doodad template, if any.
    /// </summary>
    /// <param name="templateId"></param>
    private DoodadCofferTemplate GetCofferTemplate(uint templateId)
    {
        if (templateId == 0)
        {
            return null;
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
                if (!_funcTemplates.TryGetValue(func.FuncType, out var funcTemplates))
                {
                    continue;
                }

                if (!funcTemplates.TryGetValue(func.FuncId, out var funcTemplate))
                {
                    continue;
                }

                if (funcTemplate is DoodadFuncPrivateCoffer privateCoffer)
                {
                    return new DoodadCofferTemplate
                    {
                        Capacity = privateCoffer.Capacity,
                        IsPrivate = true,
                        IsManikin = privateCoffer.IsManikin,
                        AllowedItemCategoryIds = [.. privateCoffer.AllowedItemCategoryIds]
                    };
                }

                if (funcTemplate is DoodadFuncCoffer coffer)
                {
                    return new DoodadCofferTemplate { Capacity = coffer.Capacity };
                }
            }
        }

        return null;
    }

    public Doodad Create(WorldInstance parentWorld, uint bcId, uint templateId, GameObject ownerObject = null, bool skipPhaseInitialization = false)
    {
        if (!_templates.TryGetValue(templateId, out var template))
        {
            return null;
        }

        Doodad doodad = null;

        // Check if template is a Coffer
        if (template is DoodadCofferTemplate doodadCofferTemplate)
        {
            doodad = new DoodadCoffer
            {
                Capacity = doodadCofferTemplate.Capacity,
                IsPrivate = doodadCofferTemplate.IsPrivate,
                IsManikin = doodadCofferTemplate.IsManikin,
                AllowedItemCategoryIds = [.. doodadCofferTemplate.AllowedItemCategoryIds]
            };
        }

        doodad ??= new Doodad();
        if (parentWorld == null)
        {
            Logger.Fatal($"Tried to create a doodad without a world");
            return null;
        }
        doodad.ParentWorld = parentWorld;

        doodad.ObjId = bcId > 0 ? bcId : objectIdManager.GetNextId();
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

        RefreshFaction(doodad, ownerObject as BaseUnit, ownerObject as House);

        if (!skipPhaseInitialization)
        {
            Task.Run(() => doodad.InitDoodad());
        }

        //Logger.Debug($"Create: TemplateId {doodad.TemplateId}, ObjId {doodad.ObjId}, FuncGroupId {doodad.FuncGroupId}");

        return doodad;
    }

    /// <summary>
    /// Applies the server's fail-closed doodad faction policy. A non-zero static template faction
    /// is used unless the template explicitly uses its creator. Faction-less non-creator housing
    /// children inherit their owning House; unrelated faction-less world props remain unresolved.
    /// </summary>
    public void RefreshFaction(Doodad doodad, BaseUnit creator = null, House owningHouse = null,
        FactionsEnum creatorFactionId = FactionsEnum.Invalid)
    {
        if (doodad?.Template == null)
            return;

        var template = doodad.Template;
        if (!template.UseCreatorFaction && template.FactionId != FactionsEnum.Invalid)
        {
            doodad.Faction = factionManager.GetFaction(template.FactionId);
            return;
        }

        if (!template.UseCreatorFaction)
        {
            owningHouse ??= doodad.ParentObj as House;
            if (owningHouse == null && doodad.OwnerDbId > 0)
                owningHouse = housingManager.Value.GetHouseById(doodad.OwnerDbId);
            doodad.Faction = owningHouse?.Faction;
            return;
        }

        creator ??= doodad.OwnerObjId > 0
            ? doodad.ParentWorld?.GetBaseUnit(doodad.OwnerObjId)
            : null;
        if (creator == null && doodad.OwnerId > 0 &&
            doodad.OwnerType is DoodadOwnerType.Character or DoodadOwnerType.Housing)
            creator = WorldManager.Instance.GetCharacterById(doodad.OwnerId);
        doodad.Faction = creator?.Faction;
        if (doodad.Faction == null && creatorFactionId != FactionsEnum.Invalid)
            doodad.Faction = factionManager.GetFaction(creatorFactionId);
    }

    /// <summary>
    /// Returns the current effective faction under the fail-closed server policy. Live creator and
    /// House relations are resolved dynamically so ownership changes do not leave stale authority.
    /// </summary>
    public SystemFaction GetEffectiveFaction(Doodad doodad)
    {
        if (doodad?.Template == null)
            return null;

        var template = doodad.Template;
        if (!template.UseCreatorFaction && template.FactionId != FactionsEnum.Invalid)
            return factionManager.GetFaction(template.FactionId);

        if (!template.UseCreatorFaction)
        {
            var owningHouse = doodad.ParentObj as House;
            if (owningHouse == null && doodad.OwnerDbId > 0)
                owningHouse = housingManager.Value.GetHouseById(doodad.OwnerDbId);
            return owningHouse?.Faction;
        }

        if (doodad.OwnerObjId > 0)
        {
            var creator = doodad.ParentWorld?.GetBaseUnit(doodad.OwnerObjId);
            if (creator != null)
                return creator.Faction;
        }
        if (doodad.OwnerId > 0 &&
            doodad.OwnerType is DoodadOwnerType.Character or DoodadOwnerType.Housing)
        {
            var creator = WorldManager.Instance.GetCharacterById(doodad.OwnerId);
            if (creator != null)
                return creator.Faction;
        }

        return doodad.Faction;
    }

    /// <summary>
    /// Merchant pack a doodad sells from, 0 when it is not a shop. Reads the DoodadFuncStoreUi on the
    /// doodad's current phase, which is where doodad_func_store_uis hangs its merchant_pack_id.
    /// </summary>
    public uint GetStoreMerchantPackId(Models.Game.DoodadObj.Doodad doodad)
    {
        if (doodad?.CurrentFuncs == null)
            return 0;

        foreach (var func in doodad.CurrentFuncs)
        {
            if (func.FuncType != nameof(Models.Game.DoodadObj.Funcs.DoodadFuncStoreUi))
                continue;

            if (GetFuncTemplate(func.FuncId, func.FuncType) is Models.Game.DoodadObj.Funcs.DoodadFuncStoreUi storeUi)
                return storeUi.MerchantPackId;
        }

        return 0;
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
        return _funcsByGroups.TryGetValue(funcGroupId, out var group) ? group : [];
    }

    public List<DoodadPhaseFunc> GetPhaseFunc(uint funcGroupId)
    {
        return _phaseFuncs.TryGetValue(funcGroupId, out var func) ? func : [];
    }

    public DoodadFuncTemplate GetFuncTemplate(uint funcId, string funcType)
    {
        if (!_funcTemplates.TryGetValue(funcType, out var funcs))
        {
            return null;
        }

        return funcs.GetValueOrDefault(funcId);
    }

    public bool OffersQuest(uint doodadTemplateId, uint questId)
    {
        foreach (var group in GetDoodadFuncGroups(doodadTemplateId))
        {
            foreach (var func in GetFuncsForGroup(group.Id))
            {
                if (GetFuncTemplate(func.FuncId, func.FuncType) is DoodadFuncQuest questFunc &&
                    questFunc.QuestId == questId)
                    return true;
            }
        }

        return false;
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
        return _funcsByGroups.TryGetValue(doodadFuncGroupId, out var funcs) ? funcs : [];
    }

    /// <summary>
    /// GetDoodadPhaseFuncs - Get all phase functions
    /// </summary>
    /// <param name="funcGroupId"></param>
    /// <returns>DoodadFunc[]</returns>
    public List<DoodadPhaseFunc> GetDoodadPhaseFuncs(uint funcGroupId)
    {
        return _phaseFuncs.TryGetValue(funcGroupId, out var funcs) ? funcs : [];
    }

    /// <summary>
    /// Saves and creates a doodad
    /// </summary>
    public Doodad CreatePlayerDoodad(Character character, uint id, float x, float y, float z, float zRot, float scale, ulong itemId, FarmType farmType = FarmType.Invalid, uint itemTemplateId = 0, int customData = 0, bool ignoreHouses = false)
    {
        Logger.Warn($"{character.Name} is placing a doodad {id} at position {x} {y} {z}");

        // NOTE: If you would ever want to use player housing outside of main_world, you'll need to modify this
        var targetHouse = !ignoreHouses ? housingManager.Value.GetHouseAtLocation(x, y) : null;

        // Create doodad
        var doodad = Instance.Create(character.ParentWorld, 0, id, character, true);
        doodad.IsPersistent = true;
        doodad.Transform = character.Transform.CloneDetached(doodad);
        doodad.Transform.InstanceId = character.ParentWorld.Id;
        doodad.Transform.Local.SetPosition(x, y, z);
        doodad.Transform.Local.SetRotation(0, 0, zRot);
        // doodad.Transform.WorldId = world.Template.Id;
        doodad.ItemId = itemId;
        doodad.PlantTime = DateTime.UtcNow;
        doodad.FarmType = farmType;
        doodad.ItemTemplateId = itemTemplateId;
        doodad.Data = customData;
        if (targetHouse != null)
        {
            doodad.OwnerDbId = targetHouse.Id;
            doodad.AttachPoint = AttachPointKind.None;
            doodad.OwnerType = DoodadOwnerType.Housing;
            doodad.ParentObj = targetHouse;
            doodad.ParentObjId = targetHouse.ObjId;
            doodad.Transform.Parent = targetHouse.Transform;
            RefreshFaction(doodad, character, targetHouse);
        }
        else
        {
            doodad.OwnerDbId = 0;
        }

        if (scale > 0f)
        {
            doodad.SetScale(scale);
        }

        var items = itemManager.GetItemIdsFromDoodad(id);
        var preferredItem = itemId > 0 ? character.Inventory.Bag.GetItemByItemId(itemId) : null;
        if (itemId > 0)
        {
            // Consume item

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
        }

        if (doodad is DoodadCoffer coffer)
        {
            coffer.InitializeCoffer(character.Id);
        }

        foreach (var item in items)
        {
            character.ItemUse(preferredItem);
            character.Inventory.ConsumeItem([SlotType.Inventory], ItemTaskType.DoodadCreate, item, 1,
                preferredItem);
        }

        doodad.InitDoodad();
        doodad.Spawn();
        doodad.Save();
        character.ParentWorld.SpawnManager.AddPlayerDoodad(doodad);

        return doodad;
    }

    public bool OpenCofferDoodad(Character character, uint objId)
    {
        var doodad = character?.ParentWorld?.GetDoodad(objId);
        if (doodad is not DoodadCoffer coffer)
        {
            susManager.LogActivity(SusManager.CategoryCheating, character, $"{character.Name} tried to open doodad {objId} as a Coffer");
            return false;
        }

        if (!coffer.IsVisible ||
            !WorldManager.GetAround<Doodad>(character).Any(candidate => candidate.ObjId == objId) ||
            !coffer.AllowedToInteract(character))
        {
            susManager.LogActivity(SusManager.CategoryCheating, character,
                $"{character.Name} tried to open inaccessible coffer doodad {objId}");
            return false;
        }

        // Somebody already using this ?
        if (coffer.OpenedBy != null)
        {
            return false;
        }

        coffer.OpenedBy = character;
        coffer.OpenedItemBagId = 0;

        var firstSlot = 0;
        while (firstSlot < coffer.Capacity)
        {
            character.SendPacket(new SCCofferContentsUpdatePacket(coffer, checked((byte)firstSlot)));
            firstSlot += SCCofferContentsUpdatePacket.MaxSlotsToSend;
        }

        return true;
    }

    public bool CloseCofferDoodad(Character character, uint objId)
    {
        var doodad = character.ParentWorld.GetDoodad(objId);
        if (doodad is not DoodadCoffer coffer)
        {
            susManager.LogActivity(SusManager.CategoryCheating, character, $"{character.Name} tried to close doodad {objId} as a Coffer");
            return false;
        }

        if (coffer.OpenedBy is not null && coffer.OpenedBy.Id != character.Id)
        {
            return false;
        }

        coffer.OpenedItemBagId = 0;
        coffer.OpenedBy = null;

        return true;
    }

    public bool SetCofferSubbagOpen(Character character, ulong itemId, bool opening)
    {
        if (character == null || itemId == 0)
            return false;

        var coffer = WorldManager.GetAround<Doodad>(character)
            .OfType<DoodadCoffer>()
            .FirstOrDefault(candidate => candidate.OpenedBy?.Id == character.Id);
        if (coffer == null || coffer.ParentWorld != character.ParentWorld || !coffer.IsVisible ||
            !coffer.AllowedToInteract(character))
        {
            susManager.LogActivity(SusManager.CategoryCheating, character,
                $"{character.Name} tried to change subbag {itemId} without an accessible open coffer");
            return false;
        }

        if (!opening)
        {
            if (coffer.OpenedItemBagId != itemId)
                return false;

            coffer.OpenedItemBagId = 0;
            return true;
        }

        if (coffer.ItemContainer.GetItemByItemId(itemId) is not ItemBag itemBag ||
            itemBag.Template is not ItemBagTemplate itemBagTemplate)
        {
            susManager.LogActivity(SusManager.CategoryCheating, character,
                $"{character.Name} tried to open non-bag coffer item {itemId} as a subbag");
            return false;
        }

        if (!UnitRequirementsGameData.Instance.CanUseItemBag(itemBagTemplate, character))
            return false;

        var itemContainer = ItemManager.Instance.GetOrCreateItemBagContainer(itemBag);
        coffer.OpenedItemBagId = itemBag.Id;

        var firstSlot = 0;
        while (firstSlot < itemContainer.ContainerSize)
        {
            character.SendPacket(new SCCofferContentsUpdatePacket(coffer, itemBag, itemContainer,
                checked((byte)firstSlot)));
            firstSlot += SCCofferContentsUpdatePacket.MaxSlotsToSend;
        }

        return true;
    }

    public static bool ChangeDoodadData(Character player, Doodad doodad, int data)
    {
        // TODO: Can non-coffer doodads that use this packet only be changed by their owner ?
        if (doodad.OwnerId != player.Id)
        {
            return false;
        }

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
        doodadIdManager.ReleaseId(dbId); // Free up the Id

        // Handle attached items
        if (attachedItemId > 0)
        {
            var item = itemManager.GetItemByItemId(attachedItemId);
            if (item != null)
            {
                item._holdingContainer = null;
                itemManager.ReleaseId(item.Id);
            }
        }

        // Delete attached container
        if (attachedContainer > 0)
        {
            var container = itemManager.GetItemContainerByDbId(attachedContainer);
            if (container != null)
                itemManager.DeleteItemContainer(container);
        }
    }

    public List<uint> GetTreasureChestTemplateIds()
    {
        return _templates?.Values.Where(t => t.GroupId is >= 55 and <= 59).Select(t => t.Id).ToList();
    }
}
