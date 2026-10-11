//! Ray slot 54 `RegisterIntersection(primA, primB, gtestA, gtestB, prim_inters*)`
//! (0x3519A380). Every other primitive uses the `CGeometry` base (0x3514F3F0, package P2b).

use super::gtest::GeomUnderTest;
use crate::collision::contact::{GeomContact, PrimInters};
use crate::geometry::shapes::RayGeom;
use crate::math::Vec3;

/// Ray slot 54 (0x3519A380): appends one contact without any unprojection.
///
/// * `t = (double)(((l − o)·ndir))` with `l = Rᵀ·(pt − offset)` (gtest `+48`/`+120`, no
///   scale) and the ray's own origin `o` / `ndir` (object `+48` / `+72`): the distance of the
///   contact along the ray in the geometry frame;
/// * `pt` and the border centre = `inters.pt[0]`; `n = −inters.n`; `dir = 0`, mode 0, `vel = 0`;
/// * `id = [-1, inters.id[1]]` (sign-extended), `iPrim = [0, primB.idx]` where `primB.idx` is
///   the int at `primB + 48` (the triangle index of a 52-byte mesh record) when `gtestB` is
///   given and its `+280` primitive type is 1 (triangles), else 0;
/// * `iFeature = [0x20, inters.iFeature[0][1]]`, `iNode = inters.iNode`;
/// * one border point (`ptborder = &pt`), no contact area.
///
/// Fields the binary does not write (`+84`, `idxborder +96`, `+124`) keep the stale contents of
/// the arena entry; the port's new contact has them defaulted. Sets the stop flag (`+40`) when
/// the count reaches `nMaxContacts` (`+384`) or `params+98` is set. Returns 1.
pub fn ray_register_intersection(
    g: &RayGeom,
    prim_b_index: i32,
    gt: &mut GeomUnderTest,
    gt_b: Option<&GeomUnderTest>,
    inters: &PrimInters,
    contacts: &mut Vec<GeomContact>,
) -> i32 {
    let pt = inters.pt[0];
    let d = Vec3::new(pt.x - gt.offset.x, pt.y - gt.offset.y, pt.z - gt.offset.z);
    let l = gt.r.t_mul_vec(d);
    let (o, nd) = (g.prim.origin, g.ndir);
    let t = ((l.y - o.y) * nd.y + nd.x * (l.x - o.x)) + (l.z - o.z) * nd.z;
    let i_prim_b = match gt_b {
        Some(b) if b.prim_type == 1 => prim_b_index,
        _ => 0,
    };
    contacts.push(GeomContact {
        t: f64::from(t),
        pt,
        n: -inters.n,
        dir: Vec3::ZERO,
        i_unproj_mode: 0,
        vel: 0.0,
        id: [-1, i32::from(inters.id[1])],
        i_prim: [0, i_prim_b],
        i_feature: [0x20, i32::from(inters.i_feature[0][1])],
        i_node: inters.i_node,
        border: vec![pt],
        idx_border: Vec::new(),
        center: pt,
        border_consecutive: false,
        area: None,
        border_closed: 0,
    });
    if contacts.len() as i32 >= gt.n_max_contacts || gt.params_stop_at_first {
        gt.stop = 1;
    }
    1
}
