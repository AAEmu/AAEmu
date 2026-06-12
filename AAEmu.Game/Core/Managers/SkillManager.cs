using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Effects.Enums;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils.DB;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class SkillManager(IAnimationManager animationManager, IPlotManager plotManager) : Singleton<SkillManager>, ISkillManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;

    private Dictionary<uint, SkillTemplate> _skills;
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
        return _startAbilitySkills[ability];
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
        if (_types.TryGetValue(id, out var type))
        {
            Logger.Trace($"Get Effect Template: type = {type.Type}, id = {type.ActualId}");

            if (_effects.TryGetValue(type.Type, out _))
            {
                return _effects[type.Type][type.ActualId];
            }
            else
            {
                Logger.Warn($"No such Effect Type[{type.Type}] found.");
                return null;
            }
        }
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
            { "FlyingStateChangeEffect", [] },
            { "GainLootPackItemEffect", [] },
            { "HealEffect", [] },
            { "ImprintUccEffect", [] },
            { "ImpulseEffect", [] },
            { "InteractionEffect", [] },
            { "KillNpcWithoutCorpseEffect", [] },
            { "ManaBurnEffect", [] },
            { "MoveToRezPointEffect", [] },
            { "NpcControlEffect", [] },
            { "NpcSpawnerDespawnEffect", [] },
            { "NpcSpawnerSpawnEffect", [] },
            { "OpenPortalEffect", [] },
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
            { "SkillController", [] }, // missing from the effect table
            { "SpawnFishEffect", [] }, // missing from the effect table
            { "ResetAoeDiminishingEffect", [] } // missing from the effect table
        };

        _buffs = [];
        // TODO 
        /*
            _effects.Add("PlayLogEffect", new Dictionary<uint, EffectTemplate>()); // missing from the effect table
        */

        _buffTags = [];
        _taggedBuffs = [];
        _skillModifiers = [];
        _skillTags = [];
        _taggedSkills = [];
        _combatBuffs = [];
        _linearFuncs = [];
        _skillReagents = [];
        _skillProducts = [];

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
                        var template = new SkillTemplate
                        {
                            Id = reader.GetUInt32("id"), Cost = reader.GetInt32("cost"), Show = reader.GetBoolean("show", true),
                            FireAnim = animationManager.GetAnimation(reader.GetUInt32("fire_anim_id", 0)),
                            AbilityId = (AbilityType)reader.GetByte("ability_id"),
                            ManaCost = reader.GetInt32("mana_cost"),
                            TimingId = reader.GetInt32("timing_id"),
                            CooldownTime = reader.GetInt32("cooldown_time"),
                            CastingTime = reader.GetInt32("casting_time"),
                            IgnoreGlobalCooldown = reader.GetBoolean("ignore_global_cooldown", true),
                            EffectDelay = reader.GetInt32("effect_delay"),
                            EffectSpeed = reader.GetFloat("effect_speed"),
                            EffectRepeatCount = reader.GetInt32("effect_repeat_count"),
                            EffectRepeatTick = reader.GetInt32("effect_repeat_tick"),
                            ActiveWeaponId = reader.GetInt32("active_weapon_id"),
                            TargetType = (SkillTargetType)reader.GetInt32("target_type_id"),
                            TargetSelection = (SkillTargetSelection)reader.GetInt32("target_selection_id"),
                            TargetRelation = (SkillTargetRelation)reader.GetInt32("target_relation_id"),
                            TargetAreaCount = reader.GetInt32("target_area_count"),
                            TargetAreaRadius = reader.GetInt32("target_area_radius"),
                            TargetSiege = reader.GetBoolean("target_siege", true),
                            WeaponSlotForAngleId = reader.GetInt32("weapon_slot_for_angle_id"),
                            TargetAngle = reader.GetInt32("target_angle"),
                            WeaponSlotForRangeId = reader.GetInt32("weapon_slot_for_range_id"),
                            WeaponSlotForAutoAttackId = reader.GetInt32("weapon_slot_for_autoattack_id"),
                            MinRange = reader.GetInt32("min_range"),
                            MaxRange = reader.GetInt32("max_range"),
                            KeepStealth = reader.GetBoolean("keep_stealth", true),
                            Aggro = reader.GetInt32("aggro"),
                            ChannelingTime = reader.GetInt32("channeling_time"),
                            ChannelingTick = reader.GetInt32("channeling_tick"),
                            ChannelingMana = reader.GetInt32("channeling_mana"),
                            ChannelingTargetBuffId = reader.GetUInt32("channeling_target_buff_id", 0),
                            TargetAreaAngle = reader.GetInt32("target_area_angle"),
                            AbilityLevel = reader.GetInt32("ability_level"),
                            ChannelingDoodadId = reader.GetUInt32("channeling_doodad_id", 0)
                        };
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
                        template.NeedLearn = reader.GetBoolean("need_learn", true);
                        template.MainhandToolId = reader.GetUInt32("mainhand_tool_id", 0);
                        template.OffhandToolId = reader.GetUInt32("offhand_tool_id", 0);
                        template.FrontAngle = reader.GetInt32("front_angle");
                        template.ManaLevelMd = reader.GetFloat("mana_level_md");
                        template.Unmount = reader.GetBoolean("unmount", true);
                        template.DamageTypeId = reader.GetUInt32("damage_type_id", 0);
                        template.AllowToPrisoner = reader.GetBoolean("allow_to_prisoner", true);
                        template.MilestoneId = reader.GetUInt32("milestone_id", 0);
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
                        template.LevelRuleNoConsideration =
                            reader.GetBoolean("level_rule_no_consideration", true);
                        template.UseWeaponCooldownTime = reader.GetBoolean("use_weapon_cooldown_time", true);
                        template.CombatDiceId = reader.GetInt32("combat_dice_id");
                        template.CustomGcd = reader.GetInt32("custom_gcd");
                        template.CancelOngoingBuffs = reader.GetBoolean("cancel_ongoing_buffs", true);
                        template.CancelOngoingBuffExceptionTagId = reader.GetUInt32("cancel_ongoing_buff_exception_tag_id", 0);
                        template.SourceCannotUseWhileWalk =
                            reader.GetBoolean("source_cannot_use_while_walk", true);
                        template.SourceMountMate = reader.GetBoolean("source_mount_mate", true);
                        template.CheckTerrain = reader.GetBoolean("check_terrain", true);
                        template.TargetOnlyWater = reader.GetBoolean("target_only_water", true);
                        template.SourceNotSwim = reader.GetBoolean("source_not_swim", true);
                        template.TargetPreoccupied = reader.GetBoolean("target_preoccupied", true);
                        template.StopChannelingOnStartSkill =
                            reader.GetBoolean("stop_channeling_on_start_skill", true);
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
                        _skills.Add(template.Id, template);
                    }
                }
            }

            Logger.Info($"Loaded {_skills.Count} skills");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM default_skills";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var id = (uint)reader.GetInt32("skill_id");
                        var skill = new DefaultSkill
                        {
                            Template = _skills[id],
                            Slot = reader.GetByte("slot_index"),
                            AddToSlot = reader.GetBoolean("add_to_slot", true)
                        };
                        _defaultSkills.Add(skill.Template.Id, skill);
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
                        var template = new PassiveBuffTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            AbilityId = (AbilityType)reader.GetByte("ability_id"),
                            Level = reader.GetByte("level"),
                            BuffId = reader.GetUInt32("buff_id"),
                            ReqPoints = reader.GetInt32("req_points"),
                            Active = reader.GetBoolean("active", true)
                        };
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
                        // These string values are read here so they can fit nicely in the init-only part of the template
                        var skillControllerIdValue = reader.GetString("skill_controller_id", "0");
                        var mainHandToolIdValue = reader.GetString("mainhand_tool_id", "0");
                        var offhandToolIdValue = reader.GetString("offhand_tool_id", "0");
                        var tickMainHandToolIdValue = reader.GetString("tick_mainhand_tool_id", "0");
                        var tickOffHandToolIdValue = reader.GetString("tick_offhand_tool_id", "0");
                        var template = new BuffTemplate
                        {
                            Id = reader.GetUInt32("id"),
                            AnimStartId = reader.GetUInt32("anim_start_id", 0),
                            AnimEndId = reader.GetUInt32("anim_end_id", 0),
                            Duration = reader.GetInt32("duration"),
                            Tick = reader.GetDouble("tick"),
                            Silence = reader.GetBoolean("silence", true),
                            Root = reader.GetBoolean("root", true),
                            Sleep = reader.GetBoolean("sleep", true),
                            Stun = reader.GetBoolean("stun", true),
                            Cripled = reader.GetBoolean("crippled", true),
                            Stealth = reader.GetBoolean("stealth", true),
                            RemoveOnSourceDead = reader.GetBoolean("remove_on_source_dead", true),
                            LinkBuffId = reader.GetUInt32("link_buff_id", 0),
                            TickManaCost = reader.GetInt32("tick_mana_cost"),
                            StackRule = (BuffStackRule)reader.GetUInt32("stack_rule_id"),
                            InitMinCharge = reader.GetInt32("init_min_charge"),
                            InitMaxCharge = reader.GetInt32("init_max_charge"),
                            MaxStack = reader.GetInt32("max_stack"),
                            DamageAbsorptionTypeId = reader.GetUInt32("damage_absorption_type_id"),
                            DamageAbsorptionPerHit = reader.GetInt32("damage_absorption_per_hit"),
                            AuraRadius = reader.GetInt32("aura_radius"),
                            ManaShieldRatio = reader.GetInt32("mana_shield_ratio"),
                            FrameHold = reader.GetBoolean("framehold", true),
                            Ragdoll = reader.GetBoolean("ragdoll", true),
                            OneTime = reader.GetBoolean("one_time", true),
                            ReflectionChance = reader.GetInt32("reflection_chance"),
                            ReflectionTypeId = reader.GetUInt32("reflection_type_id"),
                            RequireBuffId = reader.GetUInt32("require_buff_id", 0),
                            Taunt = reader.GetBoolean("taunt", true),
                            TauntWithTopAggro = reader.GetBoolean("taunt_with_top_aggro", true),
                            RemoveOnUseSkill = reader.GetBoolean("remove_on_use_skill", true),
                            MeleeImmune = reader.GetBoolean("melee_immune", true),
                            SpellImmune = reader.GetBoolean("spell_immune", true),
                            RangedImmune = reader.GetBoolean("ranged_immune", true),
                            SiegeImmune = reader.GetBoolean("siege_immune", true),
                            ImmuneDamage = reader.GetInt32("immune_damage"),
                            SkillControllerId =
                                skillControllerIdValue.Contains("null") ? 0 : uint.Parse(skillControllerIdValue),
                            ResurrectionHealth = reader.GetInt32("resurrection_health"),
                            ResurrectionMana = reader.GetInt32("resurrection_mana"),
                            ResurrectionPercent = reader.GetBoolean("resurrection_percent", true),
                            LevelDuration = reader.GetInt32("level_duration"),
                            ReflectionRatio = reader.GetInt32("reflection_ratio"),
                            ReflectionTargetRatio = reader.GetInt32("reflection_target_ratio"),
                            KnockbackImmune = reader.GetBoolean("knockback_immune"),
                            ImmuneBuffTagId = reader.GetUInt32("immune_buff_tag_id", 0),
                            AuraRelationId = reader.GetUInt32("aura_relation_id"),
                            GroupId = reader.GetUInt32("group_id", 0),
                            GroupRank = reader.GetInt32("group_rank"),
                            PerUnitCreation = reader.GetBoolean("per_unit_creation"),
                            TickAreaRadius = reader.GetFloat("tick_area_radius"),
                            TickAreaRelationId = reader.GetUInt32("tick_area_relation_id"),
                            RemoveOnMove = reader.GetBoolean("remove_on_move", true),
                            UseSourceFaction = reader.GetBoolean("use_source_faction", true),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0),
                            Exempt = reader.GetBoolean("exempt", true),
                            TickAreaFrontAngle = reader.GetInt32("tick_area_front_angle"),
                            TickAreaAngle = reader.GetInt32("tick_area_angle"),
                            Psychokinesis = reader.GetBoolean("psychokinesis", true),
                            NoCollide = reader.GetBoolean("no_collide", true),
                            PsychokinesisSpeed = reader.GetFloat("psychokinesis_speed"),
                            RemoveOnDeath = reader.GetBoolean("remove_on_death", true),
                            TickAnimId = reader.GetUInt32("tick_anim_id", 0),
                            TickActiveWeaponId = reader.GetUInt32("tick_active_weapon_id"),
                            ConditionalTick = reader.GetBoolean("conditional_tick", true),
                            System = reader.GetBoolean("system", true),
                            AuraSlaveBuffId = reader.GetUInt32("aura_slave_buff_id", 0),
                            NonPushable = reader.GetBoolean("non_pushable", true),
                            ActiveWeaponId = reader.GetUInt32("active_weapon_id"),
                            MaxCharge = reader.GetInt32("max_charge"),
                            DetectStealth = reader.GetBoolean("detect_stealth", true),
                            RemoveOnExempt = reader.GetBoolean("remove_on_exempt", true),
                            RemoveOnLand = reader.GetBoolean("remove_on_land", true),
                            Gliding = reader.GetBoolean("gliding", true),
                            GlidingRotateSpeed = reader.GetInt32("gliding_rotate_speed"),
                            GlidingLiftHeight = reader.GetFloat("gliding_lift_height", 0f),
                            GlidingLiftSpeed = reader.GetFloat("gliding_lift_speed", 0f),
                            GlidingLiftDuration = reader.GetFloat("gliding_lift_duration", 0f),
                            Knockdown = reader.GetBoolean("knock_down", true),
                            TickAreaExcludeSource = reader.GetBoolean("tick_area_exclude_source", true),
                            // TODO 
                            /*
                                string_instrument_start_anim_id INT,
                                percussion_instrument_start_anim_id INT,
                                tube_instrument_start_anim_id INT,
                                string_instrument_tick_anim_id INT,
                                percussion_instrument_tick_anim_id INT,
                                tube_instrument_tick_anim_id INT,
                                gliding_startup_time REAL,
                                gliding_startup_speed REAL,
                                gliding_fall_speed_slow REAL,
                                gliding_fall_speed_normal REAL,
                                gliding_fall_speed_fast REAL,
                                gliding_smooth_time REAL,
                                gliding_lift_count INT,
                                gliding_lift_height REAL,
                                gliding_lift_valid_time REAL,
                                gliding_lift_duration REAL,
                                gliding_lift_speed REAL,
                                gliding_land_height REAL,
                                gliding_sliding_time REAL,
                                gliding_move_speed_slow REAL,
                                gliding_move_speed_normal REAL,
                                gliding_move_speed_fast REAL,
                             */
                            FallDamageImmune = reader.GetBoolean("fall_damage_immune", true),
                            Kind = (BuffKind)reader.GetInt32("kind_id"),
                            TransformBuffId = reader.GetUInt32("transform_buff_id", 0),
                            BlankMinded = reader.GetBoolean("blank_minded", true),
                            Fastened = reader.GetBoolean("fastened", true),
                            SlaveApplicable = reader.GetBoolean("slave_applicable", true),
                            Pacifist = reader.GetBoolean("pacifist", true),
                            RemoveOnInteraction = reader.GetBoolean("remove_on_interaction", true),
                            Crime = reader.GetBoolean("crime", true),
                            RemoveOnUnmount = reader.GetBoolean("remove_on_unmount", true),
                            AuraChildOnly = reader.GetBoolean("aura_child_only", true),
                            RemoveOnMount = reader.GetBoolean("remove_on_mount", true),
                            RemoveOnStartSkill = reader.GetBoolean("remove_on_start_skill", true),
                            SprintMotion = reader.GetBoolean("sprint_motion", true),
                            TelescopeRange = reader.GetFloat("telescope_range"),
                            MainhandToolId = mainHandToolIdValue.Length > 0 ? uint.Parse(mainHandToolIdValue) : 0,
                            OffhandToolId = offhandToolIdValue.Length > 0 ? uint.Parse(offhandToolIdValue) : 0,
                            TickMainhandToolId = tickMainHandToolIdValue.Length > 0 ? uint.Parse(tickMainHandToolIdValue) : 0,
                            TickOffhandToolId = tickOffHandToolIdValue.Length > 0 ? uint.Parse(tickOffHandToolIdValue) : 0,
                            TickLevelManaCost = reader.GetFloat("tick_level_mana_cost"),
                            WalkOnly = reader.GetBoolean("walk_only", true),
                            CannotJump = reader.GetBoolean("cannot_jump", true),
                            CrowdBuffId = reader.GetUInt32("crowd_buff_id", 0),
                            CrowdRadius = reader.GetFloat("crowd_radius"),
                            CrowdNumber = reader.GetInt32("crowd_number"),
                            EvadeTelescope = reader.GetBoolean("evade_telescope", true),
                            TransferTelescopeRange = reader.GetFloat("transfer_telescope_range"),
                            RemoveOnAttackSpellDot = reader.GetBoolean("remove_on_attack_spell_dot", true),
                            RemoveOnAttackEtcDot = reader.GetBoolean("remove_on_attack_etc_dot", true),
                            RemoveOnAttackBuffTrigger = reader.GetBoolean("remove_on_attack_buff_trigger", true),
                            RemoveOnAttackEtc = reader.GetBoolean("remove_on_attack_etc", true),
                            RemoveOnAttackedSpellDot = reader.GetBoolean("remove_on_attacked_spell_dot", true),
                            RemoveOnAttackedEtcDot = reader.GetBoolean("remove_on_attacked_etc_dot", true),
                            RemoveOnAttackedBuffTrigger = reader.GetBoolean("remove_on_attacked_buff_trigger", true),
                            RemoveOnAttackedEtc = reader.GetBoolean("remove_on_attacked_etc", true),
                            RemoveOnDamageSpellDot = reader.GetBoolean("remove_on_damage_spell_dot", true),
                            RemoveOnDamageEtcDot = reader.GetBoolean("remove_on_damage_etc_dot", true),
                            RemoveOnDamageBuffTrigger = reader.GetBoolean("remove_on_damage_buff_trigger", true),
                            RemoveOnDamageEtc = reader.GetBoolean("remove_on_damage_etc", true),
                            RemoveOnDamagedSpellDot = reader.GetBoolean("remove_on_damaged_spell_dot", true),
                            RemoveOnDamagedEtcDot = reader.GetBoolean("remove_on_damaged_etc_dot", true),
                            RemoveOnDamagedBuffTrigger = reader.GetBoolean("remove_on_damaged_buff_trigger", true),
                            RemoveOnDamagedEtc = reader.GetBoolean("remove_on_damaged_etc", true),
                            OwnerOnly = reader.GetBoolean("owner_only", true),
                            RemoveOnAutoAttack = reader.GetBoolean("remove_on_autoattack", true),
                            SaveRuleId = (BuffSaveRuleType)reader.GetUInt32("save_rule_id"),
                            AntiStealth = reader.GetBoolean("anti_stealth", true),
                            Scale = reader.GetFloat("scale"),
                            ScaleDuration = reader.GetFloat("scaleDuration"),
                            ImmuneExceptCreator = reader.GetBoolean("immune_except_creator", true),
                            ImmuneExceptSkillTagId = reader.GetUInt32("immune_except_skill_tag_id", 0),
                            FindSchoolOfFishRange = reader.GetFloat("find_school_of_fish_range"),
                            AnimActionId = reader.GetUInt32("anim_action_id", 0),
                            DeadApplicable = reader.GetBoolean("dead_applicable", true),
                            TickAreaUseOriginSource = reader.GetBoolean("tick_area_use_origin_source", true),
                            RealTime = reader.GetBoolean("real_time", true),
                            DoNotRemoveByOtherSkillController =
                                reader.GetBoolean("do_not_remove_by_other_skill_controller", true),
                            CooldownSkillId = reader.GetUInt32("cooldown_skill_id", 0),
                            CooldownSkillTime = reader.GetInt32("cooldown_skill_time"),
                            ManaBurnImmune = reader.GetBoolean("mana_burn_immune", true),
                            FreezeShip = reader.GetBoolean("freeze_ship", true),
                            CrowdFriendly = reader.GetBoolean("crowd_friendly", true),
                            CrowdHostile = reader.GetBoolean("crowd_hostile", true),
                        };

                        // _effects["Buff"].Add(template.Id, template);
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
                        template.AbLevel = reader.GetInt32("ab_level");
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
                            ActiveWeaponId = reader.GetByte("active_weapon_id"),
                            // TODO 1.2 // EndSkillId = reader.GetUInt32("end_skill_id")
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
                        var template = new BubbleEffect { Id = reader.GetUInt32("id"), KindId = reader.GetUInt32("kind_id") };
                        _effects["BubbleEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
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
                        var template = new DamageEffect
                        {
                            Id = reader.GetUInt32("id"),
                            DamageType = (DamageType)reader.GetInt32("damage_type_id"),
                            FixedMin = reader.GetInt32("fixed_min"),
                            FixedMax = reader.GetInt32("fixed_max"),
                            Multiplier = reader.GetFloat("multiplier"),
                            UseMainhandWeapon = reader.GetBoolean("use_mainhand_weapon", true),
                            UseOffhandWeapon = reader.GetBoolean("use_offhand_weapon", true),
                            UseRangedWeapon = reader.GetBoolean("use_ranged_weapon", true),
                            CriticalBonus = reader.GetInt32("critical_bonus"),
                            TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0),
                            TargetBuffBonus = reader.GetInt32("target_buff_bonus"),
                            UseFixedDamage = reader.GetBoolean("use_fixed_damage", true),
                            UseLevelDamage = reader.GetBoolean("use_level_damage", true),
                            LevelMd = reader.GetFloat("level_md"),
                            LevelVaStart = reader.GetInt32("level_va_start"),
                            LevelVaEnd = reader.GetInt32("level_va_end"),
                            TargetBuffBonusMul = reader.GetFloat("target_buff_bonus_mul"),
                            UseChargedBuff = reader.GetBoolean("use_charged_buff", true),
                            ChargedBuffId = reader.GetUInt32("charged_buff_id", 0),
                            ChargedMul = reader.GetFloat("charged_mul"),
                            AggroMultiplier = reader.GetFloat("aggro_multiplier"),
                            HealthStealRatio = reader.GetInt32("health_steal_ratio"),
                            ManaStealRatio = reader.GetInt32("mana_steal_ratio"),
                            DpsMultiplier = reader.GetFloat("dps_multiplier"),
                            WeaponSlotId = reader.GetInt32("weapon_slot_id"),
                            CheckCrime = reader.GetBoolean("check_crime", true),
                            HitAnimTimingId = reader.GetUInt32("hit_anim_timing_id"),
                            UseTargetChargedBuff = reader.GetBoolean("use_target_charged_buff", true),
                            TargetChargedBuffId = reader.GetUInt32("target_charged_buff_id", 0),
                            TargetChargedMul = reader.GetFloat("target_charged_mul"),
                            DpsIncMultiplier = reader.GetFloat("dps_inc_multiplier"),
                            EngageCombat = reader.GetBoolean("engage_combat", true),
                            Synergy = reader.GetBoolean("synergy", true),
                            ActabilityGroupId = reader.GetUInt32("actability_group_id", 0),
                            ActabilityStep = reader.GetInt32("actability_step"),
                            ActabilityMul = reader.GetFloat("actability_mul"),
                            ActabilityAdd = reader.GetFloat("actability_add"),
                            ChargedLevelMul = reader.GetFloat("charged_level_mul"),
                            AdjustDamageByHeight = reader.GetBoolean("adjust_damage_by_height", true),
                            UsePercentDamage = reader.GetBoolean("use_percent_damage", true),
                            PercentMin = reader.GetInt32("percent_min"),
                            PercentMax = reader.GetInt32("percent_max"),
                            UseCurrentHealth = reader.GetBoolean("use_current_health", true),
                            TargetHealthMin = reader.GetInt32("target_health_min"),
                            TargetHealthMax = reader.GetInt32("target_health_max"),
                            TargetHealthMul = reader.GetFloat("target_health_mul"),
                            TargetHealthAdd = reader.GetInt32("target_health_add"),
                            FireProc = reader.GetBoolean("fire_proc", true)
                        };
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
                            BuffTagId = reader.GetUInt32("buff_tag_id", 0)
                        };
                        _effects["DispelEffect"].Add(template.Id, template);
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
                            ActabilityAdd = reader.GetFloat("actability_add")
                        };
                        _effects["HealEffect"].Add(template.Id, template);
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
                            NeedPriest = reader.GetBoolean("need_priest", true)
                        };
                        // TODO 1.2 // template.Penaltied = reader.GetBoolean("penaltied", true);
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
                command.CommandText = "SELECT * FROM scoped_f_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ScopedFEffect
                        {
                            Id = reader.GetUInt32("id"), Range = reader.GetInt32("range"), Key = reader.GetBoolean("key", true),
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
                        var template = new SpawnEffect
                        {
                            Id = reader.GetUInt32("id"),
                            OwnerTypeId = (BaseUnitType)reader.GetUInt32("owner_type_id"),
                            SubType = reader.GetUInt32("sub_type"),
                            PosDirId = reader.GetUInt32("pos_dir_id"),
                            PosAngle = reader.GetFloat("pos_angle"),
                            PosDistance = reader.GetFloat("pos_distance"),
                            OriDirId = reader.GetUInt32("ori_dir_id"),
                            OriAngle = reader.GetFloat("ori_angle"),
                            UseSummonerFaction = reader.GetBoolean("use_summoner_faction", true),
                            LifeTime = reader.GetFloat("life_time"),
                            DespawnOnCreatorDeath = reader.GetBoolean("despawn_on_creator_death", true),
                            UseSummonerAggroTarget = reader.GetBoolean("use_summoner_aggro_target", true),
                            MateStateId = (MateState)reader.GetUInt32("mate_state_id", 0)
                        };
                        _effects["SpawnEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
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
                            ActivationState = reader.GetBoolean("activation_state", true)
                        };
                        _effects["NpcSpawnerSpawnEffect"].Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM npc_spawner_despawn_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new NpcSpawnerDespawnEffect { Id = reader.GetUInt32("id"), SpawnerId = reader.GetUInt32("spawner_id") };
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
                            Value4 = reader.GetInt32("value4")
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
                        var skillId = reader.GetUInt32("skill_id");
                        if (!_skills.ContainsKey(skillId))
                            continue;

                        var template = new SkillEffect();
                        var effectId = reader.GetUInt32("effect_id");

                        //for easier debugging
                        template.EffectId = effectId;

                        var type = _types[effectId];
                        if (_effects.TryGetValue(type.Type, out var effect))
                            template.Template = effect[type.ActualId];
                        template.Weight = reader.GetInt32("weight");
                        template.StartLevel = reader.GetByte("start_level");
                        template.EndLevel = reader.GetByte("end_level");
                        template.Friendly = reader.GetBoolean("friendly", true);
                        template.NonFriendly = reader.GetBoolean("non_friendly", true);
                        template.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                        template.TargetNoBuffTagId = reader.GetUInt32("target_nobuff_tag_id", 0);
                        template.SourceBuffTagId = reader.GetUInt32("source_buff_tag_id", 0);
                        template.SourceNoBuffTagId = reader.GetUInt32("source_nobuff_tag_id", 0);
                        template.Chance = reader.GetInt32("chance");
                        template.Front = reader.GetBoolean("front", true);
                        template.Back = reader.GetBoolean("back", true);
                        template.TargetNpcTagId = reader.GetUInt32("target_npc_tag_id", 0);
                        template.ApplicationMethod = (SkillEffectApplicationMethod)reader.GetUInt32("application_method_id");
                        template.ConsumeSourceItem = reader.GetBoolean("consume_source_item", true);
                        template.ConsumeItemId = reader.GetUInt32("consume_item_id", 0);
                        template.ConsumeItemCount = reader.GetInt32("consume_item_count");
                        template.AlwaysHit = reader.GetBoolean("always_hit", true);
                        template.ItemSetId = reader.GetUInt32("item_set_id", 0);
                        template.InteractionSuccessHit = reader.GetBoolean("interaction_success_hit", true);
                        _skills[skillId].Effects.Add(template);
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
                        var template = new SkillModifier
                        {
                            Id = reader.GetUInt32("id"),
                            OwnerId = reader.GetUInt32("owner_id"),
                            OwnerType = reader.GetString("owner_type"),
                            TagId = reader.GetUInt32("tag_id", 0),
                            SkillAttribute = (SkillAttribute)reader.GetUInt32("skill_attribute_id"),
                            UnitModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id"),
                            Value = reader.GetInt32("value"),
                            SkillId = reader.GetUInt32("skill_id", 0),
                            Synergy = reader.GetBoolean("synergy", true),
                        };

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

            using (var command = connection.CreateCommand())
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
                        var trigger = new BuffTriggerTemplate();
                        var buffId = reader.GetUInt32("buff_id");
                        if (!_buffTriggers.TryGetValue(buffId, out var value))
                        {
                            value = [];
                            _buffTriggers.Add(buffId, value);
                        }
                        trigger.Id = reader.GetUInt32("id");
                        trigger.Kind = (BuffEventTriggerKind)reader.GetUInt16("event_id");
                        trigger.Effect = GetEffectTemplate(reader.GetUInt32("effect_id"));
                        trigger.EffectOnSource = reader.GetBoolean("effect_on_source", true);
                        trigger.UseDamageAmount = reader.GetBoolean("use_damage_amount", true);
                        trigger.UseOriginalSource = reader.GetBoolean("use_original_source", true);
                        trigger.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                        trigger.TargetNoBuffTagId = reader.GetUInt32("target_no_buff_tag_id", 0);
                        trigger.Synergy = reader.GetBoolean("synergy", true);

                        // Apparently this is possible.
                        if (trigger.Effect != null)
                        {
                            value.Add(trigger);
                        }
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

        foreach (var skillTemplate in _skills.Values.Where(x => x.AutoLearn))
        {
            if (!skillTemplate.NeedLearn && skillTemplate.AbilityId == 0 &&
                !_defaultSkills.ContainsKey(skillTemplate.Id))
                _commonSkills.Add(skillTemplate.Id);
            if (!skillTemplate.NeedLearn || skillTemplate.AbilityId == 0 || skillTemplate.AbilityLevel > 1 ||
                !skillTemplate.Show)
                continue;
            var ability = skillTemplate.AbilityId;
            if (!_startAbilitySkills.ContainsKey(ability))
                _startAbilitySkills.Add(ability, []);
            _startAbilitySkills[ability].Add(skillTemplate);
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
