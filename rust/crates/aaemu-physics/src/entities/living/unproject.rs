//! The living unprojection test `sub_35027E10` (used by `player_dimensions`, GetStatus 21 and
//! the deferred dimension check).

#![allow(clippy::float_cmp, clippy::too_many_arguments)]

use super::ld;
use crate::collision::contact::{GeomContact, IntersectionParams};
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Quat, Vec3};
use crate::world::{EntIdx, World};

/// `sub_35027E10(pos, q, heightCollider, heightPivot, size, bCapsule, &dir, iCaller)`: how
/// far a collider of the given shape at `pos`/`q` must move to be free.
///
/// The neighbours come from GetEntitiesInBox over the collider's box (`collTypes | 0x80000`,
/// `| 0x100000` for the external caller); the entity itself, entities with the same foreign
/// data and entities whose slot 45 ignores it are skipped. A living neighbour whose collider
/// overlaps (vertical cylinders) returns `1e10`. Otherwise the first part (flag 2) that
/// intersects the collider — moving along `−dir` over `2·size.z`, the other parts of the
/// entity tested with their own geometry against the neighbour part when their collider flags
/// match — gives the result: the contact depth (the double `t` as float); a zero `dir` is
/// replaced by the contact's unprojection direction. 0 when nothing intersects.
pub fn unproject_test(
    w: &mut World,
    e: EntIdx,
    pos: Vec3,
    q: Quat,
    hc: f32,
    pivot: f32,
    size: Vec3,
    capsule: bool,
    dir: &mut Vec3,
    caller: i32,
) -> f32 {
    let col = super::make_collider(capsule, size.x, size.z);
    let mn = Vec3::new(pos.x - size.x, -size.x + pos.y, pos.z - pivot);
    let mx = Vec3::new(
        size.x + pos.x,
        size.x + pos.y,
        ((hc + size.z) - pivot) + pos.z,
    );
    let mut objtypes = ld(w, e).coll_types | 0x8_0000;
    if caller == 4 {
        objtypes |= 0x10_0000;
    }
    let list = w.get_entities_in_box(mn, mx, objtypes, None, caller.clamp(0, 4) as usize);
    let m = Mat33::from_quat(q);
    let r = &m.m;
    let d = hc - pivot;
    let gwd = GeomWorldData {
        offset: Vec3::new(
            ((r[0][1] + r[0][0]) * 0.0 + d * r[0][2]) + pos.x,
            ((r[1][1] + r[1][0]) * 0.0 + d * r[1][2]) + pos.y,
            ((r[2][1] + r[2][0]) * 0.0 + d * r[2][2]) + pos.z,
        ),
        r: m,
        scale: 1.0,
        v: Vec3::new(-dir.x, -dir.y, -dir.z),
        w: Vec3::ZERO,
        center_of_mass: Vec3::ZERO,
    };
    let mut ip = IntersectionParams {
        time_interval: ld(w, e).size.z * 2.0,
        ..IntersectionParams::default()
    };
    let mut contacts: Vec<GeomContact> = Vec::new();
    let fd = w.ent(e).foreign_data;
    for o in list {
        if o == e {
            continue;
        }
        if fd != 0 && w.ent(o).foreign_data == fd {
            continue;
        }
        // slot 45 (rigid 0x350AB5F0): constraints are not ported, so it never ignores
        if w.ent(o).etype == 4 {
            let oe = w.ent(o);
            let ol = oe.living().expect("living");
            if oe.parts.first().is_some_and(|p| p.flags & 2 != 0)
                && (((hc + pos.z) - pivot) - ((ol.height_collider + oe.pos.z) - ol.height_pivot))
                    .abs()
                    < ol.size.z + size.z
                && ((pos.y - oe.pos.y) * (pos.y - oe.pos.y) + (pos.x - oe.pos.x) * (pos.x - oe.pos.x))
                    < (ol.size.x + size.x) * (ol.size.x + size.x)
            {
                return 1.0e10;
            }
            continue;
        }
        let np = w.ent(o).parts.len();
        for j in 0..np {
            let (pf, pgeom, gwd2) = {
                let oe = w.ent(o);
                let p = &oe.parts[j];
                if p.flags & 2 == 0 {
                    continue;
                }
                let qp = oe.q.product(p.q);
                let rr = oe.q.rotate(p.pos);
                let sc = if oe.id_bits & 0xFF00_0000 == 0x0300_0000 {
                    1.0
                } else {
                    p.scale
                };
                (
                    p.flags,
                    p.pgeom_proxy.geom.clone(),
                    GeomWorldData {
                        offset: Vec3::new(rr.x + oe.pos.x, rr.y + oe.pos.y, rr.z + oe.pos.z),
                        r: Mat33::from_quat(qp),
                        scale: sc,
                        ..GeomWorldData::default()
                    },
                )
            };
            contacts.clear();
            let mut hit = col.geom.intersect(pgeom.as_ref(), &gwd, &gwd2, &mut ip, &mut contacts) != 0;
            if !hit {
                let sp: Vec<(u32, Vec3, std::sync::Arc<crate::geometry::PhysGeometry>)> = w
                    .ent(e)
                    .parts
                    .iter()
                    .skip(1)
                    .map(|p| (p.flags_collider, p.pos, p.pgeom_proxy.clone()))
                    .collect();
                for (fc, ppos, pg) in sp {
                    if pf & fc == 0 {
                        continue;
                    }
                    let rr = q.rotate(ppos);
                    let g1 = GeomWorldData {
                        offset: Vec3::new(rr.x + pos.x, rr.y + pos.y, rr.z + pos.z),
                        r: Mat33::from_quat(q),
                        scale: 1.0,
                        v: Vec3::new(-dir.x, -dir.y, -dir.z),
                        w: Vec3::ZERO,
                        center_of_mass: Vec3::ZERO,
                    };
                    contacts.clear();
                    if pg.geom.intersect(pgeom.as_ref(), &g1, &gwd2, &mut ip, &mut contacts) != 0 {
                        hit = true;
                        break;
                    }
                }
            }
            if hit {
                let c = &contacts[0];
                if (dir.y * dir.y + dir.x * dir.x) + dir.z * dir.z == 0.0 {
                    *dir = c.dir;
                }
                return c.t as f32;
            }
        }
    }
    0.0
}
