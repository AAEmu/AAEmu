//! `SkillTemplate`, `BuffTemplate` (all columns the 1.2 `SkillManager.Load` reads), `SkillEffect`,
//! `TickEffect`, bonus templates, `SkillControllerTemplate`.

use rusqlite::Connection;

use crate::data::sqlite::{for_each_row, SqlRow};
use crate::models::unit::{BonusTemplate, DynamicBonusTemplate, UnitAttribute, UnitModifierType};

use super::effects::EffectKind;

/// `SkillEffect` (skill_effects rows).
#[derive(Debug, Clone, PartialEq)]
pub struct SkillEffect {
    pub effect_id: u32,
    /// `Template` (`None` when the effect type / id has no template).
    pub template: Option<(EffectKind, u32)>,
    pub weight: i32,
    pub start_level: u8,
    pub end_level: u8,
    pub friendly: bool,
    pub non_friendly: bool,
    pub target_buff_tag_id: u32,
    pub target_no_buff_tag_id: u32,
    pub source_buff_tag_id: u32,
    pub source_no_buff_tag_id: u32,
    pub chance: i32,
    pub front: bool,
    pub back: bool,
    pub target_npc_tag_id: u32,
    pub application_method: u32,
    pub consume_source_item: bool,
    pub consume_item_id: u32,
    pub consume_item_count: i32,
    pub always_hit: bool,
    pub item_set_id: u32,
    pub interaction_success_hit: bool,
}

/// `SkillTemplate`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct SkillTemplate {
    pub id: u32,
    pub cost: i32,
    pub show: bool,
    pub category_id: i32,
    /// `FireAnim?.Id` (the animation must exist in `anims`).
    pub fire_anim_id: u32,
    pub ability_id: u8,
    pub mana_cost: i32,
    pub timing_id: i32,
    pub cooldown_time: i32,
    pub casting_time: i32,
    pub ignore_global_cooldown: bool,
    pub effect_delay: i32,
    pub effect_speed: f32,
    pub effect_repeat_count: i32,
    pub effect_repeat_tick: i32,
    pub active_weapon_id: i32,
    pub target_type: i32,
    pub target_selection: i32,
    pub target_relation: i32,
    pub target_area_count: i32,
    pub target_area_radius: i32,
    pub target_siege: bool,
    pub weapon_slot_for_angle_id: i32,
    pub target_angle: i32,
    pub weapon_slot_for_range_id: i32,
    pub weapon_slot_for_auto_attack_id: i32,
    pub min_range: i32,
    pub max_range: i32,
    pub keep_stealth: bool,
    pub aggro: i32,
    pub channeling_time: i32,
    pub channeling_tick: i32,
    pub channeling_mana: i32,
    pub channeling_target_buff_id: u32,
    pub target_area_angle: i32,
    pub ability_level: i32,
    pub channeling_doodad_id: u32,
    pub cooldown_tag_id: i32,
    pub skill_controller_id: u32,
    pub repeat_count: i32,
    pub repeat_tick: i32,
    pub toggle_buff_id: u32,
    pub target_dead: bool,
    pub channeling_buff_id: u32,
    pub reagent_corpse_status_id: i32,
    pub source_dead: bool,
    pub level_step: i32,
    pub valid_height: f32,
    pub target_valid_height: f32,
    pub source_mount: bool,
    pub stop_casting_on_big_hit: bool,
    pub stop_channeling_on_big_hit: bool,
    pub auto_learn: bool,
    pub need_learn: bool,
    pub mainhand_tool_id: u32,
    pub offhand_tool_id: u32,
    pub front_angle: i32,
    pub mana_level_md: f32,
    pub unmount: bool,
    pub damage_type_id: u32,
    pub allow_to_prisoner: bool,
    pub milestone_id: u32,
    pub match_animation: bool,
    /// `Plot?.Id` (`None` for a NULL plot_id or an unknown plot).
    pub plot_id: Option<u32>,
    pub use_anim_time: bool,
    pub consume_labor_power: i32,
    pub source_stun: bool,
    pub target_alive: bool,
    pub target_water: bool,
    pub casting_inc: i32,
    pub casting_cancelable: bool,
    pub casting_delayable: bool,
    pub channeling_cancelable: bool,
    pub target_offset_angle: f32,
    pub target_offset_distance: f32,
    pub actability_group_id: i32,
    pub plot_only: bool,
    pub skill_controller_at_end: bool,
    pub end_skill_controller: bool,
    pub or_unit_reqs: bool,
    pub default_gcd: bool,
    pub keep_mana_regen: bool,
    pub crime_point: i32,
    pub level_rule_no_consideration: bool,
    pub use_weapon_cooldown_time: bool,
    pub combat_dice_id: i32,
    pub custom_gcd: i32,
    pub cancel_ongoing_buffs: bool,
    pub cancel_ongoing_buff_exception_tag_id: u32,
    pub source_cannot_use_while_walk: bool,
    pub source_mount_mate: bool,
    pub check_terrain: bool,
    pub target_only_water: bool,
    pub source_not_swim: bool,
    pub target_preoccupied: bool,
    pub stop_channeling_on_start_skill: bool,
    pub stop_casting_by_turn: bool,
    pub target_my_npc: bool,
    pub gain_life_point: i32,
    pub target_fishing: bool,
    pub source_no_slave: bool,
    pub auto_reuse: bool,
    pub auto_reuse_delay: i32,
    pub source_not_collided: bool,
    pub skill_points: i32,
    pub doodad_hit_family: i32,
    pub first_reagent_only: bool,
    pub effects: Vec<SkillEffect>,
}

/// `value.Contains("null") ? 0 : int.Parse(value)` (a parse failure throws in C#).
fn parse_nullable(v: &str, col: &str) -> rusqlite::Result<i64> {
    if v.contains("null") {
        return Ok(0);
    }
    v.trim().parse::<i64>().map_err(|_| {
        rusqlite::Error::InvalidParameterName(format!("{col}: cannot parse '{v}' (int.Parse)"))
    })
}

fn read_skill(
    r: &SqlRow<'_, '_>,
    plot_exists: &dyn Fn(u32) -> bool,
    anim_exists: &dyn Fn(u32) -> bool,
) -> rusqlite::Result<SkillTemplate> {
    let fire_anim = r.u32_or("fire_anim_id", 0)?;
    let plot_id = if r.is_null("plot_id")? {
        None
    } else {
        let p = r.u32("plot_id")?;
        plot_exists(p).then_some(p)
    };
    Ok(SkillTemplate {
        id: r.u32("id")?,
        cost: r.i32("cost")?,
        show: r.bool_str("show")?,
        category_id: r.i32("category_id")?,
        fire_anim_id: if anim_exists(fire_anim) { fire_anim } else { 0 },
        ability_id: r.u8("ability_id")?,
        mana_cost: r.i32("mana_cost")?,
        timing_id: r.i32("timing_id")?,
        cooldown_time: r.i32("cooldown_time")?,
        casting_time: r.i32("casting_time")?,
        ignore_global_cooldown: r.bool_str("ignore_global_cooldown")?,
        effect_delay: r.i32("effect_delay")?,
        effect_speed: r.f32("effect_speed")?,
        effect_repeat_count: r.i32("effect_repeat_count")?,
        effect_repeat_tick: r.i32("effect_repeat_tick")?,
        active_weapon_id: r.i32("active_weapon_id")?,
        target_type: r.i32("target_type_id")?,
        target_selection: r.i32("target_selection_id")?,
        target_relation: r.i32("target_relation_id")?,
        target_area_count: r.i32("target_area_count")?,
        target_area_radius: r.i32("target_area_radius")?,
        target_siege: r.bool_str("target_siege")?,
        weapon_slot_for_angle_id: r.i32("weapon_slot_for_angle_id")?,
        target_angle: r.i32("target_angle")?,
        weapon_slot_for_range_id: r.i32("weapon_slot_for_range_id")?,
        weapon_slot_for_auto_attack_id: r.i32("weapon_slot_for_autoattack_id")?,
        min_range: r.i32("min_range")?,
        max_range: r.i32("max_range")?,
        keep_stealth: r.bool_str("keep_stealth")?,
        aggro: r.i32("aggro")?,
        channeling_time: r.i32("channeling_time")?,
        channeling_tick: r.i32("channeling_tick")?,
        channeling_mana: r.i32("channeling_mana")?,
        channeling_target_buff_id: r.u32_or("channeling_target_buff_id", 0)?,
        target_area_angle: r.i32("target_area_angle")?,
        ability_level: r.i32("ability_level")?,
        channeling_doodad_id: r.u32_or("channeling_doodad_id", 0)?,
        cooldown_tag_id: parse_nullable(&r.string_or("cooldown_tag_id", "0")?, "cooldown_tag_id")?
            as i32,
        skill_controller_id: parse_nullable(
            &r.string_or("skill_controller_id", "0")?,
            "skill_controller_id",
        )? as u32,
        repeat_count: r.i32("repeat_count")?,
        repeat_tick: r.i32("repeat_tick")?,
        toggle_buff_id: r.u32_or("toggle_buff_id", 0)?,
        target_dead: r.bool_str("target_dead")?,
        channeling_buff_id: r.u32_or("channeling_buff_id", 0)?,
        reagent_corpse_status_id: r.i32("reagent_corpse_status_id")?,
        source_dead: r.bool_str("source_dead")?,
        level_step: r.i32("level_step")?,
        valid_height: r.f32("valid_height")?,
        target_valid_height: r.f32("target_valid_height")?,
        source_mount: r.bool_str("source_mount")?,
        stop_casting_on_big_hit: r.bool_str("stop_casting_on_big_hit")?,
        stop_channeling_on_big_hit: r.bool_str("stop_channeling_on_big_hit")?,
        auto_learn: r.bool_str("auto_learn")?,
        need_learn: r.bool_str("need_learn")?,
        mainhand_tool_id: r.u32_or("mainhand_tool_id", 0)?,
        offhand_tool_id: r.u32_or("offhand_tool_id", 0)?,
        front_angle: r.i32("front_angle")?,
        mana_level_md: r.f32("mana_level_md")?,
        unmount: r.bool_str("unmount")?,
        damage_type_id: r.u32_or("damage_type_id", 0)?,
        allow_to_prisoner: r.bool_str("allow_to_prisoner")?,
        milestone_id: r.u32_or("milestone_id", 0)?,
        match_animation: r.bool_str("match_animation")?,
        plot_id,
        use_anim_time: r.bool_str("use_anim_time")?,
        consume_labor_power: r.i32_or("consume_lp", 0)?,
        source_stun: r.bool_str("source_stun")?,
        target_alive: r.bool_str("target_alive")?,
        target_water: r.bool_str("target_water")?,
        casting_inc: r.i32("casting_inc")?,
        casting_cancelable: r.bool_str("casting_cancelable")?,
        casting_delayable: r.bool_str("casting_delayable")?,
        channeling_cancelable: r.bool_str("channeling_cancelable")?,
        target_offset_angle: r.f32("target_offset_angle")?,
        target_offset_distance: r.f32("target_offset_distance")?,
        actability_group_id: r.i32_or("actability_group_id", 0)?,
        plot_only: r.bool_str("plot_only")?,
        skill_controller_at_end: r.bool_str("skill_controller_at_end")?,
        end_skill_controller: r.bool_str("end_skill_controller")?,
        or_unit_reqs: r.bool_str("or_unit_reqs")?,
        default_gcd: r.bool_str("default_gcd")?,
        keep_mana_regen: r.bool_str("keep_mana_regen")?,
        crime_point: r.i32("crime_point")?,
        level_rule_no_consideration: r.bool_str("level_rule_no_consideration")?,
        use_weapon_cooldown_time: r.bool_str("use_weapon_cooldown_time")?,
        combat_dice_id: r.i32("combat_dice_id")?,
        custom_gcd: r.i32("custom_gcd")?,
        cancel_ongoing_buffs: r.bool_str("cancel_ongoing_buffs")?,
        cancel_ongoing_buff_exception_tag_id: r
            .u32_or("cancel_ongoing_buff_exception_tag_id", 0)?,
        source_cannot_use_while_walk: r.bool_str("source_cannot_use_while_walk")?,
        source_mount_mate: r.bool_str("source_mount_mate")?,
        check_terrain: r.bool_str("check_terrain")?,
        target_only_water: r.bool_str("target_only_water")?,
        source_not_swim: r.bool_str("source_not_swim")?,
        target_preoccupied: r.bool_str("target_preoccupied")?,
        stop_channeling_on_start_skill: r.bool_str("stop_channeling_on_start_skill")?,
        stop_casting_by_turn: r.bool_str("stop_casting_by_turn")?,
        target_my_npc: r.bool_str("target_my_npc")?,
        gain_life_point: r.i32("gain_life_point")?,
        target_fishing: r.bool_str("target_fishing")?,
        source_no_slave: r.bool_str("source_no_slave")?,
        auto_reuse: r.bool_str("auto_reuse")?,
        auto_reuse_delay: r.i32_or("auto_reuse_delay", 0)?,
        source_not_collided: r.bool_str("source_not_collided")?,
        skill_points: r.i32("skill_points")?,
        doodad_hit_family: r.i32("doodad_hit_family")?,
        first_reagent_only: r.bool_str("first_reagent_only")?,
        effects: Vec::new(),
    })
}

/// `SELECT * FROM skills` in table order (duplicate ids are an error, `Dictionary.Add`).
pub fn load_skills(
    conn: &Connection,
    plot_exists: &dyn Fn(u32) -> bool,
    anim_exists: &dyn Fn(u32) -> bool,
) -> rusqlite::Result<Vec<SkillTemplate>> {
    let mut out = Vec::new();
    for_each_row(conn, "SELECT * FROM skills", [], |r| {
        out.push(read_skill(r, plot_exists, anim_exists)?);
        Ok(())
    })?;
    Ok(out)
}

/// `TickEffect`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TickEffect {
    pub effect_id: u32,
    pub target_buff_tag_id: u32,
    pub target_no_buff_tag_id: u32,
}

/// `BuffTemplate` (every column the 1.2 `SkillManager.Load` reads, plus the tick effects and the
/// static / dynamic bonuses attached to it).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct BuffTemplate {
    pub id: u32,
    pub anim_start_id: u32,
    pub anim_end_id: u32,
    pub duration: i32,
    pub tick: f64,
    pub silence: bool,
    pub root: bool,
    pub sleep: bool,
    pub stun: bool,
    pub crippled: bool,
    pub stealth: bool,
    pub remove_on_source_dead: bool,
    pub link_buff_id: u32,
    pub tick_mana_cost: i32,
    pub stack_rule: u32,
    pub init_min_charge: i32,
    pub init_max_charge: i32,
    pub max_stack: i32,
    pub damage_absorption_type_id: u32,
    pub damage_absorption_per_hit: i32,
    pub aura_radius: i32,
    pub mana_shield_ratio: i32,
    pub frame_hold: bool,
    pub ragdoll: bool,
    pub one_time: bool,
    pub reflection_chance: i32,
    pub reflection_type_id: u32,
    pub require_buff_id: u32,
    pub taunt: bool,
    pub taunt_with_top_aggro: bool,
    pub remove_on_use_skill: bool,
    pub melee_immune: bool,
    pub spell_immune: bool,
    pub ranged_immune: bool,
    pub siege_immune: bool,
    pub immune_damage: i32,
    pub skill_controller_id: u32,
    pub resurrection_health: i32,
    pub resurrection_mana: i32,
    pub resurrection_percent: bool,
    pub level_duration: i32,
    pub reflection_ratio: i32,
    pub reflection_target_ratio: i32,
    pub knockback_immune: bool,
    pub immune_buff_tag_id: u32,
    pub aura_relation_id: u32,
    pub group_id: u32,
    pub group_rank: i32,
    pub per_unit_creation: bool,
    pub tick_area_radius: f32,
    pub tick_area_relation_id: u32,
    pub remove_on_move: bool,
    pub use_source_faction: bool,
    pub faction_id: u32,
    pub exempt: bool,
    pub tick_area_front_angle: i32,
    pub tick_area_angle: i32,
    pub psychokinesis: bool,
    pub no_collide: bool,
    pub psychokinesis_speed: f32,
    pub remove_on_death: bool,
    pub tick_anim_id: u32,
    pub tick_active_weapon_id: u32,
    pub conditional_tick: bool,
    pub system: bool,
    pub aura_slave_buff_id: u32,
    pub non_pushable: bool,
    pub active_weapon_id: u32,
    pub max_charge: i32,
    pub detect_stealth: bool,
    pub remove_on_exempt: bool,
    pub remove_on_land: bool,
    pub gliding: bool,
    pub gliding_rotate_speed: i32,
    pub gliding_lift_height: f32,
    pub gliding_lift_speed: f32,
    pub gliding_lift_duration: f32,
    pub knockdown: bool,
    pub tick_area_exclude_source: bool,
    pub fall_damage_immune: bool,
    /// `BuffKind` (raw `kind_id`).
    pub kind: i32,
    pub transform_buff_id: u32,
    pub blank_minded: bool,
    pub fastened: bool,
    pub slave_applicable: bool,
    pub pacifist: bool,
    pub remove_on_interaction: bool,
    pub crime: bool,
    pub remove_on_unmount: bool,
    pub aura_child_only: bool,
    pub remove_on_mount: bool,
    pub remove_on_start_skill: bool,
    pub sprint_motion: bool,
    pub telescope_range: f32,
    pub mainhand_tool_id: u32,
    pub offhand_tool_id: u32,
    pub tick_mainhand_tool_id: u32,
    pub tick_offhand_tool_id: u32,
    pub tick_level_mana_cost: f32,
    pub walk_only: bool,
    pub cannot_jump: bool,
    pub crowd_buff_id: u32,
    pub crowd_radius: f32,
    pub crowd_number: i32,
    pub evade_telescope: bool,
    pub transfer_telescope_range: f32,
    pub remove_on_attack_spell_dot: bool,
    pub remove_on_attack_etc_dot: bool,
    pub remove_on_attack_buff_trigger: bool,
    pub remove_on_attack_etc: bool,
    pub remove_on_attacked_spell_dot: bool,
    pub remove_on_attacked_etc_dot: bool,
    pub remove_on_attacked_buff_trigger: bool,
    pub remove_on_attacked_etc: bool,
    pub remove_on_damage_spell_dot: bool,
    pub remove_on_damage_etc_dot: bool,
    pub remove_on_damage_buff_trigger: bool,
    pub remove_on_damage_etc: bool,
    pub remove_on_damaged_spell_dot: bool,
    pub remove_on_damaged_etc_dot: bool,
    pub remove_on_damaged_buff_trigger: bool,
    pub remove_on_damaged_etc: bool,
    pub owner_only: bool,
    pub remove_on_auto_attack: bool,
    pub save_rule_id: u32,
    pub anti_stealth: bool,
    pub scale: f32,
    pub scale_duration: f32,
    pub immune_except_creator: bool,
    pub immune_except_skill_tag_id: u32,
    pub find_school_of_fish_range: f32,
    pub anim_action_id: u32,
    pub dead_applicable: bool,
    pub tick_area_use_origin_source: bool,
    pub real_time: bool,
    pub do_not_remove_by_other_skill_controller: bool,
    pub cooldown_skill_id: u32,
    pub cooldown_skill_time: i32,
    pub mana_burn_immune: bool,
    pub freeze_ship: bool,
    pub crowd_friendly: bool,
    pub crowd_hostile: bool,
    pub tick_effects: Vec<TickEffect>,
    pub bonuses: Vec<BonusTemplate>,
    pub dynamic_bonuses: Vec<DynamicBonusTemplate>,
}

impl BuffTemplate {
    /// `GetDuration(abLevel)`.
    pub fn get_duration(&self, ab_level: u32) -> i32 {
        (self.level_duration.wrapping_mul(ab_level as i32))
            .wrapping_add(self.duration)
            .max(0)
    }

    /// `OnActionTime`.
    pub fn on_action_time(&self) -> bool {
        self.tick > 0.0
    }

    /// `BuffKind.Good/Bad/Hidden` as the phase-2 bookkeeping type.
    pub fn buff_kind(&self) -> crate::data::skills::BuffKind {
        crate::data::skills::BuffKind::from(self.kind)
    }
}

/// `string.Length > 0 ? uint.Parse(value) : 0` (the tool id columns).
fn parse_tool(v: &str, col: &str) -> rusqlite::Result<u32> {
    if v.is_empty() {
        return Ok(0);
    }
    v.trim().parse::<u32>().map_err(|_| {
        rusqlite::Error::InvalidParameterName(format!("{col}: cannot parse '{v}' (uint.Parse)"))
    })
}

fn read_buff(r: &SqlRow<'_, '_>) -> rusqlite::Result<BuffTemplate> {
    Ok(BuffTemplate {
        id: r.u32("id")?,
        anim_start_id: r.u32_or("anim_start_id", 0)?,
        anim_end_id: r.u32_or("anim_end_id", 0)?,
        duration: r.i32("duration")?,
        tick: r.f64("tick")?,
        silence: r.bool_str("silence")?,
        root: r.bool_str("root")?,
        sleep: r.bool_str("sleep")?,
        stun: r.bool_str("stun")?,
        crippled: r.bool_str("crippled")?,
        stealth: r.bool_str("stealth")?,
        remove_on_source_dead: r.bool_str("remove_on_source_dead")?,
        link_buff_id: r.u32_or("link_buff_id", 0)?,
        tick_mana_cost: r.i32("tick_mana_cost")?,
        stack_rule: r.u32("stack_rule_id")?,
        init_min_charge: r.i32("init_min_charge")?,
        init_max_charge: r.i32("init_max_charge")?,
        max_stack: r.i32("max_stack")?,
        damage_absorption_type_id: r.u32("damage_absorption_type_id")?,
        damage_absorption_per_hit: r.i32("damage_absorption_per_hit")?,
        aura_radius: r.i32("aura_radius")?,
        mana_shield_ratio: r.i32("mana_shield_ratio")?,
        frame_hold: r.bool_str("framehold")?,
        ragdoll: r.bool_str("ragdoll")?,
        one_time: r.bool_str("one_time")?,
        reflection_chance: r.i32("reflection_chance")?,
        reflection_type_id: r.u32("reflection_type_id")?,
        require_buff_id: r.u32_or("require_buff_id", 0)?,
        taunt: r.bool_str("taunt")?,
        taunt_with_top_aggro: r.bool_str("taunt_with_top_aggro")?,
        remove_on_use_skill: r.bool_str("remove_on_use_skill")?,
        melee_immune: r.bool_str("melee_immune")?,
        spell_immune: r.bool_str("spell_immune")?,
        ranged_immune: r.bool_str("ranged_immune")?,
        siege_immune: r.bool_str("siege_immune")?,
        immune_damage: r.i32("immune_damage")?,
        skill_controller_id: parse_nullable(
            &r.string_or("skill_controller_id", "0")?,
            "skill_controller_id",
        )? as u32,
        resurrection_health: r.i32("resurrection_health")?,
        resurrection_mana: r.i32("resurrection_mana")?,
        resurrection_percent: r.bool_str("resurrection_percent")?,
        level_duration: r.i32("level_duration")?,
        reflection_ratio: r.i32("reflection_ratio")?,
        reflection_target_ratio: r.i32("reflection_target_ratio")?,
        // GetBoolean(column) without fromString
        knockback_immune: r.bool("knockback_immune")?,
        immune_buff_tag_id: r.u32_or("immune_buff_tag_id", 0)?,
        aura_relation_id: r.u32("aura_relation_id")?,
        group_id: r.u32_or("group_id", 0)?,
        group_rank: r.i32("group_rank")?,
        per_unit_creation: r.bool("per_unit_creation")?,
        tick_area_radius: r.f32("tick_area_radius")?,
        tick_area_relation_id: r.u32("tick_area_relation_id")?,
        remove_on_move: r.bool_str("remove_on_move")?,
        use_source_faction: r.bool_str("use_source_faction")?,
        faction_id: r.u32_or("faction_id", 0)?,
        exempt: r.bool_str("exempt")?,
        tick_area_front_angle: r.i32("tick_area_front_angle")?,
        tick_area_angle: r.i32("tick_area_angle")?,
        psychokinesis: r.bool_str("psychokinesis")?,
        no_collide: r.bool_str("no_collide")?,
        psychokinesis_speed: r.f32("psychokinesis_speed")?,
        remove_on_death: r.bool_str("remove_on_death")?,
        tick_anim_id: r.u32_or("tick_anim_id", 0)?,
        tick_active_weapon_id: r.u32("tick_active_weapon_id")?,
        conditional_tick: r.bool_str("conditional_tick")?,
        system: r.bool_str("system")?,
        aura_slave_buff_id: r.u32_or("aura_slave_buff_id", 0)?,
        non_pushable: r.bool_str("non_pushable")?,
        active_weapon_id: r.u32("active_weapon_id")?,
        max_charge: r.i32("max_charge")?,
        detect_stealth: r.bool_str("detect_stealth")?,
        remove_on_exempt: r.bool_str("remove_on_exempt")?,
        remove_on_land: r.bool_str("remove_on_land")?,
        gliding: r.bool_str("gliding")?,
        gliding_rotate_speed: r.i32("gliding_rotate_speed")?,
        gliding_lift_height: r.f32_or("gliding_lift_height", 0.0)?,
        gliding_lift_speed: r.f32_or("gliding_lift_speed", 0.0)?,
        gliding_lift_duration: r.f32_or("gliding_lift_duration", 0.0)?,
        knockdown: r.bool_str("knock_down")?,
        tick_area_exclude_source: r.bool_str("tick_area_exclude_source")?,
        fall_damage_immune: r.bool_str("fall_damage_immune")?,
        kind: r.i32("kind_id")?,
        transform_buff_id: r.u32_or("transform_buff_id", 0)?,
        blank_minded: r.bool_str("blank_minded")?,
        fastened: r.bool_str("fastened")?,
        slave_applicable: r.bool_str("slave_applicable")?,
        pacifist: r.bool_str("pacifist")?,
        remove_on_interaction: r.bool_str("remove_on_interaction")?,
        crime: r.bool_str("crime")?,
        remove_on_unmount: r.bool_str("remove_on_unmount")?,
        aura_child_only: r.bool_str("aura_child_only")?,
        remove_on_mount: r.bool_str("remove_on_mount")?,
        remove_on_start_skill: r.bool_str("remove_on_start_skill")?,
        sprint_motion: r.bool_str("sprint_motion")?,
        telescope_range: r.f32("telescope_range")?,
        mainhand_tool_id: parse_tool(&r.string_or("mainhand_tool_id", "0")?, "mainhand_tool_id")?,
        offhand_tool_id: parse_tool(&r.string_or("offhand_tool_id", "0")?, "offhand_tool_id")?,
        tick_mainhand_tool_id: parse_tool(
            &r.string_or("tick_mainhand_tool_id", "0")?,
            "tick_mainhand_tool_id",
        )?,
        tick_offhand_tool_id: parse_tool(
            &r.string_or("tick_offhand_tool_id", "0")?,
            "tick_offhand_tool_id",
        )?,
        tick_level_mana_cost: r.f32("tick_level_mana_cost")?,
        walk_only: r.bool_str("walk_only")?,
        cannot_jump: r.bool_str("cannot_jump")?,
        crowd_buff_id: r.u32_or("crowd_buff_id", 0)?,
        crowd_radius: r.f32("crowd_radius")?,
        crowd_number: r.i32("crowd_number")?,
        evade_telescope: r.bool_str("evade_telescope")?,
        transfer_telescope_range: r.f32("transfer_telescope_range")?,
        remove_on_attack_spell_dot: r.bool_str("remove_on_attack_spell_dot")?,
        remove_on_attack_etc_dot: r.bool_str("remove_on_attack_etc_dot")?,
        remove_on_attack_buff_trigger: r.bool_str("remove_on_attack_buff_trigger")?,
        remove_on_attack_etc: r.bool_str("remove_on_attack_etc")?,
        remove_on_attacked_spell_dot: r.bool_str("remove_on_attacked_spell_dot")?,
        remove_on_attacked_etc_dot: r.bool_str("remove_on_attacked_etc_dot")?,
        remove_on_attacked_buff_trigger: r.bool_str("remove_on_attacked_buff_trigger")?,
        remove_on_attacked_etc: r.bool_str("remove_on_attacked_etc")?,
        remove_on_damage_spell_dot: r.bool_str("remove_on_damage_spell_dot")?,
        remove_on_damage_etc_dot: r.bool_str("remove_on_damage_etc_dot")?,
        remove_on_damage_buff_trigger: r.bool_str("remove_on_damage_buff_trigger")?,
        remove_on_damage_etc: r.bool_str("remove_on_damage_etc")?,
        remove_on_damaged_spell_dot: r.bool_str("remove_on_damaged_spell_dot")?,
        remove_on_damaged_etc_dot: r.bool_str("remove_on_damaged_etc_dot")?,
        remove_on_damaged_buff_trigger: r.bool_str("remove_on_damaged_buff_trigger")?,
        remove_on_damaged_etc: r.bool_str("remove_on_damaged_etc")?,
        owner_only: r.bool_str("owner_only")?,
        remove_on_auto_attack: r.bool_str("remove_on_autoattack")?,
        save_rule_id: r.u32("save_rule_id")?,
        anti_stealth: r.bool_str("anti_stealth")?,
        scale: r.f32("scale")?,
        scale_duration: r.f32("scaleDuration")?,
        immune_except_creator: r.bool_str("immune_except_creator")?,
        immune_except_skill_tag_id: r.u32_or("immune_except_skill_tag_id", 0)?,
        find_school_of_fish_range: r.f32("find_school_of_fish_range")?,
        anim_action_id: r.u32_or("anim_action_id", 0)?,
        dead_applicable: r.bool_str("dead_applicable")?,
        tick_area_use_origin_source: r.bool_str("tick_area_use_origin_source")?,
        real_time: r.bool_str("real_time")?,
        do_not_remove_by_other_skill_controller: r
            .bool_str("do_not_remove_by_other_skill_controller")?,
        cooldown_skill_id: r.u32_or("cooldown_skill_id", 0)?,
        cooldown_skill_time: r.i32("cooldown_skill_time")?,
        mana_burn_immune: r.bool_str("mana_burn_immune")?,
        freeze_ship: r.bool_str("freeze_ship")?,
        crowd_friendly: r.bool_str("crowd_friendly")?,
        crowd_hostile: r.bool_str("crowd_hostile")?,
        tick_effects: Vec::new(),
        bonuses: Vec::new(),
        dynamic_bonuses: Vec::new(),
    })
}

/// `SELECT * FROM buffs` in table order.
pub fn load_buffs(conn: &Connection) -> rusqlite::Result<Vec<BuffTemplate>> {
    let mut out = Vec::new();
    for_each_row(conn, "SELECT * FROM buffs", [], |r| {
        out.push(read_buff(r)?);
        Ok(())
    })?;
    Ok(out)
}

/// A `unit_modifiers` / `dynamic_unit_modifiers` attribute. The C# casts the byte to the enum
/// without validation; values the Rust enum lacks are skipped (they match no getter in C#).
pub fn unit_attribute(v: u8) -> Option<UnitAttribute> {
    UnitAttribute::from_u8(v)
}

/// `BonusTemplate` from a unit_modifiers row.
pub fn read_bonus(r: &SqlRow<'_, '_>) -> rusqlite::Result<Option<BonusTemplate>> {
    let attr = r.u8("unit_attribute_id")?;
    let Some(attribute) = unit_attribute(attr) else {
        return Ok(None);
    };
    Ok(Some(BonusTemplate {
        attribute,
        modifier_type: UnitModifierType::from_u8(r.u8("unit_modifier_type_id")?),
        value: r.i32("value")?,
        linear_level_bonus: r.i32("linear_level_bonus")?,
    }))
}

/// `SkillControllerTemplate`.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct SkillControllerTemplate {
    pub id: u32,
    pub kind_id: u32,
    pub active_weapon_id: u8,
    pub value: [i32; 15],
}

pub fn read_skill_controller(r: &SqlRow<'_, '_>) -> rusqlite::Result<SkillControllerTemplate> {
    let mut value = [0i32; 15];
    for (i, v) in value.iter_mut().enumerate() {
        *v = r.i32_or(&format!("value{}", i + 1), 0)?;
    }
    Ok(SkillControllerTemplate {
        id: r.u32("id")?,
        kind_id: r.u32("kind_id")?,
        active_weapon_id: r.u8("active_weapon_id")?,
        value,
    })
}
