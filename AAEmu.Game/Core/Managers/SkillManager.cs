using AAEmu.Commons.Utils;
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

    // Initialized at declaration so a SkillManager.Load() failure (the 10.0.2.13 DB still surfaces
    // load errors) leaves these EMPTY rather than null — runtime Get*/lookup paths (e.g.
    // GetBuffTriggerTemplates during HousingManager.Create) must not NullRef and crash the server.
    private Dictionary<uint, SkillTemplate> _skills = [];
    private readonly Dictionary<string, uint> _constSkillTypes = [];
    private Dictionary<uint, DefaultSkill> _defaultSkills = [];
    private List<uint> _commonSkills = [];
    private Dictionary<AbilityType, List<SkillTemplate>> _startAbilitySkills = [];
    private Dictionary<uint, PassiveBuffTemplate> _passiveBuffs = [];
    private Dictionary<uint, EffectType> _types = [];
    private Dictionary<string, Dictionary<uint, EffectTemplate>> _effects = [];
    private Dictionary<uint, BuffTemplate> _buffs = [];
    private Dictionary<uint, List<uint>> _buffTags = [];
    private Dictionary<uint, List<uint>> _taggedBuffs = [];
    private Dictionary<uint, List<uint>> _skillTags = [];
    private Dictionary<uint, List<uint>> _taggedSkills = [];
    private Dictionary<uint, List<SkillModifier>> _skillModifiers = [];
    private Dictionary<uint, List<BuffTriggerTemplate>> _buffTriggers = [];
    private Dictionary<uint, List<CombatBuffTemplate>> _combatBuffs = [];
    private Dictionary<uint, LinearFuncTemplate> _linearFuncs = [];
    private Dictionary<uint, SkillReagent> _skillReagents = [];
    private Dictionary<uint, SkillProduct> _skillProducts = [];
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

    /// <summary>Skill id behind a <c>const_skill_types</c> name, or 0 when the constant is absent.</summary>
    public uint GetConstSkillId(string name)
    {
        return _constSkillTypes.GetValueOrDefault(name, 0u);
    }

    /// <summary>The skill the client uses to take a rider off whatever it is attached to.</summary>
    public bool IsDetachSkill(uint skillId)
    {
        var detachSkillId = GetConstSkillId("detached_unit");
        return detachSkillId != 0 && detachSkillId == skillId;
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
        // Not every skillset ships starter skills (e.g. the v10 skillsets past the 1.2 set), so a missing
        // key is normal — return an empty list instead of throwing.
        return _startAbilitySkills.TryGetValue(ability, out var skills) ? skills : [];
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

            if (_effects.TryGetValue(type.Type, out var effDict))
            {
                return effDict.TryGetValue(type.ActualId, out var effTmpl) ? effTmpl : null;
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
    /// <summary>
    /// Resolves an <c>np_skills</c> row to a castable skill.
    ///
    /// This used to drop every <see cref="SkillUseConditionKind.InCombat"/> entry that did not
    /// ignore the global cooldown or that carried a plot. That test arrived with the dungeon work
    /// in #790 and was inherited wholesale by #994 when NPC events moved off dungeons and onto all
    /// NPCs, at which point it silenced ordinary NPC combat kits: a sport fish has eight InCombat
    /// skills and the pair of clauses rejected all eight — 입질 (21608) and the basic attack for the
    /// cooldown clause, the five movement plots (21646/21647/21648/21098/21289) for the plot clause.
    /// The fish therefore spawned inert and plot 821 always timed out into 대어 소환 안됨.
    ///
    /// The null check also has to come first; the old order dereferenced the template before testing
    /// it, so an np_skills row naming a missing skill threw instead of being skipped.
    /// </summary>
    public Skill GetNpSkillTemplate(NpcSkill npcSkill)
    {
        var skillTemplate = GetSkillTemplate(npcSkill.SkillId);

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
            { "SkillController", [] }, // missing from the effect table
            { "SpawnFishEffect", [] }, // missing from the effect table
            { "ResetAoeDiminishingEffect", [] }, // missing from the effect table
            // Present in effects but previously unregistered, so GetEffectTemplate logged
            // "No such Effect Type" and returned null - the skill cast and did nothing at all.
            { "DoodadItemChangeEffect", [] },
            { "LevelUpEffect", [] },
            { "MoveToLocationEffect", [] },
            { "GainMerchantReopenPackItemEffect", [] },
            // v10 effect types loaded below from their compact.sqlite3 tables. WorldMessage/PlayLog apply for
            // real; CombatResource/ExtendCharge/SkillMap/CharTransform load their data with the behavior left
            // as a precise TODO (they depend on systems not yet modeled server-side).
            { "WorldMessageEffect", [] },
            { "PlayLogEffect", [] },
            { "CombatResourceEffect", [] },
            { "ExtendChargeEffect", [] },
            { "SkillMapEffect", [] },
            { "CharTransformEffect", [] }
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
                            Id = reader.GetUInt32("id", 0), Cost = reader.GetInt32("cost", 0), Show = reader.GetBoolean("show", true),
                            FireAnim = animationManager.GetAnimation(reader.GetUInt32("fire_anim_id", 0)),
                            AbilityId = (AbilityType)reader.GetByte("ability_id", 0),
                            ManaCost = reader.GetInt32("mana_cost", 0),
                            TimingId = reader.GetInt32("timing_id", 0),
                            CooldownTime = reader.GetInt32("cooldown_time", 0),
                            CastingTime = reader.GetInt32("casting_time", 0),
                            IgnoreGlobalCooldown = reader.GetBoolean("ignore_global_cooldown", true),
                            EffectDelay = reader.GetInt32("effect_delay", 0),
                            EffectSpeed = reader.GetFloat("effect_speed", 0f),
                            EffectRepeatCount = reader.GetInt32("effect_repeat_count", 0),
                            EffectRepeatTick = reader.GetInt32("effect_repeat_tick", 0),
                            ActiveWeaponId = reader.GetInt32("active_weapon_id", 0),
                            TargetType = (SkillTargetType)reader.GetInt32("target_type_id", 0),
                            TargetSelection = (SkillTargetSelection)reader.GetInt32("target_selection_id", 0),
                            TargetRelation = (SkillTargetRelation)reader.GetInt32("target_relation_id", 0),
                            TargetAreaCount = reader.GetInt32("target_area_count", 0),
                            TargetAreaRadius = reader.GetInt32("target_area_radius", 0),
                            WeaponSlotForAngleId = reader.GetInt32("weapon_slot_for_angle_id", 0),
                            TargetAngle = reader.GetInt32("target_angle", 0),
                            WeaponSlotForRangeId = reader.GetInt32("weapon_slot_for_range_id", 0),
                            WeaponSlotForAutoAttackId = reader.GetInt32("weapon_slot_for_autoattack_id", 0),
                            StartAutoAttack = reader.GetBoolean("start_autoattack", true),
                            StopAutoAttack = reader.GetBoolean("stop_autoattack", true),
                            MinRange = reader.GetInt32("min_range", 0),
                            MaxRange = reader.GetInt32("max_range", 0),
                            KeepStealth = reader.GetBoolean("keep_stealth", true),
                            Aggro = reader.GetInt32("aggro", 0),
                            ChannelingTime = reader.GetInt32("channeling_time", 0),
                            ChannelingTick = reader.GetInt32("channeling_tick", 0),
                            ChannelingMana = reader.GetInt32("channeling_mana", 0),
                            ChannelingTargetBuffId = reader.GetUInt32("channeling_target_buff_id", 0),
                            TargetAreaAngle = reader.GetInt32("target_area_angle", 0),
                            AbilityLevel = reader.GetInt32("ability_level", 0),
                            ChannelingDoodadId = reader.GetUInt32("channeling_doodad_id", 0)
                        };
                        template.CooldownTagId = reader.GetInt32("cooldown_tag_id", 0);
                        template.SkillControllerId = reader.GetUInt32("skill_controller_id", 0);
                        template.RepeatCount = reader.GetInt32("repeat_count", 0);
                        template.RepeatTick = reader.GetInt32("repeat_tick", 0);
                        template.ToggleBuffId = reader.GetUInt32("toggle_buff_id", 0);
                        template.TargetDead = reader.GetBoolean("target_dead", true);
                        template.ChannelingBuffId = reader.GetUInt32("channeling_buff_id", 0);
                        template.ReagentCorpseStatusId = reader.GetInt32("reagent_corpse_status_id", 0);
                        template.LevelStep = reader.GetInt32("level_step", 0);
                        template.ValidHeight = reader.GetFloat("valid_height", 0f);
                        template.TargetValidHeight = reader.GetFloat("target_valid_height", 0f);
                        template.StopCastingOnBigHit = reader.GetBoolean("stop_casting_on_big_hit", true);
                        template.StopChannelingOnBigHit = reader.GetBoolean("stop_channeling_on_big_hit", true);
                        template.AutoLearn = reader.GetBoolean("auto_learn", true);
                        template.MainhandToolId = reader.GetUInt32("mainhand_tool_id", 0);
                        template.OffhandToolId = reader.GetUInt32("offhand_tool_id", 0);
                        template.FrontAngle = reader.GetInt32("front_angle", 0);
                        template.ManaLevelMd = reader.GetFloat("mana_level_md", 0f);
                        template.Unmount = reader.GetBoolean("unmount", true);
                        template.DamageTypeId = reader.GetUInt32("damage_type_id", 0);
                        template.MilestoneId = reader.GetUInt32("milestone_id", 0);
                        template.MatchAnimation = reader.GetBoolean("match_animation", true);
                        template.Plot = reader.IsDBNull("plot_id") ? null : plotManager.GetPlot(reader.GetUInt32("plot_id", 0));
                        template.UseAnimTime = reader.GetBoolean("use_anim_time", true);
                        template.ConsumeLaborPower = reader.GetInt32("consume_lp", 0);
                        template.TargetAlive = reader.GetBoolean("target_alive", true);
                        template.TargetWater = reader.GetBoolean("target_water", true);
                        template.CastingInc = reader.GetInt32("casting_inc", 0);
                        template.CastingCancelable = reader.GetBoolean("casting_cancelable", true);
                        template.CastingDelayable = reader.GetBoolean("casting_delayable", true);
                        template.ChannelingCancelable = reader.GetBoolean("channeling_cancelable", true);
                        template.TargetOffsetAngle = reader.GetFloat("target_offset_angle", 0f);
                        template.TargetOffsetDistance = reader.GetFloat("target_offset_distance", 0f);
                        template.ActabilityGroupId = reader.GetInt32("actability_group_id", 0);
                        template.PlotOnly = reader.GetBoolean("plot_only", true);
                        template.SkillControllerAtEnd = reader.GetBoolean("skill_controller_at_end", true);
                        template.EndSkillController = reader.GetBoolean("end_skill_controller", true);
                        template.OrUnitReqs = reader.GetBoolean("or_unit_reqs", true);
                        template.DefaultGcd = reader.GetBoolean("default_gcd", true);
                        template.KeepManaRegen = reader.GetBoolean("keep_mana_regen", true);
                        template.CrimePoint = reader.GetInt32("crime_point", 0);
                        template.LevelRuleNoConsideration =
                            reader.GetBoolean("level_rule_no_consideration", true);
                        template.UseWeaponCooldownTime = reader.GetBoolean("use_weapon_cooldown_time", true);
                        template.CombatDiceId = reader.GetInt32("combat_dice_id", 0);
                        template.CustomGcd = reader.GetInt32("custom_gcd", 0);
                        template.CancelOngoingBuffs = reader.GetBoolean("cancel_ongoing_buffs", true);
                        template.CancelOngoingBuffExceptionTagId = reader.GetUInt32("cancel_ongoing_buff_exception_tag_id", 0);
                        template.CheckTerrain = reader.GetBoolean("check_terrain", true);
                        template.TargetOnlyWater = reader.GetBoolean("target_only_water", true);
                        template.TargetPreoccupied = reader.GetBoolean("target_preoccupied", true);
                        template.StopChannelingOnStartSkill =
                            reader.GetBoolean("stop_channeling_on_start_skill", true);
                        template.StopCastingByTurn = reader.GetBoolean("stop_casting_by_turn", true);
                        template.TargetMyNpc = reader.GetBoolean("target_my_npc", true);
                        template.GainLifePoint = reader.GetInt32("gain_life_point", 0);
                        template.TargetFishing = reader.GetBoolean("target_fishing", true);
                        template.AutoReUse = reader.GetBoolean("auto_reuse", true);
                        template.AutoReUseDelay = reader.GetInt32("auto_reuse_delay", 0);
                        template.SkillPoints = reader.GetInt32("skill_points", 0);
                        template.DoodadHitFamily = reader.GetInt32("doodad_hit_family", 0);
                        template.FirstReagentOnly = reader.GetBoolean("first_reagent_only", true);
                        template.TargetDecalRadius = reader.GetInt32("target_decal_radius", 0);
                        template.DoodadBundleId = reader.GetUInt32("doodad_bundle_id", 0);
                        template.SkipQuestApplyUseItem = reader.GetBoolean("skip_quest_apply_use_item", false);
                        template.CalcUserLevel = reader.GetBoolean("calc_user_level", false);
                        template.CastingUseable = reader.GetBoolean("casting_useable", false);
                        template.SkipValidateSource = reader.GetBoolean("skip_validate_source", false);
                        template.CharRaceId = reader.GetInt32("char_race_id", 0);
                        template.MaxCombatResource = reader.GetInt32("max_combat_resource", 0);
                        template.MinCombatResource = reader.GetInt32("min_combat_resource", 0);
                        template.AccountCooldown = reader.GetBoolean("account_cooldown", false);
                        template.SwitchToSkillCooldown = reader.GetBoolean("switch_to_skill_cooldown", false);
                        template.SecondCooldownTagId = reader.GetInt32("second_cooldown_tag_id", 0);
                        template.ThirdCooldownTagId = reader.GetInt32("third_cooldown_tag_id", 0);
                        template.IsDropableBackpack = reader.GetBoolean("is_dropable_backpack", false);
                        template.ChargeCount = reader.GetInt32("charge_count", 0);
                        template.ChargeCooldownTime = reader.GetInt32("charge_cooldown_time", 0);
                        template.PrecedenceSkillId = reader.GetUInt32("precedence_skill_id", 0);
                        template.Comments = reader.GetString("comments", "");
                        template.ReqPoints = reader.GetInt32("req_points", 0);
                        template.WeaponGcdId = reader.GetInt32("weapon_gcd_id", 0);
                        template.RandomUnitTargeting = reader.GetBoolean("random_unit_targeting", false);
                        template.TargetableStealth = reader.GetBoolean("targetable_stealth", false);
                        template.TargetUnitParam = reader.GetInt32("target_unit_param", 0);
                        template.ShotGunStartAnimId = reader.GetUInt32("shot_gun_start_anim_id", 0);
                        template.ShotGunFireAnimId = reader.GetUInt32("shot_gun_fire_anim_id", 0);
                        template.CombatResourceId = reader.GetInt32("combat_resource_id", 0);
                        template.UseInputDirection = reader.GetBoolean("use_input_direction", false);
                        template.UseConditionBits = reader.GetInt64("use_condition_bits", 0);
                        template.SkillLearnItemId = reader.GetUInt32("skill_learn_item_id", 0);
                        template.SkillLearnItemAmount = reader.GetInt32("skill_learn_item_amount", 0);
                        _skills[template.Id] = template; // 10.0.2.13 skills has duplicate ids (e.g. 33984) -> overwrite, don't crash
                    }
                }
            }

            Logger.Info($"Loaded {_skills.Count} skills");

            using (var command = connection.CreateCommand())
            {
                // Named skill constants. The client resolves actions like dismounting through these
                // rather than through skill effects, so "detached_unit" is the only thing that marks
                // 35837 as the skill that takes a rider off its parent.
                command.CommandText = "SELECT * FROM const_skill_types";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var name = reader.GetString("name", string.Empty);
                        if (!string.IsNullOrEmpty(name))
                            _constSkillTypes[name] = reader.GetUInt32("skill_id", 0);
                    }
                }
            }

            Logger.Info($"Loaded {_constSkillTypes.Count} skill type constants");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM default_skills";
                command.Prepare();
                using (var sqliteReader = command.ExecuteReader())
                using (var reader = new SQLiteWrapperReader(sqliteReader))
                {
                    while (reader.Read())
                    {
                        var id = (uint)reader.GetInt32("skill_id", 0);
                        if (!_skills.TryGetValue(id, out var defSkillTemplate))
                            continue; // 10.0.2.13: default_skills may reference a skill that didn't load
                        var skill = new DefaultSkill
                        {
                            Template = defSkillTemplate,
                            Slot = reader.GetByte("slot_index", 0),
                            AddToSlot = reader.GetBoolean("add_to_slot", true)
                        };
                        _defaultSkills[skill.Template.Id] = skill; // 10.0.2.13 default_skills has duplicate skill_ids (e.g. 33984) -> overwrite, don't crash
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
                            Id = reader.GetUInt32("id", 0),
                            AbilityId = (AbilityType)reader.GetByte("ability_id", 0),
                            Level = reader.GetByte("level", 0),
                            BuffId = reader.GetUInt32("buff_id", 0),
                            ReqPoints = reader.GetInt32("req_points", 0),
                            SkillPoints = reader.GetInt32("skill_points", 0),
                            Active = reader.GetBoolean("active", true)
                        };
                        _passiveBuffs[template.Id] = template;
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
                        var template = new BuffTemplate
                        {
                            Id = reader.GetUInt32("id", 0),
                            AnimStartId = reader.GetUInt32("anim_start_id", 0),
                            AnimEndId = reader.GetUInt32("anim_end_id", 0),
                            Duration = reader.GetInt32("duration", 0),
                            Tick = reader.GetDouble("tick"),
                            Silence = reader.GetBoolean("silence", true),
                            Root = reader.GetBoolean("root", true),
                            Sleep = reader.GetBoolean("sleep", true),
                            Stun = reader.GetBoolean("stun", true),
                            Cripled = reader.GetBoolean("crippled", true),
                            Stealth = reader.GetBoolean("stealth", true),
                            RemoveOnSourceDead = reader.GetBoolean("remove_on_source_dead", true),
                            LinkBuffId = reader.GetUInt32("link_buff_id", 0),
                            TickManaCost = reader.GetInt32("tick_mana_cost", 0),
                            StackRule = (BuffStackRule)reader.GetUInt32("stack_rule_id", 0),
                            InitMinCharge = reader.GetInt32("init_min_charge", 0),
                            InitMaxCharge = reader.GetInt32("init_max_charge", 0),
                            MaxStack = reader.GetInt32("max_stack", 0),
                            DamageAbsorptionTypeId = reader.GetUInt32("damage_absorption_type_id", 0),
                            DamageAbsorptionPerHit = reader.GetInt32("damage_absorption_per_hit", 0),
                            AuraRadius = reader.GetInt32("aura_radius", 0),
                            ManaShieldRatio = reader.GetInt32("mana_shield_ratio", 0),
                            FrameHold = reader.GetBoolean("framehold", true),
                            Ragdoll = reader.GetBoolean("ragdoll", true),
                            OneTime = reader.GetBoolean("one_time", true),
                            ReflectionChance = reader.GetInt32("reflection_chance", 0),
                            RequireBuffId = reader.GetUInt32("require_buff_id", 0),
                            Taunt = reader.GetBoolean("taunt", true),
                            TauntWithTopAggro = reader.GetBoolean("taunt_with_top_aggro", true),
                            RemoveOnUseSkill = reader.GetBoolean("remove_on_use_skill", true),
                            MeleeImmune = reader.GetBoolean("melee_immune", true),
                            SpellImmune = reader.GetBoolean("spell_immune", true),
                            RangedImmune = reader.GetBoolean("ranged_immune", true),
                            SiegeImmune = reader.GetBoolean("siege_immune", true),
                            ImmuneDamage = reader.GetInt32("immune_damage", 0),
                            SkillControllerId = reader.GetUInt32("skill_controller_id", 0),
                            ResurrectionHealth = reader.GetInt32("resurrection_health", 0),
                            ResurrectionMana = reader.GetInt32("resurrection_mana", 0),
                            ResurrectionPercent = reader.GetBoolean("resurrection_percent", true),
                            LevelDuration = reader.GetInt32("level_duration", 0),
                            ReflectionRatio = reader.GetInt32("reflection_ratio", 0),
                            ReflectionTargetRatio = reader.GetInt32("reflection_target_ratio", 0),
                            KnockbackImmune = reader.GetBoolean("knockback_immune"),
                            AuraRelationId = reader.GetUInt32("aura_relation_id", 0),
                            GroupId = reader.GetUInt32("group_id", 0),
                            GroupRank = reader.GetInt32("group_rank", 0),
                            PerUnitCreation = reader.GetBoolean("per_unit_creation"),
                            TickAreaRadius = reader.GetFloat("tick_area_radius", 0f),
                            TickAreaRelationId = reader.GetUInt32("tick_area_relation_id", 0),
                            RemoveOnMove = reader.GetBoolean("remove_on_move", true),
                            UseSourceFaction = reader.GetBoolean("use_source_faction", true),
                            FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0),
                            Exempt = reader.GetBoolean("exempt", true),
                            TickAreaFrontAngle = reader.GetInt32("tick_area_front_angle", 0),
                            TickAreaAngle = reader.GetInt32("tick_area_angle", 0),
                            Psychokinesis = reader.GetBoolean("psychokinesis", true),
                            NoCollide = reader.GetBoolean("no_collide", true),
                            PsychokinesisSpeed = reader.GetFloat("psychokinesis_speed", 0f),
                            RemoveOnDeath = reader.GetBoolean("remove_on_death", true),
                            TickAnimId = reader.GetUInt32("tick_anim_id", 0),
                            TickActiveWeaponId = reader.GetUInt32("tick_active_weapon_id", 0),
                            ConditionalTick = reader.GetBoolean("conditional_tick", true),
                            System = reader.GetBoolean("system", true),
                            AuraSlaveBuffId = reader.GetUInt32("aura_slave_buff_id", 0),
                            NonPushable = reader.GetBoolean("non_pushable", true),
                            ActiveWeaponId = reader.GetUInt32("active_weapon_id", 0),
                            MaxCharge = reader.GetInt32("max_charge", 0),
                            DetectStealth = reader.GetBoolean("detect_stealth", true),
                            RemoveOnExempt = reader.GetBoolean("remove_on_exempt", true),
                            RemoveOnLand = reader.GetBoolean("remove_on_land", true),
                            Gliding = reader.GetBoolean("gliding", true),
                            GlidingRotateSpeed = reader.GetInt32("gliding_rotate_speed", 0),
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
                            Kind = (BuffKind)reader.GetInt32("kind_id", 0),
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
                            TelescopeRange = reader.GetFloat("telescope_range", 0f),
                            MainhandToolId = reader.GetUInt32("mainhand_tool_id", 0),
                            OffhandToolId = reader.GetUInt32("offhand_tool_id", 0),
                            TickMainhandToolId = reader.GetUInt32("tick_mainhand_tool_id", 0),
                            TickOffhandToolId = reader.GetUInt32("tick_offhand_tool_id", 0),
                            TickLevelManaCost = reader.GetFloat("tick_level_mana_cost", 0f),
                            WalkOnly = reader.GetBoolean("walk_only", true),
                            CannotJump = reader.GetBoolean("cannot_jump", true),
                            CrowdBuffId = reader.GetUInt32("crowd_buff_id", 0),
                            CrowdRadius = reader.GetFloat("crowd_radius", 0f),
                            CrowdNumber = reader.GetInt32("crowd_number", 0),
                            EvadeTelescope = reader.GetBoolean("evade_telescope", true),
                            TransferTelescopeRange = reader.GetFloat("transfer_telescope_range", 0f),
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
                            SaveRuleId = (BuffSaveRuleType)reader.GetUInt32("save_rule_id", 0),
                            AntiStealth = reader.GetBoolean("anti_stealth", true),
                            Scale = reader.GetFloat("scale", 0f),
                            ScaleDuration = reader.GetFloat("scaleDuration", 0f),
                            ImmuneExceptCreator = reader.GetBoolean("immune_except_creator", true),
                            ImmuneExceptSkillTagId = reader.GetUInt32("immune_except_skill_tag_id", 0),
                            FindSchoolOfFishRange = reader.GetFloat("find_school_of_fish_range", 0f),
                            AnimActionId = reader.GetUInt32("anim_action_id", 0),
                            DeadApplicable = reader.GetBoolean("dead_applicable", true),
                            TickAreaUseOriginSource = reader.GetBoolean("tick_area_use_origin_source", true),
                            RealTime = reader.GetBoolean("real_time", true),
                            DoNotRemoveByOtherSkillController =
                                reader.GetBoolean("do_not_remove_by_other_skill_controller", true),
                            CooldownSkillId = reader.GetUInt32("cooldown_skill_id", 0),
                            CooldownSkillTime = reader.GetInt32("cooldown_skill_time", 0),
                            ManaBurnImmune = reader.GetBoolean("mana_burn_immune", true),
                            FreezeShip = reader.GetBoolean("freeze_ship", true),
                            CrowdFriendly = reader.GetBoolean("crowd_friendly", true),
                            CrowdHostile = reader.GetBoolean("crowd_hostile", true),
                            NoExpPenalty = reader.GetBoolean("no_exp_penalty", false),
                            AuraCreatorOnly = reader.GetBoolean("aura_creator_only", false),
                            NotToSlaveRider = reader.GetBoolean("not_to_slave_rider", false),
                            RemoveOnUnmountAttachPointId = reader.GetInt32("remove_on_unmount_attach_point_id", 0),
                            StopOnlineLpRegen = reader.GetBoolean("stop_online_lp_regen", false),
                            RemoveOnUnbond = reader.GetBoolean("remove_on_unbond", false),
                            BossTelescopeRange = reader.GetFloat("boss_telescope_range", 0f),
                            FixAbilityLevelToOne = reader.GetBoolean("fix_ability_level_to_one", false),
                            ImmuneHealth = reader.GetFloat("immune_health", 0f),
                            MaxLifeTime = reader.GetInt32("max_life_time", 0),
                            BalanceLevel = reader.GetInt32("balance_level", 0),
                            DisarmamentMainHand = reader.GetBoolean("disarmament_main_hand", false),
                            DisarmamentOffHand = reader.GetBoolean("disarmament_off_hand", false),
                            DisarmamentRanged = reader.GetBoolean("disarmament_ranged", false),
                            DisarmamentMusical = reader.GetBoolean("disarmament_musical", false),
                            MeleeImmortality = reader.GetBoolean("melee_immortality", false),
                            SpellImmortality = reader.GetBoolean("spell_immortality", false),
                            RangedImmortality = reader.GetBoolean("ranged_immortality", false),
                            SiegeImmortality = reader.GetBoolean("siege_immortality", false),
                            FallDamageImmortality = reader.GetBoolean("fall_damage_immortality", false),
                            OneTimeImmortality = reader.GetBoolean("one_time_immortality", false),
                            AddDurationBuffId = reader.GetUInt32("add_duration_buff_id", 0),
                            AddDurationBuffMul = reader.GetInt32("add_duration_buff_mul", 0),
                            OffPassive = reader.GetBoolean("off_passive", false),
                            OffPassiveExecutionTagId = reader.GetUInt32("off_passive_exection_tag_id", 0),
                            MaxCombatResource = reader.GetInt32("max_combat_resource", 0),
                            MinCombatResource = reader.GetInt32("min_combat_resource", 0),
                            RestrictActionbar = reader.GetBoolean("restrict_actionbar", false),
                            ImmuneExceptCreatorRelationCheck = reader.GetBoolean("immune_except_creator_relation_check", false),
                            ImmuneExceptCreatorRelationId = reader.GetUInt32("immune_except_creator_relation_id", 0),
                            ImpossibleTargeting = reader.GetBoolean("impossible_targeting", false),
                            ImpossibleChangeTargeting = reader.GetBoolean("impossible_change_targeting", false),
                            TargetingRelationId = reader.GetUInt32("targeting_relation_id", 0),
                            TargetingUseOriginSource = reader.GetBoolean("targeting_use_origin_source", false),
                            OnlyMyPet = reader.GetBoolean("only_my_pet", false),
                            OnlyPetOwner = reader.GetBoolean("only_pet_owner", false),
                            ImpossibleRotate = reader.GetBoolean("impossible_rotate", false),
                            SetHeadScale = reader.GetBoolean("set_head_scale", false),
                            HeadScale = reader.GetFloat("head_scale", 0f),
                            NotToMateRider = reader.GetBoolean("not_to_mate_rider", false),
                            DrowningImmortality = reader.GetBoolean("drowning_immortality", false),
                            CrowdCheckOwner = reader.GetBoolean("crowd_check_owner", false),
                            CrowdCheckBuffTagId = reader.GetUInt32("crowd_check_buff_tag_id", 0),
                            CrowdCheckBuffId = reader.GetUInt32("crowd_check_buff_id", 0),
                            RemoveBySummoned = reader.GetBoolean("remove_by_summoned", false),
                            CooldownSkillTagId = reader.GetUInt32("cooldown_skill_tag_id", 0),
                            AliveNotApplicable = reader.GetBoolean("alive_not_applicable", false),
                            AuraMaxCount = reader.GetInt32("aura_max_count", 0),
                            TickAreaMaxCount = reader.GetInt32("tick_area_max_count", 0),
                            MilestoneId = reader.GetUInt32("milestone_id", 0),
                            Comments = reader.GetString("comments", ""),
                            ReflectionMelee = reader.GetBoolean("reflection_melee", false),
                            ReflectionSpell = reader.GetBoolean("reflection_spell", false),
                            ReflectionSiege = reader.GetBoolean("reflection_siege", false),
                            ReflectionRanged = reader.GetBoolean("reflection_ranged", false),
                            ReflectionHeal = reader.GetBoolean("reflection_heal", false),
                            ReflectionIgnoreAttacker = reader.GetBoolean("reflection_ignore_attacker", false),
                            ReflectionIgnoreDefender = reader.GetBoolean("reflection_ignore_defender", false),
                            SavePos = reader.GetBoolean("save_pos", false),
                            Transparent = reader.GetBoolean("transparent", false),
                            CombatResourceId = reader.GetInt32("combat_resource_id", 0),
                            RemoveOnChangeEquipments = reader.GetInt64("remove_on_change_equipments", 0),
                            IpnirFx = reader.GetBoolean("ipnir_fx", false),
                            CollidePushable = reader.GetBoolean("collide_pushable", false),
                        };

                        // _effects["Buff"][template.Id] = template;
                        _buffs[template.Id] = template;
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
                        var template = new BuffEffect { Id = reader.GetUInt32("id", 0) };
                        var buffId = reader.GetUInt32("buff_id", 0);
                        if (_buffs.TryGetValue(buffId, out var buff))
                            template.Buff = buff;
                        template.Chance = reader.GetInt32("chance", 0);
                        template.Stack = reader.GetInt32("stack", 0);
                        template.AbLevel = reader.GetInt32("ab_level", 0);
                        _effects["BuffEffect"][template.Id] = template;
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
                        var buffId = reader.GetUInt32("buff_id", 0);
                        if (!_buffs.TryGetValue(buffId, out var template))
                            continue; // 10.0.2.13: buff_tick_effects may reference a buff that didn't load
                        var tickEffect = new TickEffect
                        {
                            EffectId = reader.GetUInt32("effect_id", 0), TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0),
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
                        var buffId = reader.GetUInt32("owner_id", 0);
                        if (!_buffs.TryGetValue(buffId, out var buff))
                            continue;
                        var template = new BonusTemplate
                        {
                            Attribute = (UnitAttribute)reader.GetUInt32("unit_attribute_id", 0), ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id", 0),
                            Value = reader.GetInt64("value", 0),
                            LinearLevelBonus = reader.GetInt32("linear_level_bonus", 0)
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
                            Id = reader.GetUInt32("id", 0),
                            StartValue = reader.GetInt32("start_value", 0),
                            EndValue = reader.GetInt32("end_value", 0)
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
                        var buffId = reader.GetUInt32("buff_id", 0);
                        if (!_buffs.TryGetValue(buffId, out var buff))
                            continue;
                        var template = new DynamicBonusTemplate
                        {
                            Attribute = (UnitAttribute)reader.GetUInt32("unit_attribute_id", 0), ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id", 0),
                            FuncId = reader.GetUInt32("func_id", 0),
                            FuncType = reader.GetString("func_type", "")
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
                            Id = reader.GetUInt32("id", 0),
                            KindId = reader.GetUInt32("kind_id", 0),
                            ActiveWeaponId = reader.GetByte("active_weapon_id", 0),
                            EndSkillId = reader.GetUInt32("end_skill_id", 0)
                        };
                        for (var i = 0; i < 15; i++)
                            template.Value[i] = reader.GetInt32($"value{i + 1}", 0);
                        _effects["SkillController"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), KindId = reader.GetUInt32("kind_id", 0), BindWorld = reader.GetBoolean("bind_world"),
                            IsAdd = reader.GetBoolean("is_add"),
                            Count = reader.GetUInt32("count", 0),
                            Time = reader.GetUInt32("time", 0),
                            KindValue = reader.GetUInt32("kind_value", 0)
                        };
                        _effects["AccountAttributeEffect"][template.Id] = template;
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
                        var template = new AcceptQuestEffect { Id = reader.GetUInt32("id", 0), QuestId = reader.GetUInt32("quest_id", 0) };
                        _effects["AcceptQuestEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            UseFixedAggro = reader.GetBoolean("use_fixed_aggro", true),
                            FixedMin = reader.GetInt32("fixed_min", 0),
                            FixedMax = reader.GetInt32("fixed_max", 0),
                            UseLevelAggro = reader.GetBoolean("use_level_aggro", true),
                            LevelMd = reader.GetFloat("level_md", 0f),
                            LevelVaStart = reader.GetInt32("level_va_start", 0),
                            LevelVaEnd = reader.GetInt32("level_va_end", 0),
                            UseChargedBuff = reader.GetBoolean("use_charged_buff", true),
                            ChargedBuffId = reader.GetUInt32("charged_buff_id", 0),
                            ChargedMul = reader.GetFloat("charged_mul", 0f)
                        };
                        _effects["AggroEffect"][template.Id] = template;
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
                        var template = new BubbleEffect { Id = reader.GetUInt32("id", 0), KindId = reader.GetUInt32("kind_id", 0) };
                        _effects["BubbleEffect"][template.Id] = template;
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
                        var template = new CinemalEffect { Id = reader.GetUInt32("id", 0), CinemaId = reader.GetUInt32("cinema_id", 0) };
                        _effects["CinemaEffect"][template.Id] = template;
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
                        var template = new CleanupUccEffect { Id = reader.GetUInt32("id", 0) };
                        _effects["CleanupUccEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            CategoryId = reader.GetUInt32("category_id", 0),
                            SourceCategoryId = reader.GetUInt32("source_category_id", 0),
                            SourceValue = reader.GetInt32("source_value", 0),
                            TargetCategoryId = reader.GetUInt32("target_category_id", 0),
                            TargetValue = reader.GetInt32("target_value", 0)
                        };
                        _effects["ConversionEffect"][template.Id] = template;
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
                        var template = new CraftEffect { Id = reader.GetUInt32("id", 0), WorldInteraction = (WorldInteractionType)reader.GetUInt32("wi_id", 0) };
                        _effects["CraftEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            DamageType = (DamageType)reader.GetInt32("damage_type_id", 0),
                            FixedMin = reader.GetInt32("fixed_min", 0),
                            FixedMax = reader.GetInt32("fixed_max", 0),
                            Multiplier = reader.GetFloat("multiplier", 0f),
                            UseMainhandWeapon = reader.GetBoolean("use_mainhand_weapon", true),
                            UseOffhandWeapon = reader.GetBoolean("use_offhand_weapon", true),
                            UseRangedWeapon = reader.GetBoolean("use_ranged_weapon", true),
                            CriticalBonus = reader.GetInt32("critical_bonus", 0),
                            TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0),
                            TargetBuffBonus = reader.GetInt32("target_buff_bonus", 0),
                            UseFixedDamage = reader.GetBoolean("use_fixed_damage", true),
                            UseLevelDamage = reader.GetBoolean("use_level_damage", true),
                            LevelMd = reader.GetFloat("level_md", 0f),
                            LevelVaStart = reader.GetInt32("level_va_start", 0),
                            LevelVaEnd = reader.GetInt32("level_va_end", 0),
                            TargetBuffBonusMul = reader.GetFloat("target_buff_bonus_mul", 0f),
                            UseChargedBuff = reader.GetBoolean("use_charged_buff", true),
                            ChargedBuffId = reader.GetUInt32("charged_buff_id", 0),
                            ChargedMul = reader.GetFloat("charged_mul", 0f),
                            AggroMultiplier = reader.GetFloat("aggro_multiplier", 0f),
                            HealthStealRatio = reader.GetInt32("health_steal_ratio", 0),
                            ManaStealRatio = reader.GetInt32("mana_steal_ratio", 0),
                            DpsMultiplier = reader.GetFloat("dps_multiplier", 0f),
                            WeaponSlotId = reader.GetInt32("weapon_slot_id", 0),
                            // check_crime renamed to crime in 10.0.2.13 schema
                            CheckCrime = reader.GetBoolean("crime", true),
                            HitAnimTimingId = reader.GetUInt32("hit_anim_timing_id", 0),
                            UseTargetChargedBuff = reader.GetBoolean("use_target_charged_buff", true),
                            TargetChargedBuffId = reader.GetUInt32("target_charged_buff_id", 0),
                            TargetChargedMul = reader.GetFloat("target_charged_mul", 0f),
                            DpsIncMultiplier = reader.GetFloat("dps_inc_multiplier", 0f),
                            EngageCombat = reader.GetBoolean("engage_combat", true),
                            Synergy = reader.GetBoolean("synergy", true),
                            ActabilityGroupId = reader.GetUInt32("actability_group_id", 0),
                            ActabilityStep = reader.GetInt32("actability_step", 0),
                            ActabilityMul = reader.GetFloat("actability_mul", 0f),
                            ActabilityAdd = reader.GetFloat("actability_add", 0f),
                            ChargedLevelMul = reader.GetFloat("charged_level_mul", 0f),
                            AdjustDamageByHeight = reader.GetBoolean("adjust_damage_by_height", true),
                            UsePercentDamage = reader.GetBoolean("use_percent_damage", true),
                            PercentMin = reader.GetInt32("percent_min", 0),
                            PercentMax = reader.GetInt32("percent_max", 0),
                            // use_current_health renamed to use_source_health in 10.0.2.13 schema
                            UseCurrentHealth = reader.GetBoolean("use_source_health", true),
                            TargetHealthMin = reader.GetInt32("target_health_min", 0),
                            TargetHealthMax = reader.GetInt32("target_health_max", 0),
                            TargetHealthMul = reader.GetFloat("target_health_mul", 0f),
                            TargetHealthAdd = reader.GetInt32("target_health_add", 0),
                            FireProc = reader.GetBoolean("fire_proc", true)
                        };
                        _effects["DamageEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            DispelCount = reader.GetInt32("dispel_count", 0),
                            CureCount = reader.GetInt32("cure_count", 0),
                            BuffTagId = reader.GetUInt32("buff_tag_id", 0)
                        };
                        _effects["DispelEffect"][template.Id] = template;
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
                        var template = new FlyingStateChangeEffect { Id = reader.GetUInt32("id", 0), FlyingState = reader.GetBoolean("flying_state", true) };
                        _effects["FlyingStateChangeEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            ZoneGroupOnly = reader.GetBoolean("zone_group_only", false),
                            Message = reader.GetString("message", ""),
                            ZoneGroupWarState = reader.GetBoolean("zone_group_war_state", false),
                            FactionScopeId = reader.GetInt32("faction_scope_id", 0),
                            KillStreakCount = reader.GetInt32("kill_streak_count", 0),
                            KillHero = reader.GetBoolean("kill_hero", false),
                            IconKey = reader.GetString("icon_key", ""),
                            ChatMsg = reader.GetBoolean("chat_msg", false),
                            NameWithForeignWorld = reader.GetBoolean("name_with_foreign_world", false)
                        };
                        _effects["WorldMessageEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            Message = reader.GetString("message", "")
                        };
                        _effects["PlayLogEffect"][template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM combat_resource_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CombatResourceEffect
                        {
                            Id = reader.GetUInt32("id", 0),
                            MinCombatResource = reader.GetInt32("min_combat_resource", 0),
                            MaxCombatResource = reader.GetInt32("max_combat_resource", 0),
                            CombatResourceId = reader.GetInt32("combat_resource_id", 0),
                            Chance = reader.GetInt32("chance", 0),
                            ResetRemainTime = reader.GetBoolean("reset_remain_time", false)
                        };
                        _effects["CombatResourceEffect"][template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM extend_charge_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new ExtendChargeEffect
                        {
                            Id = reader.GetUInt32("id", 0),
                            DamageTypeId = reader.GetInt32("damage_type_id", 0),
                            UseFixedCharge = reader.GetBoolean("use_fixed_charge", false),
                            FixedMin = reader.GetInt32("fixed_min", 0),
                            FixedMax = reader.GetInt32("fixed_max", 0),
                            UsePercentCharge = reader.GetBoolean("use_percent_charge", false),
                            PercentMin = reader.GetInt32("percent_min", 0),
                            PercentMax = reader.GetInt32("percent_max", 0),
                            UseLevelCharge = reader.GetBoolean("use_level_charge", false),
                            LevelMd = reader.GetFloat("level_md", 0f),
                            LevelVaStart = reader.GetInt32("level_va_start", 0),
                            LevelVaEnd = reader.GetInt32("level_va_end", 0),
                            UseDpsCharge = reader.GetBoolean("use_dps_charge", false),
                            DpsIncMultiplier = reader.GetFloat("dps_inc_multiplier", 0f),
                            UseMainhandWeapon = reader.GetBoolean("use_mainhand_weapon", false),
                            UseOffhandWeapon = reader.GetBoolean("use_offhand_weapon", false),
                            UseRangedWeapon = reader.GetBoolean("use_ranged_weapon", false),
                            DpsMultiplier = reader.GetFloat("dps_multiplier", 0f),
                            ChargeBuffId = reader.GetInt32("charge_buff_id", 0),
                            PercentDamageResourceTypeId = reader.GetInt32("percent_damage_resource_type_id", 0),
                            UseSourceHealth = reader.GetBoolean("use_source_health", false)
                        };
                        _effects["ExtendChargeEffect"][template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skill_map_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new SkillMapEffect
                        {
                            Id = reader.GetUInt32("id", 0),
                            ViewTime = reader.GetInt32("view_time", 0),
                            UseFactionColor = reader.GetBoolean("use_faction_color", false),
                            UseUiEffect = reader.GetBoolean("use_ui_effect", false),
                            Radius = reader.GetInt32("radius", 0),
                            TexturePath = reader.GetString("texture_path", ""),
                            TextureKey = reader.GetString("texture_key", ""),
                            TextureColorKey = reader.GetString("texture_color_key", "")
                        };
                        _effects["SkillMapEffect"][template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM char_transform_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new CharTransformEffect
                        {
                            Id = reader.GetUInt32("id", 0),
                            CharRaceId = reader.GetInt32("char_race_id", 0),
                            CharGenderId = reader.GetInt32("char_gender_id", 0),
                            IsTransform = reader.GetBoolean("is_transform", false)
                        };
                        _effects["CharTransformEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            LootPackId = reader.GetUInt32("loot_pack_id", 0),
                            ConsumeSourceItem = reader.GetBoolean("consume_source_item", true),
                            ConsumeItemId = reader.GetUInt32("consume_item_id", 0),
                            ConsumeCount = reader.GetInt32("consume_count", 0),
                            InheritGrade = reader.GetBoolean("inherit_grade", true)
                        };
                        _effects["GainLootPackItemEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            UseFixedHeal = reader.GetBoolean("use_fixed_heal", true),
                            FixedMin = reader.GetInt32("fixed_min", 0),
                            FixedMax = reader.GetInt32("fixed_max", 0),
                            UseLevelHeal = reader.GetBoolean("use_level_heal", true),
                            LevelMd = reader.GetFloat("level_md", 0f),
                            LevelVaStart = reader.GetInt32("level_va_start", 0),
                            LevelVaEnd = reader.GetInt32("level_va_end", 0),
                            Percent = reader.GetBoolean("percent", true),
                            UseChargedBuff = reader.GetBoolean("use_charged_buff", true),
                            ChargedBuffId = reader.GetUInt32("charged_buff_id", 0),
                            ChargedMul = reader.GetFloat("charged_mul", 0f),
                            SlaveApplicable = reader.GetBoolean("slave_applicable", true),
                            IgnoreHealAggro = reader.GetBoolean("ignore_heal_aggro", true),
                            DpsMultiplier = reader.GetFloat("dps_multiplier", 0f),
                            ActabilityGroupId = reader.GetUInt32("actability_group_id", 0),
                            ActabilityStep = reader.GetInt32("actability_step", 0),
                            ActabilityMul = reader.GetFloat("actability_mul", 0f),
                            ActabilityAdd = reader.GetFloat("actability_add", 0f)
                        };
                        _effects["HealEffect"][template.Id] = template;
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
                        var template = new ImprintUccEffect { Id = reader.GetUInt32("id", 0), ItemId = reader.GetUInt32("item_id", 0) };
                        _effects["ImprintUccEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            VelImpulseX = reader.GetFloat("vel_impulse_x", 0f),
                            VelImpulseY = reader.GetFloat("vel_impulse_y", 0f),
                            VelImpulseZ = reader.GetFloat("vel_impulse_z", 0f),
                            AngvelImpulseX = reader.GetFloat("angvel_impulse_x", 0f),
                            AngvelImpulseY = reader.GetFloat("angvel_impulse_y", 0f),
                            AngvelImpulseZ = reader.GetFloat("angvel_impulse_z", 0f),
                            ImpulseX = reader.GetFloat("impulse_x", 0f),
                            ImpulseY = reader.GetFloat("impulse_y", 0f),
                            ImpulseZ = reader.GetFloat("impulse_z", 0f),
                            AngImpulseX = reader.GetFloat("ang_impulse_x", 0f),
                            AngImpulseY = reader.GetFloat("ang_impulse_y", 0f),
                            AngImpulseZ = reader.GetFloat("ang_impulse_z", 0f)
                        };
                        _effects["ImpulseEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            WorldInteraction = (WorldInteractionType)reader.GetInt32("wi_id", 0),
                            DoodadId = reader.GetUInt32("doodad_id", 0)
                        };
                        _effects["InteractionEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), NpcId = reader.GetUInt32("npc_id", 0), Radius = reader.GetFloat("radius", 0f),
                            GiveExp = reader.GetBoolean("give_exp", true),
                            Vanish = reader.GetBoolean("vanish", true)
                        };
                        _effects["KillNpcWithoutCorpseEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), BaseMin = reader.GetInt32("base_min", 0), BaseMax = reader.GetInt32("base_max", 0),
                            DamageRatio = reader.GetInt32("damage_ratio", 0),
                            LevelMd = reader.GetFloat("level_md", 0f),
                            LevelVaStart = reader.GetInt32("level_va_start", 0),
                            LevelVaEnd = reader.GetInt32("level_va_end", 0)
                        };
                        _effects["ManaBurnEffect"][template.Id] = template;
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
                        var template = new MoveToRezPointEffect { Id = reader.GetUInt32("id", 0) };
                        _effects["MoveToRezPointEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            CategoryId = (NpcControlCategory)reader.GetUInt32("category_id", 0),
                            ParamString = reader.GetString("param_string", ""),
                            ParamInt = reader.GetUInt32("param_int", 0)
                        };
                        _effects["NpcControlEffect"][template.Id] = template;
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
                        var template = new OpenPortalEffect { Id = reader.GetUInt32("id", 0), Distance = reader.GetFloat("distance", 0f) };
                        _effects["OpenPortalEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), Radius = reader.GetFloat("radius", 0f), HoleSize = reader.GetFloat("hole_size", 0f),
                            Pressure = reader.GetFloat("pressure", 0f)
                        };
                        _effects["PhysicalExplosionEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), BackpackDoodadId = reader.GetUInt32("backpack_doodad_id", 0)
                        };
                        _effects["PutDownBackpackEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            NeedMoney = reader.GetBoolean("need_money", true),
                            NeedLaborPower = reader.GetBoolean("need_labor_power", true),
                            NeedPriest = reader.GetBoolean("need_priest", true)
                        };
                        template.Penaltied = reader.GetBoolean("penaltied", true);
                        _effects["RecoverExpEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), Health = reader.GetInt32("health", 0), Mana = reader.GetInt32("mana", 0)
                        };
                        _effects["RepairSlaveEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), Value = reader.GetInt32("value", 0), CrimeKindId = reader.GetUInt32("crime_kind_id", 0)
                        };
                        _effects["ReportCrimeEffect"][template.Id] = template;
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
                        var template = new ResetAoeDiminishingEffect { Id = reader.GetUInt32("id", 0) };
                        _effects["ResetAoeDiminishingEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            UseFixedValue = reader.GetBoolean("use_fixed_value", true),
                            FixedMin = reader.GetInt32("fixed_min", 0),
                            FixedMax = reader.GetInt32("fixed_max", 0),
                            UseLevelValue = reader.GetBoolean("use_level_value", true),
                            LevelMd = reader.GetFloat("level_md", 0f),
                            LevelVaStart = reader.GetInt32("level_va_start", 0),
                            LevelVaEnd = reader.GetInt32("level_va_end", 0),
                            Percent = reader.GetBoolean("percent", true)
                        };
                        _effects["RestoreManaEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), Range = reader.GetInt32("range", 0), Key = reader.GetString("key", ""),
                            DoodadId = reader.GetUInt32("doodad_id", 0)
                        };
                        _effects["ScopedFEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            OwnerTypeId = (BaseUnitType)reader.GetUInt32("owner_type_id", 0),
                            SubType = reader.GetUInt32("sub_type", 0),
                            PosDirId = reader.GetUInt32("pos_dir_id", 0),
                            // pos_angle/pos_distance split into _min/_max in 10.0.2.13 schema; use _min
                            PosAngle = reader.GetFloat("pos_angle_min", 0f),
                            PosDistance = reader.GetFloat("pos_distance_min", 0f),
                            OriDirId = reader.GetUInt32("ori_dir_id", 0),
                            OriAngle = reader.GetFloat("ori_angle", 0f),
                            UseSummonerFaction = reader.GetBoolean("use_summoner_faction", true),
                            LifeTime = reader.GetFloat("life_time", 0f),
                            DespawnOnCreatorDeath = reader.GetBoolean("despawn_on_creator_death", true),
                            UseSummonerAggroTarget = reader.GetBoolean("use_summoner_aggro_target", true),
                            MateStateId = (MateState)reader.GetUInt32("mate_state_id", 0),
                            // Crimson 963/969: ray-cast land under the high portal XY.
                            EnableRayCast = reader.GetBoolean("enable_ray_cast", true)
                        };
                        _effects["SpawnEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            SpawnerId = reader.GetUInt32("spawner_id", 0),
                            LifeTime = reader.GetFloat("life_time", 0f),
                            DespawnOnCreatorDeath = reader.GetBoolean("despawn_on_creator_death", true),
                            UseSummonerAggroTarget = reader.GetBoolean("use_summoner_aggro_target", true),
                            ActivationState = reader.GetBoolean("activation_state", true)
                        };
                        _effects["NpcSpawnerSpawnEffect"][template.Id] = template;
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
                        var template = new NpcSpawnerDespawnEffect { Id = reader.GetUInt32("id", 0), SpawnerId = reader.GetUInt32("spawner_id", 0) };
                        _effects["NpcSpawnerDespawnEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), Idx = reader.GetInt32("idx", 0)
                        };
                        _effects["DoodadItemChangeEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            Level = reader.GetInt32("level", 1),
                            ApplyAllAbilities = reader.GetBoolean("apply_all_abilities", true)
                        };
                        _effects["LevelUpEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            OwnHouseOnly = reader.GetBoolean("own_house_only", true)
                        };
                        _effects["MoveToLocationEffect"][template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM gain_merchant_reopen_pack_item_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new GainMerchantReopenPackItemEffect
                        {
                            Id = reader.GetUInt32("id", 0),
                            MerchantReopenPackId = reader.GetUInt32("merchant_reopen_pack_id", 0),
                            LifeTime = reader.GetInt32("life_time", 0)
                        };
                        _effects["GainMerchantReopenPackItemEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), Range = reader.GetUInt32("range", 0), DoodadId = reader.GetUInt32("doodad_id", 0)
                        };
                        _effects["SpawnFishEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            GimmickId = reader.GetUInt32("gimmick_id", 0),
                            OffsetFromSource = reader.GetBoolean("offset_from_source", true),
                            OffsetCoordinateId = reader.GetUInt32("offset_coordiate_id", 0),
                            OffsetX = reader.GetFloat("offset_x", 0f),
                            OffsetY = reader.GetFloat("offset_y", 0f),
                            OffsetZ = reader.GetFloat("offset_z", 0f),
                            Scale = reader.GetFloat("scale", 0f),
                            VelocityCoordinateId = reader.GetUInt32("velocity_coordiate_id", 0),
                            VelocityX = reader.GetFloat("velocity_x", 0f),
                            VelocityY = reader.GetFloat("velocity_y", 0f),
                            VelocityZ = reader.GetFloat("velocity_z", 0f),
                            AngVelCoordinateId = reader.GetUInt32("ang_vel_coordiate_id", 0),
                            AngVelX = reader.GetFloat("ang_vel_x", 0f),
                            AngVelY = reader.GetFloat("ang_vel_y", 0f),
                            AngVelZ = reader.GetFloat("ang_vel_z", 0f)
                        };
                        _effects["SpawnGimmickEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            SpecialEffectTypeId = (SpecialType)reader.GetInt32("special_effect_type_id", 0),
                            Value1 = reader.GetInt32("value1", 0),
                            Value2 = reader.GetInt32("value2", 0),
                            Value3 = reader.GetInt32("value3", 0),
                            Value4 = reader.GetInt32("value4", 0),
                            Value5 = reader.GetInt32("value5", 0),
                            Value6 = reader.GetInt32("value6", 0),
                            Value7 = reader.GetInt32("value7", 0)
                        };
                        _effects["SpecialEffect"][template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0), ActualId = reader.GetUInt32("actual_id", 0), Type = reader.GetString("actual_type", "")
                        };
                        _types[template.Id] = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM skill_effects WHERE enable = 't'";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var skillId = reader.GetUInt32("skill_id", 0);
                        if (!_skills.ContainsKey(skillId))
                            continue;

                        var template = new SkillEffect();
                        var effectId = reader.GetUInt32("effect_id", 0);

                        //for easier debugging
                        template.EffectId = effectId;

                        if (!_types.TryGetValue(effectId, out var type))
                            continue; // 10.0.2.13: effect_id may reference an effect type that didn't load
                        if (_effects.TryGetValue(type.Type, out var effect) && effect.TryGetValue(type.ActualId, out var tmpl))
                            template.Template = tmpl; // dangling effect ref (e.g. 3612) -> leave Template null, don't crash
                        template.Weight = reader.GetInt32("weight", 0);
                        template.StartLevel = reader.GetByte("start_level", 0);
                        template.EndLevel = reader.GetByte("end_level", 0);
                        template.Friendly = reader.GetBoolean("friendly", true);
                        template.NonFriendly = reader.GetBoolean("non_friendly", true);
                        template.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                        template.TargetNoBuffTagId = reader.GetUInt32("target_nobuff_tag_id", 0);
                        template.SourceBuffTagId = reader.GetUInt32("source_buff_tag_id", 0);
                        template.SourceNoBuffTagId = reader.GetUInt32("source_nobuff_tag_id", 0);
                        template.Chance = reader.GetInt32("chance", 0);
                        template.Front = reader.GetBoolean("front", true);
                        template.Back = reader.GetBoolean("back", true);
                        template.TargetNpcTagId = reader.GetUInt32("target_npc_tag_id", 0);
                        template.ApplicationMethod = (SkillEffectApplicationMethod)reader.GetUInt32("application_method_id", 0);
                        template.ConsumeSourceItem = reader.GetBoolean("consume_source_item", true);
                        template.ConsumeItemId = reader.GetUInt32("consume_item_id", 0);
                        template.ConsumeItemCount = reader.GetInt32("consume_item_count", 0);
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
                        var tagId = reader.GetUInt32("tag_id", 0);
                        var buffId = reader.GetUInt32("buff_id", 0);

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
                            Id = reader.GetUInt32("id", 0),
                            OwnerId = reader.GetUInt32("owner_id", 0),
                            OwnerType = reader.GetString("owner_type", ""),
                            TagId = reader.GetUInt32("tag_id", 0),
                            SkillAttribute = (SkillAttribute)reader.GetUInt32("skill_attribute_id", 0),
                            UnitModifierType = (UnitModifierType)reader.GetUInt32("unit_modifier_type_id", 0),
                            Value = reader.GetInt32("value", 0),
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
                        var tagId = reader.GetUInt32("tag_id", 0);
                        var skillId = reader.GetUInt32("skill_id", 0);

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
                            Id = reader.GetUInt32("id", 0),
                            HitSkillId = reader.GetUInt32("hit_skill_id", 0),
                            // hit_type_id renamed to hit_type_bits in 10.0.2.13 schema
                            HitType = (SkillHitType)reader.GetUInt32("hit_type_bits", 0),
                            BuffId = reader.GetUInt32("buff_id", 0),
                            BuffFromSource = reader.GetBoolean("buff_from_source", true),
                            BuffToSource = reader.GetBoolean("buff_to_source", true),
                            ReqSkillId = reader.GetUInt32("req_skill_id", 0),
                            ReqBuffId = reader.GetUInt32("req_buff_id", 0),
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
                command.CommandText = "SELECT * FROM buff_triggers WHERE enable = 't'";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var trigger = new BuffTriggerTemplate();
                        var buffId = reader.GetUInt32("buff_id", 0);
                        if (!_buffTriggers.TryGetValue(buffId, out var value))
                        {
                            value = [];
                            _buffTriggers.Add(buffId, value);
                        }
                        trigger.Id = reader.GetUInt32("id", 0);
                        trigger.Kind = (BuffEventTriggerKind)reader.GetUInt16("event_id");
                        trigger.Effect = GetEffectTemplate(reader.GetUInt32("effect_id", 0));
                        trigger.UseDamageAmount = reader.GetBoolean("use_damage_amount", true);
                        trigger.TargetBuffTagId = reader.GetUInt32("target_buff_tag_id", 0);
                        trigger.TargetNoBuffTagId = reader.GetUInt32("target_no_buff_tag_id", 0);

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
                            Id = reader.GetUInt32("id", 0),
                            SkillId = reader.GetUInt32("skill_id", 0),
                            ItemId = reader.GetUInt32("item_id", 0),
                            Amount = reader.GetInt16("amount")
                        };
                        _skillReagents[template.Id] = template;
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
                            Id = reader.GetUInt32("id", 0),
                            SkillId = reader.GetUInt32("skill_id", 0),
                            ItemId = reader.GetUInt32("item_id", 0),
                            Amount = reader.GetInt16("amount")
                        };
                        _skillProducts[template.Id] = template;
                    }
                }
                Logger.Info("Skill Products loaded");

                OnSkillsLoaded?.Invoke(this, EventArgs.Empty);
            }
        }

        foreach (var skillTemplate in _skills.Values.Where(x => x.AutoLearn))
        {
            // 10.0.2.13: skills.need_learn removed; AutoLearn (filtered above) now solely drives auto-learning.
            if (skillTemplate.AbilityId == 0 &&
                !_defaultSkills.ContainsKey(skillTemplate.Id))
                _commonSkills.Add(skillTemplate.Id);
            if (skillTemplate.AbilityId == 0 || skillTemplate.AbilityLevel > 1 ||
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
