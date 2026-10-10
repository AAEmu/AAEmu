//! Per-kind `AddVisibleObject(character)` / `RemoveVisibleObject(character)` (the C# virtual
//! overrides of `Character`, `Npc`, `Doodad`, `Slave`, `Mate`, `House`, `Transfer`,
//! `Shipyard`, `Gimmick` and their `GameObject` base), plus the doodad / gimmick body builders
//! the region batches use.
//!
//! Called by `WorldInstance` with its grid lock held and no object data lock held; each
//! function locks one object at a time (see `world::object` locking rules).

use std::sync::Arc;

use aaemu_commons::PacketStream;

use super::object::{DoodadRef, GimmickRef, WorldObject};
use super::{unit_state, PlayerRef, ViewCtx, WorldInstance};
use crate::packets::world::{SCUnitPointsPacket, SCUnitsRemovedPacket};
use crate::packets::world_objects::{
    SCDoodadCreatedPacket, SCDoodadRemovedPacket, SCDoodadsCreatedPacket, SCDoodadsRemovedPacket,
    SCGimmickJointsBrokenPacket, SCGimmicksCreatedPacket, SCGimmicksRemovedPacket,
    SCHouseStatePacket, SCMateStatePacket, SCShipyardStatePacket, SCSlaveStatePacket,
    SCTargetChangedPacket, SCUnitAttachedPacket,
};

/// `AttachUnitReason.None`.
const ATTACH_UNIT_REASON_NONE: u8 = 0;

fn owner_name(ctx: &ViewCtx<'_>, owner_id: u32) -> String {
    if owner_id == 0 {
        return String::new();
    }
    ctx.names.get_character_name(owner_id).unwrap_or_default()
}

/// `Doodad.Write` body for `SCDoodadCreatedPacket` / one `SCDoodadsCreatedPacket` entry.
pub(crate) fn doodad_body(world: &WorldInstance, d: &DoodadRef, now_ms: i64) -> Vec<u8> {
    let pos = world.world_pos_rot(&WorldObject::Doodad(d.clone()));
    let mut s = PacketStream::new();
    d.data().write(d.obj_id, &pos, now_ms, &mut s);
    s.into_bytes()
}

/// `Gimmick.Write` body for one `SCGimmicksCreatedPacket` entry.
pub(crate) fn gimmick_body(world: &WorldInstance, g: &GimmickRef) -> Vec<u8> {
    let pos = world.world_pos_rot(&WorldObject::Gimmick(g.clone()));
    let mut s = PacketStream::new();
    g.data().write(g.obj_id, &pos, &mut s);
    s.into_bytes()
}

/// `GameObject.AddVisibleObject(character)` (base): propagate to the transform children.
fn base_add(world: &WorldInstance, obj: &WorldObject, target: &PlayerRef, ctx: &ViewCtx<'_>) {
    for child in obj.core().children() {
        add_visible_object(world, &child, target, ctx);
    }
}

/// `GameObject.RemoveVisibleObject(character)` (base): drop the target if it was this object,
/// then propagate to the transform children.
fn base_remove(world: &WorldInstance, obj: &WorldObject, target: &PlayerRef) {
    let reset = {
        let mut c = target.data();
        if c.unit.current_target != 0 && c.unit.current_target == obj.obj_id() {
            c.unit.current_target = 0;
            true
        } else {
            false
        }
    };
    if reset {
        target.send(SCTargetChangedPacket {
            obj_id: target.obj_id,
            target_obj_id: 0,
        });
    }
    for child in obj.core().children() {
        remove_visible_object(world, &child, target);
    }
}

/// `obj.AddVisibleObject(target)`: `target` learns about `obj`.
pub(crate) fn add_visible_object(
    world: &WorldInstance,
    obj: &WorldObject,
    target: &PlayerRef,
    ctx: &ViewCtx<'_>,
) {
    let now = ctx.now_ms;
    match obj {
        WorldObject::Player(p) => {
            // Character: never send the own SCUnitStatePacket (the client crashes).
            let (state, points) = {
                let mut c = p.data();
                let state = (!Arc::ptr_eq(p, target))
                    .then(|| unit_state::unit_state(&mut c, ctx.data, now));
                (
                    state,
                    SCUnitPointsPacket {
                        obj_id: p.obj_id,
                        hp: c.unit.hp,
                        mp: c.unit.mp,
                    },
                )
            };
            if let Some(state) = state {
                target.send(state);
            }
            target.send(points);
            base_add(world, obj, target, ctx);
        }
        WorldObject::Npc(e) => {
            let name = owner_name(ctx, e.data().unit.owner_id);
            let (state, points) = {
                let mut n = e.data();
                let state = unit_state::npc_unit_state(e.obj_id, &mut n, &name, now);
                (state, points_of(e.obj_id, &n.unit))
            };
            target.send(state);
            target.send(points);
            base_add(world, obj, target, ctx);
        }
        WorldObject::Doodad(d) => {
            target.send(SCDoodadCreatedPacket {
                body: doodad_body(world, d, now),
            });
            base_add(world, obj, target, ctx);
        }
        WorldObject::Slave(e) => {
            let name = owner_name(ctx, e.data().unit.owner_id);
            let (state, points, slave_state, attached) = {
                let mut sl = e.data();
                let state = unit_state::slave_unit_state(e.obj_id, &mut sl, &name, now);
                let slave_state = SCSlaveStatePacket {
                    obj_id: e.obj_id,
                    tl_id: sl.unit.tl_id,
                    creator_name: sl.summoner_name.clone(),
                    owner_obj_id: sl.summoner_obj_id,
                    db_id: sl.id,
                };
                (
                    state,
                    points_of(e.obj_id, &sl.unit),
                    slave_state,
                    sl.attached_characters.clone(),
                )
            };
            target.send(state);
            target.send(points);
            target.send(slave_state);
            base_add(world, obj, target, ctx);
            for (point, char_obj_id) in attached {
                if char_obj_id > 0 && world.character_by_obj_id(char_obj_id).is_some() {
                    target.send(SCUnitAttachedPacket {
                        child_obj_id: char_obj_id,
                        point,
                        reason: ATTACH_UNIT_REASON_NONE,
                        obj_id: e.obj_id,
                    });
                }
            }
        }
        WorldObject::Mate(e) => {
            // Mate: base first, then its own packets.
            base_add(world, obj, target, ctx);
            let name = owner_name(ctx, e.data().unit.owner_id);
            let (state, points, passengers) = {
                let mut m = e.data();
                let state = unit_state::mate_unit_state(e.obj_id, &mut m, &name, now);
                (state, points_of(e.obj_id, &m.unit), m.passengers.clone())
            };
            target.send(state);
            target.send(SCMateStatePacket { obj_id: e.obj_id });
            target.send(points);
            for (point, info) in passengers {
                if info.obj_id > 0 && world.character_by_obj_id(info.obj_id).is_some() {
                    target.send(SCUnitAttachedPacket {
                        child_obj_id: info.obj_id,
                        point,
                        reason: info.reason,
                        obj_id: e.obj_id,
                    });
                }
            }
        }
        WorldObject::House(e) => {
            let (owner_id, sell_to) = {
                let h = e.data();
                (h.unit.owner_id, h.sell_to_player_id)
            };
            let name = owner_name(ctx, owner_id);
            let sell_to_name = owner_name(ctx, sell_to);
            let pos = world.world_pos_rot(obj);
            let (state, house_state, doodad_ids) = {
                let mut h = e.data();
                let state = unit_state::house_unit_state(e.obj_id, &mut h, &name, now);
                let mut s = PacketStream::new();
                h.write(e.obj_id, &pos, &name, &sell_to_name, &mut s);
                (state, s.into_bytes(), h.attached_doodads.clone())
            };
            target.send(state);
            target.send(SCHouseStatePacket { body: house_state });
            let bodies: Vec<Vec<u8>> = doodad_ids
                .iter()
                .filter_map(|id| world.doodad(*id))
                .map(|d| doodad_body(world, &d, now))
                .collect();
            for chunk in bodies.chunks(SCDoodadsCreatedPacket::MAX_COUNT_PER_PACKET) {
                target.send(SCDoodadsCreatedPacket {
                    bodies: chunk.to_vec(),
                });
            }
            base_add(world, obj, target, ctx);
        }
        WorldObject::Transfer(e) => {
            // C#: `IsVisible = true` here.
            e.core().set_visible(true);
            let name = owner_name(ctx, e.data().unit.owner_id);
            let state = unit_state::transfer_unit_state(e.obj_id, &mut e.data(), &name, now);
            target.send(state);
            base_add(world, obj, target, ctx);
        }
        WorldObject::Shipyard(e) => {
            let name = owner_name(ctx, e.data().unit.owner_id);
            let (state, data) = {
                let mut sy = e.data();
                let state = unit_state::shipyard_unit_state(e.obj_id, &mut sy, &name, now);
                (state, sy.data.clone())
            };
            target.send(state);
            target.send(SCShipyardStatePacket { data });
            base_add(world, obj, target, ctx);
        }
        WorldObject::Gimmick(g) => {
            target.send(SCGimmicksCreatedPacket {
                bodies: vec![gimmick_body(world, g)],
            });
            target.send(SCGimmickJointsBrokenPacket::default());
            base_add(world, obj, target, ctx);
        }
    }
}

fn points_of(obj_id: u32, unit: &crate::models::unit::UnitBase) -> SCUnitPointsPacket {
    SCUnitPointsPacket {
        obj_id,
        hp: unit.hp,
        mp: unit.mp,
    }
}

/// `obj.RemoveVisibleObject(target)`: `target` forgets `obj`.
pub(crate) fn remove_visible_object(world: &WorldInstance, obj: &WorldObject, target: &PlayerRef) {
    base_remove(world, obj, target);
    let id = obj.obj_id();
    match obj {
        WorldObject::Player(p) => {
            if !Arc::ptr_eq(p, target) {
                target.send(SCUnitsRemovedPacket { ids: vec![id] });
            }
        }
        WorldObject::Doodad(_) => target.send(SCDoodadRemovedPacket { obj_id: id }),
        WorldObject::Gimmick(_) => target.send(SCGimmicksRemovedPacket { ids: vec![id] }),
        WorldObject::House(e) => {
            target.send(SCUnitsRemovedPacket { ids: vec![id] });
            let ids = e.data().attached_doodads.clone();
            // Note (C#): the chunk offset is `i * MaxCountPerPacket` with `i` already stepping
            // by MaxCountPerPacket, so any chunk after the first throws (negative array size)
            // and aborts the rest of the removal. Only the first chunk is ever sent.
            let max = SCDoodadsRemovedPacket::MAX_COUNT_PER_PACKET;
            if !ids.is_empty() {
                let last = ids.len() <= max;
                target.send(SCDoodadsRemovedPacket {
                    last,
                    ids: ids[..ids.len().min(max)].to_vec(),
                });
                if !last {
                    tracing::error!(
                        "House {id}: more than {max} attached doodads (C# throws while removing)"
                    );
                }
            }
        }
        WorldObject::Npc(_)
        | WorldObject::Slave(_)
        | WorldObject::Mate(_)
        | WorldObject::Transfer(_)
        | WorldObject::Shipyard(_) => target.send(SCUnitsRemovedPacket { ids: vec![id] }),
    }
}
