//! `CParticleEntity::RayTrace(SRayTraceRes&)` (slot 57, 0x3503AB10): what RayWorldIntersection
//! tests for a part-less entity in a grid cell (particles with flag 0x400).

use super::pd;
use crate::collision::contact::{GeomContact, PrimInters};
use crate::collision::pairs::box_pairs::intersect_box_ray;
use crate::geometry::primitives::BoxPrim;
use crate::geometry::shapes::{orthogonal, RayGeom};
use crate::math::{Mat33, Vec3};
use crate::world::{EntIdx, World};

/// Slot 57: the ray against the particle's box — half extents `(r, r, thickness/2)`, axes
/// `q·orthogonal(normal)` (normalised), `that × (q·normal)` and `q·normal` — through the
/// box–ray INTERSECT (0x3515F110). The contact (the per-thread static `geom_contact`):
/// `t = (pt − org)·ndir` (single, stored as double), `pt`, `n` from the intersection,
/// `id[0]` = the particle's surface index, `iNode[0] = 0` (all other fields stay 0).
///
/// Skipped (`None`) when the particle the calling thread is stepping and this one both have
/// flag 0x100.
#[must_use]
pub fn ray_trace(w: &World, e: EntIdx, ray: &RayGeom) -> Option<GeomContact> {
    let tid = w.tls_thread.clamp(0, 4) as usize;
    let cur = w.particle_tls.cur[tid];
    if !cur.is_none() && (w.ent(e).flags & w.ent(cur).flags & 0x100) != 0 {
        return None;
    }
    let ent = w.ent(e);
    let p = pd(w, e);
    let q = ent.q;
    let nw = q.rotate(p.normal); // v42..v44
    let a = orthogonal(p.normal);
    let l2 = ((a.y * a.y) + (a.x * a.x)) + (a.z * a.z);
    let a = if l2 <= f32::from_bits(0x1E3C_E508) {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(a.x * inv, a.y * inv, a.z * inv)
    };
    let ar = q.rotate(a); // v36..v38
    let row1 = Vec3::new(
        (ar.z * nw.y) - (ar.y * nw.z),
        (ar.x * nw.z) - (ar.z * nw.x),
        (ar.y * nw.x) - (ar.x * nw.y),
    );
    let bx = BoxPrim {
        basis: Mat33::from_rows(ar, row1, nw),
        // `bOriented` is left uninitialised by the original (the INTERSECT does not read it)
        oriented: 0,
        center: ent.pos,
        size: Vec3::new(p.size, p.size, p.thickness),
    };
    let mut out = PrimInters::default();
    if !intersect_box_ray(&bx, &ray.prim, &mut out) {
        return None;
    }
    let pt = out.pt[0];
    let org = ray.prim.origin;
    let nd = ray.ndir;
    let (v28, v29, v30) = (pt.x - org.x, pt.y - org.y, pt.z - org.z);
    let t = ((v29 * nd.y) + (nd.x * v28)) + (v30 * nd.z);
    Some(GeomContact {
        t: f64::from(t),
        pt,
        n: out.n,
        id: [p.surface_idx, 0],
        i_node: [0, 0],
        ..GeomContact::default()
    })
}
