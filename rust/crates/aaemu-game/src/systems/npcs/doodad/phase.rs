//! The `Doodad` phase machine (`Models/Game/DoodadObj/Doodad.cs`): the `FuncGroupId` setter,
//! `Use`, `DoFunc`, `DoPhaseFuncs`, `DoChangePhase`, `HasOnlyGroupKindStart`,
//! `IsGroupKindStart`, `GetFuncGroupId`, `OnSkillHit`, `InitDoodad`, `GetOwnerCharacter`,
//! `IsPublicPropertyByAge`, the theft evidence of `Use` and `ZoneManager.DoodadHasMatchingClimate`.
//!
//! Locking: every function takes the doodad's lock only for short reads / writes and never holds
//! it while calling funcs, phase funcs, the world or another doodad.
//!
//! Concurrency (as in C#): `Use` / timers of one doodad are not serialised; two concurrent uses
//! interleave at the lock granularity like the C# threads do.

use rand::Rng;

use super::data::DoodadData;
use super::packets::{error_message, error_msg, SCDoodadPhaseChangedPacket, SCDoodadSoundPacket};
use super::runtime::DoodadCtx;
use super::templates::{doodad_func_group_kind, DoodadFunc, FuncTemplate};
use super::{db, funcs, phase_funcs, tasks};
use crate::models::doodad::{doodad_owner_type, Doodad};
use crate::server::GameServer;
use crate::world::{now_ms, DoodadRef, PlayerRef, WorldInstance, WorldObject};

/// `Climate.Any`.
const CLIMATE_ANY: i32 = 1;

/// The `CurrentFuncs` / `CurrentPhaseFuncs` / `CurrentToDTriggers` / `TimeLeft` caches of the
/// current `FuncGroupId` (the second half of the `FuncGroupId` setter).
pub fn refresh_phase_caches(data: &DoodadData, d: &mut Doodad) {
    let group = d.func_group_id;
    d.current_func_types = data
        .funcs_for_group(group)
        .iter()
        .map(|f| f.func_type.clone())
        .collect();
    // Register new ToD triggers (if any)
    d.phase.tod_triggers.clear();
    let mut final_after = None;
    for pf in data.phase_funcs(group) {
        let template = data.phase_func_template(pf.func_id, &pf.func_type);
        if final_after.is_none() {
            if let Some(FuncTemplate::Final(f)) = template {
                if f.after > 0 {
                    final_after = Some(f.after);
                }
            }
        }
        if pf.func_type != "DoodadFuncTod" {
            continue;
        }
        let Some(FuncTemplate::Tod(tod)) = template else {
            tracing::error!("DoodadFuncTod is not a DoodadFuncTod");
            continue;
        };
        // Dictionary.TryAdd
        if !d.phase.tod_triggers.iter().any(|t| t.0 == tod.tod_as_hours) {
            d.phase
                .tod_triggers
                .push((tod.tod_as_hours, tod.next_phase));
        }
    }
    d.phase.final_after_ms = final_after;
    d.update_final_phase_end();
}

/// The `FuncGroupId` setter: on a change, `PhaseTime = now`, `Save()` when persistent, and the
/// current funcs / phase funcs / ToD trigger caches.
pub fn set_func_group_id(server: &GameServer, doodad: &DoodadRef, value: u32) {
    let save = {
        let mut d = doodad.data();
        if value == d.func_group_id {
            return;
        }
        d.func_group_id = value;
        d.phase_time_ms = now_ms(); // Save PhaseTime at start of new phase (group)
        let persistent = d.is_persistent;
        refresh_phase_caches(&server.game_data.systems.npcs.doodad, &mut d);
        persistent
    };
    if save {
        db::save(server, doodad);
    }
}

/// The `Data` setter: on a change, `Save()` when persistent (`SetData` is a plain write).
pub fn set_data(server: &GameServer, doodad: &DoodadRef, value: i32) {
    let save = {
        let mut d = doodad.data();
        if value == d.data {
            return;
        }
        d.data = value;
        d.is_persistent
    };
    if save {
        db::save(server, doodad);
    }
}

/// `GetFuncGroupId()`: the first `Start` func group of the template (0 when none).
pub fn get_func_group_id(data: &DoodadData, template_id: u32) -> u32 {
    data.doodad_func_groups(template_id)
        .iter()
        .find(|g| g.group_kind_id == doodad_func_group_kind::START)
        .map_or(0, |g| g.id)
}

/// `IsGroupKindStart(funcGroupId)`.
pub fn is_group_kind_start(ctx: &DoodadCtx<'_>, func_group_id: u32) -> bool {
    let template_id = ctx.doodad.data().template_id;
    ctx.data()
        .doodad_func_groups(template_id)
        .iter()
        .any(|g| g.group_kind_id == doodad_func_group_kind::START && g.id == func_group_id)
}

/// `HasOnlyGroupKindStart()`.
pub fn has_only_group_kind_start(ctx: &DoodadCtx<'_>) -> bool {
    let template_id = ctx.doodad.data().template_id;
    ctx.data().doodad_func_groups(template_id).iter().all(|g| {
        g.group_kind_id != doodad_func_group_kind::NORMAL
            && g.group_kind_id != doodad_func_group_kind::END
    })
}

/// `GetOwnerCharacter()` of any object (`Character` itself, a doodad's owner, the owner of a
/// slave / mate / house; `null` otherwise).
pub fn owner_character(
    server: &GameServer,
    world: &WorldInstance,
    obj: &WorldObject,
) -> Option<PlayerRef> {
    match obj {
        WorldObject::Player(p) => Some(p.clone()),
        WorldObject::Doodad(d) => doodad_owner_character(server, world, d),
        WorldObject::Slave(s) => {
            let owner = s.data().owner_obj_id;
            server.world.character_by_obj_id(owner)
        }
        WorldObject::Mate(m) => {
            let owner = m.data().owner_obj_id;
            server.world.character_by_obj_id(owner)
        }
        WorldObject::House(h) => {
            let owner = h.data().unit.owner_id;
            server.world.character_by_id(owner)
        }
        _ => None,
    }
}

/// `Doodad.GetOwnerCharacter()`.
pub fn doodad_owner_character(
    server: &GameServer,
    world: &WorldInstance,
    doodad: &DoodadRef,
) -> Option<PlayerRef> {
    let (owner_type, owner_id, owner_obj_id, owner_db_id) = {
        let d = doodad.data();
        (d.owner_type, d.owner_id, d.owner_obj_id, d.owner_db_id)
    };
    match owner_type {
        doodad_owner_type::CHARACTER => server.world.character_by_id(owner_id),
        doodad_owner_type::SLAVE => world
            .slave(owner_obj_id)
            .and_then(|s| owner_character(server, world, &WorldObject::from(&s))),
        doodad_owner_type::HOUSING => {
            crate::systems::npcs::hooks::house_by_db_id(server, owner_db_id)
                .and_then(|h| owner_character(server, world, &WorldObject::from(&h)))
        }
        _ => None,
    }
}

/// `IsPublicPropertyByAge(now)`.
pub fn is_public_property_by_age(ctx: &DoodadCtx<'_>, now_ms: i64) -> bool {
    let (template_id, plant_time) = {
        let d = ctx.doodad.data();
        (d.template_id, d.plant_time)
    };
    let Some(template) = ctx.data().template(template_id) else {
        return false;
    };
    let guard = template.group.guard_on_field_time;
    guard > 0 && (plant_time + i64::from(guard)) * 1000 < now_ms
}

/// `ZoneManager.DoodadHasMatchingClimate(doodad)`.
pub fn doodad_has_matching_climate(ctx: &DoodadCtx<'_>) -> bool {
    let template_id = ctx.doodad.data().template_id;
    let Some(template) = ctx.data().template(template_id) else {
        return false;
    };
    // If no climate defined, then don't give a bonus
    if template.climate_id == CLIMATE_ANY {
        return false;
    }
    let zone_id = {
        let zone_id = ctx.doodad.data().transform.zone_id;
        if zone_id > 0 {
            zone_id
        } else {
            // If ZoneId wasn't set yet, calculate it
            let pos = ctx.world.world_pos_rot(&ctx.obj());
            let zone_id = ctx.world.template.zone_id_at(pos.x, pos.y);
            ctx.doodad.data().transform.zone_id = zone_id;
            zone_id
        }
    };
    // C#: a missing zone throws (NullReferenceException in GetClimatesByZone); no bonus here.
    let Some(zone) = ctx.server.game_data.zones.zone_by_key(zone_id) else {
        return false;
    };
    ctx.server
        .systems
        .npcs
        .doodads
        .climates_of(zone.zone_climate_id)
        .contains(&template.climate_id)
}

/// `Doodad.Use(caster, startedSkillId, funcGroupId)`.
pub fn use_doodad(
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    started_skill_id: u32,
    func_group_id: i32,
) {
    let server = ctx.server;
    let mut skill_id = started_skill_id;
    let Some(caster) = caster else {
        return;
    };

    // Fix #1443 (defense in depth): refuse interaction with a doodad scheduled for despawn.
    if server
        .systems
        .npcs
        .spawns
        .despawn_field(ctx.doodad.obj_id)
        .is_some()
    {
        if let Some(p) = caster.as_player() {
            tracing::debug!(
                "Use refused: doodad {} is scheduled for despawn, ignoring use from {}",
                ctx.doodad.obj_id,
                p.data().unit.name
            );
            p.send(error_msg(error_message::NO_INTERACTION_AVAILABLE));
        }
        return;
    }

    if func_group_id > 0 {
        set_func_group_id(server, &ctx.doodad, func_group_id as u32);
    }

    loop {
        let (template_id, current) = {
            let mut d = ctx.doodad.data();
            d.phase.to_next_phase = false;
            d.phase.list_group_id.clear();
            (d.template_id, d.func_group_id)
        };
        tracing::trace!(
            "Use: TemplateId {template_id}, Using phase {current} with SkillId {skill_id}"
        );

        // first we find the functions, then we execute
        let func_with_skill = ctx.data().func_for_skill(current, skill_id).cloned();
        let all_funcs = ctx.data().funcs_for_group(current);
        if all_funcs.is_empty() {
            return; // Phase has no funcs
        }

        if skill_id == 0 {
            for f in all_funcs.iter().filter(|f| {
                matches!(
                    f.func_type.as_str(),
                    "DoodadFuncLootItem" | "DoodadFuncLootPack" | "DoodadFuncCutdowning"
                )
            }) {
                if do_func(ctx, Some(caster), started_skill_id, Some(f)) {
                    ctx.doodad.data().phase.list_group_id.clear();
                    return;
                }
            }
        } else if do_func(
            ctx,
            Some(caster),
            started_skill_id,
            func_with_skill.as_ref(),
        ) {
            // FuncGroupId is the current phase, func.NextPhase or OverridePhase
            let fg = ctx.doodad.data().func_group_id;
            do_change_phase(ctx, Some(caster), fg as i32);
            return;
        }

        theft_evidence(ctx, caster, started_skill_id);

        // then execute the phase functions (the FuncGroupId may have changed)
        let fg = ctx.doodad.data().func_group_id;
        let stop = do_change_phase(ctx, Some(caster), fg as i32);
        if stop || !ctx.doodad.data().phase.to_next_phase {
            tracing::trace!("Use: Did not pass the conditions check! TemplateId {template_id}");
            return;
        }
        skill_id = 0;
    }
}

/// The theft-evidence part of `Use` (`GenerateTheftEvidence` via the skills hook).
fn theft_evidence(ctx: &DoodadCtx<'_>, caster: &WorldObject, started_skill_id: u32) {
    let server = ctx.server;
    let caster_owner = owner_character(server, &ctx.world, caster);
    let (owner_type, owner_id) = {
        let d = ctx.doodad.data();
        (d.owner_type, d.owner_id)
    };
    let caster_owner_id = caster_owner.as_ref().map(|p| p.data().id);
    let is_different_character_owner =
        owner_type == doodad_owner_type::CHARACTER && Some(owner_id) != caster_owner_id;
    if !is_different_character_owner {
        return;
    }
    let hooks = server.systems.npcs.hooks.skills.get();
    let is_explicit_crime_skill = started_skill_id != 0
        && hooks
            .as_ref()
            .is_some_and(|h| h.skill_crime_point(server, started_skill_id) > 0);
    let is_skill_less_pickup = started_skill_id == 0;
    if (is_skill_less_pickup || is_explicit_crime_skill)
        && !is_public_property_by_age(ctx, now_ms())
    {
        // C#: GenerateTheftEvidence(casterOwningCharacter) with a possibly null criminal.
        if let (Some(h), Some(criminal)) = (hooks, caster_owner.as_ref()) {
            h.generate_theft_evidence(server, criminal, &ctx.doodad);
        }
    }
}

/// `DoFunc(caster, skillId, func)`: `true` stops the `Use` loop (wait for interaction).
pub fn do_func(
    ctx: &DoodadCtx<'_>,
    caster: Option<&WorldObject>,
    skill_id: u32,
    func: Option<&DoodadFunc>,
) -> bool {
    let Some(func) = func else {
        tracing::trace!("DoFunc: Finished execution with func = null");
        return true;
    };

    // then perform the function (DoodadFunc.Use -> template?.Use)
    if let Some(t) = ctx.data().func_template(func.func_id, &func.func_type) {
        funcs::use_func(t, ctx, caster, skill_id, func.next_phase);
    }
    if func.sound_id > 0 {
        ctx.broadcast(&SCDoodadSoundPacket {
            obj_id: ctx.doodad.obj_id,
            sound_id: func.sound_id,
        });
    }

    let (to_next_phase, override_phase) = {
        let d = ctx.doodad.data();
        (d.phase.to_next_phase, d.phase.override_phase)
    };
    if !to_next_phase {
        tracing::trace!("DoFunc Finished execution without ToNextPhase");
        // DoodadFuncLootItem sets ToNextPhase=false when loot fails; let Use() try the next one.
        return func.func_type != "DoodadFuncLootItem";
    }
    if func.next_phase == -1 {
        // We stay in the current phase; the check is needed for Windstone id=1473
        if !has_only_group_kind_start(ctx) {
            if tasks::cancel_func_task(&ctx.doodad) {
                tracing::debug!("DoFunc: The current timer has been canceled.");
            }
            despawn_or_delete(ctx);
        }
        return true;
    }
    if override_phase > 0 {
        // occurs in DoodadFuncConditionalUse
        set_func_group_id(ctx.server, &ctx.doodad, override_phase as u32);
        ctx.doodad.data().phase.override_phase = 0;
    } else {
        set_func_group_id(ctx.server, &ctx.doodad, func.next_phase as u32);
    }
    false
}

/// `Spawner is not null ? Spawner.Despawn(this) : Delete()`.
pub fn despawn_or_delete(ctx: &DoodadCtx<'_>) {
    match ctx
        .server
        .systems
        .npcs
        .spawns
        .doodad_spawner_of(ctx.doodad.obj_id)
    {
        Some(spawner) => spawner.despawn(ctx.server, &ctx.doodad),
        None => super::manager::delete(ctx.server, &ctx.world, &ctx.doodad),
    }
}

/// `DoPhaseFuncs(caster, ref nextPhase)`: `true` = the phase did not pass (abort).
fn do_phase_funcs(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, next_phase: &mut i32) -> bool {
    if *next_phase <= 0 {
        return true;
    }
    // Changing the phase.
    set_func_group_id(ctx.server, &ctx.doodad, *next_phase as u32);
    let group = *next_phase as u32;
    let recursed = {
        let mut d = ctx.doodad.data();
        if d.phase.list_group_id.contains(&group) {
            d.phase.list_group_id.clear();
            true
        } else {
            d.phase.list_group_id.push(group); // to check CheckPhase()
            false
        }
    };
    if recursed && !ctx.data().funcs_for_group(group).is_empty() {
        // e.g. ID=2231, Target: break the recursion (ID=898, Prison Gate has no funcs: continue)
        tracing::trace!("DoPhase: Finished execution with recurse: phase {group}");
        return true;
    }

    if tasks::cancel_func_task(&ctx.doodad) {
        tracing::trace!("DoPhaseFuncs:DoodadFuncTimer: The current timer has been canceled.");
    }
    tracing::trace!(
        "DoPhaseFuncs: ObjId {}, nextPhase {}",
        ctx.doodad.obj_id,
        *next_phase
    );

    let phase_funcs = ctx.data().phase_funcs(group);
    if phase_funcs.is_empty() {
        return false; // no phase functions for FuncGroupId
    }

    let mut stop = false;
    let manager = &ctx.server.systems.npcs.doodads;
    for pf in phase_funcs {
        let ratio = manager
            .forced_phase_ratio()
            .unwrap_or_else(|| rand::thread_rng().gen_range(0..10000));
        ctx.doodad.data().phase.phase_ratio = ratio;
        // DoodadPhaseFunc.Use: template != null && template.Use(caster, owner)
        stop = match ctx.data().phase_func_template(pf.func_id, &pf.func_type) {
            Some(t) => phase_funcs::use_phase_func(t, ctx, caster),
            None => false,
        };
        if stop {
            break; // interrupt execution of phase functions and switch to OverridePhase
        }
    }

    let (override_phase, func_group_id, deleted) = {
        let d = ctx.doodad.data();
        (d.phase.override_phase, d.func_group_id, d.phase.deleted)
    };
    if override_phase != 0 && stop && func_group_id as i32 != override_phase {
        *next_phase = override_phase;
        ctx.doodad.data().phase.override_phase = 0;
        return do_phase_funcs(ctx, caster, next_phase);
    }
    if !deleted {
        db::save(ctx.server, &ctx.doodad); // let's save the doodad in the database
    }
    stop
}

/// `DoChangePhase(caster, nextPhase)`: phase funcs of `next_phase`, then
/// `SCDoodadPhaseChangedPacket` (unless deleted). `true` = aborted.
pub fn do_change_phase(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, next_phase: i32) -> bool {
    if next_phase <= 0 {
        return false;
    }
    tracing::trace!(
        "DoChangePhase: ObjId {}, nextPhase {next_phase}",
        ctx.doodad.obj_id
    );
    let mut np = next_phase;
    let stop = do_phase_funcs(ctx, caster, &mut np);
    // the phase change packet must be after the phase functions (correct FuncGroupId)
    let packet = {
        let d = ctx.doodad.data();
        (!d.phase.deleted).then(|| SCDoodadPhaseChangedPacket::new(ctx.doodad.obj_id, &d, now_ms()))
    };
    if let Some(p) = packet {
        ctx.broadcast(&p);
    }
    stop
}

/// `OnSkillHit(caster, skillId)`: `Use(caster, skillId)` once per `DoodadFuncSkillHit` of the
/// current phase.
pub fn on_skill_hit(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, skill_id: u32) {
    let group = ctx.doodad.data().func_group_id;
    let n = ctx
        .data()
        .funcs_for_group(group)
        .iter()
        .filter(|f| f.func_type == "DoodadFuncSkillHit")
        .count();
    for _ in 0..n {
        use_doodad(ctx, caster, skill_id, 0);
    }
}

/// `InitDoodad()`: growth time (growth rate + climate bonus), then `DoChangePhase(owner unit,
/// FuncGroupId)`.
pub fn init_doodad(ctx: &DoodadCtx<'_>) {
    let (template_id, plant_time, owner_obj_id) = {
        let d = ctx.doodad.data();
        (d.template_id, d.plant_time, d.owner_obj_id)
    };
    let total = ctx
        .data()
        .template(template_id)
        .map_or(0, |t| t.total_doodad_growth_time);
    // Apply Climate settings
    let mut grow_time = f64::from(total) / ctx.server.systems.npcs.doodads.growth_rate();
    if total > 0 && doodad_has_matching_climate(ctx) {
        grow_time = (grow_time * f64::from(0.73f32)).round_ties_even() as i32 as f64;
    }
    ctx.doodad.data().growth_time_ms = plant_time * 1000 + grow_time as i64;

    // Actually do the phase change
    let unit = ctx.world.unit(owner_obj_id);
    let fg = ctx.doodad.data().func_group_id;
    do_change_phase(ctx, unit.as_ref(), fg as i32);
}

#[cfg(test)]
#[path = "phase_tests.rs"]
mod tests;
