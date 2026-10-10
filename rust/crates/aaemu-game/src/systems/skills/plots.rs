//! Plots: `Plot.RunAsync`, `PlotTree.ExecuteAsync` (event queue with delays, tickets,
//! conditions, channeling finish, cancellation), `PlotNode.Execute` (`SCPlotEventPacket`),
//! `PlotEventEffect.ApplyEffect`, `PlotEventCondition` / `PlotCondition.Check`,
//! `PlotTargetInfo` (source / target update methods incl. area / random unit / random area) and
//! `PlotNextEvent.GetDelay`.

use std::collections::{HashMap, HashSet, VecDeque};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::Duration;

use rand::Rng;

use crate::models::unit::SkillAttribute;
use crate::server::GameServer;
use crate::world::{now_ms, WorldObject};

use super::broadcast;
use super::data::{EffectKind, EffectTemplate, PlotEventTemplate, PlotTree};
use super::effects::{self, EffectArgs, EffectSource};
use super::geometry::{around_shape, pos_unit_from, world_pos, PosUnit, Target};
use super::packets::{
    SCPlotCastingStoppedPacket, SCPlotChannelingStoppedPacket, SCPlotEndedPacket, SCPlotEventPacket,
};
use super::relation::{filter_with_relation, is_relation_valid, relation_to};
use super::skill::{apply_skill_modifiers, CastCtx};
use super::state::{PlotStateRef, SkillRef};
use super::stats::{stat, StatKind};
use super::types::{tags, CastAction, PlotObject, RelationState, SkillTargetRelation};

/// The mutable part of `PlotState`.
#[derive(Debug, Default)]
pub struct PlotStateInner {
    pub tickets: HashMap<u32, i32>,
    pub variables: [i32; 12],
    pub combat_dice_roll: u8,
    pub is_casting: bool,
    pub is_channeling: bool,
    /// `ChanneledBuffs` (unit, buff id).
    pub channeled_buffs: Vec<(WorldObject, u32)>,
    /// `HitObjects` (event id -> obj ids).
    pub hit_objects: HashMap<u32, Vec<u32>>,
}

/// `PlotState`.
#[derive(Debug)]
pub struct PlotState {
    pub skill: SkillRef,
    pub ctx: CastCtx,
    cancellation: AtomicBool,
    finish_channeling: AtomicBool,
    inner: Mutex<PlotStateInner>,
}

impl PlotState {
    pub fn new(skill: SkillRef, ctx: CastCtx) -> Arc<Self> {
        Arc::new(Self {
            skill,
            ctx,
            cancellation: AtomicBool::new(false),
            finish_channeling: AtomicBool::new(false),
            inner: Mutex::new(PlotStateInner::default()),
        })
    }

    pub fn inner(&self) -> MutexGuard<'_, PlotStateInner> {
        self.inner.lock().expect("plot state poisoned")
    }

    pub fn cancellation_requested(&self) -> bool {
        self.cancellation.load(Ordering::SeqCst)
    }

    pub fn request_cancellation(&self) {
        self.cancellation.store(true, Ordering::SeqCst);
    }

    pub fn channeling_finish_requested(&self) -> bool {
        self.finish_channeling.load(Ordering::SeqCst)
    }

    /// `FinishChanneling()`.
    pub fn finish_channeling(&self) {
        self.finish_channeling.store(true, Ordering::SeqCst);
    }

    /// `PermitChanneling()`.
    pub fn permit_channeling(&self) {
        self.finish_channeling.store(false, Ordering::SeqCst);
    }

    pub fn caster(&self) -> &WorldObject {
        &self.ctx.caster
    }
}

/// `PlotTargetInfo`.
#[derive(Debug, Clone)]
pub struct PlotTargetInfo {
    pub source: Option<Target>,
    previous_source: Option<Target>,
    pub target: Option<Target>,
    previous_target: Option<Target>,
    pub effected: Vec<Target>,
}

impl PlotTargetInfo {
    /// `new PlotTargetInfo(state)`.
    pub fn from_state(state: &PlotState) -> Self {
        Self {
            source: None,
            previous_source: Some(Target::Obj(state.ctx.caster.clone())),
            target: None,
            previous_target: Some(state.ctx.target.clone()),
            effected: Vec::new(),
        }
    }

    /// `new PlotTargetInfo(source, target)`.
    pub fn new(source: Option<Target>, target: Option<Target>) -> Self {
        Self {
            source: source.clone(),
            previous_source: source,
            target: target.clone(),
            previous_target: target,
            effected: Vec::new(),
        }
    }
}

/// `Plot.RunAsync`: starts the plot tree of `plot_id` on its own task.
pub fn run_plot(server: &GameServer, plot_id: u32, ctx: CastCtx, skill: SkillRef) {
    let state = PlotState::new(skill.clone(), ctx);
    let caster = state.ctx.caster.clone();
    caster.with_unit(|u| u.unit_mut().skill_state.active_plot = Some(PlotStateRef(state.clone())));
    skill.state().active_plot = Some(state.clone());
    let Some(srv) = server.systems.skills.server() else {
        tracing::debug!("skills: no bound server, plot {plot_id} not run");
        return;
    };
    let Ok(handle) = tokio::runtime::Handle::try_current() else {
        return;
    };
    handle.spawn(async move {
        execute_tree(&srv, plot_id, &state).await;
        if let (super::types::SkillCaster::Item { item_id, .. }, Some(p)) =
            (state.ctx.caster_caster, state.ctx.caster.as_player())
        {
            if item_id != 0 {
                if !state.cancellation_requested() {
                    srv.systems.skills.ext.quests().item_use(&srv, p, item_id);
                }
                srv.systems.skills.ext.items().unlock_item(&srv, p, item_id);
            }
        }
    });
}

type QueueItem = (usize, i64, PlotTargetInfo);

/// `PlotTree.ExecuteAsync`.
pub async fn execute_tree(server: &GameServer, plot_id: u32, state: &Arc<PlotState>) {
    let data = &server.game_data.systems.skills;
    let Some(tree) = data.plots.tree(plot_id).cloned() else {
        tracing::error!("Plot {plot_id} has no tree");
        do_plot_end(server, state);
        return;
    };
    let mut queue: VecDeque<QueueItem> = VecDeque::new();
    let mut execute_queue: VecDeque<(usize, PlotTargetInfo)> = VecDeque::new();
    queue.push_back((0, now_ms(), PlotTargetInfo::from_state(state)));
    let mut last_event = 1u8;
    while let Some(mut item) = queue.pop_front() {
        let now = now_ms();
        let node = item.0;
        let is_channeling = state.inner().is_channeling;
        if is_channeling && state.channeling_finish_requested() {
            handle_channeling_finish(server, &tree, node, state, &mut queue, &item);
            last_event = 0;
            continue;
        }
        if state.cancellation_requested() {
            if state.inner().is_casting {
                let tl = state.skill.tl_id();
                broadcast(
                    server,
                    state.caster(),
                    &SCPlotCastingStoppedPacket {
                        tl,
                        duration: 0,
                        last_event,
                    },
                    true,
                );
                broadcast(
                    server,
                    state.caster(),
                    &SCPlotChannelingStoppedPacket {
                        tl,
                        duration: 0,
                        last_event: 1,
                    },
                    true,
                );
            }
            do_plot_end(server, state);
            return;
        }
        let event_id = tree.nodes[node].event_id;
        let Some(event) = data.plots.event(event_id) else {
            continue;
        };
        if now >= item.1 {
            let count = {
                let mut inner = state.inner();
                let c = inner.tickets.entry(event_id).or_insert(0);
                *c += 1;
                *c
            };
            if count > event.tickets && event.tickets > 1 {
                continue;
            }
            update_target_info(server, &mut item.2, event, state);
            if item.2.target.is_none() {
                continue;
            }
            let condition = check_conditions(server, event, state, &item.2);
            if condition {
                execute_queue.push_back((node, item.2.clone()));
            }
            for &child in &tree.nodes[node].children {
                let Some(next) = data.plots.parent_next_event(&tree, child) else {
                    continue;
                };
                if condition != next.fail {
                    if next.per_target {
                        for t in &item.2.effected {
                            let ti = PlotTargetInfo::new(item.2.source.clone(), Some(t.clone()));
                            let d = compute_delay(server, &tree, child, state, &ti);
                            queue.push_back((child, now + i64::from(d), ti));
                        }
                    } else {
                        let ti = PlotTargetInfo::new(item.2.source.clone(), item.2.target.clone());
                        let d = compute_delay(server, &tree, child, state, &ti);
                        queue.push_back((child, now + i64::from(d), ti));
                    }
                }
            }
        } else {
            queue.push_back(item);
            flush_execution_queue(server, &tree, &mut execute_queue, state);
        }
        if !queue.is_empty() {
            let now = now_ms();
            let delay = queue.iter().map(|q| q.1 - now).min().unwrap_or(0).max(0);
            if delay > 0 {
                tokio::time::sleep(Duration::from_millis(15)).await;
            }
        }
    }
    flush_execution_queue(server, &tree, &mut execute_queue, state);
    do_plot_end(server, state);
}

fn handle_channeling_finish(
    server: &GameServer,
    tree: &PlotTree,
    node: usize,
    state: &Arc<PlotState>,
    queue: &mut VecDeque<QueueItem>,
    item: &QueueItem,
) {
    let data = &server.game_data.systems.skills;
    end_plot_channel(server, state);
    let Some(parent_next) = data.plots.parent_next_event(tree, node) else {
        return;
    };
    if !parent_next.channeling {
        return;
    }
    state.permit_channeling();
    execute_node(server, tree, node, state, &item.2);
    let mut queued: HashSet<u32> = queue.iter().map(|q| tree.nodes[q.0].event_id).collect();
    for &child in &tree.nodes[node].children {
        let child_event = tree.nodes[child].event_id;
        if queued.contains(&child_event) {
            continue;
        }
        let per_target = data
            .plots
            .parent_next_event(tree, child)
            .is_some_and(|n| n.per_target);
        let now = now_ms();
        if per_target {
            for t in &item.2.effected {
                let ti = PlotTargetInfo::new(item.2.source.clone(), Some(t.clone()));
                queue.push_back((child, now, ti));
                queued.insert(child_event);
            }
        } else {
            let ti = PlotTargetInfo::new(item.2.source.clone(), item.2.target.clone());
            queue.push_back((child, now, ti));
            queued.insert(child_event);
        }
    }
}

fn flush_execution_queue(
    server: &GameServer,
    tree: &PlotTree,
    q: &mut VecDeque<(usize, PlotTargetInfo)>,
    state: &Arc<PlotState>,
) {
    let mut packets = super::packets::CompressedGamePackets::new();
    while let Some((node, ti)) = q.pop_front() {
        if let Some(p) = node_execute_packet(server, tree, node, state, &ti) {
            packets.add(&p);
        }
    }
    if !packets.is_empty() {
        broadcast(server, state.caster(), &packets, true);
    }
}

fn end_plot_channel(server: &GameServer, state: &PlotState) {
    let buffs: Vec<(WorldObject, u32)> = state.inner().channeled_buffs.clone();
    for (unit, buff_id) in buffs {
        super::buffs::remove_buff(server, &unit, buff_id);
    }
}

/// `PlotTree.DoPlotEnd`.
fn do_plot_end(server: &GameServer, state: &Arc<PlotState>) {
    let caster = state.caster().clone();
    let skill = &state.skill;
    let tl = skill.tl_id();
    broadcast(server, &caster, &SCPlotEndedPacket { tl }, true);
    end_plot_channel(server, state);
    let now = now_ms();
    let (id, cd) = (skill.template.id, skill.template.cooldown_time);
    caster.with_unit(|u| u.unit_mut().cooldowns.add_cooldown(id, cd as u32, now));
    if let Some(o) = super::relation::owner_character(server, &caster) {
        if o.data().skill_char.ignore_skill_cooldowns {
            o.send(super::packets::SCSkillCooldownResetPacket {
                obj_id: o.obj_id,
                skill_id: id,
                tag_id: 0,
                gcd: false,
            });
            caster.with_unit(|u| u.unit_mut().cooldowns.remove_cooldown(id));
        }
    }
    server.systems.skills.tl_ids.release(tl);
    skill.state().tl_id = 0;
    skill.invoke_callback();
    caster.with_unit(|u| {
        let s = &mut u.unit_mut().skill_state;
        if s.active_plot
            .as_ref()
            .is_some_and(|p| Arc::ptr_eq(&p.0, state))
        {
            s.active_plot = None;
        }
    });
}

/// `PlotNode.ComputeDelayMs` = `ParentNextEvent.GetDelay(state, targetInfo, Parent)`.
fn compute_delay(
    server: &GameServer,
    tree: &PlotTree,
    node: usize,
    state: &PlotState,
    ti: &PlotTargetInfo,
) -> i32 {
    let data = &server.game_data.systems.skills;
    let Some(next) = data.plots.parent_next_event(tree, node) else {
        return 0;
    };
    let Some(parent) = tree.nodes[node].parent else {
        return 0;
    };
    let Some(parent_event) = data.plots.event(tree.nodes[parent].event_id) else {
        return 0;
    };
    let caster = state.caster();
    // GetAnimDelay
    let mut anim = 0i32;
    if next.add_anim_cs_time {
        for e in &parent_event.effects {
            if let Some(EffectTemplate::Special(s)) =
                data.effect_by_name(e.actual_id, &e.actual_type)
            {
                if s.special_effect_type_id == 34 {
                    anim = data
                        .anims
                        .get(s.value1 as u32)
                        .map_or(0, |a| a.combat_sync_time);
                    break;
                }
            }
        }
    }
    let gcd_mul = stat(server, caster, StatKind::GlobalCooldownMul) as f32;
    let anim_time = (anim as f32 * (gcd_mul / 100.0)) as i32;
    // GetProjectileDelay
    let mut projectile = 0;
    if next.speed > 0 {
        if let (Some(s), Some(t)) = (&ti.source, &ti.target) {
            let d =
                super::geometry::raw_distance(&world_pos(server, s), &world_pos(server, t), true);
            projectile = (d / next.speed as f32 * 1000.0).round_ties_even() as i32;
        }
    }
    // GetSkillControllerDelay
    let sc = parent_event
        .effects
        .iter()
        .filter(|e| e.actual_type == "SkillController")
        .filter_map(|e| data.skill_controller(e.actual_id))
        .filter(|t| t.value[4] == 1)
        .map(|t| t.value[2])
        .max()
        .unwrap_or(0);
    let mut delay = anim_time + projectile + sc;
    if next.casting {
        let cast_mul = stat(server, caster, StatKind::CastTimeMul);
        delay += (apply_skill_modifiers(
            server,
            &state.skill,
            caster,
            SkillAttribute::CastTime,
            f64::from(next.delay),
        ) * cast_mul) as i32;
    } else {
        delay += next.delay;
    }
    delay.max(0)
}

/// `PlotNode.CheckConditions`.
fn check_conditions(
    server: &GameServer,
    event: &PlotEventTemplate,
    state: &PlotState,
    ti: &PlotTargetInfo,
) -> bool {
    event
        .conditions
        .iter()
        .all(|c| event_condition(server, c, state, ti))
}

fn plot_source(id: i32, state: &PlotState, ti: &PlotTargetInfo) -> Option<Target> {
    match id {
        1 => Some(Target::Obj(state.ctx.caster.clone())),
        2 => Some(state.ctx.target.clone()),
        3 => ti.source.clone(),
        4 => ti.target.clone(),
        _ => None,
    }
}

fn plot_target(
    id: i32,
    state: &PlotState,
    ti: &PlotTargetInfo,
    new_target: &Target,
) -> Option<Target> {
    match id {
        1 => Some(Target::Obj(state.ctx.caster.clone())),
        2 => Some(state.ctx.target.clone()),
        3 => ti.source.clone(),
        4 => Some(new_target.clone()),
        5 => ti.target.clone(),
        _ => None,
    }
}

/// `PlotEventCondition.CheckCondition`.
fn event_condition(
    server: &GameServer,
    c: &super::data::plots::PlotEventCondition,
    state: &PlotState,
    ti: &PlotTargetInfo,
) -> bool {
    let Some(source) = plot_source(c.source_id, state, ti) else {
        tracing::error!("PlotEventCondition: invalid source {}", c.source_id);
        return false;
    };
    for t in &ti.effected {
        let Some(target) = plot_target(c.target_id, state, ti, t) else {
            tracing::error!("PlotEventCondition: invalid target {}", c.target_id);
            return false;
        };
        if !condition_check(server, &c.condition, &source, &target, state) {
            return false;
        }
    }
    true
}

fn compare_with_operator(value: i32, op: i32, compare: i32) -> bool {
    match op {
        1 => value == compare,
        2 => value > compare,
        3 => value >= compare,
        4 => value < compare,
        5 => value <= compare,
        _ => {
            tracing::error!("CompareWithOperator: Unknown Comparison Operation {op}");
            false
        }
    }
}

/// `PlotCondition.Check`.
pub fn condition_check(
    server: &GameServer,
    cond: &super::data::plots::PlotCondition,
    caster: &Target,
    target: &Target,
    state: &PlotState,
) -> bool {
    let (p1, p2, p3) = (cond.param1, cond.param2, cond.param3);
    let caster_unit = caster.as_obj().filter(|o| o.is_unit());
    let res = match cond.kind {
        1 => match caster_unit {
            Some(u) => {
                let l = i32::from(u.with_unit(|x| x.unit().level).unwrap_or(0));
                l >= p1 && l <= p2
            }
            None => {
                tracing::warn!("PlotCondition Level check without caster being a Unit");
                false
            }
        },
        2 => match p1 {
            1 => relation_to(server, caster, target) == RelationState::Friendly,
            4 => relation_to(server, caster, target) == RelationState::Hostile,
            _ => true,
        },
        3 => super::geometry::is_front(&world_pos(server, caster), &world_pos(server, target)),
        5 => target
            .as_obj()
            .is_some_and(|t| super::buffs::check_buffs_by_tag(server, t, p1 as u32)),
        6 => caster
            .as_obj()
            .and_then(|c| c.as_player())
            .is_some_and(|_| weapon_wield_kind(server, caster.as_obj().expect("player")) == p1),
        7 => match caster_unit {
            Some(u) => {
                let roll = rand::thread_rng().gen_range(0..100);
                let ok = roll <= p1;
                u.with_unit(|x| x.unit_mut().skill_state.condition_chance = ok);
                ok
            }
            None => {
                tracing::warn!("PlotCondition Chance check without caster being a Unit");
                false
            }
        },
        8 => target
            .as_obj()
            .and_then(|t| t.with_unit(|u| u.unit().hp == 0))
            .unwrap_or(false),
        9 => match (caster.as_obj(), target.as_obj().filter(|t| t.is_unit())) {
            (Some(c), Some(t)) => {
                let hit = super::skill::roll_combat_dice(server, &state.skill, c, t);
                state.skill.state().hit_types.insert(t.obj_id(), hit);
                hit.is_miss()
            }
            _ => true,
        },
        10 => {
            // InstrumentType: the musical slot weapon's holdable slot type
            caster
                .as_obj()
                .and_then(|c| c.as_player())
                .is_some_and(|_| {
                    server
                        .systems
                        .skills
                        .ext
                        .items()
                        .weapon_holdable(
                            server,
                            caster.as_obj().expect("player"),
                            super::stats::equip_slot::MUSICAL,
                        )
                        .is_some_and(|h| h.slot_type_id as i32 == p1)
                })
        }
        11 => {
            let range = super::geometry::distance_to(server, caster, target, false);
            range >= p1 as f32 && range <= p2 as f32
        }
        12 => match caster_unit {
            Some(u) => {
                let plot = u
                    .with_unit(|x| x.unit().skill_state.active_plot.clone())
                    .flatten();
                let value = plot
                    .map(|p| p.0.inner().variables.get(p1 as usize).copied().unwrap_or(0))
                    .unwrap_or(0);
                compare_with_operator(value, p2, p3)
            }
            None => {
                tracing::warn!("PlotCondition Variable check without caster being a Unit");
                false
            }
        },
        13 => match caster_unit {
            Some(u) => {
                let value = crate::models::unit::UnitAttribute::from_u8(p1 as u8)
                    .and_then(StatKind::from_unit_attribute)
                    .map_or(0, |k| stat(server, u, k) as i32);
                compare_with_operator(value, p2, p3)
            }
            None => false,
        },
        14 => caster
            .as_obj()
            .and_then(|c| c.as_player())
            .is_some_and(|p| {
                let points = p
                    .data()
                    .actability
                    .actabilities
                    .iter()
                    .find(|a| a.id == p1 as u32)
                    .map_or(0, |a| a.point);
                compare_with_operator(points, p2, p3)
            }),
        15 => target
            .as_obj()
            .is_some_and(|t| super::buffs::check_buff_tag(server, t, tags::STEALTH)),
        16 => target.as_obj().is_some_and(|t| {
            !super::buffs::check_buff_tag(server, t, tags::STEALTH) && t.is_visible()
        }),
        17 => caster
            .as_obj()
            .and_then(|c| c.as_player())
            .is_some_and(|p| {
                let c = p.data();
                let l = crate::db_character::ab_level(server, &c, p1 as u8);
                l >= p2 && l <= p3
            }),
        _ => true,
    };
    if cond.not_condition {
        !res
    } else {
        res
    }
}

/// `Unit.GetWeaponWieldKind()` (`WeaponWieldKind`: 0 None, 1 OneHanded, 2 TwoHanded, 3 Duel).
pub fn weapon_wield_kind(server: &GameServer, unit: &WorldObject) -> i32 {
    use super::stats::{equip_slot, slot_type};
    let items = server.systems.skills.ext.items();
    match items.weapon_holdable(server, unit, equip_slot::MAINHAND) {
        Some(h) if h.slot_type_id == slot_type::TWO_HANDED => 2,
        Some(h)
            if h.slot_type_id == slot_type::ONE_HANDED || h.slot_type_id == slot_type::MAINHAND =>
        {
            match items.weapon_holdable(server, unit, equip_slot::OFFHAND) {
                Some(o)
                    if o.slot_type_id == slot_type::ONE_HANDED
                        || o.slot_type_id == slot_type::OFFHAND =>
                {
                    3
                }
                _ => 1,
            }
        }
        _ => 0,
    }
}

/// `PlotTargetInfo.UpdateTargetInfo` (source then targets).
fn update_target_info(
    server: &GameServer,
    ti: &mut PlotTargetInfo,
    event: &PlotEventTemplate,
    state: &PlotState,
) {
    match event.source_update_method_id {
        1 => ti.source = Some(Target::Obj(state.ctx.caster.clone())),
        2 => ti.source = Some(state.ctx.target.clone()),
        3 => ti.source = ti.previous_source.clone(),
        4 => ti.source = ti.previous_target.clone(),
        _ => {}
    }
    let template_relation =
        SkillTargetRelation::from_raw(i64::from(state.skill.template.target_relation))
            .unwrap_or(SkillTargetRelation::Any);
    let caster_t = Target::Obj(state.ctx.caster.clone());
    let add_if_allowed = |ti: &mut PlotTargetInfo, t: Option<Target>| {
        if let Some(t) = t {
            if is_relation_valid(server, template_relation, &caster_t, &t) {
                ti.effected.push(t);
            }
        }
    };
    let p = &event.target_update_method_params;
    match event.target_update_method_id {
        1 => {
            ti.target = Some(caster_t.clone());
            ti.effected.push(caster_t.clone());
        }
        2 => {
            ti.target = Some(state.ctx.target.clone());
            add_if_allowed(ti, ti.target.clone());
        }
        3 => {
            ti.target = ti.previous_source.clone();
            if let Some(t) = ti.target.clone() {
                ti.effected.push(t);
            }
        }
        4 => {
            ti.target = ti.previous_target.clone();
            add_if_allowed(ti, ti.target.clone());
        }
        5 => {
            let args = AreaArgs {
                shape: p[0] as u32,
                max_targets: p[1],
                distance: p[2],
                angle: p[3],
                height_offset: p[4],
                hit_once: p[6] == 1,
                relation: p[7],
                type_flag: p[8] as u8,
            };
            ti.target = update_area_target(server, ti, state, event, &args, false);
        }
        6 => {
            let args = AreaArgs {
                shape: p[0] as u32,
                max_targets: 0,
                distance: 0,
                angle: 0,
                height_offset: 0,
                hit_once: p[1] == 1,
                relation: p[2],
                type_flag: p[3] as u8,
            };
            ti.target = update_random_unit_target(server, ti, state, event, &args);
        }
        7 => {
            let args = AreaArgs {
                shape: p[0] as u32,
                max_targets: p[1],
                distance: p[2],
                angle: 0,
                height_offset: p[3],
                hit_once: p[5] == 1,
                relation: p[6],
                type_flag: p[7] as u8,
            };
            ti.target = update_area_target(server, ti, state, event, &args, true);
        }
        _ => {}
    }
}

/// `PlotTargetAreaParams` / `PlotTargetRandomAreaParams` / `PlotTargetRandomUnitParams`.
struct AreaArgs {
    shape: u32,
    max_targets: i32,
    distance: i32,
    angle: i32,
    height_offset: i32,
    hit_once: bool,
    relation: i32,
    type_flag: u8,
}

/// `PlotTargetInfo.FilterTargets`.
fn filter_targets(
    server: &GameServer,
    units: Vec<WorldObject>,
    state: &PlotState,
    args: &AreaArgs,
    event_id: u32,
) -> Vec<WorldObject> {
    let t = &state.skill.template;
    let hit = state
        .inner()
        .hit_objects
        .get(&event_id)
        .cloned()
        .unwrap_or_default();
    let caster_t = Target::Obj(state.ctx.caster.clone());
    let filtered: Vec<WorldObject> = units
        .into_iter()
        .filter(|o| o.is_unit())
        .filter(|o| {
            let hp = o.with_unit(|u| u.unit().hp).unwrap_or(0);
            (t.target_alive || hp == 0) && (t.target_dead || hp > 0)
        })
        .filter(|o| !args.hit_once || !hit.contains(&o.obj_id()))
        .filter(|o| {
            relation_to(server, &caster_t, &Target::Obj(o.clone())) != RelationState::Neutral
        })
        .collect();
    let rel = SkillTargetRelation::from_raw(i64::from(t.target_relation))
        .unwrap_or(SkillTargetRelation::Any);
    let filtered = filter_with_relation(server, rel, &caster_t, filtered);
    let rel2 =
        SkillTargetRelation::from_raw(i64::from(args.relation)).unwrap_or(SkillTargetRelation::Any);
    let filtered = filter_with_relation(server, rel2, &caster_t, filtered);
    filtered
        .into_iter()
        .filter(|o| {
            o.with_unit(|u| u.type_flag() as u8 & args.type_flag != 0)
                .unwrap_or(false)
        })
        .collect()
}

fn update_area_target(
    server: &GameServer,
    ti: &mut PlotTargetInfo,
    state: &PlotState,
    event: &PlotEventTemplate,
    args: &AreaArgs,
    random: bool,
) -> Option<Target> {
    let prev = ti.previous_target.clone()?;
    let mut pos: PosUnit = pos_unit_from(server, &prev);
    if random {
        let deg: i32 = rand::thread_rng().gen_range(-180..180);
        pos.pos.rot_z = (deg as f32).to_radians();
        pos.add_distance_to_front(args.distance as f32 / 1000.0);
    } else {
        pos.rotate_z(-(args.angle as f32).to_radians());
        if args.distance != 0 {
            pos.add_distance_to_front(args.distance as f32 / 1000.0 - 0.01);
        }
    }
    // WorldManager.GetHeight: no heightmaps are loaded (0)
    let prev_z = world_pos(server, &prev).z;
    pos.pos.z = (prev_z + args.height_offset as f32 / 1000.0).max(0.0);
    let pos_t = Target::Pos(pos);
    if args.max_targets == 0 {
        ti.effected.push(pos_t.clone());
        return Some(pos_t);
    }
    let shape = server
        .game_data
        .systems
        .skills
        .area_shapes
        .get(&args.shape)
        .copied();
    let Some(shape) = shape else {
        tracing::error!(
            "Plot event {}: unknown area shape {} (NullReferenceException)",
            event.id,
            args.shape
        );
        return Some(pos_t);
    };
    let around = around_shape(server, &pos_t, &shape).unwrap_or_default();
    let units: Vec<WorldObject> = filter_targets(server, around, state, args, event.id)
        .into_iter()
        .take(args.max_targets.max(0) as usize)
        .collect();
    state
        .inner()
        .hit_objects
        .entry(event.id)
        .or_default()
        .extend(units.iter().map(WorldObject::obj_id));
    ti.effected.extend(units.into_iter().map(Target::Obj));
    Some(pos_t)
}

fn update_random_unit_target(
    server: &GameServer,
    ti: &mut PlotTargetInfo,
    state: &PlotState,
    event: &PlotEventTemplate,
    args: &AreaArgs,
) -> Option<Target> {
    let source = ti.source.clone()?;
    let shape = server
        .game_data
        .systems
        .skills
        .area_shapes
        .get(&args.shape)
        .copied();
    let Some(shape) = shape else {
        tracing::error!(
            "Plot event {}: unknown area shape {} (NullReferenceException)",
            event.id,
            args.shape
        );
        return None;
    };
    let around = around_shape(server, &source, &shape).unwrap_or_default();
    let mut filtered = filter_targets(server, around, state, args, event.id);
    if args.hit_once {
        let prev = ti.previous_target.as_ref().map_or(0, Target::obj_id);
        filtered.retain(|o| o.obj_id() != prev);
    }
    if filtered.is_empty() {
        return None;
    }
    let idx = rand::thread_rng().gen_range(0..filtered.len());
    let pick = filtered.swap_remove(idx);
    ti.effected.push(Target::Obj(pick.clone()));
    state
        .inner()
        .hit_objects
        .entry(event.id)
        .or_default()
        .push(pick.obj_id());
    Some(Target::Obj(pick))
}

/// `PlotObject` for a source / target.
fn plot_object(server: &GameServer, t: &Target) -> PlotObject {
    match t {
        Target::Obj(o) => PlotObject::Unit(o.obj_id()),
        Target::Pos(_) => {
            let p = world_pos(server, t);
            let (roll, pitch, yaw) = p.roll_pitch_yaw_sbytes();
            PlotObject::Position {
                x: p.x,
                y: p.y,
                z: p.z,
                roll,
                pitch,
                yaw,
            }
        }
    }
}

/// `PlotNode.Execute(state, targetInfo, packets)` (the effects run immediately; the
/// `SCPlotEventPacket` is returned for the caller to batch).
fn node_execute_packet(
    server: &GameServer,
    tree: &PlotTree,
    node: usize,
    state: &Arc<PlotState>,
    ti: &PlotTargetInfo,
) -> Option<SCPlotEventPacket> {
    let data = &server.game_data.systems.skills;
    let event = data.plots.event(tree.nodes[node].event_id)?;
    let channel_start = tree.nodes[node].children.iter().any(|&c| {
        data.plots
            .parent_next_event(tree, c)
            .is_some_and(|n| n.channeling)
    });
    let mut flag = 2u8;
    for eff in &event.effects {
        apply_plot_effect(server, eff, state, ti, event, &mut flag, channel_start);
    }
    let cast_time = event
        .next_events
        .iter()
        .filter(|n| n.casting || n.channeling)
        .map(|n| n.delay / 10)
        .max()
        .unwrap_or(0);
    let caster = state.caster();
    let cast_mul = stat(server, caster, StatKind::CastTimeMul);
    let cast_time = (apply_skill_modifiers(
        server,
        &state.skill,
        caster,
        SkillAttribute::CastTime,
        f64::from(cast_time),
    ) * cast_mul)
        .max(0.0);
    let parent_next = data.plots.parent_next_event(tree, node);
    {
        let mut inner = state.inner();
        if cast_time > 0.0 {
            inner.is_casting = true;
        }
        if parent_next.is_some_and(|n| n.casting) {
            inner.is_casting = false;
        }
        inner.is_channeling = !parent_next.is_some_and(|n| n.channeling);
    }
    let has_special = event.effects.iter().any(|e| {
        matches!(
            data.effect_by_name(e.actual_id, &e.actual_type),
            Some(EffectTemplate::Special(_) | EffectTemplate::SkillController(_))
        )
    });
    if has_special || cast_time > 0.0 || !event.conditions.is_empty() {
        let unk_id = if parent_next.is_some_and(|n| n.casting || n.channeling) {
            caster.obj_id()
        } else {
            0
        };
        let source = ti.source.clone()?;
        let target = ti.target.clone()?;
        return Some(SCPlotEventPacket {
            tl: state.skill.tl_id(),
            event_id: event.id,
            skill_id: state.skill.template.id,
            caster: plot_object(server, &source),
            target: plot_object(server, &target),
            obj_id: unk_id,
            casting_time: cast_time as u16,
            flag,
            item_id: 0,
            target_unit_count: ti.effected.len() as u8,
        });
    }
    None
}

/// `PlotNode.Execute` without a packet builder (broadcasts the plot event itself).
fn execute_node(
    server: &GameServer,
    tree: &PlotTree,
    node: usize,
    state: &Arc<PlotState>,
    ti: &PlotTargetInfo,
) {
    if let Some(p) = node_execute_packet(server, tree, node, state, ti) {
        broadcast(server, state.caster(), &p, true);
    }
}

/// `PlotEventEffect.ApplyEffect`.
fn apply_plot_effect(
    server: &GameServer,
    eff: &super::data::plots::PlotEventEffect,
    state: &Arc<PlotState>,
    ti: &PlotTargetInfo,
    event: &PlotEventTemplate,
    flag: &mut u8,
    channeled: bool,
) {
    let data = &server.game_data.systems.skills;
    let kind = EffectKind::from_name(&eff.actual_type);
    let template = kind.and_then(|k| data.effect(k, eff.actual_id));
    let buff_effect_id = match template {
        Some(EffectTemplate::Buff(b)) => {
            *flag = 2;
            b.buff_id
        }
        _ => None,
    };
    let Some(source) = plot_source(eff.source_id, state, ti) else {
        tracing::error!("PlotEventEffect: invalid source {}", eff.source_id);
        return;
    };
    for new_target in &ti.effected {
        let Some(target) = plot_target(eff.target_id, state, ti, new_target) else {
            tracing::error!("PlotEventEffect: invalid target {}", eff.target_id);
            return;
        };
        if channeled {
            if let (Some(id), Some(o)) = (buff_effect_id, target.as_obj()) {
                state.inner().channeled_buffs.push((o.clone(), id));
            }
        }
        let (Some(kind), Some(template)) = (kind, template) else {
            return;
        };
        let args = EffectArgs {
            caster: source.as_obj().cloned(),
            caster_caster: state.ctx.caster_caster,
            target: target.clone(),
            target_caster: state.ctx.target_caster,
            cast: CastAction::Plot {
                plot_id: event.plot_id,
                tl_id: state.skill.tl_id(),
                event_id: event.id,
                skill_id: state.skill.template.id,
            },
            source: EffectSource::from_skill(state.skill.clone()),
            skill_object: &state.ctx.skill_object,
        };
        effects::apply(server, kind, template, &args, None);
    }
}
