//! Logic links and family pulses. The target compact labels operations 1..4 AND/NAND/OR/XOR,
//! 5 NOT (template 1706), and 6..7 NOR/XNOR (template 1572). PulseTrigger.flag is the expected
//! input value; its next_phase is the authored transition on that input.

use std::cell::RefCell;
use std::collections::HashSet;
use std::sync::Arc;

use super::{phase, runtime::DoodadCtx, templates::FuncTemplate};
use crate::models::doodad::doodad_owner_type;
use crate::server::GameServer;
use crate::world::{DoodadRef, PlayerRef, WorldObject};

thread_local! { static PULSE_PATH: RefCell<HashSet<usize>> = RefCell::new(HashSet::new()); }

struct PulseScope(bool);
impl Drop for PulseScope {
    fn drop(&mut self) {
        if self.0 {
            PULSE_PATH.with(|path| path.borrow_mut().clear());
        }
    }
}

fn identity(d: &DoodadRef) -> usize {
    Arc::as_ptr(d) as usize
}

fn families(ctx: &DoodadCtx<'_>, doodad: &DoodadRef, provider: bool) -> HashSet<u32> {
    let group = doodad.data().func_group_id;
    ctx.data()
        .phase_funcs(group)
        .iter()
        .filter_map(
            |pf| match ctx.data().phase_func_template(pf.func_id, &pf.func_type) {
                Some(FuncTemplate::LogicFamilyProvider(f)) if provider => Some(f.family_id),
                Some(FuncTemplate::LogicFamilySubscriber(f)) if !provider => Some(f.family_id),
                _ => None,
            },
        )
        .collect()
}

/// Client-created wires require both objects to belong to the caller, in the same world and
/// within reach. The live handle identity prevents an old wire addressing a recycled ID.
pub fn link(server: &GameServer, player: &PlayerRef, provider_id: u32, subscriber_id: u32) -> bool {
    let Some(world) = server.world.world_of(&WorldObject::from(player)) else {
        return false;
    };
    let (Some(provider), Some(subscriber)) =
        (world.doodad(provider_id), world.doodad(subscriber_id))
    else {
        return false;
    };
    if provider_id == subscriber_id {
        return false;
    }
    let player_pos = world.world_pos_rot(&WorldObject::from(player));
    for doodad in [&provider, &subscriber] {
        let (owner, deleted) = {
            let d = doodad.data();
            (d.owner_id, d.phase.deleted)
        };
        if owner != player.character_id || deleted {
            return false;
        }
        let p = world.world_pos_rot(&WorldObject::from(doodad));
        if (p.x - player_pos.x).powi(2)
            + (p.y - player_pos.y).powi(2)
            + (p.z - player_pos.z).powi(2)
            > 400.0
        {
            return false;
        }
    }
    let ctx = DoodadCtx::new(server, world.clone(), provider.clone());
    let group = subscriber.data().func_group_id;
    let is_logic = ctx
        .data()
        .phase_funcs(group)
        .iter()
        .any(|pf| pf.func_type == "DoodadFuncLogic");
    if families(&ctx, &provider, true).is_empty()
        || (!is_logic && families(&ctx, &subscriber, false).is_empty())
    {
        return false;
    }
    provider
        .data()
        .phase
        .logic_links
        .insert(subscriber_id, identity(&subscriber));
    let output = provider.data().phase.logic_output;
    if let Some(value) = output {
        receive(&ctx, &subscriber, Some(&WorldObject::from(player)), value);
    }
    true
}

pub fn clear_links(ctx: &DoodadCtx<'_>) {
    let active = {
        let d = ctx.doodad.data();
        !d.phase.logic_links.is_empty()
            || !d.phase.logic_inputs.is_empty()
            || d.phase.logic_output.is_some()
    };
    if !active {
        return;
    }
    if ctx.doodad.data().phase.logic_output.is_some() {
        emit(ctx, None, false);
    }
    for provider in ctx.world.all_doodads() {
        provider
            .data()
            .phase
            .logic_links
            .retain(|id, handle| *id != ctx.doodad.obj_id || *handle != identity(&ctx.doodad));
    }
    let mut d = ctx.doodad.data();
    d.phase.logic_links.clear();
    d.phase.logic_inputs.clear();
    d.phase.logic_output = None;
}

pub fn emit(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, value: bool) {
    let root = PULSE_PATH.with(|path| {
        let mut path = path.borrow_mut();
        let root = path.is_empty();
        path.insert(identity(&ctx.doodad));
        root
    });
    let _scope = PulseScope(root);
    ctx.doodad.data().phase.logic_output = Some(value);
    let (links, owner, owner_type, zone) = {
        let d = ctx.doodad.data();
        (
            d.phase.logic_links.clone(),
            d.owner_id,
            d.owner_type,
            d.transform.zone_id,
        )
    };
    let mut targets = Vec::new();
    for (id, handle) in links {
        if let Some(target) = ctx.world.doodad(id).filter(|d| identity(d) == handle) {
            targets.push(target);
        }
    }
    let provided = families(ctx, &ctx.doodad, true);
    if !provided.is_empty() {
        for target in ctx.world.all_doodads() {
            let same_domain = {
                let d = target.data();
                d.transform.zone_id == zone
                    && d.owner_id == owner
                    && d.owner_type == owner_type
                    && (owner_type == doodad_owner_type::SYSTEM || owner != 0)
            };
            if same_domain && !provided.is_disjoint(&families(ctx, &target, false)) {
                targets.push(target);
            }
        }
    }
    if let Some(spawner) = ctx
        .server
        .systems
        .npcs
        .spawns
        .doodad_spawner_of(ctx.doodad.obj_id)
    {
        if let Some(related) = &spawner.related_ids {
            targets.extend(
                ctx.world
                    .around(&ctx.obj(), |o| o.as_doodad().cloned())
                    .into_iter()
                    .filter(|d| related.contains(&d.data().template_id)),
            );
        }
    }
    for target in targets {
        let fresh = PULSE_PATH.with(|path| {
            let mut path = path.borrow_mut();
            path.len() < 256 && path.insert(identity(&target))
        });
        if fresh {
            receive(ctx, &target, caster, value);
        }
    }
}

fn evaluate(operation: u32, inputs: impl Iterator<Item = bool>) -> Option<bool> {
    let inputs: Vec<_> = inputs.collect();
    if inputs.is_empty() {
        return Some(false);
    }
    let all = inputs.iter().all(|v| *v);
    let any = inputs.iter().any(|v| *v);
    let odd = inputs.iter().filter(|v| **v).count() % 2 == 1;
    Some(match operation {
        1 => all,
        2 => !all,
        3 => any,
        4 => odd,
        5 | 6 => !any,
        7 => !odd,
        _ => return None,
    })
}

pub(crate) fn receive(
    source: &DoodadCtx<'_>,
    target: &DoodadRef,
    caster: Option<&WorldObject>,
    value: bool,
) {
    let Some(_operation) = phase::Operation::enter(target) else {
        if !target.data().phase.deleted {
            super::tasks::schedule_pulse(source, target, caster, value);
        }
        return;
    };
    let ctx = DoodadCtx::new(source.server, source.world.clone(), target.clone());
    target
        .data()
        .phase
        .logic_inputs
        .insert(identity(&source.doodad), value);
    let group = target.data().func_group_id;
    let funcs = ctx.data().phase_funcs(group);
    if let Some(f) = funcs.iter().find_map(|pf| {
        match ctx.data().phase_func_template(pf.func_id, &pf.func_type) {
            Some(FuncTemplate::Logic(f)) => Some(f),
            _ => None,
        }
    }) {
        if f.delay_id != 1 {
            tracing::warn!(
                doodad = target.obj_id,
                delay = f.delay_id,
                "Unsupported logic delay descriptor; refusing to substitute immediate behavior"
            );
            return;
        }
        let output = {
            let d = target.data();
            evaluate(f.operation_id, d.phase.logic_inputs.values().copied())
        };
        if let Some(output) = output {
            if target.data().phase.logic_output != Some(output) {
                emit(&ctx, caster, output);
            }
        }
        return;
    }
    for pf in funcs {
        if let Some(FuncTemplate::PulseTrigger(f)) =
            ctx.data().phase_func_template(pf.func_id, &pf.func_type)
        {
            if f.flag == value && f.next_phase > 0 {
                phase::do_change_phase(&ctx, caster.or(Some(&source.obj())), f.next_phase);
                return;
            }
        }
    }
}

/// A subscriber adopts already loaded family providers, independently of spawn file order.
pub fn subscribe(ctx: &DoodadCtx<'_>, caster: Option<&WorldObject>, family: u32) -> bool {
    let group = ctx.doodad.data().func_group_id;
    let (owner, owner_type, zone) = {
        let d = ctx.doodad.data();
        (d.owner_id, d.owner_type, d.transform.zone_id)
    };
    for provider in ctx.world.all_doodads() {
        if Arc::ptr_eq(&provider, &ctx.doodad) {
            continue;
        }
        let output = {
            let d = provider.data();
            if d.owner_id == owner && d.owner_type == owner_type && d.transform.zone_id == zone {
                d.phase.logic_output
            } else {
                None
            }
        };
        if let Some(output) = output {
            if families(ctx, &provider, true).contains(&family) {
                let source = DoodadCtx::new(ctx.server, ctx.world.clone(), provider);
                receive(&source, &ctx.doodad, caster, output);
                if ctx.doodad.data().func_group_id != group {
                    return true;
                }
            }
        }
    }
    false
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn authored_logic_operations_cover_true_false_and_mixed_inputs() {
        for (op, both, mixed, neither) in [
            (1, true, false, false),
            (2, false, true, true),
            (3, true, true, false),
            (4, false, true, false),
            (5, false, false, true),
            (6, false, false, true),
            (7, true, false, true),
        ] {
            assert_eq!(evaluate(op, [true, true].into_iter()), Some(both));
            assert_eq!(evaluate(op, [true, false].into_iter()), Some(mixed));
            assert_eq!(evaluate(op, [false, false].into_iter()), Some(neither));
        }
        assert_eq!(evaluate(99, [true].into_iter()), None);
    }
}
