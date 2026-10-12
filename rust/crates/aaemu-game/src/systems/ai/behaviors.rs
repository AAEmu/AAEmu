//! `Models/Game/AI/v2/Framework/Behavior` (skill picking, aggression / alert checks, aggro
//! help), `Behaviors/BaseCombatBehavior` (`MoveInRange`, `UpdateTarget`, `ShouldReturn`,
//! `RefreshSkillQueue`, ...) and every concrete behavior of `Behaviors/**` (Common, Archer,
//! BigMonster, Flytrap, WildBoar).
//!
//! Each C# behavior method is a function taking the [`AiCtx`] and the behavior's
//! [`BehaviorKind`] (its instance); the instance fields live in [`BehaviorSlot`] and are only
//! borrowed for single statements, so nested `Ai.GoToX()` calls behave like the C# ones.

use std::sync::Arc;

use rand::Rng;

use crate::models::movement::{GameStanceType, MoveTypeAlertness};
use crate::packets::world::SCUnitPointsPacket;
use crate::systems::npcs::npc::packets::{
    SCAggroTargetChangedPacket, SCNpcChatMessagePacket, SCUnitModelPostureChangedPacket,
};
use crate::systems::npcs::npc::spawner::random_element_by_weight;
use crate::systems::skills::controllers::{calculate_distance, Vec3};
use crate::systems::skills::state::AggroKind;
use crate::systems::skills::types::{skill_result, KillReason, SkillCastTarget, SkillCaster};
use crate::systems::skills::{buffs, combat as skills_combat};
use crate::world::{now_ms, WorldObject};

use super::combat::{self, add_unit_aggro, can_attack, clear_aggro_of_unit, set_in_battle};
use super::framework::{AiCtx, ArcherPhase, BehaviorKind, NpcIntervalSite};
use super::npc::{self, MoveAi};
use super::params::{AiParamType, AiParams, AiParamsKind, AiSkill, AiSkillList, SkillList};
use super::path_handler::{AiPathPoint, AiPathPointAction, PathStep};
use super::skills::{self as ai_skills, SkillUseConditionKind};

use BehaviorKind as B;

// ===========================================================================================
// dispatch
// ===========================================================================================

/// `behavior.Enter()`.
pub fn enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    match k {
        B::Alert => alert_enter(c, k),
        B::AlmightyAttack => almighty_attack_enter(c, k),
        B::Attack => attack_enter(c, k),
        B::Dead => dead_enter(c, k),
        B::Despawning => despawning_enter(c),
        B::DoNothing | B::Dummy => {
            npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
            npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
        }
        B::FollowPath => follow_path_enter(c, k),
        B::FollowUnit => {
            npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
            npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
            c.st(k).enter = true;
        }
        B::HoldPosition => hold_position_enter(c, k),
        B::Idle => idle_enter(c, k),
        B::ReturnState => return_state_enter(c, k),
        B::Roaming => {
            ai_skills::interrupt_skills(c);
            npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
            npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
            c.st(k).enter = true;
        }
        B::RunCommandSet => {
            npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
            npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
        }
        B::Spawning => spawning_enter(c, k),
        B::Talk => talk_enter(c, k),
        B::ArcherAttack => archer_attack_enter(c, k),
        B::BigMonsterAttack => big_monster_attack_enter(c, k),
        B::FlytrapAlert => flytrap_alert_enter(c, k),
        B::FlytrapAttack => flytrap_attack_enter(c, k),
        B::WildBoarAttack => wild_boar_attack_enter(c, k),
    }
}

/// `behavior.Tick(delta)` (`delta_ms` = the AI manager's tick delta).
pub fn tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    match k {
        B::Alert => alert_tick(c, k),
        B::AlmightyAttack => almighty_attack_tick(c, k, delta_ms),
        B::Attack => attack_tick(c, k, delta_ms),
        B::Dead => {
            if c.st(k).enter && c.npc.data().unit.hp == 0 {
                c.ai.already_targeted = false;
            }
        }
        B::Despawning | B::DoNothing | B::Dummy => {}
        B::FollowPath => follow_path_tick(c, k, delta_ms),
        B::FollowUnit => follow_unit_tick(c, k, delta_ms),
        B::HoldPosition => hold_position_tick(c, k),
        B::Idle => idle_tick(c, k),
        B::ReturnState => return_state_tick(c, k, delta_ms),
        B::Roaming => roaming_tick(c, k, delta_ms),
        B::RunCommandSet => run_command_set_tick(c, delta_ms),
        B::Spawning => spawning_tick(c, k),
        B::Talk => talk_tick(c, k),
        B::ArcherAttack => archer_attack_tick(c, k, delta_ms),
        B::BigMonsterAttack => big_monster_attack_tick(c, k, delta_ms),
        B::FlytrapAlert => {
            if c.st(k).enter {
                check_aggression(c, k);
            }
        }
        B::FlytrapAttack => flytrap_attack_tick(c, k, delta_ms),
        B::WildBoarAttack => wild_boar_attack_tick(c, k, delta_ms),
    }
}

/// `behavior.Exit()`.
pub fn exit(c: &mut AiCtx<'_>, k: BehaviorKind) {
    match k {
        B::Alert => {
            let posture = posture_packet(c, true);
            npc::broadcast_packet(c, &posture);
            c.st(k).enter = false;
        }
        B::AlmightyAttack => almighty_attack_exit(c, k),
        B::ReturnState => {
            let posture = posture_packet(c, true);
            npc::broadcast_packet(c, &posture);
            buffs::remove_buff(c.server, &c.obj, npc::BUFF_NPC_RETURN);
            c.st(k).enter = false;
        }
        B::HoldPosition => {
            let s = c.st(k);
            if s.initialized {
                s.initialized = false;
                s.following_npc = false;
            }
        }
        B::Idle | B::Talk => {
            let s = c.st(k);
            if s.initialized {
                s.initialized = false;
            }
        }
        B::Despawning | B::DoNothing | B::Dummy | B::RunCommandSet => {}
        _ => c.st(k).enter = false,
    }
}

// ===========================================================================================
// shared helpers (Behavior / BaseCombatBehavior)
// ===========================================================================================

fn posture_packet(c: &AiCtx<'_>, activate: bool) -> SCUnitModelPostureChangedPacket {
    let n = c.npc.data();
    SCUnitModelPostureChangedPacket {
        obj_id: c.npc.obj_id,
        posture: crate::models::unit::UnitData::model_posture_type(&*n),
        has_loot_left: n.unit.has_loot_left,
        anim_action_id: n.anim_action_id,
        activate_animation: activate,
    }
}

/// `Ai.Owner.BroadcastPacket(new SCUnitModelPostureChangedPacket(Ai.Owner, AnimActionId, x), false)`.
fn broadcast_posture(c: &AiCtx<'_>, activate: bool) {
    let p = posture_packet(c, activate);
    npc::broadcast_packet(c, &p);
}

/// `this.CheckInterval(interval)` at call site `site` of behavior instance `k`.
fn behavior_check_interval(c: &mut AiCtx<'_>, k: BehaviorKind, site: u8, interval_ms: i64) -> bool {
    let now = now_ms();
    let s = c.st(k);
    let next = s.intervals.get(&site).copied().unwrap_or(now);
    if next > now {
        return false;
    }
    s.intervals.insert(site, now + interval_ms);
    true
}

/// `Behavior.Throttle(interval = 0.1)`.
fn throttle(c: &mut AiCtx<'_>, k: BehaviorKind) -> bool {
    let now = now_ms();
    let s = c.st(k);
    if let Some(last) = s.last_tick {
        if now - last < 100 {
            return false;
        }
    }
    s.last_tick = Some(now);
    true
}

fn template_scalars(c: &AiCtx<'_>) -> TemplateScalars {
    match npc::template(c.server, &c.npc) {
        Some(t) => TemplateScalars {
            aggression: t.aggression,
            attack_start_range_scale: t.attack_start_range_scale,
            sight_range_scale: t.sight_range_scale,
            sight_fov_scale: t.sight_fov_scale,
            base_skill_id: t.base_skill_id,
            base_skill_delay: t.base_skill_delay,
            base_skill_strafe: t.base_skill_strafe,
            use_range_mod: t.use_range_mod,
            skills_count: t.skills.len(),
            return_distance: t.return_distance,
            absolute_return_distance: t.absolute_return_distance,
            npc_ai_param_id: t.npc_ai_param_id,
            npc_nickname_id: t.npc_nickname_id,
            scale: t.scale,
        },
        None => TemplateScalars::default(),
    }
}

/// The `NpcTemplate` values the behaviors read.
#[derive(Debug, Clone, Copy, Default)]
struct TemplateScalars {
    aggression: bool,
    attack_start_range_scale: f32,
    sight_range_scale: f32,
    sight_fov_scale: f32,
    base_skill_id: i32,
    base_skill_delay: f32,
    base_skill_strafe: bool,
    use_range_mod: bool,
    skills_count: usize,
    return_distance: f32,
    absolute_return_distance: f32,
    npc_ai_param_id: i32,
    npc_nickname_id: i32,
    scale: f32,
}

/// `Ai.Owner.Template.AiParams`.
fn template_ai_params(c: &AiCtx<'_>) -> Option<Arc<AiParams>> {
    let id = template_scalars(c).npc_ai_param_id;
    c.server.game_data.systems.ai.ai.ai_params_for_id(id as u32)
}

fn delta_seconds(delta_ms: i64) -> f64 {
    // `delta.Milliseconds / 1000.0`: the millisecond component of the TimeSpan.
    (delta_ms.rem_euclid(1000)) as f64 / 1000.0
}

fn move_ai(c: &AiCtx<'_>) -> Option<MoveAi> {
    Some(MoveAi {
        hold_or_idle: matches!(c.ai.current_behavior(), Some(B::HoldPosition | B::Idle)),
    })
}

/// `Ai.Owner.MoveTowards(target, distance, flags, rangeTolerance)`.
pub fn npc_move_towards(
    c: &AiCtx<'_>,
    target: Vec3,
    distance: f32,
    flags: u8,
    tolerance: f32,
) -> bool {
    npc::move_towards(
        c.server,
        &c.npc,
        move_ai(c),
        target,
        distance,
        flags,
        tolerance,
    )
}

/// `Ai.Owner.BaseMoveSpeed`.
fn base_move_speed(c: &AiCtx<'_>) -> f32 {
    npc::base_move_speed(
        c.server,
        &c.npc,
        c.ai.current_behavior() == Some(B::ReturnState),
    )
}

/// `Ai.Owner.SetTarget(other)`.
pub fn set_target(c: &mut AiCtx<'_>, other: Option<&WorldObject>) {
    npc::set_target(c.server, &c.npc, other);
    c.ai.already_targeted = other.is_some();
}

/// `Ai.Owner.CurrentTarget` (as a world object).
fn current_target(c: &AiCtx<'_>) -> Option<WorldObject> {
    let id = c.npc.data().unit.current_target;
    if id == 0 {
        return None;
    }
    if id == c.npc.obj_id {
        return Some(c.obj.clone());
    }
    c.server.world.object(id)
}

fn owner_pos(c: &AiCtx<'_>) -> Vec3 {
    npc::world_pos(c.server, &c.obj)
}

/// `Behavior.PickSkillAndUseIt(kind, target, targetDist)`.
pub fn pick_skill_and_use_it(
    c: &mut AiCtx<'_>,
    k: BehaviorKind,
    kind: SkillUseConditionKind,
    target: &WorldObject,
    target_dist: f32,
) -> u8 {
    let mut res = skill_result::INVALID_SKILL;
    let t = template_scalars(c);
    let mut skills = ai_skills::template_skills(c, kind);
    if !skills.is_empty() {
        skills.retain(|s| {
            !ai_skills::on_cooldown(c, *s)
                && c.server
                    .game_data
                    .systems
                    .skills
                    .skill(*s)
                    .is_some_and(|tpl| {
                        (target_dist >= tpl.min_range as f32 && target_dist <= tpl.max_range as f32)
                            || ai_skills::is_self_target(tpl)
                    })
        });
    }
    if target_dist == 0.0 && kind == SkillUseConditionKind::InIdle {
        // This SkillTargetType.Self & SkillUseConditionKind.InIdle
        if skills.is_empty() {
            return res;
        }
        let skill_self_id = skills[rand::thread_rng().gen_range(0..skills.len())];
        let Some(skill) = ai_skills::new_skill(c, skill_self_id) else {
            return res;
        };
        let mut delay1 = (t.base_skill_delay * 1000.0) as i64;
        if t.base_skill_delay == 0.0 {
            delay1 = rand::thread_rng().gen_range(10000..13000);
        }
        if behavior_check_interval(c, k, 1, delay1) {
            tracing::debug!(
                "PickSkillAndUseIt:UseSelfSkill Owner.ObjId {}, SkillId {skill_self_id}",
                c.npc.obj_id
            );
            res = use_skill(c, k, skill, Some(target), 0.0);
        }
        return res;
    }
    // This SkillUseConditionKind.InCombat
    let mut picked = t.base_skill_id as u32;
    if !skills.is_empty() {
        picked = skills[rand::thread_rng().gen_range(0..skills.len())];
    }
    // Hackfix for Melee attack. Needs to look at the held weapon (if any) or default to 3m
    if picked == 2 && target_dist > 4.0 {
        return skill_result::TOO_FAR_RANGE;
    }
    let Some(skill) = ai_skills::new_skill(c, picked) else {
        // new Skill(null) throws NullReferenceException in the C#.
        tracing::error!(
            "PickSkillAndUseIt: skill {picked} of npc {} has no template",
            c.npc.obj_id
        );
        return res;
    };
    set_weapon_range(c, k, &skill, target);
    let mut delay2 = (t.base_skill_delay * 1000.0) as i64;
    if t.base_skill_delay == 0.0 {
        delay2 = rand::thread_rng().gen_range(1500..1550);
    }
    if behavior_check_interval(c, k, 2, delay2) {
        tracing::debug!(
            "PickSkillAndUseIt:UseSkill Owner.ObjId {}, SkillId {picked} on Target {}",
            c.npc.obj_id,
            target.obj_id()
        );
        res = use_skill(c, k, skill, Some(target), 0.0);
    }
    res
}

/// `Behavior.UseSkill(skill, target, delay)`.
pub fn use_skill(
    c: &mut AiCtx<'_>,
    k: BehaviorKind,
    skill: crate::systems::skills::state::SkillRef,
    target: Option<&WorldObject>,
    delay: f32,
) -> u8 {
    let Some(target) = target else {
        return skill_result::NO_TARGET;
    };
    if ai_skills::on_cooldown(c, skill.id) {
        return skill_result::COOLDOWN_TIME;
    }
    let target_dist = npc::distance_to(c.server, &c.obj, target, false);
    if target_dist < skill.template.min_range as f32 {
        return skill_result::TOO_CLOSE_RANGE;
    }
    if target_dist > skill.template.max_range as f32 {
        return skill_result::TOO_FAR_RANGE;
    }
    c.st(k).next_time_to_delay = delay;
    let caster = SkillCaster::Unit(c.npc.obj_id);
    let mut cast_target = if ai_skills::is_pos_target(&skill.template) {
        let p = crate::systems::skills::geometry::world_pos(
            c.server,
            &crate::systems::skills::geometry::Target::Obj(c.obj.clone()),
        );
        SkillCastTarget::Position {
            obj_id: c.npc.obj_id,
            x: p.x,
            y: p.y,
            z: p.z,
            rot: p.rot_z.to_degrees(),
            obj_id1: 0,
            obj_id2: 0,
        }
    } else {
        SkillCastTarget::Unit(target.obj_id())
    };
    // skill.Callback = OnSkillEnded
    ai_skills::set_skill_ended_callback(c, &skill, k);
    let result = ai_skills::use_skill(c, &skill, caster, &mut cast_target, false);
    // fix the eastward turn when using SelfSkill
    if !ai_skills::is_self_target(&skill.template) && result == skill_result::SUCCESS {
        let tp = npc::world_pos(c.server, target);
        npc::look_towards(c.server, &c.npc, tp, 4);
    }
    result
}

/// `Behavior.OnSkillEnded()` (`_delayEnd = now + _nextTimeToDelay`).
pub fn on_skill_ended(c: &mut AiCtx<'_>, k: BehaviorKind) {
    let s = c.st(k);
    let add = f64::from(s.next_time_to_delay) * 1000.0;
    if add.is_finite() {
        s.delay_end = now_ms() + add as i64;
    }
}

/// `Behavior.OnEnemySeen(target)`.
fn on_enemy_seen(c: &mut AiCtx<'_>, target: &WorldObject) {
    add_unit_aggro(c.server, &c.obj, AggroKind::Damage, target, 1);
    c.go_to_combat();
}

/// The units sorted by 3D distance (`CheckAggression` / `CheckAlert`).
fn units_by_distance(c: &AiCtx<'_>, radius: f32) -> Vec<(WorldObject, f32)> {
    let me = c.obj.clone();
    let mut v: Vec<(WorldObject, f32)> = npc::around(c.server, &me, radius, false)
        .into_iter()
        .filter(WorldObject::is_unit)
        .map(|u| {
            let d = npc::distance_raw(c.server, &me, &u, true);
            (u, d)
        })
        .collect();
    v.sort_by(|a, b| a.1.partial_cmp(&b.1).unwrap_or(std::cmp::Ordering::Equal));
    v
}

fn unit_dead(u: &WorldObject) -> bool {
    u.with_unit(|x| x.unit().is_dead() || x.unit().hp <= 0)
        .unwrap_or(false)
}

/// The shared scan of `CheckAggression` / `CheckAlert`.
fn scan_for_enemy(
    c: &AiCtx<'_>,
    radius: f32,
    gap_fly: f32,
    gap_ground: f32,
    near_mul: f32,
) -> Option<WorldObject> {
    let t = template_scalars(c);
    let (can_fly, scale) = {
        let n = c.npc.data();
        (n.can_fly, n.unit.scale)
    };
    let model_size = npc::model_size(c.server, &c.obj);
    let my_z = owner_pos(c).z;
    for (unit, range) in units_by_distance(c, radius) {
        if unit_dead(&unit) {
            continue; // not counting dead Npc
        }
        // Arbitrary value
        let max_height_gap = if can_fly {
            model_size * scale * gap_fly
        } else {
            model_size * scale * gap_ground
        };
        let uz = npc::world_pos(c.server, &unit).z;
        // Check if in front, and not too far up or down
        if npc::is_front(c.server, &c.obj, &unit, f64::from(t.sight_fov_scale))
            && (my_z - uz).abs() < max_height_gap
        {
            if can_attack(c.server, &c.obj, &unit)
                && (range < 1.0 || npc::can_see_target(c.server, &unit))
            {
                return Some(unit);
            }
        } else if range < near_mul * t.sight_range_scale
            && can_attack(c.server, &c.obj, &unit)
            && (range < 0.5 || npc::can_see_target(c.server, &unit))
        {
            return Some(unit);
        }
    }
    None
}

/// `Behavior.CheckAggression()`.
pub fn check_aggression(c: &mut AiCtx<'_>, _k: BehaviorKind) -> bool {
    let t = template_scalars(c);
    if !t.aggression {
        return false;
    }
    match scan_for_enemy(c, t.attack_start_range_scale * 10.0, 3.5, 1.5, 1.5) {
        Some(unit) => {
            on_enemy_seen(c, &unit);
            true
        }
        None => false,
    }
}

/// `Behavior.OnEnemyAlert(target)`.
fn on_enemy_alert(c: &mut AiCtx<'_>, target: &WorldObject) {
    let now = now_ms();
    c.ai.alert_end_time = now + 5000;
    c.ai.next_alert_check_time = now + 7000;
    set_target(c, Some(target));
    c.go_to_alert();
}

/// `Behavior.CheckAlert()`.
pub fn check_alert(c: &mut AiCtx<'_>, _k: BehaviorKind) -> bool {
    if c.ai.next_alert_check_time > now_ms() {
        return false;
    }
    // Don't do alerts if already in combat
    if c.npc.data().unit.is_in_battle {
        return false;
    }
    let t = template_scalars(c);
    match scan_for_enemy(c, t.sight_range_scale * 15.0, 4.0, 1.75, 2.0) {
        Some(unit) => {
            on_enemy_alert(c, &unit);
            true
        }
        None => false,
    }
}

/// `Behavior.UpdateAggroHelp(abuser, radius)`.
pub fn update_aggro_help(c: &mut AiCtx<'_>, abuser: &WorldObject, radius: f32) {
    let t = template_scalars(c);
    let npcs: Vec<WorldObject> =
        npc::around(c.server, &c.obj, t.attack_start_range_scale * radius, false)
            .into_iter()
            .filter(|o| o.as_npc().is_some())
            .collect();
    let my_faction = c.npc.data().unit.faction_id;
    for other in npcs {
        let Some(other_npc) = other.as_npc().cloned() else {
            continue;
        };
        let Some(ot) = npc::template(c.server, &other_npc) else {
            continue;
        };
        let (in_battle, faction) = {
            let d = other_npc.data();
            (d.unit.is_in_battle, d.unit.faction_id)
        };
        if in_battle || !ot.accept_aggro_link {
            continue;
        }
        if npc::distance_to(c.server, &other, &c.obj, false) > ot.aggro_link_help_dist {
            continue;
        }
        let need_help = if ot.aggression && ot.aggro_link_special_rule_id == 0 {
            true
        } else {
            if !(ot.aggro_link_sight_check && npc::can_see_target(c.server, abuser)) {
                continue;
            }
            let relation = || {
                crate::systems::skills::relation::relation_to(
                    c.server,
                    &crate::systems::skills::geometry::Target::Obj(other.clone()),
                    &crate::systems::skills::geometry::Target::Obj(c.obj.clone()),
                )
            };
            use crate::systems::skills::types::RelationState as R;
            match ot.aggro_link_special_rule_id {
                1 => faction == my_faction,
                2 => relation() == R::Friendly,
                3 => relation() == R::Neutral,
                4 => true,
                _ => false,
            }
        };
        if !need_help {
            continue;
        }
        // npc.Ai.Owner.AddUnitAggro(...) + npc.Ai.OnAggroTargetChanged(): an NPC without an
        // AI throws a NullReferenceException there, which ends this loop.
        if !super::manager::has_ai(c.server, other_npc.obj_id) {
            tracing::error!(
                "UpdateAggroHelp: npc {} has no AI (NullReferenceException in C#)",
                other_npc.obj_id
            );
            return;
        }
        add_unit_aggro(c.server, &other, AggroKind::Damage, abuser, 1);
        super::manager::post(c.server, other_npc.obj_id, |o| o.on_aggro_target_changed());
    }
}

/// `Behavior.SetWeaponRange(skill, target)`.
fn set_weapon_range(
    c: &mut AiCtx<'_>,
    k: BehaviorKind,
    skill: &crate::systems::skills::state::SkillRef,
    target: &WorldObject,
) {
    let skill_range = crate::systems::skills::skill::apply_skill_modifiers(
        c.server,
        skill,
        &c.obj,
        crate::models::unit::SkillAttribute::Range,
        f64::from(skill.template.max_range),
    );
    let mut min_range_check = f64::from(skill.template.min_range);
    let mut max_range_check = skill_range;
    if skill.template.weapon_slot_for_range_id > 0 {
        let (mut min_w, mut max_w) = (0.0f32, 3.0f32); // Fist default
        if let Some(h) =
            ai_skills::weapon_holdable(c, target, skill.template.weapon_slot_for_range_id)
        {
            min_w = h.0;
            max_w = h.1;
        }
        min_range_check = f64::from(min_w);
        max_range_check = f64::from(max_w);
    }
    let s = c.st(k);
    s.min_weapon_range = min_range_check as f32;
    s.max_weapon_range = max_range_check as f32;
}

/// `Behavior.CheckFollowPath()`.
fn check_follow_path(c: &AiCtx<'_>) -> bool {
    c.ai.path_handler.has_path_movement_data()
}

/// `BaseCombatBehavior.CanStrafe`.
fn can_strafe(c: &mut AiCtx<'_>, k: BehaviorKind) -> bool {
    let s = c.st(k);
    now_ms() > s.delay_end || s.strafe_during_delay
}

/// `BaseCombatBehavior.IsUsingSkill`.
fn is_using_skill(c: &AiCtx<'_>) -> bool {
    let n = c.npc.data();
    n.unit.skill_state.skill_task.is_some() || n.unit.skill_state.active_plot.is_some()
}

/// `BaseCombatBehavior.CanUseSkill`.
fn can_use_skill(c: &mut AiCtx<'_>, k: BehaviorKind) -> bool {
    if is_using_skill(c) {
        return false;
    }
    if npc::skill_controller_running(&c.obj) {
        return false;
    }
    if npc::is_silenced(c.server, &c.obj) {
        return false;
    }
    let now = now_ms();
    let delay_end = c.st(k).delay_end;
    // `!Ai.Owner.IsGlobalCooldownDone` where IsGlobalCooldownDone = GlobalCooldown > now
    let gcd = c.npc.data().unit.global_cooldown_ms;
    now >= delay_end && gcd <= now
}

/// `BaseCombatBehavior.ShouldReturn`.
fn should_return(c: &AiCtx<'_>) -> bool {
    // Never trigger return while a SkillController is actively driving the NPC.
    if npc::skill_controller_running(&c.obj) {
        return false;
    }
    let t = template_scalars(c);
    let mut return_distance = 50.0f32;
    let mut absolute_return_distance = 200.0f32;
    if t.return_distance > 0.0 {
        return_distance = t.return_distance;
    }
    if t.absolute_return_distance > 0.0 {
        absolute_return_distance = t.absolute_return_distance;
    }
    let Some(target) = current_target(c) else {
        return true; // no target, returning
    };
    let me = owner_pos(c);
    let distance_to_target = calculate_distance(me, npc::world_pos(c.server, &target), true);
    let distance_to_idle = calculate_distance(me, c.ai.idle_position, true);
    let mut res = distance_to_target > return_distance || distance_to_idle > return_distance;
    if res {
        // if it's greater, then we need a teleport to the spawn point
        res = distance_to_idle <= absolute_return_distance;
    }
    res
}

fn geo_data_mode(c: &AiCtx<'_>) -> bool {
    c.server.config.world.geo_data_mode
}

/// `BaseCombatBehavior.UpdateTarget()`: the abuser with the most aggro becomes the target.
fn update_target(c: &mut AiCtx<'_>) -> bool {
    if npc::skill_controller_running(&c.obj) {
        return current_target(c).is_some();
    }
    let abusers = sorted_abusers(c);
    for abuser in abusers {
        let Some(abuser) = abuser else { continue };
        if geo_data_mode(c) {
            if npc::unit_is_visible(c.server, &c.obj, &abuser) && !npc::is_dead(&abuser) {
                let current_aggro = c.npc.data().current_aggro_target;
                if current_aggro != abuser.obj_id() && !c.ai.already_targeted {
                    find_path(c, &abuser);
                }
                npc::set_current_aggro_target(&c.npc, Some(&abuser));
                set_target(c, Some(&abuser));
                update_aggro_help(c, &abuser, 200.0);
                return true;
            }
        } else if npc::unit_is_visible(c.server, &c.obj, &abuser) && !npc::is_dead(&abuser) {
            // check that such a Npc is in the database
            let in_world = abuser.obj_id() > 0
                && super::npc::world_unit(c.server, &c.obj, abuser.obj_id()).is_some();
            if !in_world {
                continue;
            }
            npc::set_current_aggro_target(&c.npc, Some(&abuser));
            set_target(c, Some(&abuser));
            update_aggro_help(c, &abuser, 200.0);
            return true;
        }
        clear_aggro_of_unit(c.server, &c.obj, &abuser);
    }
    // Only remove CurrentTarget if either no unit selected, or if target is already dead
    match current_target(c) {
        Some(t) if t.is_unit() => {
            let hp = t.with_unit(|u| u.unit().hp).unwrap_or(0);
            if hp <= 0 || npc::is_dead(&t) {
                npc::set_current_aggro_target(&c.npc, None);
                set_target(c, None);
            }
        }
        _ => {
            npc::set_current_aggro_target(&c.npc, None);
            set_target(c, None);
        }
    }
    false
}

/// `AggroTable.Values.OrderByDescending(TotalAggro).Select(Owner)` (missing objects stay as
/// `None` and are skipped).
fn sorted_abusers(c: &AiCtx<'_>) -> Vec<Option<WorldObject>> {
    let mut entries: Vec<(u32, i32)> = c
        .npc
        .data()
        .unit
        .skill_state
        .aggro_table
        .iter()
        .map(|(k, a)| (*k, a.total()))
        .collect();
    entries.sort_by_key(|e| std::cmp::Reverse(e.1));
    entries
        .into_iter()
        .map(|(id, _)| c.server.world.object(id))
        .collect()
}

/// `Npc.FindPath(abuser)` (geodata A*).
fn find_path(c: &mut AiCtx<'_>, abuser: &WorldObject) {
    super::geodata::npc_find_path(c, abuser);
}

/// `BaseCombatBehavior.CheckPipeName()`.
fn check_pipe_name(c: &mut AiCtx<'_>, k: BehaviorKind) {
    let (pipe, phase) = {
        let s = c.st(k);
        (s.pipe_name.clone(), s.phase_type)
    };
    if pipe == "phase_dragon_ground" || phase == 1 {
        // try to find Z first in GeoData, and then in HeightMaps, if not found, leave Z as it is
        let pos = owner_pos(c);
        let z = super::geodata::geo_data_height(c.server, &c.obj, pos);
        c.obj.with_transform(|t| t.set_height(z));
    } else if pipe == "phase_dragon_fly_hovering" || phase == 2 {
        c.obj.with_transform(|t| t.z += 15.0);
        npc::stop_movement(c.server, &c.npc);
    } else if pipe == "phase_dragon_fly_path" {
        c.go_to_follow_path();
    }
}

/// `BaseCombatBehavior.MoveInRange(target, delta)`.
fn move_in_range(c: &mut AiCtx<'_>, k: BehaviorKind, target: Option<&WorldObject>, delta_ms: i64) {
    if npc::is_displaced(&c.obj) {
        return;
    }
    if npc::is_rooted(c.server, &c.obj) || npc::is_dead(&c.obj) {
        return;
    }
    if npc::is_shackled_or_snared(c.server, &c.obj) {
        return;
    }
    if npc::skill_controller_running(&c.obj) {
        return;
    }
    let mut speed = npc::real_movement_speed(&c.obj, f64::from(base_move_speed(c)));
    let move_flags = npc::real_movement_flags(speed);
    speed *= delta_seconds(delta_ms);
    // Sport-fish direction plots provide bounded movement through SkillController.
    // The former C# experimental global-axis drift added movement after each controller
    // and changed the heading used by the next relative plot (Dive even moved along Y).
    // Between authored controllers, stop without changing fish position or rotation.
    if buffs::check_buffs_by_tag(c.server, &c.obj, npc::tags::FISH) {
        npc::stop_movement(c.server, &c.npc);
        return;
    }
    let t = template_scalars(c);
    let mut range = t.attack_start_range_scale;
    let max_weapon_range = c.st(k).max_weapon_range;
    if t.use_range_mod && max_weapon_range != 0.0 {
        range *= max_weapon_range;
    }
    if t.base_skill_id == 2 && t.skills_count == 0 && range == 4.0 {
        range -= 1.0; // Fix that ID=7927, Plateau Earth Elemental can hit with a melee attack
    }
    let mut distance_to_target = match target {
        Some(t) => npc::distance_to(c.server, &c.obj, t, true),
        None => 0.0,
    };
    let Some(target) = target else {
        npc::stop_movement(c.server, &c.npc);
        return;
    };
    let target_pos = npc::world_pos(c.server, target);
    if geo_data_mode(c) {
        // If not at target position (model size error margin), calculate a new route
        let end = c.ai.path_node.end_point_pos;
        let model_size = npc::model_size(c.server, &c.obj);
        let d = ((end.x - target_pos.x).powi(2)
            + (end.y - target_pos.y).powi(2)
            + (end.z - target_pos.z).powi(2))
        .sqrt();
        if d.abs() <= model_size {
            let started = std::time::Instant::now();
            find_path(c, target);
            if started.elapsed() >= std::time::Duration::from_millis(1) {
                tracing::warn!(
                    "FindPath took {:?} for Ai.Owner.ObjId:{}",
                    started.elapsed(),
                    c.npc.obj_id
                );
            }
            c.ai.path_node.end_point_pos = target_pos;
        }
        let next = c.ai.path_node.found_path.front().copied();
        match next {
            Some(next) if !(next.x == 0.0 && next.y == 0.0 && next.z == 0.0) => {
                distance_to_target = calculate_distance(owner_pos(c), next, true);
                if distance_to_target > range {
                    npc_move_towards(c, next, speed as f32, move_flags, range);
                } else {
                    if c.ai.path_node.found_path.is_empty() {
                        npc::stop_movement(c.server, &c.npc);
                        c.ai.path_node.found_path.clear();
                        return;
                    }
                    if let Some(p) = c.ai.path_node.found_path.pop_front() {
                        c.ai.path_node.current_target_pos = p;
                    }
                }
            }
            _ => {
                if distance_to_target > range {
                    npc_move_towards(c, target_pos, speed as f32, move_flags, range);
                } else {
                    npc::stop_movement(c.server, &c.npc);
                }
            }
        }
    } else if distance_to_target > range {
        npc_move_towards(c, target_pos, speed as f32, move_flags, 1.0);
    } else {
        npc::stop_movement(c.server, &c.npc);
    }
}

/// `RequestAvailableAiSkillList` / `RequestAvailableSkillList` (same filter on both list kinds).
fn health_and_time_ok(
    health_ratio: i32,
    time_elapsed: f64,
    hmin: i32,
    hmax: i32,
    tstart: f32,
    tend: f32,
) -> bool {
    if !((hmin == 0 && hmax == 0) || (hmin < health_ratio && health_ratio <= hmax)) {
        return false;
    }
    let (ts, te) = (f64::from(tstart), f64::from(tend));
    if (ts >= 0.0 && te > 0.0) || (ts > 0.0 && te >= 0.0) {
        if ts <= time_elapsed && te == 0.0 {
            return true;
        }
        return ts <= time_elapsed && time_elapsed <= te;
    }
    ts == 0.0 && te == 0.0
}

fn health_ratio(c: &AiCtx<'_>) -> i32 {
    let (hp, max) = {
        let n = c.npc.data();
        (n.unit.hp, n.unit.max_hp)
    };
    (hp as f32 / max as f32 * 100.0) as i32
}

/// `BaseCombatBehavior.RefreshSkillQueue(skillLists, aiParams)`.
fn refresh_skill_queue(
    c: &mut AiCtx<'_>,
    k: BehaviorKind,
    lists: &[AiSkillList],
    params: &AiParams,
) -> bool {
    let target_dist = match current_target(c) {
        Some(t) => npc::distance_to(c.server, &c.obj, &t, false),
        None => 0.0,
    };
    let hr = health_ratio(c);
    let elapsed = (now_ms() - c.st(k).combat_start_time) as f64 / 1000.0;
    let available: Vec<&AiSkillList> = lists
        .iter()
        .filter(|s| {
            health_and_time_ok(
                hr,
                elapsed,
                s.health_range_min,
                s.health_range_max,
                s.time_range_start,
                s.time_range_end,
            )
        })
        .collect();
    if !available.is_empty() {
        // select a set of skills by dice
        if let Some(selected) = random_element_by_weight(&available, |s| s.dice as f32) {
            let selected = *selected;
            {
                let s = c.st(k);
                s.pipe_name = selected.pipe_name.clone();
                s.phase_type = selected.phase_type;
            }
            params.set_restoration_on_return(selected.restoration);
            params.set_go_return_state(selected.go_return);
            // add startAiSkill first to the queue if it is available
            if !selected.start_ai_skills.is_empty() && !c.st(k).starting_skill_already_used {
                for skill in &selected.start_ai_skills {
                    if ai_skills::on_cooldown(c, skill.skill_id) {
                        continue;
                    }
                    let s = c.st(k);
                    s.skill_queue.push_back(*skill);
                    s.starting_skill_already_used = true;
                }
            }
            let avail_lists: Vec<&SkillList> = selected
                .skill_lists
                .iter()
                .filter(|s| {
                    health_and_time_ok(
                        hr,
                        elapsed,
                        s.health_range_min,
                        s.health_range_max,
                        s.time_range_start,
                        s.time_range_end,
                    )
                })
                .collect();
            let Some(list) = random_element_by_weight(&avail_lists, |s| s.dice as f32) else {
                return !c.st(k).skill_queue.is_empty();
            };
            for skill in &list.skills {
                if ai_skills::on_cooldown(c, skill.skill_id) {
                    continue;
                }
                let Some(tpl) = c.server.game_data.systems.skills.skill(skill.skill_id) else {
                    continue;
                };
                if (target_dist >= tpl.min_range as f32 && target_dist <= tpl.max_range as f32)
                    || ai_skills::is_self_target(tpl)
                {
                    c.st(k).skill_queue.push_back(*skill);
                }
            }
        }
        return !c.st(k).skill_queue.is_empty();
    }
    let t = template_scalars(c);
    if t.base_skill_id == 0 {
        return false;
    }
    c.st(k).skill_queue.push_back(AiSkill {
        skill_id: t.base_skill_id as u32,
        strafe: t.base_skill_strafe,
        delay: t.base_skill_delay,
    });
    true
}

/// `BaseCombatBehavior.GetPlayersInRange(owner, range, fovScale, greeted, cooldown)`.
fn players_in_range(
    c: &AiCtx<'_>,
    range: f32,
    fov: f64,
    k_greeted: &std::collections::BTreeMap<u32, i64>,
    cooldown_ms: i64,
) -> Vec<WorldObject> {
    let now = now_ms();
    npc::around(c.server, &c.obj, range, true)
        .into_iter()
        .filter(|o| o.as_player().is_some())
        .filter(|p| npc::is_front(c.server, &c.obj, p, fov))
        .filter(|p| {
            k_greeted
                .get(&p.obj_id())
                .is_none_or(|last| now - last >= cooldown_ms)
        })
        .collect()
}

const GREET_RANGE: f32 = 5.0;
const GREET_FOV: f64 = 0.6667;
const GREET_COOLDOWN_MS: i64 = 5 * 60 * 1000;

/// `CheckForTalk()` of Idle / HoldPosition.
fn check_for_talk(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if c.ai.current_behavior() == Some(B::Talk) {
        return;
    }
    let now = now_ms();
    let greeted = c.st(k).greeted.clone();
    let players = players_in_range(c, GREET_RANGE, GREET_FOV, &greeted, GREET_COOLDOWN_MS);
    let ids: Vec<u32> = players.iter().map(WorldObject::obj_id).collect();
    let s = c.st(k);
    for id in &ids {
        if s.greeted
            .get(id)
            .is_none_or(|last| now - last >= GREET_COOLDOWN_MS)
        {
            s.greeted.insert(*id, now);
        }
    }
    s.greeted
        .retain(|id, t| ids.contains(id) || now - *t < GREET_COOLDOWN_MS);
    if !ids.is_empty() {
        c.go_to_talk();
    }
}

// ===========================================================================================
// Common behaviors
// ===========================================================================================

fn alert_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    check_pipe_name(c, k);
    npc::stop_movement(c.server, &c.npc);
    broadcast_posture(c, false);
    if let Some(t) = current_target(c) {
        let r = c.obj.transform();
        c.st(k).old_rotation = (r.rot_x, r.rot_y, r.rot_z);
        let tp = npc::world_pos(c.server, &t);
        npc::look_towards(c.server, &c.npc, tp, 4);
    }
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::ALERT);
    super::npc_events::in_alert(c);
    let s = c.st(k);
    s.pipe_name = "phase_dragon_ground".into();
    s.enter = true;
}

fn alert_tick(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if !c.st(k).enter {
        return; // not initialized yet Enter()
    }
    if let Some(t) = current_target(c) {
        let tp = npc::world_pos(c.server, &t);
        npc::look_towards(c.server, &c.npc, tp, 4);
    }
    let skill_task = c.npc.data().unit.skill_state.skill_task.is_some();
    if now_ms() > c.ai.alert_end_time && !skill_task {
        let (x, y, z) = c.st(k).old_rotation;
        c.obj.with_transform(|t| {
            t.rot_x = x;
            t.rot_y = y;
            t.rot_z = z;
        });
        set_target(c, None);
        c.go_to_idle();
        return;
    }
    check_aggression(c, k);
}

fn almighty_attack_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    c.st(k).skill_queue.clear();
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
    broadcast_posture(c, false);
    c.st(k).combat_start_time = now_ms();
    if !c.npc.data().unit.is_in_battle {
        super::npc_events::on_combat_started(c);
    }
    c.ai.param = template_ai_params(c);
    c.st(k).enter = true;
}

fn almighty_attack_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    let Some(params) = c.ai.param.clone() else {
        return;
    };
    let AiParamsKind::Almighty { ai_skill_lists, .. } = &params.kind else {
        return;
    };
    c.st(k).ai_params = Some(params.clone());
    if !update_target(c) || should_return(c) {
        c.on_no_aggro_target();
        return;
    }
    if can_strafe(c, k) && !is_using_skill(c) {
        let t = current_target(c);
        move_in_range(c, k, t.as_ref(), delta_ms);
    }
    check_pipe_name(c, k);
    if !can_use_skill(c, k) {
        return;
    }
    c.st(k).strafe_during_delay = false;
    // Will delay for 150 Milliseconds to eliminate the hanging of the skill
    if !c.npc_check_interval(NpcIntervalSite::AlmightyAttackBehavior, 150) {
        tracing::trace!("Skill: CooldownTime [150]!");
        return;
    }
    if c.st(k).skill_queue.is_empty() && !refresh_skill_queue(c, k, ai_skill_lists, &params) {
        return;
    }
    let Some(selected) = c.st(k).skill_queue.pop_front() else {
        return;
    };
    let Some(skill) = ai_skills::new_skill(c, selected.skill_id) else {
        return;
    };
    let target = current_target(c);
    use_skill(c, k, skill, target.as_ref(), selected.delay);
    c.st(k).strafe_during_delay = selected.strafe;
}

fn almighty_attack_exit(c: &mut AiCtx<'_>, k: BehaviorKind) {
    // Experimental handling of guards returning to their home position after chasing somebody
    let aggro_empty = c.npc.data().unit.skill_state.aggro_table.is_empty();
    if aggro_empty && c.ai.path_handler.ai_path_points_remaining.is_empty() {
        c.ai.path_handler.target_position = Vec3::default();
        npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
        npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
        let home = c.ai.home_position;
        c.enqueue_path_point(AiPathPoint {
            action: AiPathPointAction::Speed,
            param: "3".into(),
            position: home,
        });
        c.go_to_follow_path();
    }
    c.st(k).enter = false;
}

fn attack_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
    broadcast_posture(c, false);
    set_in_battle(c.server, &c.obj, true);
    super::npc_events::on_combat_started(c);
    c.st(k).enter = true;
}

fn attack_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if !update_target(c) || should_return(c) {
        c.on_no_aggro_target();
        return;
    }
    let Some(target) = current_target(c) else {
        return;
    };
    if can_strafe(c, k) && !is_using_skill(c) {
        move_in_range(c, k, Some(&target), delta_ms);
    }
    if !can_use_skill(c, k) {
        return;
    }
    // Will delay for 150 Milliseconds to eliminate the hanging of the skill
    if !c.npc_check_interval(NpcIntervalSite::AttackBehavior, 150) {
        tracing::trace!("Skill: CooldownTime [150]!");
        return;
    }
    let target = current_target(c).unwrap_or(target);
    let target_dist = npc::distance_to(c.server, &c.obj, &target, false);
    pick_skill_and_use_it(c, k, SkillUseConditionKind::InCombat, &target, target_dist);
}

fn dead_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    npc::stop_movement(c.server, &c.npc);
    combat::npc_clear_all_aggro(c);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    super::npc_events::on_death(c);
    c.st(k).enter = true;
}

fn despawning_enter(c: &mut AiCtx<'_>) {
    super::npc_events::on_despawn(c);
}

fn follow_path_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    c.st(k).skill_queue.clear();
    npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    c.st(k).combat_start_time = now_ms();
    if !c.npc.data().unit.is_in_battle {
        super::npc_events::on_combat_started(c);
    }
    c.ai.param = template_ai_params(c);
    c.npc.data().unit.is_in_patrol = true;
    if !c.ai.has_simulation {
        // Ai.Owner.Simulation is null for NPCs not spawned by a spawner: NullReferenceException.
        tracing::error!(
            "FollowPathBehavior.Enter: npc {} has no Simulation (NullReferenceException in C#)",
            c.npc.obj_id
        );
        return;
    }
    // Simulation.MoveToPathEnabled = true; Simulation.GoToPath(owner, true): GoToPath toggles
    // MoveToPathEnabled back to false and runs StopMove (StopMovement + IsInPatrol = false).
    npc::stop_movement(c.server, &c.npc);
    c.npc.data().unit.is_in_patrol = false;
    c.st(k).enter = true;
}

fn follow_path_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if !update_target(c) {
        set_target(c, None);
    }
    if check_aggression(c, k) {
        return;
    }
    if check_alert(c, k) {
        return;
    }
    // If still aggro, go back to combat
    let (in_battle, aggro_empty) = {
        let n = c.npc.data();
        (
            n.unit.is_in_battle,
            n.unit.skill_state.aggro_table.is_empty(),
        )
    };
    if in_battle && !aggro_empty {
        c.go_to_combat();
        return;
    }
    if !run_current_path(c, delta_ms) {
        c.go_to_idle();
    }
    let h = &c.ai.path_handler;
    let t = h.target_position;
    if t.x == 0.0
        && t.y == 0.0
        && t.z == 0.0
        && h.ai_path_points.is_empty()
        && h.ai_path_points_remaining.is_empty()
    {
        c.go_to_idle();
    }
}

/// `AiPathHandler.RunCurrentPath(delta)`.
fn run_current_path(c: &mut AiCtx<'_>, delta_ms: i64) -> bool {
    let pos = owner_pos(c);
    let scale = template_scalars(c).scale;
    match c.ai.path_handler.run_current_path(pos, scale) {
        PathStep::ReturnToCommandSet => {
            c.go_to_run_command_set();
            true
        }
        PathStep::Move {
            target,
            speed,
            actor_flags,
            result,
        } => {
            let mut move_speed = npc::real_movement_speed(&c.obj, f64::from(speed));
            move_speed *= delta_seconds(delta_ms);
            npc_move_towards(c, target, move_speed as f32, actor_flags, 1.0);
            // Move the idle "home" location along with the path
            c.ai.idle_position = owner_pos(c);
            result
        }
        PathStep::Idle(r) => r,
        PathStep::Unsupported(msg) => {
            tracing::error!("{msg} (NotSupportedException)");
            true
        }
    }
}

fn follow_unit_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if !update_target(c) {
        set_target(c, None);
    }
    if check_aggression(c, k) {
        return;
    }
    if check_alert(c, k) {
        return;
    }
    let follow = c.ai.follow_unit.clone();
    let alive = follow.as_ref().is_some_and(|f| {
        f.with_unit(|u| u.unit().hp > 0 && !u.unit().is_dead())
            .unwrap_or(false)
    });
    let Some(follow) = follow.filter(|_| alive) else {
        c.ai.follow_unit = None;
        c.go_to_idle();
        return;
    };
    let target_distance = npc::distance_to(c.server, &c.obj, &follow, true);
    let follow_speed_multiplier = (f64::from(target_distance) / 1.5).min(5.0) as f32;
    let mut move_speed = npc::real_movement_speed(&c.obj, f64::from(base_move_speed(c)))
        * f64::from(follow_speed_multiplier);
    let move_flags = npc::real_movement_flags(move_speed);
    move_speed *= delta_seconds(delta_ms);
    let fp = npc::world_pos(c.server, &follow);
    npc_move_towards(c, fp, move_speed as f32, move_flags, 1.0);
    c.ai.idle_position = owner_pos(c);
}

fn hold_position_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    ai_skills::interrupt_skills(c);
    npc::stop_movement(c.server, &c.npc);
    // Ai.Owner.CurrentTarget = Ai.Owner (no packet)
    c.npc.data().unit.current_target = c.npc.obj_id;
    let s = c.st(k);
    s.last_skill_check = now_ms();
    s.following_npc = false;
    s.initialized = true;
}

fn hold_position_tick(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if !c.st(k).initialized {
        tracing::warn!(
            "HoldPositionBehavior.Tick called before initialization for unit {}",
            c.npc.obj_id
        );
        return;
    }
    if !throttle(c, k) {
        return;
    }
    // 1. Combat states
    if check_aggression(c, k) {
        c.st(k).following_npc = false;
        return;
    }
    if check_alert(c, k) {
        c.st(k).following_npc = false;
        return;
    }
    // 2. Try to use idle self-skills (1 s between tries)
    let now = now_ms();
    if now - c.st(k).last_skill_check >= 1000 {
        c.st(k).last_skill_check = now;
        if let Some(t) = current_target(c) {
            let dist = npc::distance_to(c.server, &c.obj, &t, false);
            let me = c.obj.clone();
            pick_skill_and_use_it(c, k, SkillUseConditionKind::InIdle, &me, dist);
        }
    }
    // 3. Optional follow default NPC
    if !c.st(k).following_npc && c.do_follow_default_nearest_npc() {
        c.st(k).following_npc = true;
    }
    // 4. Talk has low priority
    check_for_talk(c, k);
}

fn idle_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    // Stop all actions and reset state
    npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    ai_skills::interrupt_skills(c);
    npc::stop_movement(c.server, &c.npc);
    set_target(c, None);
    if let Some(t) = current_target(c) {
        if let Some(p) = t.as_player() {
            p.send(SCAggroTargetChangedPacket {
                npc_id: c.npc.obj_id,
                target_id: 0,
            });
        }
    }
    super::npc_events::in_idle(c);
    c.st(k).initialized = true;
}

fn idle_tick(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if !c.st(k).initialized {
        tracing::warn!(
            "IdleBehavior.Tick called before initialization for unit {}",
            c.npc.obj_id
        );
        return;
    }
    if !throttle(c, k) {
        return;
    }
    // Try to use a skill if possible
    if let Some(t) = current_target(c) {
        let dist = npc::distance_to(c.server, &c.obj, &t, false);
        let me = c.obj.clone();
        pick_skill_and_use_it(c, k, SkillUseConditionKind::InIdle, &me, dist);
    }
    // Check for state transitions
    if check_aggression(c, k) {
        c.go_to_combat();
    } else if check_alert(c, k) {
        c.go_to_alert();
    } else if check_follow_path(c) {
        c.go_to_follow_path();
    } else {
        // Try to follow the nearest NPC, otherwise go to default behavior
        if c.do_follow_default_nearest_npc() {
            return;
        }
        c.go_to_default_behavior();
    }
    // Talk has low priority
    check_for_talk(c, k);
}

fn return_state_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if !c.npc.data().unit.skill_state.aggro_table.is_empty() {
        combat::npc_clear_all_aggro(c);
    }
    set_target(c, None);
    set_in_battle(c.server, &c.obj, false);
    npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    broadcast_posture(c, false);
    buffs::add_buff_by_id(c.server, &c.obj, npc::BUFF_NPC_RETURN, &c.obj, 0);
    let restore =
        c.ai.param
            .as_ref()
            .is_none_or(|p| p.restoration_on_return());
    if restore {
        let (hp, max_hp, max_mp) = {
            let n = c.npc.data();
            (n.unit.hp, n.unit.max_hp, n.unit.max_mp)
        };
        skills_combat::post_update_current_hp(
            c.server,
            &c.obj,
            Some(&c.obj),
            hp,
            max_hp,
            KillReason::Unknown,
        );
        {
            let mut n = c.npc.data();
            n.unit.hp = max_hp;
            n.unit.mp = max_mp;
        }
        npc::broadcast_packet(
            c,
            &SCUnitPointsPacket {
                obj_id: c.npc.obj_id,
                hp: max_hp,
                mp: max_mp,
            },
        );
    }
    if c.ai
        .param
        .as_ref()
        .is_some_and(|p| p.always_teleport_on_return)
    {
        on_completed_return(c);
        return;
    }
    if c.ai.param.as_ref().is_some_and(|p| !p.go_return_state()) {
        on_completed_return_no_teleport(c);
    }
    let s = c.st(k);
    s.timeout_time = now_ms() + 20_000;
    s.enter = true;
}

fn return_state_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    let mut move_speed = npc::real_movement_speed(&c.obj, f64::from(base_move_speed(c)));
    let move_flags = npc::real_movement_flags(move_speed);
    move_speed *= delta_seconds(delta_ms);
    let idle = c.ai.idle_position;
    npc_move_towards(c, idle, move_speed as f32, move_flags, 1.0);
    let distance_to_idle = calculate_distance(idle, owner_pos(c), false);
    if distance_to_idle < 1.0 {
        on_completed_return_no_teleport(c);
        return;
    }
    if now_ms() > c.st(k).timeout_time {
        on_completed_return(c);
    }
}

fn on_completed_return(c: &mut AiCtx<'_>) {
    let idle = c.ai.idle_position;
    let distance_to_idle = calculate_distance(idle, owner_pos(c), false);
    if distance_to_idle > 2.0 * 2.0 {
        npc_move_towards(c, idle, 1_000_000.0, 4, 1.0);
        npc::stop_movement(c.server, &c.npc);
    }
    on_completed_return_no_teleport(c);
}

fn on_completed_return_no_teleport(c: &mut AiCtx<'_>) {
    c.go_to_idle();
}

fn roaming_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if !check_aggression(c, k) {
        check_alert(c, k);
    }
    let zero = |v: Vec3| v.x == 0.0 && v.y == 0.0 && v.z == 0.0;
    if zero(c.st(k).roam_target) && now_ms() > c.st(k).next_roaming {
        let next = calc_next_roaming_position(c);
        c.st(k).roam_target = next;
        broadcast_posture(c, false);
    }
    let target = c.st(k).roam_target;
    if zero(target) {
        return;
    }
    let mut move_speed = npc::real_movement_speed(&c.obj, f64::from(base_move_speed(c)));
    let move_flags = npc::real_movement_flags(move_speed);
    move_speed *= delta_seconds(delta_ms);
    npc_move_towards(c, target, move_speed as f32, move_flags, 1.0);
    let dist = calculate_distance(owner_pos(c), target, false);
    if dist < 1.0 {
        npc::stop_movement(c.server, &c.npc);
        let s = c.st(k);
        s.roam_target = Vec3::default();
        s.next_roaming = now_ms() + rand::thread_rng().gen_range(3..6) * 1000;
        broadcast_posture(c, true);
    }
}

/// `AiUtils.CalcNextRoamingPosition(ai)` (taken from x2ai.lua).
pub fn calc_next_roaming_position(c: &AiCtx<'_>) -> Vec3 {
    let max_roaming_distance = 6.0f32;
    let idle = c.ai.idle_position;
    let mut rng = rand::thread_rng();
    let x = (rng.gen::<f32>() - 0.5) * max_roaming_distance * 2.0 + idle.x;
    let y = (rng.gen::<f32>() - 0.5) * max_roaming_distance * 2.0 + idle.y;
    // Get terrain height at new position
    let (can_fly, spawner_z, zone) = {
        let n = c.npc.data();
        (n.can_fly, n.spawner_z, n.unit.transform.zone_id)
    };
    let z = npc::reference_height_in(
        c.server,
        c.obj.transform().instance_id,
        Some(crate::systems::npcs::heightmap::ReferenceHeightAi {
            can_fly,
            hold_or_idle: matches!(c.ai.current_behavior(), Some(B::HoldPosition | B::Idle)),
            spawner_z,
            world_z: owner_pos(c).z,
        }),
        x,
        y,
        idle.z,
        zone,
    );
    Vec3::new(x, y, z)
}

fn run_command_set_tick(c: &mut AiCtx<'_>, delta_ms: i64) {
    // If everything is executed, and still time remaining, wait for it before going back
    if c.ai.current_command_run_time_ms > 0 {
        c.ai.current_command_run_time_ms -= delta_ms;
        return;
    }
    // If there are commands in the AI Command queue, execute those first
    if c.ai.current_command.is_some() || !c.ai.commands_queue.is_empty() {
        if c.ai.current_command.is_none() {
            c.ai.current_command = c.ai.commands_queue.pop_front();
            c.ai.current_command_start_time = now_ms();
        }
        tick_current_ai_command(c, delta_ms);
        return;
    }
    c.go_to_idle();
}

/// `RunCommandSetBehavior.TickCurrentAiCommand(aiCommand, delta)`.
fn tick_current_ai_command(c: &mut AiCtx<'_>, delta_ms: i64) {
    use super::params::AiCommandCategory as Cmd;
    if c.ai.current_command_run_time_ms < 0 {
        c.ai.current_command = None;
        c.ai.current_command_run_time_ms = 0;
        return;
    }
    // Check if we're still waiting
    if c.ai.current_command_run_time_ms > 0 {
        c.ai.current_command_run_time_ms -= delta_ms;
        return;
    }
    let Some(cmd) = c.ai.current_command.clone() else {
        return;
    };
    tracing::debug!(
        "{} executing AI Command: {:?}, CommandSet: {}, P1: {}, P2: {}",
        c.npc.obj_id,
        cmd.cmd_id,
        cmd.cmd_set_id,
        cmd.param1,
        cmd.param2
    );
    match cmd.cmd_id {
        Cmd::FollowUnit => {
            tracing::warn!(
                "AI Command: {:?} not implemented, NPC {}, CommandSet {}, P1 {}, P2 {}",
                cmd.cmd_id,
                c.npc.obj_id,
                cmd.cmd_set_id,
                cmd.param1,
                cmd.param2
            );
        }
        Cmd::FollowPath => {
            let name = c.ai.ai_file_name.clone();
            c.load_ai_path_points(&name, cmd.param1 == 1);
            if cmd.param1 == 1 {
                c.enqueue_path_point(AiPathPoint {
                    position: Vec3::default(),
                    action: AiPathPointAction::ReturnToCommandSet,
                    param: String::new(),
                });
            }
            c.go_to_follow_path();
            if cmd.param1 == 1 {
                c.ai.ai_file_name = cmd.param2.clone();
            } else {
                c.ai.ai_file_name2 = cmd.param2.clone();
            }
        }
        Cmd::UseSkill => {
            c.ai.ai_skill_id = cmd.param1;
            let skill_id = c.ai.ai_skill_id;
            if let Some(tpl) = c.server.game_data.systems.skills.skill(skill_id).cloned() {
                let target = current_target(c)
                    .filter(WorldObject::is_unit)
                    .unwrap_or_else(|| c.obj.clone());
                let r = crate::systems::skills::skill::unit_use_skill(
                    c.server, &c.obj, skill_id, &target,
                );
                if r == skill_result::SUCCESS {
                    let cool_down = crate::systems::skills::skill::attack_delay(
                        c.server, &tpl, &c.obj, false, 0.0,
                    );
                    c.ai.current_command_run_time_ms = cool_down as i64;
                }
            }
        }
        Cmd::Timeout => {
            c.ai.ai_time_out = cmd.param1;
            c.ai.current_command_run_time_ms = i64::from(c.ai.ai_time_out);
        }
        Cmd::Other(v) => {
            tracing::error!("AI command {v}: NotSupportedException");
            return;
        }
    }
    if c.ai.current_command_run_time_ms == 0 {
        c.ai.current_command_run_time_ms = -1000;
    }
}

fn spawning_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    super::npc_events::on_spawn(c);
    if let Some(p) = template_ai_params(c) {
        if let AiParamsKind::Almighty { .. } = p.kind {
            if p.alert_to_attack && p.alert_duration == 0.0 {
                check_aggression(c, k);
            }
        }
    }
    c.st(k).enter = true;
}

fn spawning_tick(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if !c.st(k).enter {
        return;
    }
    if !c.st(k).used_spawn_skills {
        let skills = ai_skills::template_skills_if_any(c, SkillUseConditionKind::OnSpawn);
        if let Some(skills) = skills {
            c.st(k).used_spawn_skills = true;
            for id in skills {
                let Some(skill) = ai_skills::new_skill(c, id) else {
                    tracing::error!("Spawning: OnSpawn skill {id} has no template");
                    continue;
                };
                let mut t = SkillCastTarget::Unit(c.npc.obj_id);
                ai_skills::use_skill(c, &skill, SkillCaster::Unit(c.npc.obj_id), &mut t, true);
            }
        }
    }
    // This follows the game's way of doing it.
    c.go_to_run_command_set();
}

fn talk_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    npc::stop_movement(c.server, &c.npc);
    npc::set_game_stance(&c.npc, GameStanceType::RELAXED);
    npc::set_alertness(&c.npc, MoveTypeAlertness::IDLE);
    c.st(k).initialized = true;
}

fn talk_tick(c: &mut AiCtx<'_>, k: BehaviorKind) {
    if !c.st(k).initialized {
        tracing::warn!(
            "TalkBehavior.Tick called before initialization for unit {}",
            c.npc.obj_id
        );
        return;
    }
    if !throttle(c, k) {
        return;
    }
    let now = now_ms();
    let greeted = c.st(k).greeted.clone();
    let players = players_in_range(c, GREET_RANGE, GREET_FOV, &greeted, GREET_COOLDOWN_MS);
    for p in &players {
        let id = p.obj_id();
        let fresh = c
            .st(k)
            .greeted
            .get(&id)
            .is_none_or(|last| now - last >= GREET_COOLDOWN_MS);
        if fresh {
            send_greeting(c, p);
            spawn_spyglass_if_needed(c);
            c.st(k).greeted.insert(id, now);
        }
    }
    let ids: Vec<u32> = players.iter().map(WorldObject::obj_id).collect();
    c.st(k)
        .greeted
        .retain(|id, t| ids.contains(id) || now - *t < GREET_COOLDOWN_MS);
    if players.is_empty() {
        c.go_to_default_behavior();
    }
}

/// `TalkBehavior.SendGreeting(player)`.
fn send_greeting(c: &mut AiCtx<'_>, player: &WorldObject) {
    let Some(p) = player.as_player() else { return };
    let npc_template_id = c.npc.data().unit.template_id as i32;
    let Some((event_name, weight)) = determine_event_type(c, player) else {
        return;
    };
    if event_name.is_empty() {
        // `eventType == null` is never true for the C# tuple ("" falls through); GetEvent with
        // an empty name finds nothing.
        return;
    }
    let data = &c.server.game_data.systems.ai.ai;
    let Some(selected) = data.event(npc_template_id, &event_name, weight).cloned() else {
        return;
    };
    let Some(bubble) = data.chat_bubble(selected.id).cloned() else {
        return;
    };
    let (npc_name, faction, template_id) = {
        let n = c.npc.data();
        (n.unit.name.clone(), n.unit.faction_id, n.unit.template_id)
    };
    let msg = SCNpcChatMessagePacket {
        chat_type: 0, // ChatType.White
        faction_id: faction,
        npc_obj_id: c.npc.obj_id,
        npc_name: crate::systems::npcs::npc::packets::npc_chat_name(
            c.server,
            &npc_name,
            template_id,
        ),
        character_obj_id: p.obj_id,
        kind: 1,
        type_: bubble.id as u32,
        message: String::new(),
    };
    let snd = super::packets::SCPlaySoundPacket {
        kind: 2,
        bubble_id: bubble.id as u32,
        npc_obj_id: c.npc.obj_id,
    };
    crate::systems::skills::broadcast(c.server, player, &msg, true);
    crate::systems::skills::broadcast(c.server, player, &snd, true);
    if selected.skill_id > 0 {
        crate::systems::skills::skill::unit_use_skill(
            c.server,
            &c.obj,
            selected.skill_id as u32,
            player,
        );
    }
    tracing::debug!(
        "NPC {npc_template_id} sent {event_name}:{weight} #{} to {}",
        bubble.id,
        p.obj_id
    );
}

/// `TalkBehavior.DetermineEventType(player)`.
fn determine_event_type(c: &AiCtx<'_>, player: &WorldObject) -> Option<(String, f32)> {
    let npc_id = c.npc.data().unit.template_id as i32;
    let lvl = player.as_player().map_or(0, |p| p.data().unit.level);
    let data = &c.server.game_data.systems.ai.ai;
    // 1. Enemy check
    let relation = crate::systems::skills::relation::relation_to(
        c.server,
        &crate::systems::skills::geometry::Target::Obj(c.obj.clone()),
        &crate::systems::skills::geometry::Target::Obj(player.clone()),
    );
    use crate::systems::skills::types::RelationState as R;
    let friendly = matches!(relation, R::Friendly | R::Neutral);
    if can_attack(c.server, &c.obj, player)
        && !friendly
        && !data.events(npc_id, "OnEnemySeen").is_empty()
    {
        return Some(("OnEnemySeen".into(), 500.0));
    }
    // 2. All records for NPC
    let mut records: Vec<(&str, f32)> = Vec::new();
    for name in ["OnClientGreeting", "OnFriendNearSeen"] {
        for ev in data.events(npc_id, name) {
            records.push((name, ev.weight));
        }
    }
    // 3. Determine maximum allowed weight based on level
    let max_weight = match lvl {
        l if l < 15 => 500.0,
        l if l < 30 => 300.0,
        l if l < 45 => 200.0,
        _ => 60.0,
    };
    // 4. Get the entry with the highest weight not exceeding maxWeight (first of equals)
    let mut best: Option<(&str, f32)> = None;
    for r in records.into_iter().filter(|r| r.1 <= max_weight) {
        if best.is_none_or(|b| r.1 > b.1) {
            best = Some(r);
        }
    }
    Some(best.map_or((String::new(), 0.0), |(n, w)| (n.to_string(), w)))
}

/// `TalkBehavior.SpawnSpyglassIfNeeded()`.
fn spawn_spyglass_if_needed(c: &mut AiCtx<'_>) {
    const VEHICLE_NICK_ID: i32 = 22;
    const SPYGLASS_ID: u32 = 6129;
    const SPYGLASS_DIST: f32 = 1.5;
    if template_scalars(c).npc_nickname_id != VEHICLE_NICK_ID {
        return;
    }
    let t = c.obj.transform();
    // pos = Transform.CloneDetached(); pos.Local.AddDistanceToFront(1.5)
    let (fx, fy) = crate::systems::skills::controllers::add_distance_to_front(
        SPYGLASS_DIST,
        t.x,
        t.y,
        t.rot_z + std::f32::consts::FRAC_PI_2,
    );
    // yaw = CalculateAngleFrom(pos, Owner.Transform) (degrees, used as is)
    let yaw = crate::systems::skills::controllers::calculate_angle_from(
        Vec3::new(fx, fy, t.z),
        Vec3::new(t.x, t.y, t.z),
    ) as f32;
    super::hooks::spawn_spyglass(c.server, &c.obj, SPYGLASS_ID, fx, fy, t.z, yaw);
}

// ===========================================================================================
// Archer
// ===========================================================================================

fn archer_attack_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    {
        let s = c.st(k);
        s.archer_phase = ArcherPhase::Base;
        s.make_a_gap_count = 0;
    }
    ai_skills::interrupt_skills(c);
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
    broadcast_posture(c, false);
    set_in_battle(c.server, &c.obj, true);
    super::npc_events::on_combat_started(c);
    c.ai.param = template_ai_params(c);
    c.st(k).enter = true;
}

fn archer_attack_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if c.ai.param.is_none() {
        c.ai.param = Some(AiParams::empty(AiParamType::ArcherRoaming));
    }
    let Some(params) = c.ai.param.clone() else {
        return;
    };
    let AiParamsKind::Archer { combat_skills } = &params.kind else {
        return;
    };
    if !update_target(c) || should_return(c) {
        c.on_no_aggro_target();
        return;
    }
    if c.st(k).archer_phase == ArcherPhase::NeedMakeAGap {
        let idle = c.ai.idle_position;
        let me = owner_pos(c);
        let abuser = current_target(c).map_or(Vec3::default(), |t| npc::world_pos(c.server, &t));
        let mut move_speed = npc::real_movement_speed(&c.obj, f64::from(base_move_speed(c)));
        let move_flags = npc::real_movement_flags(move_speed);
        move_speed *= delta_seconds(delta_ms);
        npc_move_towards(c, idle, move_speed as f32, move_flags, 1.0);
        let dist = calculate_distance(me, idle, true);
        let dist2 = calculate_distance(me, abuser, true);
        if dist < 1.0 || dist2 > params.prefered_combat_dist {
            npc::stop_movement(c.server, &c.npc);
            let s = c.st(k);
            s.make_a_gap_count += 1;
            s.archer_phase = ArcherPhase::TryingRangedDefSkill;
        } else {
            return;
        }
    } else if can_strafe(c, k) && !is_using_skill(c) {
        let t = current_target(c);
        move_in_range(c, k, t.as_ref(), delta_ms);
    }
    if !can_use_skill(c, k) {
        return;
    }
    c.st(k).max_weapon_range = params.prefered_combat_dist;
    let target_dist = match current_target(c) {
        Some(t) => npc::distance_to(c.server, &c.obj, &t, false),
        None => 0.0,
    };
    let list = archer_request_available_skills(c, k, &params, combat_skills, target_dist);
    let selected = archer_pick_skill(c, &list);
    let Some(skill) = ai_skills::new_skill(c, selected) else {
        return;
    };
    let casting = skill.template.casting_time as f32;
    let target = current_target(c);
    use_skill(c, k, skill, target.as_ref(), casting);
}

/// `ArcherAttackBehavior.OnUseSkillDone()`.
fn archer_on_use_skill_done(c: &mut AiCtx<'_>, k: BehaviorKind) {
    let s = c.st(k);
    s.archer_phase = match s.archer_phase {
        ArcherPhase::TryingMeleeSkill => ArcherPhase::UsedMeleeSkill,
        ArcherPhase::TryingRangedDefSkill => ArcherPhase::UsedRangedDefSkill,
        ArcherPhase::TryingMakeAGapSkill | ArcherPhase::NeedMakeAGap => ArcherPhase::NeedMakeAGap,
        _ => ArcherPhase::Base,
    };
}

/// `ArcherAttackBehavior.RequestAvailableSkills(aiParams, trgDist)` (OnRequestSkillInfo).
fn archer_request_available_skills(
    c: &mut AiCtx<'_>,
    k: BehaviorKind,
    params: &AiParams,
    base: &[super::params::ArcherCombatSkill],
    trg_dist: f32,
) -> Vec<u32> {
    let in_melee = trg_dist <= params.melee_attack_range;
    let mut list = Vec::new();
    let collect =
        |c: &mut AiCtx<'_>, f: fn(&super::params::ArcherCombatSkill) -> &Vec<u32>| -> Vec<u32> {
            let mut out = Vec::new();
            for acs in base {
                for id in f(acs) {
                    if !ai_skills::on_cooldown(c, *id) {
                        out.push(*id);
                    }
                }
            }
            out
        };
    match c.st(k).archer_phase {
        ArcherPhase::UsedMeleeSkill => {
            let need_make_a_gap =
                in_melee && c.st(k).make_a_gap_count < params.max_make_a_gap_count;
            if need_make_a_gap {
                list = collect(c, |a| &a.make_a_gap);
                c.st(k).archer_phase = ArcherPhase::TryingMakeAGapSkill;
            } else {
                c.st(k).archer_phase = ArcherPhase::Base;
                // call again with phase change ("base")
                return archer_request_available_skills(c, k, params, base, trg_dist);
            }
        }
        ArcherPhase::UsedRangedDefSkill => list = collect(c, |a| &a.ranged_strong),
        _ => {}
    }
    if list.is_empty() {
        if in_melee {
            list = collect(c, |a| &a.melee);
            c.st(k).archer_phase = ArcherPhase::TryingMeleeSkill;
        } else {
            list = collect(c, |a| &a.ranged_def);
            c.st(k).archer_phase = ArcherPhase::TryingRangedDefSkill;
        }
    }
    archer_on_use_skill_done(c, k);
    list
}

/// `ArcherAttackBehavior.PickSkill(skills)`.
fn archer_pick_skill(c: &mut AiCtx<'_>, skills: &[u32]) -> u32 {
    if !skills.is_empty() {
        return skills[rand::thread_rng().gen_range(0..skills.len())];
    }
    let base = template_scalars(c).base_skill_id as u32;
    if !ai_skills::on_cooldown(c, base) {
        return base;
    }
    0 // no melee Skill
}

// ===========================================================================================
// BigMonster
// ===========================================================================================

fn big_monster_attack_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
    broadcast_posture(c, false);
    set_in_battle(c.server, &c.obj, true);
    super::npc_events::on_combat_started(c);
    c.ai.param = template_ai_params(c);
    c.st(k).enter = true;
}

fn big_monster_attack_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if c.ai.param.is_none() {
        c.ai.param = Some(AiParams::empty(AiParamType::BigMonsterRoaming));
    }
    let Some(params) = c.ai.param.clone() else {
        return;
    };
    let AiParamsKind::BigMonster { combat_skills } = &params.kind else {
        return;
    };
    if !update_target(c) || should_return(c) {
        c.on_no_aggro_target();
        return;
    }
    if can_strafe(c, k) && !is_using_skill(c) {
        let t = current_target(c);
        move_in_range(c, k, t.as_ref(), delta_ms);
    }
    if !can_use_skill(c, k) {
        return;
    }
    c.st(k).strafe_during_delay = false;
    let target = current_target(c);
    let target_dist = target
        .as_ref()
        .map_or(0.0, |t| npc::distance_to(c.server, &c.obj, t, false));
    // RequestAvailableSkills
    let hr = health_ratio(c);
    let available: Vec<super::params::BigMonsterCombatSkill> = combat_skills
        .iter()
        .filter(|s| s.health_range_min <= hr && hr <= s.health_range_max)
        .filter(|s| !ai_skills::on_cooldown(c, s.skill_type))
        .filter(|s| {
            c.server
                .game_data
                .systems
                .skills
                .skill(s.skill_type)
                .is_some_and(|tpl| {
                    (target_dist >= tpl.min_range as f32 && target_dist <= tpl.max_range as f32)
                        || ai_skills::is_self_target(tpl)
                })
        })
        .cloned()
        .collect();
    // PickSkill
    let t = template_scalars(c);
    let selected = if !available.is_empty() {
        Some(available[rand::thread_rng().gen_range(0..available.len())].clone())
    } else if !ai_skills::on_cooldown(c, t.base_skill_id as u32) {
        Some(super::params::BigMonsterCombatSkill {
            skill_type: t.base_skill_id as u32,
            skill_delay: t.base_skill_delay,
            strafe_during_delay: t.base_skill_strafe,
            ..Default::default()
        })
    } else {
        None
    };
    let Some(selected) = selected else {
        // If skill list is empty, get Base skill
        if let Some(target) = target {
            pick_skill_and_use_it(c, k, SkillUseConditionKind::InCombat, &target, target_dist);
        }
        return;
    };
    let Some(skill) = ai_skills::new_skill(c, selected.skill_type) else {
        return;
    };
    use_skill(c, k, skill, target.as_ref(), selected.skill_delay);
    c.st(k).strafe_during_delay = selected.strafe_during_delay;
}

// ===========================================================================================
// Flytrap
// ===========================================================================================

fn flytrap_alert_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::ALERT);
    broadcast_posture(c, false);
    super::npc_events::in_alert(c);
    c.st(k).enter = true;
}

fn flytrap_attack_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    ai_skills::interrupt_skills(c);
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
    broadcast_posture(c, false);
    super::npc_events::on_combat_started(c);
    c.ai.param = template_ai_params(c);
    c.st(k).enter = true;
}

fn flytrap_attack_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if c.ai.param.is_none() {
        c.ai.param = Some(AiParams::empty(AiParamType::Flytrap));
    }
    let Some(params) = c.ai.param.clone() else {
        return;
    };
    if !matches!(params.kind, AiParamsKind::Flytrap { .. }) {
        return;
    }
    c.st(k).ai_params = Some(params.clone());
    if !flytrap_update_target(c) {
        c.on_no_aggro_target();
        return;
    }
    let Some(target) = current_target(c) else {
        return;
    };
    // if (Ai.Owner.Gimmick?.CurrentTarget != null) MoveInRange(Ai.Owner.Gimmick.CurrentTarget, delta)
    if let Some(g) = super::gimmick::npc_gimmick(c.server, &c.obj) {
        let gt = g.gimmick.data().unit.current_target;
        if gt != 0 {
            if let Some(t) = c.server.world.object(gt) {
                super::gimmick::flytrap_move_in_range(c, &g, &t, delta_ms);
            }
        }
    }
    set_in_battle(c.server, &c.obj, true);
    let target_dist = npc::distance_to(c.server, &c.obj, &target, false);
    pick_skill_and_use_it(c, k, SkillUseConditionKind::InCombat, &target, target_dist);
    flytrap_update(c, k);
}

/// `FlytrapAttackBehavior.UpdateTarget()`.
fn flytrap_update_target(c: &mut AiCtx<'_>) -> bool {
    for abuser in sorted_abusers(c).into_iter().flatten() {
        let ap = npc::world_pos(c.server, &abuser);
        npc::look_towards(c.server, &c.npc, ap, 4);
        if c.ai.already_targeted {
            return true;
        }
        if npc::unit_is_visible(c.server, &c.obj, &abuser) && !npc::is_dead(&abuser) {
            npc::set_current_aggro_target(&c.npc, Some(&abuser));
            set_target(c, Some(&abuser));
            update_aggro_help(c, &abuser, 200.0);
            if geo_data_mode(c) {
                find_path(c, &abuser);
            }
            return true;
        }
        clear_aggro_of_unit(c.server, &c.obj, &abuser);
    }
    match current_target(c) {
        Some(t) if t.is_unit() => {
            let hp = t.with_unit(|u| u.unit().hp).unwrap_or(0);
            if hp <= 0 || npc::is_dead(&t) {
                set_target(c, None);
            }
        }
        _ => set_target(c, None),
    }
    false
}

/// `FlytrapAttackBehavior.Update()`.
fn flytrap_update(c: &mut AiCtx<'_>, k: BehaviorKind) {
    let Some(abuser) = current_target(c) else {
        return;
    };
    let Some(params) = c.st(k).ai_params.clone() else {
        return;
    };
    let AiParamsKind::Flytrap {
        attack_end_distance,
        ..
    } = params.kind
    else {
        return;
    };
    let abuser_pos = npc::world_pos(c.server, &abuser);
    let current = owner_pos(c);
    let idle = c.ai.idle_position;
    let dist_sq = |a: Vec3, b: Vec3| {
        let dx = f64::from(a.x) - f64::from(b.x);
        let dy = f64::from(a.y) - f64::from(b.y);
        (dx * dx + dy * dy) as f32
    };
    // Check out of idle pos
    let teleport =
        c.ai.param
            .as_ref()
            .is_some_and(|p| p.always_teleport_on_return);
    if teleport && dist_sq(current, idle) > 3.0 * 3.0 {
        clear_aggro_of_unit(c.server, &c.obj, &abuser);
        c.on_no_aggro_target();
        return;
    }
    // Check that some target was gone out from attack end distance
    if dist_sq(abuser_pos, idle) > attack_end_distance * attack_end_distance {
        clear_aggro_of_unit(c.server, &c.obj, &abuser);
        c.on_no_aggro_target();
    }
}

// ===========================================================================================
// WildBoar
// ===========================================================================================

fn wild_boar_attack_enter(c: &mut AiCtx<'_>, k: BehaviorKind) {
    c.ai.param = template_ai_params(c);
    npc::set_game_stance(&c.npc, GameStanceType::COMBAT);
    npc::set_alertness(&c.npc, MoveTypeAlertness::COMBAT);
    broadcast_posture(c, false);
    super::npc_events::on_combat_started(c);
    set_in_battle(c.server, &c.obj, true);
    c.st(k).enter = true;
}

fn wild_boar_attack_tick(c: &mut AiCtx<'_>, k: BehaviorKind, delta_ms: i64) {
    if !c.st(k).enter {
        return;
    }
    if c.ai.param.is_none() {
        c.ai.param = Some(AiParams::empty(AiParamType::WildBoarRoaming));
    }
    let Some(params) = c.ai.param.clone() else {
        return;
    };
    let AiParamsKind::WildBoar {
        on_combat_start_skills,
        on_spurt_skills,
    } = &params.kind
    else {
        return;
    };
    {
        let (hp, max) = {
            let n = c.npc.data();
            (n.unit.hp, n.unit.max_hp)
        };
        c.st(k).curr_health = hp as f32 / max as f32 * 100.0;
    }
    if !update_target(c) || should_return(c) {
        c.on_no_aggro_target();
        return;
    }
    if can_strafe(c, k) && !is_using_skill(c) {
        let t = current_target(c);
        move_in_range(c, k, t.as_ref(), delta_ms);
    }
    if !can_use_skill(c, k) {
        return;
    }
    if !c.st(k).combat_start_skill {
        // On Combat Start Skill. Execute once
        let start = on_combat_start_skills.first().copied().unwrap_or(0);
        if start != 0 {
            npc::stop_movement(c.server, &c.npc);
            if let Some(skill) = ai_skills::new_skill(c, start) {
                let target = current_target(c);
                use_skill(c, k, skill, target.as_ref(), 0.0);
            }
            c.st(k).combat_start_skill = true;
        }
    }
    // Spurt or base?
    let Some(target) = current_target(c) else {
        return;
    };
    let target_dist = npc::distance_to(c.server, &c.obj, &target, false);
    if on_spurt_skills.is_empty() {
        pick_skill_and_use_it(c, k, SkillUseConditionKind::InCombat, &target, target_dist);
        return;
    }
    for skill_data in on_spurt_skills {
        if c.st(k).curr_health < skill_data.health_condition as f32 {
            let Some(skill) = ai_skills::new_skill(c, skill_data.skill_type) else {
                continue;
            };
            if target_dist >= skill.template.min_range as f32
                && target_dist <= skill.template.max_range as f32
            {
                set_weapon_range(c, k, &skill, &target);
                let result = use_skill(c, k, skill, Some(&target), 0.0);
                if result == skill_result::COOLDOWN_TIME {
                    pick_skill_and_use_it(
                        c,
                        k,
                        SkillUseConditionKind::InCombat,
                        &target,
                        target_dist,
                    );
                }
            }
        } else {
            pick_skill_and_use_it(c, k, SkillUseConditionKind::InCombat, &target, target_dist);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn skill_list_health_and_time_windows() {
        // no ranges: always
        assert!(health_and_time_ok(50, 0.0, 0, 0, 0.0, 0.0));
        // health window (min exclusive, max inclusive)
        assert!(!health_and_time_ok(40, 0.0, 40, 90, 0.0, 0.0));
        assert!(health_and_time_ok(90, 0.0, 40, 90, 0.0, 0.0));
        // time window [5, 0] = from 5 s on
        assert!(!health_and_time_ok(50, 4.0, 0, 100, 5.0, 0.0));
        assert!(health_and_time_ok(50, 6.0, 0, 100, 5.0, 0.0));
        // time window [5, 140]
        assert!(health_and_time_ok(50, 140.0, 0, 100, 5.0, 140.0));
        assert!(!health_and_time_ok(50, 141.0, 0, 100, 5.0, 140.0));
        // negative start: never (C# falls through every branch)
        assert!(!health_and_time_ok(50, 1.0, 0, 0, -1.0, 0.0));
    }

    #[test]
    fn delta_uses_the_millisecond_component() {
        assert_eq!(delta_seconds(120), 0.12);
        assert_eq!(delta_seconds(1120), 0.12);
    }
}

#[cfg(test)]
#[path = "fish_movement_tests.rs"]
mod fish_movement_tests;
