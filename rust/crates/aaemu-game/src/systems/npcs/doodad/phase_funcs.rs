//! `DoodadPhaseFuncTemplate.Use` of every phase func (the 42 `FuncTemplate` variants with
//! [`FuncTemplate::is_phase_func`]).
//!
//! `DoodadPhaseFunc.PulseTriggered` lives on the shared `DoodadPhaseFunc` row objects of
//! `DoodadManager._phaseFuncs` in C#, i.e. it is shared by every doodad whose phase uses that row.
//! It is kept in `DoodadManager::pulse_triggered` keyed by (func group id, row index) with the
//! same sharing.

use std::collections::HashMap;
use std::sync::Arc;

use rand::Rng;

use super::phase::{self, doodad_has_matching_climate};
use super::runtime::DoodadCtx;
use super::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
use super::tasks::{self, FuncTaskKind};
use super::templates::*;
use crate::data::world_templates::WorldSpawnPosition;
use crate::systems::npcs::area_trigger::{self, AreaTrigger};
use crate::world::{now_ms, WorldObject};

/// `Random.Shared.Next(min, max)` (`max` exclusive, `min` when `min >= max`).
fn random_next(min: i32, max: i32) -> i32 {
    if min >= max {
        min
    } else {
        rand::thread_rng().gen_range(min..max)
    }
}

/// `owner.OverridePhase = nextPhase; return true` when the caster is a character.
fn override_if_character(
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    next_phase: i32,
) -> bool {
    if DoodadCtx::as_player(caster).is_some() {
        ctx.doodad.data().phase.override_phase = next_phase;
        return true;
    }
    false
}

/// The `OverridePhaseTime` handling of `Timer` / `Growth`: returns the remaining delay.
fn time_left_with_override(ctx: &DoodadCtx<'_>, delay: f64) -> f64 {
    let mut d = ctx.doodad.data();
    let mut time_left = delay;
    if d.phase.override_phase_time_ms > 0 {
        // Reset the override
        d.phase_time_ms = d.phase.override_phase_time_ms;
        d.phase.override_phase_time_ms = 0;
        d.update_final_phase_end();
        let since_phase_start = (now_ms() - d.phase_time_ms) as f64;
        time_left = delay - since_phase_start;
    }
    if time_left < 1.0 {
        time_left = 1.0;
    }
    d.growth_time_ms = now_ms() + time_left as i64;
    time_left
}

/// `DoodadPhaseFunc.Use(caster, owner)` → the template's `Use`. Returns `true` to stop the phase
/// funcs of the current phase (C# return value).
pub fn use_phase_func(t: &FuncTemplate, ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>) -> bool {
    let server = ctx.server;
    match t {
        FuncTemplate::Timer(f) => {
            if f.next_phase > 0 {
                tracing::trace!(
                    "DoodadFuncTimer: Delay {}, NextPhase {}, KeepRequester {}, ShowTip {}, ShowEndTime {}, Tip {}",
                    f.delay, f.next_phase, f.keep_requester, f.show_tip, f.show_end_time, f.tip
                );
                let time_left = time_left_with_override(ctx, f64::from(f.delay));
                tasks::start_func_task(
                    server,
                    &ctx.doodad,
                    caster,
                    FuncTaskKind::Timer {
                        next_phase: f.next_phase,
                    },
                    tasks::ms(time_left),
                );
            }
            // we never interrupt the sequence of phase functions
            false
        }
        FuncTemplate::Final(f) => {
            tracing::trace!(
                "DoodadFuncFinal: After {}, Respawn {}, MinTime {}, MaxTime {}",
                f.after,
                f.respawn,
                f.min_time,
                f.max_time
            );
            // Clamp the range: Random.Shared.Next throws when min > max
            let min_time = f.min_time.max(0);
            let max_time = f.max_time.max(min_time);
            let delay = random_next(min_time, max_time);
            let kind = FuncTaskKind::Final {
                respawn: f.respawn,
                delay,
                respawn_stage: None,
            };
            if f.after > 0 {
                let mut after_delay = f.after as u32;
                {
                    let mut d = ctx.doodad.data();
                    if d.phase.override_phase_time_ms > 0 {
                        d.phase_time_ms = d.phase.override_phase_time_ms;
                        d.phase.override_phase_time_ms = 0;
                        d.update_final_phase_end();
                        after_delay = d.time_left(now_ms());
                    }
                }
                // After ms remove the object from visibility
                tasks::start_func_task(
                    server,
                    &ctx.doodad,
                    caster,
                    kind,
                    tasks::ms(f64::from(after_delay)),
                );
            } else {
                super::manager::delete(server, &ctx.world, &ctx.doodad);
                if !f.respawn {
                    return false;
                }
                tasks::start_func_task(
                    server,
                    &ctx.doodad,
                    caster,
                    kind,
                    tasks::ms(f64::from(delay)),
                );
            }
            true
        }
        FuncTemplate::Growth(f) => {
            // C# `owner.Scale = StartScale / 1000f` writes BaseUnit.Scale, which the get-only
            // Doodad.Scale override never reads: no visible effect (mirrored).
            let mut custom_delay = f64::from(f.delay) / server.systems.npcs.doodads.growth_rate(); // decrease delay
            if doodad_has_matching_climate(ctx) {
                custom_delay *= f64::from(0.73f32);
            }
            let time_left = time_left_with_override(ctx, custom_delay);
            tracing::trace!(
                "DoodadFuncGrowth: Delay {}, StartScale {}, EndScale {}, NextPhase {}",
                f.delay,
                f.start_scale,
                f.end_scale,
                f.next_phase
            );
            tasks::start_func_task(
                server,
                &ctx.doodad,
                caster,
                FuncTaskKind::Growth {
                    next_phase: f.next_phase,
                    end_scale: f.end_scale as f32 / 1000.0,
                },
                tasks::ms(time_left),
            );
            false
        }
        FuncTemplate::Tod(f) => {
            let cur_time = server.time.get_time();
            if cur_time >= f.tod_as_hours {
                tracing::trace!(
                    "DoodadFuncTod: curTime {cur_time}, Tod {}, OverridePhase {}",
                    f.tod,
                    f.next_phase
                );
                ctx.doodad.data().phase.override_phase = f.next_phase;
                return true;
            }
            false
        }
        FuncTemplate::RatioChange(f) => {
            let mut d = ctx.doodad.data();
            if d.phase.phase_ratio + d.phase.cumulative_phase_ratio <= f.ratio {
                d.phase.override_phase = f.next_phase;
                return true; // interrupt the phase functions and switch to NextPhase
            }
            false
        }
        FuncTemplate::RatioRespawn(f) => {
            let spawner = server
                .systems
                .npcs
                .spawns
                .doodad_spawner_of(ctx.doodad.obj_id)
                .filter(|s| s.id > 0);
            let phase_ratio = ctx.doodad.data().phase.phase_ratio;
            if let (true, Some(spawner)) = (phase_ratio <= f.ratio, spawner) {
                if !ctx.data().exist(f.spawn_doodad_id) {
                    tracing::error!(
                        "DoodadFuncRatioRespawn: Spawn template {} does not exist (spawner={}).",
                        f.spawn_doodad_id,
                        spawner.id
                    );
                    ctx.doodad.data().phase.cumulative_phase_ratio -= f.ratio;
                    return false;
                }
                spawner.set_respawn_doodad_template_id(f.spawn_doodad_id);
                spawner.despawn(server, &ctx.doodad);
                if spawner.spawn(server, 0).is_none() {
                    tracing::error!(
                        "DoodadFuncRatioRespawn: Spawn failed for template {} at spawner {}.",
                        f.spawn_doodad_id,
                        spawner.id
                    );
                }
                return true; // Interrupt the PhaseFunc because we swapped the doodad
            }
            ctx.doodad.data().phase.cumulative_phase_ratio -= f.ratio;
            false
        }
        FuncTemplate::RequireItem(f) => match DoodadCtx::as_player(caster) {
            Some(p) => {
                let count = server
                    .systems
                    .npcs
                    .hooks
                    .economy
                    .get()
                    .map_or(0, |h| h.item_count(server, p, f.item_id));
                count <= 0
            }
            None => true,
        },
        FuncTemplate::RequireQuest(f) => match DoodadCtx::as_player(caster) {
            Some(p) => !server
                .systems
                .npcs
                .hooks
                .quests
                .get()
                .is_some_and(|h| h.has_quest(server, p, f.quest_id)),
            None => false, // caster is not a player, allow execution as we can't check
        },
        FuncTemplate::Pulse(f) => {
            use_pulse(ctx, caster, f.flag);
            false
        }
        FuncTemplate::PulseTrigger(f) => {
            if DoodadCtx::as_player(caster).is_none() {
                return true;
            }
            // Grab the calling PhaseFunc (CurrentPhaseFuncs.FirstOrDefault(x => x.FuncId == Id))
            let group = ctx.doodad.data().func_group_id;
            let Some(index) = ctx
                .data()
                .phase_funcs(group)
                .iter()
                .position(|pf| pf.func_id == f.id)
            else {
                tracing::warn!(
                    "DoodadFuncPulseTrigger Flag={}, NextPhase={} was not triggered from a DoodadFuncPulseTrigger",
                    f.flag,
                    f.next_phase
                );
                return false;
            };
            let manager = &server.systems.npcs.doodads;
            let triggered = manager.pulse_triggered(group, index);
            if f.flag && !triggered {
                manager.set_pulse_triggered(group, index, true); // Prevent loops
                ctx.doodad.data().phase.override_phase = f.next_phase;
                return true;
            }
            false
        }
        FuncTemplate::Respawn(f) => {
            tracing::trace!(
                "DoodadFuncRespawn: MinTime {}, MaxTime {}",
                f.min_time,
                f.max_time
            );
            if let Some(p) = DoodadCtx::as_player(caster) {
                use_respawn(ctx, p);
            }
            false
        }
        FuncTemplate::ClimateReact(f) => {
            if !doodad_has_matching_climate(ctx) || f.next_phase <= 0 {
                return false;
            }
            ctx.doodad.data().phase.override_phase = f.next_phase;
            true
        }
        FuncTemplate::CraftDirect(f) => override_if_character(ctx, caster, f.next_phase),
        FuncTemplate::Hunger(f) => override_if_character(ctx, caster, f.next_phase),
        FuncTemplate::PuzzleOut(f) => override_if_character(ctx, caster, f.next_phase),
        FuncTemplate::SiegePeriod(f) => override_if_character(ctx, caster, f.next_phase),
        FuncTemplate::SpawnGimmick(f) => {
            tracing::info!(
                "DoodadFuncSpawnGimmick GimmickId={}, scale={}",
                f.gimmick_id,
                f.scale
            );
            override_if_character(ctx, caster, f.next_phase)
        }
        FuncTemplate::ZoneReact(f) => {
            let zone_id = ctx.doodad.data().transform.zone_id;
            let group = server
                .game_data
                .zones
                .zone_by_key(zone_id)
                .map(|z| z.group_id);
            if group == Some(f.zone_group_id) {
                ctx.doodad.data().phase.override_phase = f.next_phase;
                return true;
            }
            false
        }
        FuncTemplate::Coffer(_) => {
            ctx.doodad.data().phase.to_next_phase = false;
            if let Some(p) = DoodadCtx::as_player(caster) {
                let (is_coffer, opened_by) = {
                    let d = ctx.doodad.data();
                    (
                        d.coffer.is_some(),
                        d.coffer.as_ref().and_then(|c| c.opened_by),
                    )
                };
                if is_coffer {
                    let char_id = p.data().id;
                    if opened_by == Some(char_id) {
                        super::coffer::close_coffer_doodad(server, p, ctx.doodad.obj_id);
                    } else {
                        super::coffer::open_coffer_doodad(server, p, ctx.doodad.obj_id);
                    }
                }
            }
            false
        }
        FuncTemplate::Clout(f) => {
            use_clout(ctx, caster, f);
            false
        }
        // Phase funcs whose C# `Use` only logs (and returns false).
        FuncTemplate::Animate(_)
        | FuncTemplate::BuyFishItem(_)
        | FuncTemplate::BuyFishModel(_)
        | FuncTemplate::ConsumeChangerModel(_)
        | FuncTemplate::ConsumeChangerModelItem(_)
        | FuncTemplate::ConsumeItem(_)
        | FuncTemplate::ConvertFishItem(_)
        | FuncTemplate::CraftStartCraft(_)
        | FuncTemplate::DeclareSiege(_)
        | FuncTemplate::Exchange(_)
        | FuncTemplate::FishSchool(_)
        | FuncTemplate::HouseFarm(_)
        | FuncTemplate::Logic(_)
        | FuncTemplate::LogicFamilyProvider(_)
        | FuncTemplate::LogicFamilySubscriber(_)
        | FuncTemplate::NaviRemoveTimer(_)
        | FuncTemplate::Parrot(_)
        | FuncTemplate::PlayFlowGraph(_)
        | FuncTemplate::PuzzleIn(_)
        | FuncTemplate::Sign(_)
        | FuncTemplate::SpawnMgmt(_)
        | FuncTemplate::WaterVolume(_) => {
            tracing::trace!("{}", t.func_type());
            false
        }
        // Not a DoodadPhaseFuncTemplate (never returned by GetPhaseFuncTemplate).
        _ => false,
    }
}

/// `DoodadFuncPulse.Use`.
fn use_pulse(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, flag: bool) {
    tracing::trace!("DoodadFuncPulse: Flag {flag}");
    let Some(caster) = caster else {
        return;
    };
    let server = ctx.server;
    let Some(spawner) = server
        .systems
        .npcs
        .spawns
        .doodad_spawner_of(ctx.doodad.obj_id)
    else {
        return;
    };
    let around: Vec<_> = ctx.world.around(caster, |o| o.as_doodad().cloned());
    let mut doodads = Vec::new();
    if let Some(related) = &spawner.related_ids {
        for &related_id in related {
            doodads.extend(
                around
                    .iter()
                    .filter(|d| d.data().template_id == related_id)
                    .cloned(),
            );
        }
    }
    for doodad in doodads {
        let group = doodad.data().func_group_id;
        let funcs = ctx.data().phase_funcs(group);
        for (index, func) in funcs.iter().enumerate() {
            if func.func_type == "DoodadFuncPulseTrigger" && flag {
                server
                    .systems
                    .npcs
                    .doodads
                    .set_pulse_triggered(group, index, false); // Allow one-time execution
                let other = DoodadCtx::new(server, ctx.world.clone(), doodad.clone());
                let fg = doodad.data().func_group_id;
                phase::do_change_phase(&other, Some(caster), fg as i32);
            }
        }
    }
}

/// `DoodadFuncRespawn.Use` for a character caster: a one-off spawner 1 m in front of them.
fn use_respawn(ctx: &DoodadCtx<'_>, player: &crate::world::PlayerRef) {
    let server = ctx.server;
    let pobj = WorldObject::from(player);
    let mut pos = ctx.world.world_pos_rot(&pobj);
    let zone_id = player.data().unit.transform.zone_id;
    // AddDistanceToFront(1f)
    pos.x += -pos.rot_z.sin();
    pos.y += pos.rot_z.cos();
    // C#: GeoData.GetHeight (exception when outside the map; the old height is kept here)
    // (collision service: the ground under the point, static + terrain)
    if let Some(h) =
        crate::systems::ai::collision::ground_height(server, ctx.world.id, pos.x, pos.y, pos.z)
    {
        pos.z = h;
    } else if let Ok(h) =
        crate::systems::npcs::heightmap::world_height(server, &ctx.world, pos.x, pos.y)
    {
        pos.z = h;
    }
    let (template_id, obj_id) = (ctx.doodad.data().template_id, ctx.doodad.obj_id);
    let def = DoodadSpawnerDef {
        base: SpawnerBase {
            id: obj_id,
            unit_id: template_id,
            position: WorldSpawnPosition {
                world_id: ctx.world.template.id,
                zone_id,
                x: pos.x,
                y: pos.y,
                z: pos.z,
                yaw: pos.rot_z,
                pitch: pos.rot_y,
                roll: pos.rot_x,
            },
            ..SpawnerBase::default()
        },
        scale: 1.0,
        count: 1,
        related_ids: None,
        respawn_doodad_template_id: 0,
    };
    let spawner = Arc::new(DoodadSpawner::new(def, Some(ctx.world.id)));
    spawner.spawn(server, 0);
}

/// `DoodadFuncClout.Use`.
fn use_clout(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, f: &DoodadFuncClout) {
    tracing::trace!(
        "DoodadFuncClout : Duration {}, Tick {}, BuffId {}, NextPhase {}, AoeShapeId {}",
        f.duration,
        f.tick,
        f.buff_id,
        f.next_phase,
        f.aoe_shape_id
    );
    let server = ctx.server;
    let shape = server
        .game_data
        .systems
        .npcs
        .area_trigger
        .get_area_shape_by_id(f.aoe_shape_id);
    let owner = if f.use_origin_source {
        // look for a doodad within 4 m of the caster
        caster
            .and_then(|c| {
                let cpos = ctx.world.world_pos_rot(c);
                ctx.world
                    .around(c, |o| o.as_doodad().cloned())
                    .into_iter()
                    .find(|d| {
                        let p = ctx.world.world_pos_rot(&WorldObject::from(d));
                        let (dx, dy) = (p.x - cpos.x, p.y - cpos.y);
                        dx * dx + dy * dy <= 16.0
                    })
            })
            .unwrap_or_else(|| ctx.doodad.clone())
    } else {
        ctx.doodad.clone()
    };
    let trigger_caster = match caster {
        Some(c) if c.as_player().is_some() => c.clone(),
        _ => ctx.obj(),
    };
    let inside_buff_id = server
        .game_data
        .skills
        .buffs
        .contains_key(&f.buff_id)
        .then_some(f.buff_id);
    let mut trigger = AreaTrigger::default();
    trigger.shape = shape;
    trigger.owner = Some(owner.clone());
    trigger.caster = Some(trigger_caster.clone());
    trigger.target_relation = f.target_relation;
    trigger.inside_buff_id = inside_buff_id;
    trigger.effects_per_buff = HashMap::from([(f.buff_id, f.effects.clone())]);
    trigger.tick_rate = f.tick;
    let trigger = Arc::new(trigger);
    area_trigger::add_area_trigger(server, trigger.clone());
    if f.duration > 0 {
        // C#: `owner.FuncTask = new DoodadFuncCloutTask(areaTrigger.Caster, areaTrigger.Owner,
        // ...)`: the task is this doodad's FuncTask but acts on the trigger owner.
        tasks::start_func_task_for(
            server,
            &ctx.doodad,
            &owner,
            Some(&trigger_caster),
            FuncTaskKind::Clout {
                next_phase: f.next_phase,
                trigger,
            },
            tasks::ms(f64::from(f.duration)),
        );
    }
}
