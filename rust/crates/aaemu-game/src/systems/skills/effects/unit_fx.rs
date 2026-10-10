//! `BubbleEffect` (`SCChatBubblePacket`) and `ImpulseEffect` (`SCUnitImpulsePacket` + the
//! server-side NPC displacement).

use crate::server::GameServer;
use crate::world::WorldObject;

use super::super::broadcast;
use super::super::data::effects::ImpulseEffect;
use super::super::geometry::{world_pos, Target};
use super::super::packets_special::{SCChatBubblePacket, SCUnitImpulsePacket};
use super::special::movement::{has_knockback_immunity, move_npc, npc_non_pushable};
use super::EffectArgs;

/// `BubbleEffect.Apply`: the target shows bubble `Id` (`kind2 = 2`: by id).
///
/// Deviation: the C# then sleeps the effect thread for the estimated read time of the
/// localized bubble text (at least 1.25 s), delaying the following effects of the cast; nothing
/// waits here.
pub fn bubble(server: &GameServer, id: u32, kind_id: u32, args: &EffectArgs<'_>) {
    tracing::trace!(
        "BubbleEffect, Id {id}, KindId {kind_id}, ObjId {}",
        args.target_caster.obj_id()
    );
    if let Some(target) = args.target.as_obj() {
        broadcast(
            server,
            target,
            &SCChatBubblePacket {
                bc: args.target_caster.obj_id(),
                kind1: kind_id as u8,
                kind2: 2,
                bubble_type: id,
                text: String::new(),
            },
            true,
        );
    }
}

/// `ImpulseEffect.ResolveImpulseTarget`: a slave target, else a slave caster, else (self cast
/// of a character) its mounted / active slave, else the target.
fn resolve_impulse_target(
    server: &GameServer,
    caster: Option<&WorldObject>,
    target: &Target,
) -> Target {
    if target.as_obj().is_some_and(|t| t.as_slave().is_some()) {
        return target.clone();
    }
    if let Some(c) = caster.filter(|c| c.as_slave().is_some()) {
        return Target::Obj(c.clone());
    }
    if let (Some(c), Some(t)) = (caster, target.as_obj()) {
        if c.same(t) {
            if let Some(p) = c.as_player() {
                if let Some(slave) = server.systems.skills.ext.npcs().character_slave(server, p) {
                    return Target::Obj(slave);
                }
            }
        }
    }
    target.clone()
}

/// `ImpulseEffect.LocalImpulseToWorldDisplacement`: a caster→target local impulse (X right,
/// Y forward, Z up, 1/1000 m) as a world displacement; a degenerate direction keeps the
/// impulse in world space.
pub fn local_impulse_to_world_displacement(
    local: [f32; 3],
    caster_pos: (f32, f32),
    target_pos: (f32, f32),
) -> [f32; 3] {
    let d = [local[0] / 1000.0, local[1] / 1000.0, local[2] / 1000.0];
    let dir = (target_pos.0 - caster_pos.0, target_pos.1 - caster_pos.1);
    let len_sq = dir.0 * dir.0 + dir.1 * dir.1;
    if len_sq <= 0.01 {
        return d;
    }
    let len = len_sq.sqrt();
    let dir = (dir.0 / len, dir.1 / len);
    let right = (-dir.1, dir.0);
    [
        right.0 * d[0] + dir.0 * d[1],
        right.1 * d[0] + dir.1 * d[1],
        d[2],
    ]
}

/// `ImpulseEffect.Apply`.
pub fn impulse(server: &GameServer, e: &ImpulseEffect, args: &EffectArgs<'_>) {
    tracing::debug!(
        "ImpulseEffect: caster={:?}, target={}, impulse=({},{},{}), velImpulse=({},{},{})",
        args.caster.as_ref().map(WorldObject::obj_id),
        args.target.obj_id(),
        e.impulse[0],
        e.impulse[1],
        e.impulse[2],
        e.vel_impulse[0],
        e.vel_impulse[1],
        e.vel_impulse[2]
    );
    let caster = args.caster.as_ref();
    let impulse_target = resolve_impulse_target(server, caster, &args.target);
    if let Some(t) = impulse_target.as_obj() {
        if t.with_unit(|u| u.unit().hp <= 0).unwrap_or(false) {
            return;
        }
    }
    if let Some(t) = args.target.as_obj() {
        if has_knockback_immunity(server, t) || npc_non_pushable(server, t) {
            return;
        }
    }
    let impulse = [
        e.impulse[0] + e.vel_impulse[0],
        e.impulse[1] + e.vel_impulse[1],
        e.impulse[2] + e.vel_impulse[2],
    ];
    if impulse[0] * impulse[0] + impulse[1] * impulse[1] + impulse[2] * impulse[2] < 0.01 {
        return;
    }
    let Some(caster) = caster else {
        return;
    };
    if let Some(t) = impulse_target.as_obj() {
        broadcast(
            server,
            t,
            &SCUnitImpulsePacket {
                obj_id: t.obj_id(),
                vel_impulse: e.vel_impulse,
                angvel_impulse: e.angvel_impulse,
                impulse: e.impulse,
                ang_impulse: e.ang_impulse,
            },
            t.as_player().is_some(),
        );
    }
    let caster_pos = world_pos(server, &Target::Obj(caster.clone()));
    let target_pos = world_pos(server, &args.target);
    let d = local_impulse_to_world_displacement(
        impulse,
        (caster_pos.x, caster_pos.y),
        (target_pos.x, target_pos.y),
    );
    if let Some(npc) = args.target.as_obj().and_then(WorldObject::as_npc) {
        move_npc(
            server,
            npc,
            (
                target_pos.x + d[0],
                target_pos.y + d[1],
                target_pos.z + d[2],
            ),
            None,
            true,
        );
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn impulse_displacement_is_caster_relative() {
        // caster south of the target: forward = +Y, right = -X (C# `new Vector3(-dir.Y, dir.X)`)
        let d =
            local_impulse_to_world_displacement([1000.0, 2000.0, 500.0], (0.0, 0.0), (0.0, 10.0));
        assert!((d[0] - -1.0).abs() < 1e-6);
        assert!((d[1] - 2.0).abs() < 1e-6);
        assert!((d[2] - 0.5).abs() < 1e-6);
        // degenerate direction: world space
        let d = local_impulse_to_world_displacement([1000.0, 0.0, 0.0], (5.0, 5.0), (5.0, 5.05));
        assert_eq!(d, [1.0, 0.0, 0.0]);
    }
}
