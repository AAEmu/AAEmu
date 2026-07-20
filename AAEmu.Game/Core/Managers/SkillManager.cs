using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.DynamicEffects;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;
using Newtonsoft.Json.Linq;
using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Loads and caches skill, buff and effect templates from compact.sqlite3
/// and exposes lookup APIs used by the game server.
/// </summary>
public class SkillManager(IAnimationManager animationManager, IPlotManager plotManager) : Singleton<SkillManager>, ISkillManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;

    private Dictionary<uint, SkillTemplate> _skills;
    private Dictionary<uint, HeirSkillTemplate> _heirSkills = new();
    private Dictionary<uint, HeirSkillDetailTemplate> _heirSkillDetails = new();
    private Dictionary<uint, DefaultSkill> _defaultSkills;
    private List<uint> _commonSkills;
    private Dictionary<AbilityType, List<SkillTemplate>> _startAbilitySkills;
    private Dictionary<uint, PassiveBuffTemplate> _passiveBuffs;
    private Dictionary<uint, EffectType> _types;
    private Dictionary<string, Dictionary<uint, EffectTemplate>> _effects;
    private Dictionary<uint, BuffTemplate> _buffs;
    private Dictionary<uint, List<uint>> _buffTags;
    private Dictionary<uint, List<uint>> _taggedBuffs;
    private Dictionary<uint, List<uint>> _skillTags;
    private Dictionary<uint, List<uint>> _taggedSkills;
    private Dictionary<uint, List<SkillModifier>> _skillModifiers;
    private Dictionary<uint, List<BuffTriggerTemplate>> _buffTriggers;
    private Dictionary<uint, List<CombatBuffTemplate>> _combatBuffs;
    private Dictionary<uint, LinearFuncTemplate> _linearFuncs;
    private Dictionary<uint, SkillReagent> _skillReagents;
    private Dictionary<uint, SkillProduct> _skillProducts;
    private DynamicEffect _dynamicEffects;
    // private HashSet<ushort> _skillIds = new();
    // private ushort _skillIdIndex = 1;

    //Events
    public event EventHandler OnSkillsLoaded;

    /*
    // Replaced with SkillTlIdManager 
    public ushort NextId()
    {
        lock (_skillIds)
        {
            var id = _skillIdIndex;
            while (_skillIds.Contains(id))
            {
                if (id == ushort.MaxValue)
                    id = 1;
                else
                    id++;
            }
            _skillIds.Add(id);
            _skillIdIndex = (ushort)(id + 1u);
            if (_skillIdIndex == 0)
                _skillIdIndex = 1;
            return id;
        }
    }

    public void ReleaseId(ushort id)
    {
        lock (_skillIds)
        {
            _skillIds.Remove(id);
        }
    }
    */

    public SkillTemplate GetSkillTemplate(uint id)
    {
        return _skills.GetValueOrDefault(id);
    }

    public bool IsDefaultSkill(uint id)
    {
        return _defaultSkills.ContainsKey(id);
    }

    public bool IsCommonSkill(uint id)
    {
        return _commonSkills.Contains(id);
    }

    public List<SkillTemplate> GetStartAbilitySkills(AbilityType ability)
    {
        if (_startAbilitySkills != null && _startAbilitySkills.TryGetValue(ability, out var skills))
            return skills;

        return [];
    }

    public List<DefaultSkill> GetDefaultSkills()
    {
        return [.. _defaultSkills.Values];
    }

    public BuffTemplate GetBuffTemplate(uint id)
    {
        return _buffs.GetValueOrDefault(id);
    }

    public LinearFuncTemplate GetLinearFunc(uint funcId)
    {
        return _linearFuncs.GetValueOrDefault(funcId);
    }

    public List<BuffTriggerTemplate> GetBuffTriggerTemplates(uint buffId)
    {
        if (_buffTriggers.TryGetValue(buffId, out var triggers))
        {
            return triggers;
        }
        return [];
    }

    public EffectTemplate GetEffectTemplate(uint id)
    {
        if (_types == null || _effects == null)
            return null;

        if (_types.TryGetValue(id, out var type))
        {
            //Logger.Trace($"Get Effect Template: type = {type.Type}, id = {type.ActualId}");

            if (_effects.TryGetValue(type.Type, out var effect))
            {
                if (effect.TryGetValue(type.ActualId, out var res))
                {
                    return res;
                }
            }
            else
            {
                //Logger.Trace($"No such Effect type = {type.Type}, id = {id}");
                return null;
            }
        }
        //Logger.Trace($"No such Effect id = {id}");
        return null;
    }

    public EffectTemplate GetEffectTemplate(uint id, string type)
    {
        Logger.Trace($"Get Effect Template: type = {type}, id = {id}");

        if (_effects.TryGetValue(type, out var value))
        {
            if (value.TryGetValue(id, out var res))
            {
                return res;
            }
        }
        return null;
    }

    public List<uint> GetBuffTags(uint buffId)
    {
        return _buffTags.TryGetValue(buffId, out var tags) ? tags : [];
    }

    public List<uint> GetBuffsByTagId(uint tagId)
    {
        return _taggedBuffs.GetValueOrDefault(tagId);
    }

    public List<uint> GetSkillTags(uint skillId)
    {
        return _skillTags.TryGetValue(skillId, out var tags) ? tags : [];
    }

    public List<uint> GetSkillsByTag(uint tagId)
    {
        return _taggedSkills.TryGetValue(tagId, out var tag) ? tag : [];
    }

    public PassiveBuffTemplate GetPassiveBuffTemplate(uint id)
    {
        return _passiveBuffs.GetValueOrDefault(id);
    }

    public List<SkillModifier> GetModifiersByOwnerId(uint id)
    {
        if (_skillModifiers.TryGetValue(id, out var ownerId))
            return ownerId;
        return [];
    }

    public List<CombatBuffTemplate> GetCombatBuffs(uint reqBuffId)
    {
        return _combatBuffs.TryGetValue(reqBuffId, out var buffs) ? buffs : [];
    }

    public List<SkillReagent> GetSkillReagentsBySkillId(uint id)
    {
        List<SkillReagent> reagents = [];

        foreach (var reagent in _skillReagents)
        {
            if (reagent.Value.SkillId == id)
                reagents.Add(reagent.Value);
        }

        return reagents;
    }

    public List<SkillProduct> GetSkillProductsBySkillId(uint id)
    {
        List<SkillProduct> products = [];

        foreach (var product in _skillProducts)
        {
            if (product.Value.SkillId == id)
                products.Add(product.Value);
        }

        return products;
    }

    /// <summary>
    /// Returns a skill to be used for npcs with np_skills.
    /// </summary>
    /// <param name="npcSkill"></param>
    /// <returns></returns>
    public Skill GetNpSkillTemplate(NpcSkill npcSkill)
    {
        var skillTemplate = GetSkillTemplate(npcSkill.SkillId);

        // Temporary condition to filter for dungeon OnCombat skills.
        if (npcSkill.SkillUseCondition == SkillUseConditionKind.InCombat)
        {
            if (skillTemplate.IgnoreGlobalCooldown == false || skillTemplate.Plot is not null)
            {
                return null;
            }
        }

        return skillTemplate != null ? new Skill(skillTemplate) : null;
    }

    public void Load()
    {
        if (_loaded)
            return;

        _skills = [];
        _defaultSkills = [];
        _commonSkills = [];
        _startAbilitySkills = [];
        _passiveBuffs = [];
        _types = [];
        _dynamicEffects = new DynamicEffect();
        _effects = new Dictionary<string, Dictionary<uint, EffectTemplate>>
        {
            { "Buff", [] }, // missing from the effect table
            { "AcceptQuestEffect", [] },
            { "AccountAttributeEffect", [] },
            { "AggroEffect", [] },
            { "BubbleEffect", [] },
            { "BuffEffect", [] },
            { "CinemaEffect", [] },
            { "CleanupUccEffect", [] },
            { "ConversionEffect", [] },
            { "CraftEffect", [] },
            { "DamageEffect", [] },
            { "DispelEffect", [] },
            { "DoodadItemChangeEffect", [] },
            { "FlyingStateChangeEffect", [] },
            { "GainLootPackItemEffect", [] },
            { "HealEffect", [] },
            { "HighAbilityResourceEffect", [] },
            { "ImprintUccEffect", [] },
            { "ImpulseEffect", [] },
            { "InteractionEffect", [] },
            { "KillNpcWithoutCorpseEffect", [] },
            { "LevelUpEffect", [] },
            { "ManaBurnEffect", [] },
            { "MoveToLocationEffect", [] },
            { "MoveToRezPointEffect", [] },
            { "NpcControlEffect", [] },
            { "NpcSpawnerDespawnEffect", [] },
            { "NpcSpawnerSpawnEffect", [] },
            { "OpenPortalEffect", [] },
            { "PlayLogEffect", [] },
            { "PhysicalExplosionEffect", [] },
            { "PutDownBackpackEffect", [] },
            { "RecoverExpEffect", [] },
            { "RepairSlaveEffect", [] },
            { "ReportCrimeEffect", [] },
            { "RestoreManaEffect", [] },
            { "ScopedFEffect", [] },
            { "SpawnEffect", [] },
            { "SpawnGimmickEffect", [] },
            { "SpecialEffect", [] },
            { "TrainCraftEffect", [] },
            { "TrainCraftRankEffect", [] }, // missing from the effect table
            { "WorldMessageEffect", [] },
            { "SkillController", [] }, // missing from the effect table
            { "SpawnFishEffect", [] }, // missing from the effect table
            { "ResetAoeDiminishingEffect", [] } // missing from the effect table
        };

        _buffs = [];

        _buffTags = [];
        _taggedBuffs = [];
        _skillModifiers = [];
        _skillTags = [];
        _taggedSkills = [];
        _combatBuffs = [];
        _linearFuncs = [];
        _skillReagents = [];
        _skillProducts = [];

        using (var connection2 = SQLite.CreateConnection("Data", "compact.server.table.sqlite3"))
        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading skills...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skills";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new SkillTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.Name = reader.GetString("name");
                        template.Desc = reader.GetString("desc");
                        template.Cost = reader.GetInt32("cost");
                        template.Show = reader.GetBoolean("show", true);
                        template.FireAnim = animationManager.GetAnimation(reader.GetUInt32("fire_anim_id", 0));
                        template.AbilityId = (AbilityType)reader.GetByte("ability_id");
                        template.ManaCost = reader.GetInt32("mana_cost");
                        template.TimingId = reader.GetInt32("timing_id");
                        template.CooldownTime = reader.GetInt32("cooldown_time");
                        template.CastingTime = reader.GetInt32("casting_time");
                        template.IgnoreGlobalCooldown = reader.GetBoolean("ignore_global_cooldown", true);
                        template.EffectDelay = reader.GetInt32("effect_delay");
                        template.EffectSpeed = reader.GetFloat("effect_speed");
                        template.EffectRepeatCount = reader.GetInt32("effect_repeat_count");
                        template.EffectRepeatTick = reader.GetInt32("effect_repeat_tick");
                        template.ActiveWeaponId = reader.GetInt32("active_weapon_id");
                        template.TargetType = (SkillTargetType)reader.GetInt32("target_type_id");
                        template.TargetSelection = (SkillTargetSelection)reader.GetInt32("target_selection_id");
                        template.TargetRelation = (SkillTargetRelation)reader.GetInt32("target_relation_id");
                        template.TargetAreaCount = reader.GetInt32("target_area_count");
                        template.TargetAreaRadius = reader.GetInt32("target_area_radius");
                        template.TargetSiege = reader.GetBoolean("target_siege", true);
                        template.WeaponSlotForAngleId = reader.GetInt32("weapon_slot_for_angle_id");
                        template.TargetAngle = reader.GetInt32("target_angle");
                        template.WeaponSlotForRangeId = reader.GetInt32("weapon_slot_for_range_id");
                        template.WeaponSlotForAutoAttackId = reader.GetInt32("weapon_slot_for_autoattack_id");
                        template.MinRange = reader.GetInt32("min_range");
                        template.MaxRange = reader.GetInt32("max_range");
                        template.KeepStealth = reader.GetBoolean("keep_stealth", true);
                        template.Aggro = reader.GetInt32("aggro");
                        template.ChannelingTime = reader.GetInt32("channeling_time");
                        template.ChannelingTick = reader.GetInt32("channeling_tick");
                        template.ChannelingMana = reader.GetInt32("channeling_mana");
                        template.ChannelingTargetBuffId = reader.GetUInt32("channeling_target_buff_id", 0);
                        template.TargetAreaAngle = reader.GetInt32("target_area_angle");
                        template.AbilityLevel = reader.GetInt32("ability_level");
                        template.ChannelingDoodadId = reader.GetUInt32("channeling_doodad_id", 0);
                        var value = reader.GetString("cooldown_tag_id", "0");
                        template.CooldownTagId = value.Contains("null") ? 0 : int.Parse(value);
                        value = reader.GetString("skill_controller_id", "0");
                        template.SkillControllerId = value.Contains("null") ? 0 : uint.Parse(value);
                        template.RepeatCount = reader.GetInt32("repeat_count");
                        template.RepeatTick = reader.GetInt32("repeat_tick");
                        template.ToggleBuffId = reader.GetUInt32("toggle_buff_id", 0);
                        template.TargetDead = reader.GetBoolean("target_dead", true);
                        template.ChannelingBuffId = reader.GetUInt32("channeling_buff_id", 0);
                        template.ReagentCorpseStatusId = reader.GetInt32("reagent_corpse_status_id");
                        template.SourceDead = reader.GetBoolean("source_dead", true);
                        template.LevelStep = reader.GetInt32("level_step");
                        template.ValidHeight = reader.GetFloat("valid_height");
                        template.TargetValidHeight = reader.GetFloat("target_valid_height");
                        template.SourceMount = reader.GetBoolean("source_mount", true);
                        template.StopCastingOnBigHit = reader.GetBoolean("stop_casting_on_big_hit", true);
                        template.StopChannelingOnBigHit = reader.GetBoolean("stop_channeling_on_big_hit", true);
                        template.AutoLearn = reader.GetBoolean("auto_learn", true);
                        template.MainhandToolId = reader.GetUInt32("mainhand_tool_id", 0);
                        template.OffhandToolId = reader.GetUInt32("offhand_tool_id", 0);
                        template.FrontAngle = reader.GetInt32("front_angle");
                        template.ManaLevelMd = reader.GetFloat("mana_level_md");
                        template.Unmount = reader.GetBoolean("unmount", true);
                        template.DamageTypeId = reader.GetUInt32("damage_type_id", 0);
                        template.AllowToPrisoner = reader.GetBoolean("allow_to_prisoner", true);
                        template.MatchAnimation = reader.GetBoolean("match_animation", true);
                        template.Plot = reader.IsDBNull("plot_id") ? null : plotManager.GetPlot(reader.GetUInt32("plot_id"));
                        template.UseAnimTime = reader.GetBoolean("use_anim_time", true);
                        template.ConsumeLaborPower = reader.GetInt32("consume_lp", 0);
                        template.SourceStun = reader.GetBoolean("source_stun", true);
                        template.TargetAlive = reader.GetBoolean("target_alive", true);
                        template.TargetWater = reader.GetBoolean("target_water", true);
                        template.CastingInc = reader.GetInt32("casting_inc");
                        template.CastingCancelable = reader.GetBoolean("casting_cancelable", true);
                        template.CastingDelayable = reader.GetBoolean("casting_delayable", true);
                        template.ChannelingCancelable = reader.GetBoolean("channeling_cancelable", true);
                        template.TargetOffsetAngle = reader.GetFloat("target_offset_angle");
                        template.TargetOffsetDistance = reader.GetFloat("target_offset_distance");
                        template.ActabilityGroupId = reader.GetInt32("actability_group_id", 0);
                        template.PlotOnly = reader.GetBoolean("plot_only", true);
                        template.SkillControllerAtEnd = reader.GetBoolean("skill_controller_at_end", true);
                        template.EndSkillController = reader.GetBoolean("end_skill_controller", true);
                        template.OrUnitReqs = reader.GetBoolean("or_unit_reqs", true);
                        template.DefaultGcd = reader.GetBoolean("default_gcd", true);
                        template.KeepManaRegen = reader.GetBoolean("keep_mana_regen", true);
                        template.CrimePoint = reader.GetInt32("crime_point");
                        template.LevelRuleNoConsideration = reader.GetBoolean("level_rule_no_consideration", true);
                        template.UseWeaponCooldownTime = reader.GetBoolean("use_weapon_cooldown_time", true);
                        template.CombatDiceId = reader.GetInt32("combat_dice_id");
                        template.CustomGcd = reader.GetInt32("custom_gcd");
                        template.CancelOngoingBuffs = reader.GetBoolean("cancel_ongoing_buffs", true);
                        template.CancelOngoingBuffExceptionTagId = reader.GetUInt32("cancel_ongoing_buff_exception_tag_id", 0);
                        template.SourceCannotUseWhileWalk = reader.GetBoolean("source_cannot_use_while_walk", true);
                        template.SourceMountMate = reader.GetBoolean("source_mount_mate", true);
                        template.CheckTerrain = reader.GetBoolean("check_terrain", true);
                        template.TargetOnlyWater = reader.GetBoolean("target_only_water", true);
                        template.SourceNotSwim = reader.GetBoolean("source_not_swim", true);
                        template.TargetPreoccupied = reader.GetBoolean("target_preoccupied", true);
                        template.StopChannelingOnStartSkill = reader.GetBoolean("stop_channeling_on_start_skill", true);
                        template.StopCastingByTurn = reader.GetBoolean("stop_casting_by_turn", true);
                        template.TargetMyNpc = reader.GetBoolean("target_my_npc", true);
                        template.GainLifePoint = reader.GetInt32("gain_life_point");
                        template.TargetFishing = reader.GetBoolean("target_fishing", true);
                        template.SourceNoSlave = reader.GetBoolean("source_no_slave", true);
                        template.AutoReUse = reader.GetBoolean("auto_reuse", true);
                        template.AutoReUseDelay = reader.GetInt32("auto_reuse_delay", 0);
                        template.SourceNotCollided = reader.GetBoolean("source_not_collided", true);
                        template.SkillPoints = reader.GetInt32("skill_points");
                        template.DoodadHitFamily = reader.GetInt32("doodad_hit_family");
                        template.FirstReagentOnly = reader.GetBoolean("first_reagent_only", true);
                        template.AccountCooldown = reader.GetBoolean("account_cooldown", true);
                        template.AutoFire = reader.GetBoolean("auto_fire", true);
                        template.CalcUserLevel = reader.GetBoolean("calc_user_level", true);
                        template.CameraAcceleration = reader.GetFloat("camera_acceleration");
                        template.CameraDuration = reader.GetFloat("camera_duration");
                        template.CameraHoldZ = reader.GetBoolean("camera_hold_z", true);
                        template.CameraMaxDistance = reader.GetFloat("camera_max_distance");
                        template.CameraSlowDownDistance = reader.GetFloat("camera_slow_down_distance");
                        template.CameraSpeed = reader.GetFloat("camera_speed");
                        template.CanActiveWeaponWithoutAnim = reader.GetBoolean("can_active_weapon_without_anim", true);
                        template.CastingUseable = reader.GetBoolean("casting_useable", true);
                        template.CategoryId = reader.GetInt32("category_id");
                        template.ChannelingAnimId = reader.GetUInt32("channeling_anim_id", 0);
                        template.CharRaceId = reader.GetInt32("char_race_id");
                        template.CheckObstacle = reader.GetBoolean("check_obstacle", true);
                        template.ControllerCamera = reader.GetBoolean("controller_camera", true);
                        template.ControllerCameraSpeed = reader.GetInt32("controller_camera_speed");
                        template.DoodadBundleId = reader.GetUInt32("doodad_bundle_id", 0);
                        template.DualWieldFireAnimId = reader.GetUInt32("dual_wield_fire_anim_id", 0);
                        template.FxGroupId = reader.GetUInt32("fx_group_id", 0);
                        template.HighAbilityId = reader.GetInt32("high_ability_id");
                        template.IconId = reader.GetUInt32("icon_id", 0);
                        template.LinkBackpackTypeId = reader.GetInt32("link_backpack_type_id");
                        template.LinkEquipSlotId = reader.GetInt32("link_equip_slot_id");
                        template.MatchAnimationCount = reader.GetBoolean("match_animation_count", true);
                        template.MaxHighAbilityResource = reader.GetInt32("max_high_ability_resource");
                        template.MinHighAbilityResource = reader.GetInt32("min_high_ability_resource");
                        template.PercussionInstrumentFireAnimId = reader.GetUInt32("percussion_instrument_fire_anim_id", 0);
                        template.PercussionInstrumentStartAnimId = reader.GetUInt32("percussion_instrument_start_anim_id", 0);
                        template.PitchAngle = reader.GetFloat("pitch_angle");
                        template.ProjectileId = reader.GetUInt32("projectile_id", 0);
                        template.SecondCooldownTagId = reader.GetInt32("second_cooldown_tag_id");
                        template.SensitiveOperation = reader.GetBoolean("sensitive_operation", true);
                        template.ShowTargetCastingTime = reader.GetBoolean("show_target_casting_time", true);
                        template.SkipQuestApplyUseItem = reader.GetBoolean("skip_quest_apply_use_item", true);
                        template.SkipValidateSource = reader.GetBoolean("skip_validate_source", true);
                        template.SourceAlive = reader.GetBoolean("source_alive", true);
                        template.SourceShouldSwim = reader.GetBoolean("source_should_swim", true);
                        template.StartAnimId = reader.GetUInt32("start_anim_id", 0);
                        template.StartAutoattack = reader.GetBoolean("start_autoattack", true);
                        template.StopAutoattack = reader.GetBoolean("stop_autoattack", true);
                        template.StringInstrumentFireAnimId = reader.GetUInt32("string_instrument_fire_anim_id", 0);
                        template.StringInstrumentStartAnimId = reader.GetUInt32("string_instrument_start_anim_id", 0);
                        template.SwitchToSkillCooldown = reader.GetBoolean("switch_to_skill_cooldown", true);
                        template.SynergyIcon1BuffKind = reader.GetBoolean("synergy_icon1_buffkind", true);
                        template.SynergyIcon1Id = reader.GetUInt32("synergy_icon1_id", 0);
                        template.SynergyIcon2BuffKind = reader.GetBoolean("synergy_icon2_buffkind", true);
                        template.SynergyIcon2Id = reader.GetUInt32("synergy_icon2_id", 0);
                        template.TargetDecalRadius = reader.GetInt32("target_decal_radius");
                        template.ThirdCooldownTagId = reader.GetInt32("third_cooldown_tag_id");
                        template.TubeInstrumentFireAnimId = reader.GetUInt32("tube_instrument_fire_anim_id", 0);
                        template.TubeInstrumentStartAnimId = reader.GetUInt32("tube_instrument_start_anim_id", 0);
                        template.TwohandFireAnimId = reader.GetUInt32("twohand_fire_anim_id", 0);
                        template.UseSkillCamera = reader.GetBoolean("use_skill_camera", true);
                        template.ValidHeightEdgeToEdge = reader.GetBoolean("valid_height_edge_to_edge", true);
                        
                        _skills.Add(template.Id, template);
                    }
                }
            }

            Logger.Info($"Loaded {_skills.Count} skills");

            Logger.Info("Loading heir skills...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM heir_skills";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        // updated to 3.5.0.3
                        var template = new HeirSkillTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.SkillId = reader.GetUInt32("skill_id");
                        template.Step = reader.GetInt32("step");

                        _heirSkills.Add(template.Id, template);
                    }
                }
            }
            Logger.Info("Loaded {0} heir skills", _heirSkills.Count);

            Logger.Info("Loading heir skill details...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM heir_skill_details";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        // updated to 3.5.0.3
                        var template = new HeirSkillDetailTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.HeirSkillId = reader.GetUInt32("heir_skill_id");
                        template.Point = reader.GetInt32("point");
                        template.Pos = reader.GetInt32("pos");
                        template.SkillId = reader.GetUInt32("skill_id");

                        _heirSkillDetails.Add(template.Id, template);
                    }
                }
            }
            Logger.Info("Loaded {0} heir skill details", _heirSkillDetails.Count);

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM default_skills";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        // updated to 3.5.0.3
                        var id = reader.GetUInt32("skill_id");
                        if (!_skills.TryGetValue(id, out var skillTemplate))
                        {
                            Logger.Warn("Default skill {0} references unknown skill id {1}; skipping.", reader.GetUInt32("id"), id);
                            continue;
                        }

                        var skill = new DefaultSkill();
                        skill.Template = skillTemplate;

                        skill.Id = reader.GetInt32("id");
                        skill.AddToSlot = reader.GetBoolean("add_to_slot", true);
                        skill.SkillActiveTypeId = reader.GetInt32("skill_active_type_id");
                        skill.SkillBookCategoryId = reader.GetInt32("skill_book_category_id");
                        skill.SkillId = id;
                        skill.Slot = reader.GetByte("slot_index");

                        _defaultSkills.TryAdd(skill.Template.Id, skill);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM passive_buffs";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        // updated to 3.5.0.3
                        var template = new PassiveBuffTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.AbilityId = (AbilityType)reader.GetByte("ability_id");
                        template.Level = reader.GetByte("level");
                        template.BuffId = reader.GetUInt32("buff_id");
                        template.HighAbilityId = reader.GetInt32("high_ability_id");
                        template.ReqPoints = reader.GetInt32("req_points");
                        template.Active = reader.GetBoolean("active", true);
                        template.SkillPoints = reader.GetInt32("skill_points");

                        _passiveBuffs.Add(template.Id, template);
                    }
                }
            }

            Logger.Info("Loading skill effects/buffs...");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        // updated to 3.0.3.0
                        var template = new BuffTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.ActiveWeaponId = reader.GetUInt32("active_weapon_id");
                        template.AddDurationBuffMul = reader.GetUInt32("add_duration_buff_mul");
                        template.AddDurationBuffId = reader.GetUInt32("add_duration_buff_id");
                        template.AgStance = reader.GetString("ag_stance");
                        template.AnimActionId = reader.GetUInt32("anim_action_id");
                        template.AnimEndId = reader.GetUInt32("anim_end_id");
                        template.AnimStartId = reader.GetUInt32("anim_start_id");
                        template.AntiStealth = reader.GetBoolean("anti_stealth");
                        template.AuraChildOnly = reader.GetBoolean("aura_child_only");
                        template.AuraCreatorOnly = reader.GetBoolean("aura_creator_only");
                        template.AuraRadius = reader.GetUInt32("aura_radius");
                        template.AuraRelationId = reader.GetUInt32("aura_relation_id");
                        template.AuraSlaveBuffId = reader.GetUInt32("aura_slave_buff_id");
                        template.BalanceLevel = reader.GetUInt32("balance_level");
                        template.BlankMinded = reader.GetBoolean("blank_minded");
                        template.BossTelescopeRange = reader.GetFloat("boss_telescope_range");
                        template.CannotJump = reader.GetBoolean("cannot_jump");
                        template.CombatTextEnd = reader.GetBoolean("combat_text_end");
                        template.CombatTextStart = reader.GetBoolean("combat_text_start");
                        template.ConditionalTick = reader.GetBoolean("conditional_tick");
                        template.CooldownSkillId = reader.GetUInt32("cooldown_skill_id");
                        template.CooldownSkillTime = reader.GetUInt32("cooldown_skill_time");
                        template.Crippled = reader.GetBoolean("crippled");
                        template.CrowdBuffId = reader.GetUInt32("crowd_buff_id");
                        template.CrowdFriendly = reader.GetBoolean("crowd_friendly");
                        template.CrowdHostile = reader.GetBoolean("crowd_hostile");
                        template.CrowdNumber = reader.GetUInt32("crowd_number");
                        template.CrowdRadius = reader.GetFloat("crowd_radius");
                        template.CustomDualMaterialFadeTime = reader.GetFloat("custom_dual_material_fade_time");
                        template.CustomDualMaterialId = reader.GetUInt32("custom_dual_material_id");
                        template.DamageAbsorptionPerHit = reader.GetUInt32("damage_absorption_per_hit");
                        template.DamageAbsorptionTypeId = reader.GetUInt32("damage_absorption_type_id");
                        template.DeadApplicable = reader.GetBoolean("dead_applicable");
                        template.Desc = reader.GetString("desc");
                        template.DetectStealth = reader.GetBoolean("detect_stealth");
                        template.DisarmamentMainHand = reader.GetBoolean("disarmament_main_hand");
                        template.DisarmamentMusical = reader.GetBoolean("disarmament_musical");
                        template.DisarmamentOffHand = reader.GetBoolean("disarmament_off_hand");
                        template.DisarmamentRanged = reader.GetBoolean("disarmament_ranged");
                        template.DoNotRemoveByOtherSkillController = reader.GetBoolean("do_not_remove_by_other_skill_controller");
                        template.Duration = reader.GetInt32("duration");
                        template.EvadeTelescope = reader.GetBoolean("evade_telescope");
                        template.Exempt = reader.GetBoolean("exempt");
                        template.ExtraEffects = reader.GetString("extra_effects");
                        template.FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0);
                        template.FallDamageImmortality = reader.GetBoolean("fall_damage_immortality");
                        template.FallDamageImmune = reader.GetBoolean("fall_damage_immune");
                        template.Fastened = reader.GetBoolean("fastened");
                        template.FindSchoolOfFishRange = reader.GetFloat("find_school_of_fish_range");
                        template.FixAbilityLevelToOne = reader.GetBoolean("fix_ability_level_to_one");
                        template.Framehold = reader.GetBoolean("framehold");
                        template.FreezeShip = reader.GetBoolean("freeze_ship");
                        template.FxGroupId = reader.GetUInt32("fx_group_id");
                        template.Gliding = reader.GetBoolean("gliding");
                        template.GlidingFallSpeedFast = reader.GetFloat("gliding_fall_speed_fast");
                        template.GlidingFallSpeedNormal = reader.GetFloat("gliding_fall_speed_normal");
                        template.GlidingFallSpeedSlow = reader.GetFloat("gliding_fall_speed_slow");
                        template.GlidingLandHeight = reader.GetFloat("gliding_land_height");
                        template.GlidingLiftCount = reader.GetUInt32("gliding_lift_count");
                        template.GlidingLiftDuration = reader.GetFloat("gliding_lift_duration");
                        template.GlidingLiftHeight = reader.GetFloat("gliding_lift_height");
                        template.GlidingLiftSpeed = reader.GetFloat("gliding_lift_speed");
                        template.GlidingLiftValidTime = reader.GetFloat("gliding_lift_valid_time");
                        template.GlidingMoveSpeedFast = reader.GetFloat("gliding_move_speed_fast");
                        template.GlidingMoveSpeedNormal = reader.GetFloat("gliding_move_speed_normal");
                        template.GlidingMoveSpeedSlow = reader.GetFloat("gliding_move_speed_slow");
                        template.GlidingRotateSpeed = reader.GetUInt32("gliding_rotate_speed");
                        template.GlidingSlidingTime = reader.GetFloat("gliding_sliding_time");
                        template.GlidingSmoothTime = reader.GetFloat("gliding_smooth_time");
                        template.GlidingStartupSpeed = reader.GetFloat("gliding_startup_speed");
                        template.GlidingStartupTime = reader.GetFloat("gliding_startup_time");
                        template.GroupId = reader.GetUInt32("group_id");
                        template.GroupRank = reader.GetUInt32("group_rank");
                        template.IconId = reader.GetUInt32("icon_id");
                        template.IdleAnim = reader.GetString("idle_anim");
                        template.ImmuneDamage = reader.GetUInt32("immune_damage");
                        template.ImmuneExceptCreator = reader.GetBoolean("immune_except_creator");
                        template.ImmuneExceptCreatorRelationId = reader.GetUInt32("immune_except_creator_relation_id");
                        template.ImmuneExceptCreatorRelationCheck = reader.GetBoolean("immune_except_creator_relation_check");
                        template.ImmuneExceptSkillTagId = reader.GetUInt32("immune_except_skill_tag_id");
                        template.ImmuneHealth = reader.GetFloat("immune_health");
                        template.ImpossibleChangeTargeting = reader.GetBoolean("impossible_change_targeting");
                        template.ImpossibleTargeting = reader.GetBoolean("impossible_targeting");
                        template.InitMaxCharge = reader.GetInt32("init_max_charge");
                        template.InitMinCharge = reader.GetInt32("init_min_charge");
                        template.KindId = reader.GetUInt32("kind_id");
                        template.Kind = (BuffKind)template.KindId;
                        template.KnockDown = reader.GetBoolean("knock_down");
                        template.KnockbackImmune = reader.GetBoolean("knockback_immune");
                        template.LevelDuration = reader.GetInt32("level_duration");
                        template.LinkBuffId = reader.GetUInt32("link_buff_id");
                        template.MainhandToolId = reader.GetUInt32("mainhand_tool_id");
                        template.ManaBurnImmune = reader.GetBoolean("mana_burn_immune");
                        template.ManaShieldRatio = reader.GetUInt32("mana_shield_ratio");
                        template.MaxCharge = reader.GetInt32("max_charge");
                        template.MaxHighAbilityResource = reader.GetUInt32("max_high_ability_resource");
                        template.MaxLifeTime = reader.GetUInt32("max_life_time");
                        template.MaxStack = reader.GetUInt32("max_stack");
                        template.MeleeImmortality = reader.GetBoolean("melee_immortality");
                        template.MeleeImmune = reader.GetBoolean("melee_immune");
                        template.MinHighAbilityResource = reader.GetUInt32("min_high_ability_resource");
                        template.Name = reader.GetString("name");
                        template.NoCollide = reader.GetBoolean("no_collide");
                        template.NoCollideRigid = reader.GetBoolean("no_collide_rigid");
                        template.NoExpPenalty = reader.GetBoolean("no_exp_penalty");
                        template.NonPushable = reader.GetBoolean("non_pushable");
                        template.NotToSlaveRider = reader.GetBoolean("not_to_slave_rider");
                        template.OffPassive = reader.GetBoolean("off_passive");
                        template.OffPassiveExectionTagId = reader.GetUInt32("off_passive_exection_tag_id");
                        template.OffhandToolId = reader.GetUInt32("offhand_tool_id");
                        template.OneTime = reader.GetBoolean("one_time");
                        template.OneTimeImmortality = reader.GetBoolean("one_time_immortality");
                        template.OnlyMyPet = reader.GetBoolean("only_my_pet");
                        template.OnlyPetOwner = reader.GetBoolean("only_pet_owner");
                        template.OwnerOnly = reader.GetBoolean("owner_only");
                        template.Pacifist = reader.GetBoolean("pacifist");
                        template.PerUnitCreation = reader.GetBoolean("per_unit_creation");
                        template.PercussionInstrumentStartAnimId = reader.GetUInt32("percussion_instrument_start_anim_id");
                        template.PercussionInstrumentTickAnimId = reader.GetUInt32("percussion_instrument_tick_anim_id");
                        template.Psychokinesis = reader.GetBoolean("psychokinesis");
                        template.PsychokinesisSpeed = reader.GetFloat("psychokinesis_speed");
                        template.Ragdoll = reader.GetBoolean("ragdoll");
                        template.RangedImmortality = reader.GetBoolean("ranged_immortality");
                        template.RangedImmune = reader.GetBoolean("ranged_immune");
                        template.RealTime = reader.GetBoolean("real_time");
                        template.ReflectionChance = reader.GetUInt32("reflection_chance");
                        template.ReflectionRatio = reader.GetUInt32("reflection_ratio");
                        template.ReflectionTargetRatio = reader.GetUInt32("reflection_target_ratio");
                        template.ReflectionTypeId = reader.GetUInt32("reflection_type_id");
                        template.RemoveOnAttackBuffTrigger = reader.GetBoolean("remove_on_attack_buff_trigger");
                        template.RemoveOnAttackEtc = reader.GetBoolean("remove_on_attack_etc");
                        template.RemoveOnAttackEtcDot = reader.GetBoolean("remove_on_attack_etc_dot");
                        template.RemoveOnAttackSpellDot = reader.GetBoolean("remove_on_attack_spell_dot");
                        template.RemoveOnAttackedBuffTrigger = reader.GetBoolean("remove_on_attacked_buff_trigger");
                        template.RemoveOnAttackedEtc = reader.GetBoolean("remove_on_attacked_etc");
                        template.RemoveOnAttackedEtcDot = reader.GetBoolean("remove_on_attacked_etc_dot");
                        template.RemoveOnAttackedSpellDot = reader.GetBoolean("remove_on_attacked_spell_dot");
                        template.RemoveOnAutoAttack = reader.GetBoolean("remove_on_autoattack");
                        template.RemoveOnDamageBuffTrigger = reader.GetBoolean("remove_on_damage_buff_trigger");
                        template.RemoveOnDamageEtc = reader.GetBoolean("remove_on_damage_etc");
                        template.RemoveOnDamageEtcDot = reader.GetBoolean("remove_on_damage_etc_dot");
                        template.RemoveOnDamageSpellDot = reader.GetBoolean("remove_on_damage_spell_dot");
                        template.RemoveOnDamagedBuffTrigger = reader.GetBoolean("remove_on_damaged_buff_trigger");
                        template.RemoveOnDamagedEtc = reader.GetBoolean("remove_on_damaged_etc");
                        template.RemoveOnDamagedEtcDot = reader.GetBoolean("remove_on_damaged_etc_dot");
                        template.RemoveOnDamagedSpellDot = reader.GetBoolean("remove_on_damaged_spell_dot");
                        template.RemoveOnDeath = reader.GetBoolean("remove_on_death");
                        template.RemoveOnExempt = reader.GetBoolean("remove_on_exempt");
                        template.RemoveOnInteraction = reader.GetBoolean("remove_on_interaction");
                        template.RemoveOnLand = reader.GetBoolean("remove_on_land");
                        template.RemoveOnMount = reader.GetBoolean("remove_on_mount");
                        template.RemoveOnMove = reader.GetBoolean("remove_on_move");
                        template.RemoveOnSourceDead = reader.GetBoolean("remove_on_source_dead");
                        template.RemoveOnStartSkill = reader.GetBoolean("remove_on_start_skill");
                        template.RemoveOnUnbond = reader.GetBoolean("remove_on_unbond");
                        template.RemoveOnUnmount = reader.GetBoolean("remove_on_unmount");
                        template.RemoveOnUnmountAttachPointId = reader.GetUInt32("remove_on_unmount_attach_point_id");
                        template.RemoveOnUseSkill = reader.GetBoolean("remove_on_use_skill");
                        template.RequireBuffId = reader.GetUInt32("require_buff_id");
                        template.RestrictActionbar = reader.GetBoolean("restrict_actionbar");
                        template.ResurrectionHealth = reader.GetUInt32("resurrection_health");
                        template.ResurrectionMana = reader.GetUInt32("resurrection_mana");
                        template.ResurrectionPercent = reader.GetBoolean("resurrection_percent");
                        template.Root = reader.GetBoolean("root");
                        template.SaveRuleId = reader.GetUInt32("save_rule_id");
                        template.Scale = reader.GetFloat("scale");
                        template.ScaleDuration = reader.GetFloat("scaleDuration");
                        template.SiegeImmortality = reader.GetBoolean("siege_immortality");
                        template.SiegeImmune = reader.GetBoolean("siege_immune");
                        template.Silence = reader.GetBoolean("silence");
                        template.SkillControllerId = reader.GetUInt32("skill_controller_id");
                        template.SlaveApplicable = reader.GetBoolean("slave_applicable");
                        template.Sleep = reader.GetBoolean("sleep");
                        template.SpellImmortality = reader.GetBoolean("spell_immortality");
                        template.SpellImmune = reader.GetBoolean("spell_immune");
                        template.SprintMotion = reader.GetBoolean("sprint_motion");
                        template.StackRuleId = reader.GetUInt32("stack_rule_id");
                        template.StackRule = (BuffStackRule)template.StackRuleId;
                        template.Stealth = reader.GetBoolean("stealth");
                        template.StopOnlineLpRegen = reader.GetBoolean("stop_online_lp_regen");
                        template.StringInstrumentStartAnimId = reader.GetUInt32("string_instrument_start_anim_id");
                        template.StringInstrumentTickAnimId = reader.GetUInt32("string_instrument_tick_anim_id");
                        template.Stun = reader.GetBoolean("stun");
                        template.System = reader.GetBoolean("system");
                        template.TargetingRelationId = reader.GetUInt32("targeting_relation_id");
                        template.TargetingUseOriginSource = reader.GetBoolean("targeting_use_origin_source");
                        template.Taunt = reader.GetBoolean("taunt");
                        template.TauntWithTopAggro = reader.GetBoolean("taunt_with_top_aggro");
                        template.TelescopeRange = reader.GetFloat("telescope_range");
                        template.Tick = reader.GetDouble("tick");
                        template.TickActiveWeaponId = reader.GetUInt32("tick_active_weapon_id");
                        template.TickAnimId = reader.GetUInt32("tick_anim_id");
                        template.TickAreaAngle = reader.GetUInt32("tick_area_angle");
                        template.TickAreaExcludeSource = reader.GetBoolean("tick_area_exclude_source");
                        template.TickAreaFrontAngle = reader.GetUInt32("tick_area_front_angle");
                        template.TickAreaRadius = reader.GetFloat("tick_area_radius");
                        template.TickAreaRelationId = reader.GetUInt32("tick_area_relation_id");
                        template.TickAreaUseOriginSource = reader.GetBoolean("tick_area_use_origin_source");
                        template.TickLevelManaCost = reader.GetFloat("tick_level_mana_cost");
                        template.TickMainhandToolId = reader.GetUInt32("tick_mainhand_tool_id");
                        template.TickManaCost = reader.GetUInt32("tick_mana_cost");
                        template.TickOffhandToolId = reader.GetUInt32("tick_offhand_tool_id");
                        template.TransferTelescopeRange = reader.GetFloat("transfer_telescope_range");
                        template.TransformBuffId = reader.GetUInt32("transform_buff_id");
                        template.TubeInstrumentStartAnimId = reader.GetUInt32("tube_instrument_start_anim_id");
                        template.TubeInstrumentTickAnimId = reader.GetUInt32("tube_instrument_tick_anim_id");
                        template.UseSourceFaction = reader.GetBoolean("use_source_faction");
                        template.WalkOnly = reader.GetBoolean("walk_only");

                        _buffs.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM buff_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new BuffEffect { Id = reader.GetUInt32("id") };
                        var buffId = reader.GetUInt32("buff_id");
                        if (_buffs.TryGetValue(buffId, out var buff))
                            template.Buff = buff;
                        template.Chance = reader.GetInt32("chance");
                        template.Stack = reader.GetInt32("stack");
                        template.AbLevel = reader.GetUInt16("ab_level");
                        _effects["BuffEffect"].Add(template.Id, template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM buff_tick_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var buffId = reader.GetUInt32("buff_id");
                        var template = _buffs[buffId];
                        var tickEffect = new TickEffect
                        {
                            CheckNoTargetTagSrc = reader.GetBoolean("check_no_target_tag_src", true),
                            CheckTargetTagSrc = reader.GetBoolean("check_target_tag_src", true),
                            EffectId = reader.GetUInt32("effect_id"), TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0),
                            TargetNoBuffTagId = reader.GetUInt32("target_nobuff_tag_id", 0)
                        };
                        template.TickEffects.Add(tickEffect);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Buff'"; // TODO OwnerType: BuffUnitModifier -> buff_unit_modifiers
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var buffId = reader.GetUInt32("owner_id");
                        if (!_buffs.TryGetValue(buffId, out var buff))
                            continue;
                        var template = new BonusTemplate
                        {
                            Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id"), ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id"),
                            Value = reader.GetInt32("value"),
                            LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                        };
                        buff.Bonuses.Add(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM linear_funcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new LinearFuncTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            StartValue = reader.GetInt32("start_value"),
                            EndValue = reader.GetInt32("end_value")
                        };
                        _linearFuncs[template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM dynamic_unit_modifiers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var buffId = reader.GetUInt32("buff_id");
                        if (!_buffs.TryGetValue(buffId, out var buff))
                            continue;
                        var template = new DynamicBonusTemplate
                        {
                            Attribute = (UnitAttribute)reader.GetByte("unit_attribute_id"), ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id"),
                            FuncId = reader.GetUInt32("func_id"),
                            FuncType = reader.GetString("func_type")
                        };
                        buff.DynamicBonuses.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skill_controllers";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new SkillControllerTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            KindId = reader.GetUInt32("kind_id"),
                            ActiveWeaponId = reader.GetUInt32("active_weapon_id"),
                            EndAnimId = reader.GetUInt32("end_anim_id"),
                            EndSkillId = reader.GetUInt32("end_skill_id"),
                            StartAnimId = reader.GetUInt32("start_anim_id"),
                            StrValue1 = reader.GetString("str_value1"),
                            TransitionAnim1Id = reader.GetUInt32("transition_anim_1_id"),
                            TransitionAnim2Id = reader.GetUInt32("transition_anim_2_id")
                        };
                        for (var i = 0; i < 15; i++)
                            template.Value[i] = reader.GetInt32($"value{i + 1}", 0);
                        _effects["SkillController"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM account_attribute_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new AccountAttributeEffect
                        {
                            Id = reader.GetUInt32("id"), KindId = reader.GetUInt32("kind_id"), BindWorld = reader.GetBoolean("bind_world"),
                            KindValue = reader.GetUInt32("kind_value"),
                            IsAdd = reader.GetBoolean("is_add"),
                            Count = reader.GetUInt32("count"),
                            Time = reader.GetUInt32("time")
                        };
                        _effects["AccountAttributeEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM accept_quest_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new AcceptQuestEffect { Id = reader.GetUInt32("id"), QuestId = reader.GetUInt32("quest_id") };
                        _effects["AcceptQuestEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM aggro_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new AggroEffect
                        {
                            Id = reader.GetUInt32("id"),
                            UseFixedAggro = reader.GetBoolean("use_fixed_aggro", true),
                            FixedMin = reader.GetInt32("fixed_min"),
                            FixedMax = reader.GetInt32("fixed_max"),
                            UseLevelAggro = reader.GetBoolean("use_level_aggro", true),
                            LevelMd = reader.GetFloat("level_md"),
                            LevelVaStart = reader.GetInt32("level_va_start"),
                            LevelVaEnd = reader.GetInt32("level_va_end"),
                            UseChargedBuff = reader.GetBoolean("use_charged_buff", true),
                            ChargedBuffId = reader.GetUInt32("charged_buff_id", 0),
                            ChargedMul = reader.GetFloat("charged_mul")
                        };
                        _effects["AggroEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM bubble_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new BubbleEffect
                        {
                            Id = reader.GetUInt32("id"),
                            KindId = reader.GetUInt32("kind_id"),
                            Speech = reader.GetString("speech")
                        };
                        _effects["BubbleEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM cinema_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CinemalEffect { Id = reader.GetUInt32("id"), CinemaId = reader.GetUInt32("cinema_id") };
                        _effects["CinemaEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM cleanup_ucc_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CleanupUccEffect { Id = reader.GetUInt32("id") };
                        _effects["CleanupUccEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM conversion_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ConversionEffect
                        {
                            Id = reader.GetUInt32("id"),
                            CategoryId = reader.GetUInt32("category_id"),
                            SourceCategoryId = reader.GetUInt32("source_category_id"),
                            SourceValue = reader.GetInt32("source_value"),
                            TargetCategoryId = reader.GetUInt32("target_category_id"),
                            TargetValue = reader.GetInt32("target_value")
                        };
                        _effects["ConversionEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM craft_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CraftEffect { Id = reader.GetUInt32("id"), WorldInteraction = (WorldInteractionType)reader.GetUInt32("wi_id") };
                        _effects["CraftEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM damage_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        // update to 3.5.0.3
                        var template = new DamageEffect();
                        template.Id = reader.GetUInt32("id");
                        template.ActabilityAdd = reader.GetFloat("actability_add");
                        template.ActabilityGroupId = reader.GetUInt32("actability_group_id", 0);
                        template.ActabilityMul = reader.GetFloat("actability_mul");
                        template.ActabilityStep = reader.GetInt32("actability_step");
                        template.AdjustDamageByHeight = reader.GetBoolean("adjust_damage_by_height");
                        template.AggroMultiplier = reader.GetFloat("aggro_multiplier");
                        template.ChargedBuffId = reader.GetUInt32("charged_buff_id", 0);
                        template.ChargedLevelMul = reader.GetFloat("charged_level_mul");
                        template.ChargedMul = reader.GetFloat("charged_mul");
                        template.CriticalBonus = reader.GetInt32("critical_bonus");
                        template.DamageTypeId = reader.GetInt32("damage_type_id");
                        template.DamageType = (DamageType)template.DamageTypeId;
                        template.DpsIncMultiplier = reader.GetFloat("dps_inc_multiplier");
                        template.DpsMultiplier = reader.GetFloat("dps_multiplier");
                        template.EngageCombat = reader.GetBoolean("engage_combat", true);
                        template.ExtraEffects = reader.GetString("extra_effects");
                        template.FireProc = reader.GetBoolean("fire_proc", true);
                        template.FixedMax = reader.GetInt32("fixed_max");
                        template.FixedMin = reader.GetInt32("fixed_min");
                        template.HealthStealRatio = reader.GetInt32("health_steal_ratio");
                        template.HighAbilityResourceDpsMd = reader.GetFloat("high_ability_resource_dps_md");
                        template.HighAbilityResourceLevelMd = reader.GetFloat("high_ability_resource_level_md");
                        template.HighAbilityResourceMd = reader.GetFloat("high_ability_resource_md");
                        template.HitAnimTimingId = reader.GetUInt32("hit_anim_timing_id");
                        template.LevelMd = reader.GetFloat("level_md");
                        template.LevelVaEnd = reader.GetInt32("level_va_end");
                        template.LevelVaStart = reader.GetInt32("level_va_start");
                        template.ManaDamage = reader.GetBoolean("mana_damage");
                        template.ManaStealRatio = reader.GetInt32("mana_steal_ratio");
                        template.Multiplier = reader.GetFloat("multiplier");
                        template.PercentMax = reader.GetInt32("percent_max");
                        template.PercentMin = reader.GetInt32("percent_min");
                        template.Synergy = reader.GetBoolean("synergy", true);
                        template.TargetBuffBonus = reader.GetInt32("target_buff_bonus");
                        template.TargetBuffBonusMul = reader.GetFloat("target_buff_bonus_mul");
                        template.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                        template.TargetChargedBuffId = reader.GetUInt32("target_charged_buff_id", 0);
                        template.TargetChargedMul = reader.GetFloat("target_charged_mul");
                        template.TargetHealthAdd = reader.GetInt32("target_health_add");
                        template.TargetHealthMax = reader.GetInt32("target_health_max");
                        template.TargetHealthMin = reader.GetInt32("target_health_min");
                        template.TargetHealthMul = reader.GetFloat("target_health_mul");
                        template.UseChargedBuff = reader.GetBoolean("use_charged_buff", true);
                        template.UseCurrentHealth = reader.GetBoolean("use_current_health", true);
                        template.UseFixedDamage = reader.GetBoolean("use_fixed_damage", true);
                        template.UseHighAbilityResource = reader.GetBoolean("use_high_ability_resource");
                        template.UseLevelDamage = reader.GetBoolean("use_level_damage", true);
                        template.UseMainhandWeapon = reader.GetBoolean("use_mainhand_weapon", true);
                        template.UseOffhandWeapon = reader.GetBoolean("use_offhand_weapon", true);
                        template.UsePercentDamage = reader.GetBoolean("use_percent_damage", true);
                        template.UseRangedWeapon = reader.GetBoolean("use_ranged_weapon", true);
                        template.UseTargetChargedBuff = reader.GetBoolean("use_target_charged_buff", true);
                        template.WeaponSlotId = reader.GetInt32("weapon_slot_id");

                        _effects["DamageEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM dispel_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new DispelEffect
                        {
                            Id = reader.GetUInt32("id"),
                            DispelCount = reader.GetInt32("dispel_count"),
                            CureCount = reader.GetInt32("cure_count"),
                            Stack = reader.GetInt32("stack"),
                            BuffTagId = reader.GetUInt32("buff_tag_id", 0)
                        };
                        _effects["DispelEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM doodad_item_change_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new DoodadItemChangeEffect
                        {
                            Id = reader.GetUInt32("id"),
                            Idx = reader.GetInt32("idx")
                        };
                        _effects["DoodadItemChangeEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM flying_state_change_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new FlyingStateChangeEffect { Id = reader.GetUInt32("id"), FlyingState = reader.GetBoolean("flying_state", true) };
                        _effects["FlyingStateChangeEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM gain_loot_pack_item_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new GainLootPackItemEffect
                        {
                            Id = reader.GetUInt32("id"),
                            LootPackId = reader.GetUInt32("loot_pack_id"),
                            ConsumeSourceItem = reader.GetBoolean("consume_source_item", true),
                            ConsumeItemId = reader.GetUInt32("consume_item_id", 0),
                            ConsumeCount = reader.GetInt32("consume_count"),
                            InheritGrade = reader.GetBoolean("inherit_grade", true)
                        };
                        _effects["GainLootPackItemEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM heal_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HealEffect
                        {
                            Id = reader.GetUInt32("id"),
                            UseFixedHeal = reader.GetBoolean("use_fixed_heal", true),
                            FixedMin = reader.GetInt32("fixed_min"),
                            FixedMax = reader.GetInt32("fixed_max"),
                            UseLevelHeal = reader.GetBoolean("use_level_heal", true),
                            LevelMd = reader.GetFloat("level_md"),
                            LevelVaStart = reader.GetInt32("level_va_start"),
                            LevelVaEnd = reader.GetInt32("level_va_end"),
                            Percent = reader.GetBoolean("percent", true),
                            UseChargedBuff = reader.GetBoolean("use_charged_buff", true),
                            ChargedBuffId = reader.GetUInt32("charged_buff_id", 0),
                            ChargedMul = reader.GetFloat("charged_mul"),
                            SlaveApplicable = reader.GetBoolean("slave_applicable", true),
                            IgnoreHealAggro = reader.GetBoolean("ignore_heal_aggro", true),
                            DpsMultiplier = reader.GetFloat("dps_multiplier"),
                            ActabilityGroupId = reader.GetUInt32("actability_group_id", 0),
                            ActabilityStep = reader.GetInt32("actability_step"),
                            ActabilityMul = reader.GetFloat("actability_mul"),
                            ActabilityAdd = reader.GetFloat("actability_add"),
                            ExtraEffects = reader.GetString("extra_effects")
                        };
                        _effects["HealEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM high_ability_resource_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new HighAbilityResourceEffect
                        {
                            Id = reader.GetUInt32("id"),
                            MaxHighAbilityResource = reader.GetInt32("max_high_ability_resource"),
                            MinHighAbilityResource = reader.GetInt32("min_high_ability_resource")
                        };
                        _effects["HighAbilityResourceEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM imprint_ucc_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ImprintUccEffect { Id = reader.GetUInt32("id"), ItemId = reader.GetUInt32("item_id", 0) };
                        _effects["ImprintUccEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM impulse_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ImpulseEffect
                        {
                            Id = reader.GetUInt32("id"),
                            VelImpulseX = reader.GetFloat("vel_impulse_x"),
                            VelImpulseY = reader.GetFloat("vel_impulse_y"),
                            VelImpulseZ = reader.GetFloat("vel_impulse_z"),
                            AngvelImpulseX = reader.GetFloat("angvel_impulse_x"),
                            AngvelImpulseY = reader.GetFloat("angvel_impulse_y"),
                            AngvelImpulseZ = reader.GetFloat("angvel_impulse_z"),
                            ImpulseX = reader.GetFloat("impulse_x"),
                            ImpulseY = reader.GetFloat("impulse_y"),
                            ImpulseZ = reader.GetFloat("impulse_z"),
                            AngImpulseX = reader.GetFloat("ang_impulse_x"),
                            AngImpulseY = reader.GetFloat("ang_impulse_y"),
                            AngImpulseZ = reader.GetFloat("ang_impulse_z")
                        };
                        _effects["ImpulseEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM interaction_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new InteractionEffect
                        {
                            Id = reader.GetUInt32("id"),
                            WorldInteraction = (WorldInteractionType)reader.GetInt32("wi_id"),
                            DoodadId = reader.GetUInt32("doodad_id", 0)
                        };
                        _effects["InteractionEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM kill_npc_without_corpse_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new KillNpcWithoutCorpseEffect
                        {
                            Id = reader.GetUInt32("id"), NpcId = reader.GetUInt32("npc_id"), Radius = reader.GetFloat("radius"),
                            GiveExp = reader.GetBoolean("give_exp", true),
                            Vanish = reader.GetBoolean("vanish", true)
                        };
                        _effects["KillNpcWithoutCorpseEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM mana_burn_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ManaBurnEffect
                        {
                            Id = reader.GetUInt32("id"), BaseMin = reader.GetInt32("base_min"), BaseMax = reader.GetInt32("base_max"),
                            DamageRatio = reader.GetInt32("damage_ratio"),
                            LevelMd = reader.GetFloat("level_md"),
                            LevelVaStart = reader.GetInt32("level_va_start"),
                            LevelVaEnd = reader.GetInt32("level_va_end")
                        };
                        _effects["ManaBurnEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM move_to_rez_point_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new MoveToRezPointEffect { Id = reader.GetUInt32("id") };
                        _effects["MoveToRezPointEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM level_up_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new LevelUpEffect
                        {
                            Id = reader.GetUInt32("id"),
                            Level = reader.GetInt32("level")
                        };
                        _effects["LevelUpEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_control_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new NpcControlEffect
                        {
                            Id = reader.GetUInt32("id"),
                            CategoryId = (NpcControlCategory)reader.GetUInt32("category_id"),
                            ParamString = reader.GetString("param_string", ""),
                            ParamInt = reader.GetUInt32("param_int")
                        };
                        _effects["NpcControlEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM open_portal_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new OpenPortalEffect { Id = reader.GetUInt32("id"), Distance = reader.GetFloat("distance") };
                        _effects["OpenPortalEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM move_to_location_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new MoveToLocationEffect
                        {
                            Id = reader.GetUInt32("id"),
                            OwnHouseOnly = reader.GetBoolean("own_house_only", true)
                        };
                        _effects["MoveToLocationEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM physical_explosion_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new PhysicalExplosionEffect
                        {
                            Id = reader.GetUInt32("id"), Radius = reader.GetFloat("radius"), HoleSize = reader.GetFloat("hole_size"),
                            Pressure = reader.GetFloat("pressure")
                        };
                        _effects["PhysicalExplosionEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM put_down_backpack_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new PutDownBackpackEffect
                        {
                            Id = reader.GetUInt32("id"), BackpackDoodadId = reader.GetUInt32("backpack_doodad_id")
                        };
                        _effects["PutDownBackpackEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM recover_exp_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new RecoverExpEffect
                        {
                            Id = reader.GetUInt32("id"),
                            NeedMoney = reader.GetBoolean("need_money", true),
                            NeedLaborPower = reader.GetBoolean("need_labor_power", true),
                            NeedPriest = reader.GetBoolean("need_priest", true),
                            Penaltied = reader.GetBoolean("penaltied", true)
                        };
                        _effects["RecoverExpEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM repair_slave_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new RepairSlaveEffect
                        {
                            Id = reader.GetUInt32("id"), Health = reader.GetInt32("health"), Mana = reader.GetInt32("mana")
                        };
                        _effects["RepairSlaveEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM report_crime_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ReportCrimeEffect
                        {
                            Id = reader.GetUInt32("id"), Value = reader.GetInt32("value"), CrimeKindId = reader.GetUInt32("crime_kind_id")
                        };
                        _effects["ReportCrimeEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM reset_aoe_diminishing_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ResetAoeDiminishingEffect { Id = reader.GetUInt32("id") };
                        _effects["ResetAoeDiminishingEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM restore_mana_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new RestoreManaEffect
                        {
                            Id = reader.GetUInt32("id"),
                            UseFixedValue = reader.GetBoolean("use_fixed_value", true),
                            FixedMin = reader.GetInt32("fixed_min"),
                            FixedMax = reader.GetInt32("fixed_max"),
                            UseLevelValue = reader.GetBoolean("use_level_value", true),
                            LevelMd = reader.GetFloat("level_md"),
                            LevelVaStart = reader.GetInt32("level_va_start"),
                            LevelVaEnd = reader.GetInt32("level_va_end"),
                            Percent = reader.GetBoolean("percent", true)
                        };
                        _effects["RestoreManaEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM play_log_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new PlayLogEffect
                        {
                            Id = reader.GetUInt32("id"),
                            Message = reader.GetString("message")
                        };
                        _effects["PlayLogEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM scoped_f_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ScopedFEffect
                        {
                            Id = reader.GetUInt32("id"), Range = reader.GetInt32("range"), Key = reader.GetString("key"),
                            DoodadId = reader.GetUInt32("doodad_id")
                        };
                        _effects["ScopedFEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM spawn_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        // update to 3.5.0.3
                        var template = new SpawnEffect();
                        template.Id = reader.GetUInt32("id");
                        template.OwnerTypeId = (BaseUnitType)reader.GetUInt32("owner_type_id");
                        template.SubType = reader.GetUInt32("sub_type");
                        template.PosDirId = reader.GetUInt32("pos_dir_id");
                        // PosAngle is stale; the DB only provides pos_angle_min / pos_angle_max.
                        template.PosAngleMax = reader.GetFloat("pos_angle_max");
                        template.PosAngleMin = reader.GetFloat("pos_angle_min"); // added
                        template.PosDistanceMax = reader.GetFloat("pos_distance_max"); // added
                        template.PosDistanceMin = reader.GetFloat("pos_distance_min"); // added
                        template.OriDirId = reader.GetUInt32("ori_dir_id");
                        template.OriAngle = reader.GetFloat("ori_angle");
                        template.UseSummonerFaction = reader.GetBoolean("use_summoner_faction", true);
                        template.LifeTime = reader.GetFloat("life_time");
                        template.DespawnOnCreatorDeath = reader.GetBoolean("despawn_on_creator_death", true);
                        template.UseSummonerAggroTarget = reader.GetBoolean("use_summoner_aggro_target", true);
                        template.MateStateId = (MateState)reader.GetUInt32("mate_state_id", 0);

                        _effects["SpawnEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_spawner_spawn_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new NpcSpawnerSpawnEffect
                        {
                            Id = reader.GetUInt32("id"),
                            SpawnerId = reader.GetUInt32("spawner_id", 0),
                            LifeTime = reader.GetFloat("life_time"),
                            DespawnOnCreatorDeath = reader.GetBoolean("despawn_on_creator_death", true),
                            UseSummonerAggroTarget = reader.GetBoolean("use_summoner_aggro_target", true),
                            UseSummonerFaction = reader.GetBoolean("use_summoner_faction", true),
                            ActivationState = reader.GetBoolean("activation_state", true)
                        };
                        _effects["NpcSpawnerSpawnEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_spawner_despawn_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new NpcSpawnerDespawnEffect { Id = reader.GetUInt32("id"), SpawnerId = reader.GetUInt32("spawner_id", 0) };
                        _effects["NpcSpawnerDespawnEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM spawn_fish_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpawnFishEffect
                        {
                            Id = reader.GetUInt32("id"), Range = reader.GetUInt32("range"), DoodadId = reader.GetUInt32("doodad_id", 0)
                        };
                        _effects["SpawnFishEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM spawn_gimmick_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpawnGimmickEffect
                        {
                            Id = reader.GetUInt32("id"),
                            GimmickId = reader.GetUInt32("gimmick_id"),
                            OffsetFromSource = reader.GetBoolean("offset_from_source", true),
                            OffsetCoordinateId = reader.GetUInt32("offset_coordiate_id"),
                            OffsetX = reader.GetFloat("offset_x"),
                            OffsetY = reader.GetFloat("offset_y"),
                            OffsetZ = reader.GetFloat("offset_z"),
                            Scale = reader.GetFloat("scale"),
                            VelocityCoordinateId = reader.GetUInt32("velocity_coordiate_id"),
                            VelocityX = reader.GetFloat("velocity_x"),
                            VelocityY = reader.GetFloat("velocity_y"),
                            VelocityZ = reader.GetFloat("velocity_z"),
                            AngVelCoordinateId = reader.GetUInt32("ang_vel_coordiate_id"),
                            AngVelX = reader.GetFloat("ang_vel_x"),
                            AngVelY = reader.GetFloat("ang_vel_y"),
                            AngVelZ = reader.GetFloat("ang_vel_z")
                        };
                        _effects["SpawnGimmickEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM special_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SpecialEffect
                        {
                            Id = reader.GetUInt32("id"),
                            SpecialEffectTypeId = (SpecialType)reader.GetInt32("special_effect_type_id"),
                            Value1 = reader.GetInt32("value1"),
                            Value2 = reader.GetInt32("value2"),
                            Value3 = reader.GetInt32("value3"),
                            Value4 = reader.GetInt32("value4"),
                            Value5 = reader.GetInt32("value5"),
                            Value6 = reader.GetInt32("value6")
                        };
                        _effects["SpecialEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM train_craft_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new TrainCraftEffect { Id = reader.GetUInt32("id"), CraftId = reader.GetUInt32("craft_id") };
                        _effects["TrainCraftEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM world_message_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new WorldMessageEffect
                        {
                            Id = reader.GetUInt32("id"),
                            FactionScopeId = reader.GetInt32("faction_scope_id"),
                            IconKey = reader.GetString("icon_key"),
                            KillHero = reader.GetBoolean("kill_hero", true),
                            KillStreakCount = reader.GetInt32("kill_streak_count"),
                            Message = reader.GetString("message"),
                            ZoneGroupOnly = reader.GetBoolean("zone_group_only", true),
                            ZoneGroupWarState = reader.GetBoolean("zone_group_war_state", true)
                        };
                        _effects["WorldMessageEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM train_craft_rank_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new TrainCraftRankEffect
                        {
                            Id = reader.GetUInt32("id"), KindId = reader.GetUInt32("kind_id"), RankId = reader.GetUInt32("rank_id")
                        };
                        _effects["TrainCraftRankEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new EffectType
                        {
                            Id = reader.GetUInt32("id"), ActualId = reader.GetUInt32("actual_id"), Type = reader.GetString("actual_type")
                        };
                        _types.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skill_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        // update to 3.5.0.3
                        var skillId = reader.GetUInt32("skill_id");
                        if (!_skills.TryGetValue(skillId, out var skill))
                            continue;

                        var template = new SkillEffect();
                        var effectId = reader.GetUInt32("effect_id");

                        //for easier debugging
                        template.EffectId = effectId;

                        if (!_types.TryGetValue(effectId, out var type))
                        {
                            Logger.Warn("Skill effect {0} references unknown effect type id {1}; skipping.", reader.GetUInt32("id"), effectId);
                            continue;
                        }

                        if (!_effects.TryGetValue(type.Type, out var value) || !value.TryGetValue(type.ActualId, out var effectTemplate))
                        {
                            Logger.Warn("Skill effect {0} references missing effect {1} id {2}; skipping.", reader.GetUInt32("id"), type.Type, type.ActualId);
                            continue;
                        }

                        template.Template = effectTemplate;

                        template.Id = reader.GetUInt32("id");
                        template.AlwaysHit = reader.GetBoolean("always_hit", true);
                        template.ApplicationMethodId = reader.GetInt32("application_method_id");
                        template.ApplicationMethod = (SkillEffectApplicationMethod)template.ApplicationMethodId;
                        template.Back = reader.GetBoolean("back");
                        template.Chance = reader.GetInt32("chance");
                        template.CheckNoSourceTagSrc = reader.GetBoolean("check_no_source_tag_src", true);
                        template.CheckNoTargetTagSrc = reader.GetBoolean("check_no_target_tag_src", true);
                        template.CheckSourceTagSrc = reader.GetBoolean("check_source_tag_src", true);
                        template.CheckTargetTagSrc = reader.GetBoolean("check_target_tag_src", true);
                        template.ConsumeItemCount = reader.GetInt32("consume_item_count");
                        template.ConsumeItemId = reader.GetUInt32("consume_item_id", 0);
                        template.ConsumeSourceItem = reader.GetBoolean("consume_source_item", true);
                        template.EffectId = effectId;
                        template.EndCastingUseChance = reader.GetInt32("end_casting_use_chance");
                        template.EndHighAbilityResource = reader.GetInt32("end_high_ability_resource");
                        template.EndLevel = reader.GetByte("end_level");
                        template.Friendly = reader.GetBoolean("friendly", true);
                        template.Front = reader.GetBoolean("front");
                        template.InteractionSuccessHit = reader.GetBoolean("interaction_success_hit", true);
                        template.ItemSetId = reader.GetUInt32("item_set_id", 0);
                        template.NonFriendly = reader.GetBoolean("non_friendly", true);
                        template.SkillId = skillId;
                        template.SourceBuffTagId = reader.GetUInt32("source_buff_tag_id", 0);
                        template.SourceNoBuffTagId = reader.GetUInt32("source_nobuff_tag_id", 0);
                        template.StartCastingUseChance = reader.GetInt32("start_casting_use_chance");
                        template.StartHighAbilityResource = reader.GetInt32("start_high_ability_resource");
                        template.StartLevel = reader.GetByte("start_level");
                        template.SynergyText = reader.GetBoolean("synergy_text");
                        template.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                        template.TargetNoBuffTagId = reader.GetUInt32("target_nobuff_tag_id", 0);
                        template.TargetNpcTagId = reader.GetUInt32("target_npc_tag_id", 0);
                        template.Weight = reader.GetInt32("weight");

                        skill.Effects.Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM tagged_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var tagId = reader.GetUInt32("tag_id");
                        var buffId = reader.GetUInt32("buff_id");

                        if (!_buffTags.ContainsKey(buffId))
                            _buffTags.Add(buffId, []);
                        _buffTags[buffId].Add(tagId);

                        if (!_taggedBuffs.ContainsKey(tagId))
                            _taggedBuffs.Add(tagId, []);
                        _taggedBuffs[tagId].Add(buffId);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skill_modifiers";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        // update to 3.5.0.3
                        var template = new SkillModifier();
                        // Id is stale; skill_modifiers has no id column.
                        template.OwnerId = reader.GetUInt32("owner_id");
                        template.OwnerType = reader.GetString("owner_type");
                        template.TagId = reader.GetUInt32("tag_id", 0);
                        template.SkillAttribute = (SkillAttribute)reader.GetUInt32("skill_attribute_id");
                        template.UnitModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id");
                        template.Value = reader.GetInt32("value");
                        template.SkillId = reader.GetUInt32("skill_id", 0);
                        template.Synergy = reader.GetBoolean("synergy", true);

                        if (!_skillModifiers.ContainsKey(template.OwnerId))
                            _skillModifiers.Add(template.OwnerId, []);
                        _skillModifiers[template.OwnerId].Add(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM tagged_skills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var tagId = reader.GetUInt32("tag_id");
                        var skillId = reader.GetUInt32("skill_id");

                        //I guess we need this
                        if (!_skillTags.ContainsKey(skillId))
                            _skillTags.Add(skillId, []);
                        _skillTags[skillId].Add(tagId);

                        if (!_taggedSkills.ContainsKey(tagId))
                            _taggedSkills.Add(tagId, []);
                        _taggedSkills[tagId].Add(skillId);
                    }
                }
            }

            using (var command = connection2.CreateCommand())
            {
                command.CommandText = "SELECT * FROM combat_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var combatBuffTemplate = new CombatBuffTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            HitSkillId = reader.GetUInt32("hit_skill_id", 0),
                            HitType = (SkillHitType)reader.GetUInt32("hit_type_id"),
                            BuffId = reader.GetUInt32("buff_id"),
                            BuffFromSource = reader.GetBoolean("buff_from_source", true),
                            BuffToSource = reader.GetBoolean("buff_to_source", true),
                            ReqSkillId = reader.GetUInt32("req_skill_id", 0),
                            ReqBuffId = reader.GetUInt32("req_buff_id"),
                            IsHealSpell = reader.GetBoolean("is_heal_spell", true)
                        };

                        if (!_combatBuffs.ContainsKey(combatBuffTemplate.ReqBuffId))
                            _combatBuffs.Add(combatBuffTemplate.ReqBuffId, []);
                        _combatBuffs[combatBuffTemplate.ReqBuffId].Add(combatBuffTemplate);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skill_dynamic_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var jsonData = reader.GetString("effect");
                        var skillId = reader.GetUInt32("skill_id");
                        var jObj = JObject.Parse(jsonData);
                        var effect = jObj.GetValue("effect").ToString();

                        if (effect == "selective_item")
                            _dynamicEffects.selectiveItems.Add(skillId, new SelectiveItem(jObj));
                        else if (effect == "bless_uthstin")
                            _dynamicEffects.blessUthstins.TryAdd(skillId, new BlessUthstin(jObj, jsonData));
                    }
                }
            }

            Logger.Info("Skill effects loaded");

            _buffTriggers = [];
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM buff_triggers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        // update to 3.5.0.3
                        var trigger = new BuffTriggerTemplate();
                        var buffId = reader.GetUInt32("buff_id");
                        if (!_buffTriggers.ContainsKey(buffId))
                            _buffTriggers.Add(buffId, new List<BuffTriggerTemplate>());

                        trigger.Id = reader.GetUInt32("id");
                        trigger.BuffId = buffId;
                        trigger.CheckNoTagSrcInOwner = reader.GetBoolean("check_no_tag_src_in_owner");
                        trigger.CheckNoTagSrcInSource = reader.GetBoolean("check_no_tag_src_in_source");
                        trigger.CheckNoTagSrcInTarget = reader.GetBoolean("check_no_tag_src_in_target");
                        trigger.CheckTagSrcInOwner = reader.GetBoolean("check_tag_src_in_owner");
                        trigger.CheckTagSrcInSource = reader.GetBoolean("check_tag_src_in_source");
                        trigger.CheckTagSrcInTarget = reader.GetBoolean("check_tag_src_in_target");
                        trigger.DelayTime = reader.GetUInt32("delay_time", 0);
                        trigger.EffectId = reader.GetUInt32("effect_id");
                        trigger.Effect = GetEffectTemplate(trigger.EffectId);
                        trigger.EventId = reader.GetUInt32("event_id");
                        trigger.Kind = (BuffEventTriggerKind)trigger.EventId;
                        trigger.OwnerBuffTagId = reader.GetUInt32("owner_buff_tag_id");
                        trigger.OwnerNoBuffTagId = reader.GetUInt32("owner_no_buff_tag_id");
                        trigger.SourceAgentId = reader.GetUInt32("source_agent_id");
                        trigger.SourceBuffTagId = reader.GetUInt32("source_buff_tag_id");
                        trigger.SourceNoBuffTagId = reader.GetUInt32("source_no_buff_tag_id");
                        trigger.TargetAgentId = reader.GetUInt32("target_agent_id");
                        trigger.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id");
                        trigger.TargetNoBuffTagId = reader.GetUInt32("target_no_buff_tag_id");
                        trigger.UseCollisionImpact = reader.GetBoolean("use_collision_impact");
                        trigger.UseDamageAmount = reader.GetBoolean("use_damage_amount");
                        trigger.UseStackCount = reader.GetBoolean("use_stack_count");

                        // Apparently this is possible
                        if (trigger.Effect != null)
                            _buffTriggers[buffId].Add(trigger);
                    }
                }
            }
            Logger.Info("Buff triggers loaded");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from skill_reagents";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new SkillReagent
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt16("amount")
                        };
                        _skillReagents.Add(template.Id, template);
                    }
                }
            }
            Logger.Info("Skill Reagents loaded");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * from skill_products";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var template = new SkillProduct
                        {
                            Id = reader.GetUInt32("id"),
                            SkillId = reader.GetUInt32("skill_id"),
                            ItemId = reader.GetUInt32("item_id"),
                            Amount = reader.GetInt16("amount")
                        };
                        _skillProducts.Add(template.Id, template);
                    }
                }
                Logger.Info("Skill Products loaded");

                OnSkillsLoaded?.Invoke(this, EventArgs.Empty);
            }
        }

        // Version 3.5.0.3 does not expose need_learn, so derive the groups from ability id.
        foreach (var skillTemplate in _skills.Values)
        {
            if (!skillTemplate.AutoLearn)
                continue;

            if (skillTemplate.AbilityId == AbilityType.General)
            {
                if (!_defaultSkills.ContainsKey(skillTemplate.Id))
                    _commonSkills.Add(skillTemplate.Id);
                continue;
            }

            if (skillTemplate.AbilityId == AbilityType.None || skillTemplate.AbilityLevel > 1 || !skillTemplate.Show)
                continue;
            var ability = skillTemplate.AbilityId;
            if (!_startAbilitySkills.TryGetValue(ability, out var abilitySkills))
            {
                abilitySkills = [];
                _startAbilitySkills.Add(ability, abilitySkills);
            }
            abilitySkills.Add(skillTemplate);
        }

        _loaded = true;
    }

    /// <summary>
    /// Create an estimated use time for a skill
    /// </summary>
    /// <param name="skillTemplate">SkillTemplate to base on</param>
    /// <param name="caster">Unit to use the stats from for calculations</param>
    /// <param name="includeCooldown">Include the skill's cooldown time</param>
    /// <param name="additionalDelay">Additional delay to add (uses cooldown multiplier)</param>
    /// <returns></returns>
    public static double GetAttackDelay(SkillTemplate skillTemplate, Unit caster, bool includeCooldown = true, double additionalDelay = 1000.0)
    {
        // Auto-attack skills (2=melee, 3=offhand, 4=ranged) use weapon speed
        if (skillTemplate.Id is 2 or 3 or 4 && caster is Character character)
        {
            var weaponSpeed = GetWeaponSpeed(character, skillTemplate.Id);
            var delay = weaponSpeed * (caster.GlobalCooldownMul / 100.0);
            return Math.Clamp(delay, 400.0, 5000.0);
        }

        // Non-auto-attack skills: original formula
        var castTime = skillTemplate.CastingTime * caster.CastTimeMul * 1.0;
        var coolDownTime = includeCooldown ? skillTemplate.CooldownTime * (caster.GlobalCooldownMul / 100.0) : 0.0;
        var additionalTime = additionalDelay * (caster.GlobalCooldownMul / 100.0);
        return castTime + coolDownTime + additionalTime;
    }

    /// <summary>
    /// Get the weapon speed in ms for an auto-attack skill based on equipped weapon.
    /// </summary>
    private static double GetWeaponSpeed(Character character, uint skillId)
    {
        const double DefaultMeleeSpeed = 1500.0;
        const double DefaultRangedSpeed = 1800.0;

        EquipmentItemSlot slot;
        double fallback;
        switch (skillId)
        {
            case 2: slot = EquipmentItemSlot.Mainhand; fallback = DefaultMeleeSpeed; break;
            case 3: slot = EquipmentItemSlot.Offhand;  fallback = DefaultMeleeSpeed; break;
            case 4: slot = EquipmentItemSlot.Ranged;   fallback = DefaultRangedSpeed; break;
            default: return DefaultMeleeSpeed;
        }

        var weapon = character.Equipment?.GetItemBySlot((int)slot);
        if (weapon?.Template is WeaponTemplate wt && wt.HoldableTemplate != null && wt.HoldableTemplate.Speed > 0)
            return wt.HoldableTemplate.Speed;

        return fallback;
    }

    /// <summary>
    /// Gets the related ActAbility to a skill
    /// </summary>
    /// <param name="skillId"></param>
    /// <returns></returns>
    public ActabilityType GetSkillActAbility(uint skillId)
    {
        if (!_skills.TryGetValue(skillId, out var value))
            return ActabilityType.None;
        return (ActabilityType)value.ActabilityGroupId;
    }

    /// <summary>
    /// Gets the first spawn effect for a given Gimmick TemplateId
    /// </summary>
    /// <param name="gimmickTemplateId"></param>
    /// <returns></returns>
    public SpawnGimmickEffect GetSpawnGimmickEffect(uint gimmickTemplateId)
    {
        if (!_effects.TryGetValue("SpawnGimmickEffect", out var spawnGimmickEffects))
            return null;

        foreach (var effect in spawnGimmickEffects.Values)
        {
            if (effect is not SpawnGimmickEffect spawnGimmickEffect)
                continue;
            if (spawnGimmickEffect.GimmickId == gimmickTemplateId)
                return spawnGimmickEffect;
        }

        return null;
    }
    
}
