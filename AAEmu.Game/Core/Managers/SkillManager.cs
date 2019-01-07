using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class SkillManager : Singleton<SkillManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, SkillTemplate> _skills;
        private Dictionary<uint, DefaultSkill> _defaultSkills;
        private List<uint> _commonSkills;
        private Dictionary<AbilityType, List<SkillTemplate>> _startAbilitySkills;
        private Dictionary<uint, PassiveBuffTemplate> _passiveBuffs;

        public SkillTemplate GetSkillTemplate(uint id)
        {
            if (_skills.ContainsKey(id))
                return _skills[id];
            return null;
        }

        public List<SkillTemplate> GetStartAbilitySkills(AbilityType ability)
        {
            return _startAbilitySkills[ability];
        }

        public List<DefaultSkill> GetDefaultSkills()
        {
            return new List<DefaultSkill>(_defaultSkills.Values);
        }
        
        public void Load()
        {
            _skills = new Dictionary<uint, SkillTemplate>();
            _defaultSkills = new Dictionary<uint, DefaultSkill>();
            _commonSkills = new List<uint>();
            _startAbilitySkills = new Dictionary<AbilityType, List<SkillTemplate>>();
            _passiveBuffs = new Dictionary<uint, PassiveBuffTemplate>();

            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading skills...");
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
                            template.Cost = reader.GetInt32("cost");
                            template.Show = reader.GetBoolean("show", true);
                            template.FireAnimId =
                                reader.IsDBNull("fire_anim_id") ? 0u : reader.GetUInt32("fire_anim_id");
                            template.AbilityId = reader.GetByte("ability_id");
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
                            template.TargetType = (SkillTargetType) reader.GetInt32("target_type_id");
                            template.TargetSelectionId = reader.GetInt32("target_selection_id");
                            template.TargetRelationId = reader.GetInt32("target_relation_id");
                            template.TargetAreaCount = reader.GetInt32("target_area_count");
                            template.TargetAreaRadius = reader.GetInt32("target_area_radius");
                            template.TargetSiege = reader.GetBoolean("target_siege", true);
                            template.WeaponSlotForAngleId = reader.GetInt32("weapon_slot_for_angle_id");
                            template.TargetAngle = reader.GetInt32("target_angle");
                            template.WeaponSlotForRangeId = reader.GetInt32("weapon_slot_for_range_id");
                            template.MinRange = reader.GetInt32("min_range");
                            template.MaxRange = reader.GetInt32("max_range");
                            template.KeepStealth = reader.GetBoolean("keep_stealth", true);
                            template.Aggro = reader.GetInt32("aggro");
                            template.ChannelingTime = reader.GetInt32("channeling_time");
                            template.ChannelingTick = reader.GetInt32("channeling_tick");
                            template.ChannelingMana = reader.GetInt32("channeling_mana");
                            template.ChannelingTargetBuffId = reader.IsDBNull("channeling_target_buff_id")
                                ? 0
                                : reader.GetInt32("channeling_target_buff_id");
                            template.TargetAreaAngle = reader.GetInt32("target_area_angle");
                            template.AbilityLevel = reader.GetInt32("ability_level");
                            template.ChannelingDoodadId = reader.IsDBNull("channeling_doodad_id")
                                ? 0
                                : reader.GetInt32("channeling_doodad_id");
                            var value = reader.IsDBNull("cooldown_tag_id")
                                ? "0"
                                : reader.GetString("cooldown_tag_id");
                            template.CooldownTagId = value.Contains("null") ? 0 : int.Parse(value);
                            value = reader.IsDBNull("skill_controller_id")
                                ? "0"
                                : reader.GetString("skill_controller_id");
                            template.SkillControllerId = value.Contains("null") ? 0 : int.Parse(value);
                            template.RepeatCount = reader.GetInt32("repeat_count");
                            template.RepeatTick = reader.GetInt32("repeat_tick");
                            template.ToggleBuffId = !reader.IsDBNull("toggle_buff_id")
                                ? reader.GetUInt32("toggle_buff_id")
                                : 0;
                            template.TargetDead = reader.GetBoolean("target_dead", true);
                            template.ChannelingBuffId = !reader.IsDBNull("channeling_buff_id")
                                ? (uint) reader.GetInt32("channeling_buff_id")
                                : 0;
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
                            template.MainhandToolId = !reader.IsDBNull("mainhand_tool_id")
                                ? reader.GetInt32("mainhand_tool_id")
                                : 0;
                            template.OffhandToolId = !reader.IsDBNull("offhand_tool_id")
                                ? reader.GetInt32("offhand_tool_id")
                                : 0;
                            template.FrontAngle = reader.GetInt32("front_angle");
                            template.ManaLevelMd = reader.GetFloat("mana_level_md");
                            template.Unmount = reader.GetBoolean("unmount", true);
                            template.DamageTypeId = !reader.IsDBNull("damage_type_id")
                                ? reader.GetInt32("damage_type_id")
                                : 0;
                            template.AllowToPrisoner = reader.GetBoolean("allow_to_prisoner", true);
                            template.MilestoneId =
                                !reader.IsDBNull("milestone_id") ? reader.GetInt32("milestone_id") : 0;
                            template.PlotId = !reader.IsDBNull("plot_id") ? (uint) reader.GetInt32("plot_id") : 0;
                            template.ConsumeLaborPower =
                                !reader.IsDBNull("consume_lp") ? reader.GetInt32("consume_lp") : 0;
                            template.SourceStun = reader.GetBoolean("source_stun", true);
                            template.TargetAlive = reader.GetBoolean("target_alive", true);
                            template.TargetWater = reader.GetBoolean("target_water", true);
                            template.CastingInc = reader.GetInt32("casting_inc");
                            template.CastingCancelable = reader.GetBoolean("casting_cancelable", true);
                            template.CastingDelayable = reader.GetBoolean("casting_delayable", true);
                            template.ChannelingCancelable = reader.GetBoolean("channeling_cancelable", true);
                            template.TargetOffsetAngle = reader.GetFloat("target_offset_angle");
                            template.TargetOffsetDistance = reader.GetFloat("target_offset_distance");
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
                            template.CustonGcd = reader.GetInt32("custom_gcd");
                            template.CancelOngoingBuffs = reader.GetBoolean("cancel_ongoing_buffs", true);
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
                            template.AutoReUseDelay = reader.GetInt32("auto_reuse_delay");
                            template.SourceNotCollided = reader.GetBoolean("source_not_collided", true);
                            template.SkillPoints = reader.GetInt32("skill_points");
                            template.DoodadHitFamily = reader.GetInt32("doodad_hit_family");
                            _skills.Add(template.Id, template);
                        }
                    }
                }

                _log.Info("Loaded {0} skills", _skills.Count);

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM default_skills";
                    command.Prepare();
                    using (var sqliteReader = command.ExecuteReader())
                    using (var reader = new SQLiteWrapperReader(sqliteReader))
                    {
                        while (reader.Read())
                        {
                            var id = (uint) reader.GetInt32("skill_id");
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
                                AbilityId = reader.GetByte("ability_id"),
                                Level = reader.GetByte("level"),
                                BuffId = reader.GetUInt32("buff_id"),
                                ReqPoints = reader.GetInt32("req_points"),
                                Active = reader.GetBoolean("active", true)
                            };
                            _passiveBuffs.Add(template.Id, template);
                        }
                    }
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
                var ability = (AbilityType) skillTemplate.AbilityId;
                if (!_startAbilitySkills.ContainsKey(ability))
                    _startAbilitySkills.Add(ability, new List<SkillTemplate>());
                _startAbilitySkills[ability].Add(skillTemplate);
            }
        }
    }
}