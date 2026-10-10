//! `GetBlinkPosition` (x2game `sub_3925E670` → `sub_3925D990`): where a unit ends when it is
//! moved along a direction (blink, teleport-to-unit, leap / dash end points, knockback). The
//! zone sweeps the unit's collider along the move with `PrimitiveWorldIntersection`, stops
//! 0.1 m before the first contact, then steps down a 30° slope (`tan(π/6)·step`) to find the
//! floor and lifts the collider back by at most that much.
//!
//! * Collider (`sub_3925E670`): the living entity's `pe_player_dimensions` when there is one;
//!   otherwise a capsule of radius 0.35 and half height 0 centred 1 m above the feet. (Living
//!   dimensions plug in through [`Collider`].)
//! * `WZBlinkUnit` (0x39369B10) sweeps against the terrain only (`entTypes 256`); the other
//!   callers (0x394D15D0, 0x397C6D00, 0x397C89D0) use `263` = 0x107 (static, sleeping, rigid,
//!   terrain). The geometry filter is `geomFlagsAny = 15`.
//! * `extend` (WZBlinkUnit: the target is another unit) adds the collider radius to the
//!   distance; `include_dz` keeps the z component of the direction.

use aaemu_physics::geometry::primitives::{CapsulePrim, CylinderPrim, Primitive};
use aaemu_physics::math::Vec3;

use crate::server::GameServer;

use super::world::WorldState;

/// `geomFlagsAny` of the blink sweeps.
pub const BLINK_GEOM_FLAGS_ANY: u32 = 15;
/// `WZBlinkUnit` entity types.
pub const ENT_TYPES_BLINK_UNIT: u32 = 0x100;
/// The other `GetBlinkPosition` callers (`263`).
pub const ENT_TYPES_SOLID: u32 = 0x107;

/// The swept collider (`pe_player_dimensions` or the default capsule).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct Collider {
    /// Height of the collider centre above the feet (`heightCollider`, default 1.0).
    pub height: f32,
    pub radius: f32,
    /// Half height of the cylindrical part.
    pub half_height: f32,
    /// Capsule (`bUseCapsule`) or cylinder.
    pub capsule: bool,
}

impl Default for Collider {
    /// `sub_3925E670` without a living entity: `(x, y, z + 1)`, r 0.35, hh 0, capsule.
    fn default() -> Self {
        Self {
            height: 1.0,
            radius: 0.35,
            half_height: 0.0,
            capsule: true,
        }
    }
}

fn prim(c: &Collider, center: Vec3) -> Primitive {
    let p = CylinderPrim {
        center,
        axis: Vec3::new(0.0, 0.0, 1.0),
        r: c.radius,
        hh: c.half_height,
    };
    if c.capsule {
        Primitive::Capsule(p as CapsulePrim)
    } else {
        Primitive::Cylinder(p)
    }
}

/// `sub_3925D990` on a locked world. `feet` = the unit position, `dir` need not be unit
/// length. Returns the end position (feet).
#[allow(clippy::too_many_arguments)]
pub fn blink_position_in(
    st: &mut WorldState,
    feet: Vec3,
    dir: Vec3,
    dist: f32,
    extend: bool,
    ent_types: u32,
    include_dz: bool,
    collider: &Collider,
) -> Vec3 {
    let dz = if include_dz { dir.z } else { 0.0 };
    let len =
        (((dir.x * dir.x) + (dir.y * dir.y)) + (dz * dz) + f32::from_bits(0x0080_0000)).sqrt();
    let inv = 1.0 / len;
    let (nx, ny, nz) = (dir.x * inv, dir.y * inv, dz * inv);
    let mut c = Vec3::new(feet.x, feet.y, feet.z + collider.height);
    let offset = c.z - feet.z; // `a5`: centre height over the feet
    c.x -= nx * 0.1;
    c.z -= nz * 0.1;
    c.y -= ny * 0.1;
    let mut remaining = dist;
    if extend {
        remaining += collider.radius;
    }
    let mut out = c;
    let pwi = |st: &mut WorldState, c: Vec3, sweep: Vec3| -> f32 {
        st.sweep(
            prim(collider, c),
            sweep,
            ent_types,
            BLINK_GEOM_FLAGS_ANY,
            &[],
        )
        .map_or(0.0, |h| h.dist)
    };
    // tanf(0.52359879) (MSVCR100; the port's crt::tanf matches it)
    let tan30 = aaemu_physics::math::crt::tanf(std::f32::consts::FRAC_PI_6);
    while remaining > 0.0 {
        let sweep = Vec3::new(nx * remaining, ny * remaining, remaining * nz);
        let t = pwi(st, c, sweep);
        let step;
        if t <= 0.0 {
            c.x += nx * remaining;
            c.z += remaining * nz;
            c.y += sweep.y;
            step = remaining;
            remaining = 0.0;
        } else {
            let s = (t - 0.1).max(0.0);
            step = s;
            remaining -= s;
            c.x += nx * s;
            c.y += ny * s;
            c.z += s * nz;
        }
        if step <= 0.0 {
            break;
        }
        let drop = tan30 * step;
        let t2 = pwi(st, c, Vec3::new(0.0, 0.0, -drop));
        if t2 <= 0.0 {
            out = Vec3::new(c.x, c.y, c.z - offset);
        } else {
            c.z -= t2;
            let mut h = collider.half_height;
            if collider.capsule {
                h += collider.radius;
            }
            out = Vec3::new(c.x, c.y, c.z - (h - 0.1));
            let mut lift = if drop <= offset { drop } else { offset };
            let t3 = pwi(st, c, Vec3::new(0.0, 0.0, drop));
            if t3 > 0.0 {
                lift = if t3 - 0.1 >= 0.0 { 0.0 } else { t3 - 0.1 };
            }
            c.z += lift;
        }
        if step < 0.1 {
            break;
        }
    }
    out
}

/// [`blink_position_in`] on an instance (loads the cells of the move). `None` without the
/// collision service.
#[allow(clippy::too_many_arguments)]
pub fn blink_position(
    server: &GameServer,
    instance_id: u32,
    feet: [f32; 3],
    dir: [f32; 3],
    dist: f32,
    extend: bool,
    ent_types: u32,
    include_dz: bool,
) -> Option<[f32; 3]> {
    let pw = super::phys_world(server, instance_id)?;
    let f = Vec3::new(feet[0], feet[1], feet[2]);
    let d = Vec3::new(dir[0], dir[1], dir[2]);
    let l = d.len().max(1e-6);
    let end = f + d * ((dist + 1.0) / l);
    let min = Vec3::new(f.x.min(end.x) - 2.0, f.y.min(end.y) - 2.0, f.z - 50.0);
    let max = Vec3::new(f.x.max(end.x) + 2.0, f.y.max(end.y) + 2.0, f.z + 50.0);
    super::ensure_box(server, &pw, min, max);
    let mut st = pw.lock();
    let p = blink_position_in(
        &mut st,
        f,
        d,
        dist,
        extend,
        ent_types,
        include_dz,
        &Collider::default(),
    );
    Some([p.x, p.y, p.z])
}

/// A straight ground move from `from` to `to` (feet positions; leap / dash / knockback end
/// points): `GetBlinkPosition` with entity types 0x107 along the horizontal direction.
pub fn clip_move(
    server: &GameServer,
    instance_id: u32,
    from: [f32; 3],
    to: [f32; 3],
) -> Option<[f32; 3]> {
    let d = [to[0] - from[0], to[1] - from[1], to[2] - from[2]];
    let dist = (d[0] * d[0] + d[1] * d[1]).sqrt();
    if dist <= 0.0 {
        return Some(from);
    }
    blink_position(
        server,
        instance_id,
        from,
        d,
        dist,
        false,
        ENT_TYPES_SOLID,
        false,
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::systems::ai::collision::tests::{box_model, flat_world};
    use crate::systems::ai::collision::world::{brush_instance, CellContent};
    use aaemu_physics::math::Mat33;

    fn wall_world() -> crate::systems::ai::collision::world::PhysWorld {
        let pw = flat_world(100.0);
        // a wall 2 m thick, 10 m high, its near face at x = 30
        let m = box_model(Vec3::new(1.0, 20.0, 5.0), false);
        let rec = crate::systems::ai::collision::cells::BrushRecord {
            bbox: [Vec3::ZERO; 2],
            rnd_flags: 0,
            rot_scale: Mat33::IDENTITY,
            pos: Vec3::new(31.0, 50.0, 105.0),
            material_id: 0,
            statobj: 0,
        };
        let inst = brush_instance(m, Vec3::ZERO, &rec);
        pw.lock().insert_cell(CellContent {
            cell: (0, 0),
            instances: vec![inst],
            ..CellContent::default()
        });
        pw
    }

    #[test]
    fn blink_stops_before_a_wall() {
        let pw = wall_world();
        let mut st = pw.lock();
        let end = blink_position_in(
            &mut st,
            Vec3::new(20.0, 50.0, 100.0),
            Vec3::new(1.0, 0.0, 0.0),
            20.0,
            false,
            ENT_TYPES_SOLID,
            false,
            &Collider::default(),
        );
        // the 0.35 m capsule stops 0.1 m before touching x = 30
        assert!((end.x - (30.0 - 0.35 - 0.1)).abs() < 0.05, "{end:?}");
        // the last, tiny step finds no floor within its slope and keeps the lifted centre
        // (centre − 1 m): 0.35 above the ground, as the zone does
        assert!((end.z - 100.35).abs() < 0.01, "{end:?}");
        // terrain only (WZBlinkUnit): through the wall
        let end = blink_position_in(
            &mut st,
            Vec3::new(20.0, 50.0, 100.0),
            Vec3::new(1.0, 0.0, 0.0),
            20.0,
            false,
            ENT_TYPES_BLINK_UNIT,
            false,
            &Collider::default(),
        );
        assert!((end.x - 39.9).abs() < 0.05, "{end:?}");
    }

    #[test]
    fn blink_follows_the_ground() {
        let pw = flat_world(100.0);
        let mut st = pw.lock();
        let end = blink_position_in(
            &mut st,
            Vec3::new(20.0, 50.0, 100.0),
            Vec3::new(0.0, 1.0, 0.0),
            10.0,
            false,
            ENT_TYPES_SOLID,
            false,
            &Collider::default(),
        );
        assert!((end.y - 59.9).abs() < 0.05, "{end:?}");
        assert!((end.z - 100.0).abs() < 0.2, "{end:?}");
    }
}
