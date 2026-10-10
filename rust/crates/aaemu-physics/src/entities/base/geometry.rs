//! `AddGeometry` (entity slot 11): `CPhysicalEntity::AddGeometry` 0x35055FF0 and the rigid
//! override `CRigidEntity::AddGeometry` 0x350C41C0 with the body composition
//! `RigidBody::Create` 0x3509D230 / `RigidBody::Add` 0x3509D460.

use super::{compute_bbox, EntityKind, Part};
use crate::geometry::PhysGeometry;
use crate::ids::GeomType;
use crate::math::eigen::jacobi_sym;
use crate::math::{Mat33, Quat, Vec3};
use crate::params::GeomParams;
use crate::sentinel::is_unused;
use crate::solver::RigidBody;
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `max_idiom(a, b)` written `(|a − b| + (a + b))·0.5`.
#[inline]
fn max_i(a: f32, b: f32) -> f32 {
    ((a - b).abs() + (a + b)) * 0.5
}

/// Entity slot 11 `AddGeometry(pgeom, params, id, bThreadSafe)`: dispatches to the class
/// (rigid: 0x350C41C0, otherwise the base 0x35055FF0). Returns the part id or -1.
pub fn add_geometry(
    w: &mut World,
    e: EntIdx,
    pgeom: &Arc<PhysGeometry>,
    gp: &GeomParams,
    id: i32,
) -> i32 {
    match &w.ent(e).kind {
        EntityKind::Rigid(r) if r.wheeled.is_some() => {
            let mut b = vec![0u8; 512];
            crate::params::PeStruct::write(gp, &mut b);
            crate::entities::wheeled::add_geometry(w, e, pgeom, &b, id)
        }
        EntityKind::Rigid(_) => rigid_add_geometry(w, e, pgeom, gp, id),
        EntityKind::Living(_) => crate::entities::living::add_geometry(w, e, pgeom, gp, id),
        _ => base_add_geometry(w, e, pgeom, gp, id),
    }
}

/// Entity slot 11 with a raw `pe_geomparams` byte image (`+0` = the geomparams type: 0 plain,
/// 1 car, 2 articulated), as the oracle passes it. Wheeled vehicles read the car layout;
/// every other class reads the plain block.
pub fn add_geometry_raw(
    w: &mut World,
    e: EntIdx,
    pgeom: &Arc<PhysGeometry>,
    buf: &[u8],
    id: i32,
) -> i32 {
    if w.ent(e).wheeled().is_some() {
        return crate::entities::wheeled::add_geometry(w, e, pgeom, buf, id);
    }
    let gp = <GeomParams as crate::params::PeStruct>::read(buf);
    add_geometry(w, e, pgeom, &gp, id)
}

/// `CPhysicalEntity::AddGeometry` 0x35055FF0 (immediate path; the command-queue path is taken
/// by the world while it is locked, never in the single-threaded port). Matrices
/// (`pMtx3x4`/`pMtx3x3`) and lattices are not supported (pass null).
pub fn base_add_geometry(
    w: &mut World,
    e: EntIdx,
    pgeom: &Arc<PhysGeometry>,
    gp: &GeomParams,
    id: i32,
) -> i32 {
    let bb = pgeom.geom.bbox();
    let s = bb.size;
    // v13 = max(max(sx, sy), sz); v15 = min(min(sx, sy), sz) (scaled by params scale)
    let v11 = (s.x - s.y).abs();
    let max_xy = (v11 + (s.x + s.y)) * 0.5;
    let v13 = ((max_xy - s.z).abs() + (max_xy + s.z)) * 0.5;
    let v12 = gp.scale;
    let v14 = v12 * v13;
    let min_xy = ((s.x + s.y) - v11) * 0.5;
    let v15 = v12 * (((min_xy + s.z) - (min_xy - s.z).abs()) * 0.5);
    let mut id = id;
    if id >= 0 {
        if let Some(i) = w.ent(e).parts.iter().position(|p| p.id == id) {
            // existing id: only a proxy swap (flag 0x20000) is honoured
            if gp.flags & 0x20000 != 0 {
                w.ent_mut(e).parts[i].pgeom_proxy = pgeom.clone();
                if gp.recalc_bbox != 0 {
                    let b = compute_bbox(w, e, 2, -1, None);
                    w.reposition_entity(e, 1, Some(b));
                    w.ent_mut(e).bbox = b;
                }
            }
            return id;
        }
    }
    // validator 0x35074BC0 ("CPhysicalEntity:AddGeometry"): rejects NaN / huge placements
    // only when vars +312 is set.
    if w.vars.validation_strict != 0 && !geomparams_valid(gp) {
        return -1;
    }
    let ent = w.ent_mut(e);
    if ent.parts.len() as i32 == ent.parts_cap {
        // grow by 4 (`((cap + 4) & ~3) - ((cap - 1) >> 31 & 3)`)
        let cap = ent.parts_cap;
        ent.parts_cap = ((cap + 4) & !3) - (((cap - 1) >> 31) & 3);
    }
    if id >= 0 {
        let d = ent.next_part_id - (id + 1);
        ent.next_part_id -= d & (d >> 31);
    } else {
        id = ent.next_part_id;
        ent.next_part_id = id + 1;
    }
    let esc = ent.scale;
    let mut surface_bits = (pgeom.surface_idx as u32) & 0x3FF;
    if gp.surface_idx != i32::MIN {
        surface_bits = (gp.surface_idx as u32) & 0x3FF;
    }
    let min_contact_dist = if is_unused(gp.min_contact_dist) {
        let k = if v15 >= v14 * 0.3 { 0.1 } else { 0.4 };
        max_i(v14 * 0.03, k * v15)
    } else {
        gp.min_contact_dist
    };
    let flags = gp.flags & 0xFFFD_FFFF;
    // idmatBreakable: unused → automatic from the materials (`sub_35052CC0`, below, once the
    // part is in the array) when `flags & 0xFFB`, else 0x3FFF (-1)
    let auto_idmat = gp.idmat_breakable == i32::MIN && flags & 0xFFB != 0;
    if gp.idmat_breakable == i32::MIN {
        if !auto_idmat {
            surface_bits |= 0xFF_FC00;
        }
    } else {
        surface_bits =
            (surface_bits & 0xFF00_03FF) | (((gp.idmat_breakable & 0x3FFF) as u32) << 10);
    }
    let scale = gp.scale * esc;
    let mass = if gp.mass <= 0.0 {
        if gp.density <= 0.0 {
            0.0
        } else {
            ((scale * scale) * scale) * (gp.density * pgeom.volume)
        }
    } else {
        gp.mass
    };
    let part = Part {
        pos: Vec3::new(esc * gp.pos.x, esc * gp.pos.y, esc * gp.pos.z),
        q: gp.q,
        scale,
        bbox: [Vec3::ZERO; 2],
        pgeom: pgeom.clone(),
        pgeom_proxy: pgeom.clone(),
        id,
        mass,
        flags,
        flags_collider: gp.flags_collider,
        max_dim: v13,
        min_contact_dist,
        surface_bits,
        nmats_bits: pgeom.mat_mapping.len() as u32 & 0x7FFF_FFFF,
        mat_mapping: None,
        has_lattice: false,
        unk128: 0,
        bbox_tmp: None,
    };
    ent.parts.push(part);
    let ipart = ent.parts.len() - 1;
    if auto_idmat {
        // `nParts` (+288) is still the new part's index here
        let n = ipart as i32;
        auto_idmat_breakable(w, e, ipart, n, n + 1);
        if n == 1 {
            auto_idmat_breakable(w, e, 0, n, 2);
        }
    }
    // Not ported: a mesh part (type 1) whose idmatBreakable ended up >= 0 gets its geometry
    // prepared for breaking when its slot-19 value exceeds 0.05 (sub_351C02E0); meshes are not
    // ported yet.
    if gp.recalc_bbox != 0 {
        let b = compute_bbox(w, e, 3, -1, None);
        w.reposition_entity(e, 1, Some(b));
        w.ent_mut(e).bbox = b;
        // sub_35053DC0: OBB/grid cache refresh (only used by flag-0x10 large objects).
    }
    id
}

/// `sub_35052CC0(ent, iPart, nParts)`: automatic `idmatBreakable` and the part flag
/// `0x800000` ("breakable surface") from the surface-type flags table (`world+18792`,
/// [`crate::world::SurfaceTables::flags`]).
///
/// `n_parts` is the entity's part count field (`+288`) at the call: only read when `a3 < 0`,
/// where the part count is `a3 + n_parts + 1`. With `F(s)` the flags of surface type `s` and
/// `id ← max(id, (F >> 16) − 1)` on the 14-bit signed field (bits 10–23 of `+96`), starting
/// from -1:
/// * with a material mapping (the part's or the `phys_geometry`'s): every mapped id `m`
///   (clamped to 0..511) contributes `F(m)`; then, if (`id ≥ 0` or some `F & 0x400`) and the
///   part count `> 1`, the field is reset to -1 and every primitive `i` (from the last)
///   contributes `F(map[min(j, nMats − 1)])` with `j = matid(i)` or the part's surface index
///   when `matid(i) < 0` (no clamping of the mapped value);
/// * without a mapping every primitive contributes `F(j)` with that same `j`;
/// * finally a non-mesh part with `id ≥ 0` is reset to -1 and loses the `0x400` bit;
///   `0x400` in the OR of all contributing flags sets the part flag `0x800000` (cleared first).
///
/// Surface indices outside the 512-entry table read 0 here; the binary reads whatever world
/// memory follows the table (*unverified*, only reachable with surface ids ≥ 512 or bad
/// material ids).
pub fn auto_idmat_breakable(w: &mut World, e: EntIdx, ipart: usize, n_parts: i32, a3: i32) {
    let tab = &w.surfaces.flags;
    let sf = |i: i64| -> i32 {
        if (0..tab.len() as i64).contains(&i) {
            tab[i as usize]
        } else {
            0
        }
    };
    // `cur − ((cur − y) & ((cur − y) >> 31))` = max on the sign-extended field
    let upd = |bits: u32, f: i32| -> u32 {
        let cur = ((bits << 8) as i32) >> 18;
        let y = (f >> 16).wrapping_sub(1);
        let d = cur.wrapping_sub(y);
        let nv = cur.wrapping_sub(d & (d >> 31));
        ((((nv << 10) as u32) ^ bits) & 0xFF_FC00) ^ bits
    };
    let idmat_negative = |bits: u32| (((bits & 0xFFFF_FC00) << 8) as i32) < 0;
    let part = &w.ent(e).parts[ipart];
    let pgeom = part.pgeom.clone();
    let geom = &pgeom.geom;
    let mapping: Option<&[i32]> = match &part.mat_mapping {
        Some(m) => Some(m.as_slice()),
        None if !pgeom.mat_mapping.is_empty() => Some(pgeom.mat_mapping.as_slice()),
        None => None,
    };
    let nmats = (part.nmats_bits.wrapping_mul(2) as i32) >> 1;
    let mut bits = part.surface_bits | 0xFF_FC00;
    let mut pflags = part.flags & !0x80_0000;
    let v6 = a3 + ((a3 >> 31) & n_parts.wrapping_add(1));
    let mut v5: i32 = 0;
    let map_at = |m: &[i32], i: i64| -> i64 {
        usize::try_from(i)
            .ok()
            .and_then(|i| m.get(i).copied())
            .map_or(0, i64::from)
    };
    if let Some(map) = mapping {
        for i in 0..nmats.max(0) {
            let m = map_at(map, i64::from(i)) as i32;
            let c = -((-m) & ((-m) >> 31)); // max(m, 0)
            let t = 511 - c;
            let idx = (t & (t >> 31)) + c; // min(c, 511)
            let f = sf(i64::from(idx));
            v5 |= f;
            bits = upd(bits, f);
        }
        if (!idmat_negative(bits) || v5 & 0x400 != 0) && v6 > 1 {
            bits |= 0xFF_FC00;
            v5 = 0;
            let mut i = geom.primitive_count() - 1;
            while i >= 0 {
                let mid = geom.primitive_mat_id(i);
                let surf = ((bits << 22) as i32) >> 22;
                let j = mid.wrapping_add((mid >> 31) & surf.wrapping_add(1));
                let t = i64::from(nmats.wrapping_sub(1).wrapping_sub(j));
                let jj = (t & (t >> 31)) + i64::from(j); // min(j, nMats − 1)
                let f = sf(map_at(map, jj));
                v5 |= f;
                bits = upd(bits, f);
                i -= 1;
            }
        }
    } else {
        let mut i = geom.primitive_count() - 1;
        while i >= 0 {
            let mid = geom.primitive_mat_id(i);
            let surf = ((bits << 22) as i32) >> 22;
            let idx = i64::from(mid) + (i64::from(surf.wrapping_add(1)) & (i64::from(mid) >> 31));
            let f = sf(idx);
            v5 |= f;
            bits = upd(bits, f);
            i -= 1;
        }
    }
    if !idmat_negative(bits) && geom.geom_type() != GeomType::TriMesh {
        bits |= 0xFF_FC00;
        v5 &= !0x400;
    }
    if v5 & 0x400 != 0 {
        pflags |= 0x80_0000;
    }
    let part = &mut w.ent_mut(e).parts[ipart];
    part.surface_bits = bits;
    part.flags = pflags;
}

/// The checks of the geomparams validator 0x35074BC0 (finite, below the world limits).
fn geomparams_valid(gp: &GeomParams) -> bool {
    gp.pos.is_finite()
        && gp.q.x.is_finite()
        && gp.q.y.is_finite()
        && gp.q.z.is_finite()
        && gp.q.w.is_finite()
        && gp.scale.is_finite()
}

/// `CRigidEntity::AddGeometry` 0x350C41C0: rejects geometries with negative inertia, calls
/// the base, then adds the part's mass to the body (first part: `RigidBody::Create`,
/// further parts: `RigidBody::Add`) and snapshots the previous pose.
pub fn rigid_add_geometry(
    w: &mut World,
    e: EntIdx,
    pgeom: &Arc<PhysGeometry>,
    gp: &GeomParams,
    id: i32,
) -> i32 {
    let i = pgeom.inertia;
    if !((gp.mass.abs() + gp.density.abs()) == 0.0
        || (pgeom.volume > 0.0 && i.x >= 0.0 && i.y >= 0.0 && i.z >= 0.0))
    {
        // "CRigidEntity::AddGeometry: (%s at %.1f,%.1f,%.1f) Trying to add bad geometry"
        return -1;
    }
    let pid = base_add_geometry(w, e, pgeom, gp, id);
    if pid < 0 {
        return pid;
    }
    let v15 = gp.scale;
    let v17 = ((v15 * v15) * v15) * pgeom.volume;
    let v16 = if gp.mass <= 0.0 {
        v17 * gp.density
    } else {
        gp.mass
    };
    // part centre of mass in the world: q_ent·(q_gp·origin·s + pos_gp) + pos_ent
    let o = gp.q.rotate(pgeom.origin);
    let p1 = Vec3::new(
        o.x * v15 + gp.pos.x,
        o.y * v15 + gp.pos.y,
        o.z * v15 + gp.pos.z,
    );
    let ent = w.ent(e);
    let (epos, eq) = (ent.pos, ent.q);
    let p2 = eq.rotate(p1);
    let com = Vec3::new(p2.x + epos.x, p2.y + epos.y, p2.z + epos.z);
    // body orientation: pgeom.q ⊗ (q_ent ⊗ q_gp)
    let q52 = eq.product(gp.q);
    let qbody = q52.product(pgeom.q);
    let k = ent
        .parts
        .iter()
        .position(|p| p.id == pid)
        .unwrap_or(ent.parts.len());
    let body_id = ent.rigid().expect("rigid").body;
    w.ent_mut(e).parts[k].mass = v16;
    if v16 > 0.0 {
        let s2 = v15 * v15;
        let s3 = v15 * s2;
        let ib = Vec3::new((i.x * s2) * s3, (i.y * s2) * s3, (i.z * s2) * s3);
        let n_parts = w.ent(e).parts.len();
        let b = w.bodies.get_mut(body_id);
        if b.m == 0.0 || n_parts == 1 {
            body_create(b, com, ib, qbody, v17, v16, eq, epos);
        } else {
            body_add(b, com, ib, qbody, v17, v16);
        }
    }
    let b = *w.bodies.get(body_id);
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.prev_pos = b.pos;
    r.prev_q = b.q;
    pid
}

/// `sub_3509D230` `RigidBody::Create(pos, Ibody, q, V, M, q_ent, pos_ent)`.
#[allow(clippy::too_many_arguments)]
pub fn body_create(
    b: &mut RigidBody,
    pos: Vec3,
    ibody: Vec3,
    q: Quat,
    v: f32,
    m: f32,
    q_ent: Quat,
    pos_ent: Vec3,
) {
    b.volume = v;
    b.m = m;
    let k = m / v;
    b.q = q;
    b.pos = pos;
    if b.m > 0.0 {
        b.ibody = Vec3::new(ibody.x * k, ibody.y * k, ibody.z * k);
        b.ibody_inv = inv_diag(b.ibody);
        b.minv = 1.0 / b.m;
    } else {
        b.ibody = Vec3::ZERO;
        b.ibody_inv = Vec3::ZERO;
        b.minv = 0.0;
    }
    b.qfb = b.q.conj().product(q_ent);
    let d = Vec3::new(
        b.pos.x - pos_ent.x,
        b.pos.y - pos_ent.y,
        b.pos.z - pos_ent.z,
    );
    b.offsfb = rotate_inv(q_ent, d);
    b.update_state();
}

/// `sub_35073120`: invert a diagonal inertia in place when its product is non-zero.
#[must_use]
pub fn inv_diag(d: Vec3) -> Vec3 {
    let p = (d.x * d.y) * d.z;
    if p != 0.0 {
        let r = 1.0 / p;
        Vec3::new((d.z * d.y) * r, (d.z * d.x) * r, (d.y * d.x) * r)
    } else {
        d
    }
}

/// `sub_35023C00`: rotate `v` by the inverse of `q`.
#[must_use]
pub fn rotate_inv(q: Quat, v: Vec3) -> Vec3 {
    let v9 = (q.z * v.y - q.y * v.z) + v.x * q.w;
    let v10 = (q.x * v.z - v.x * q.z) + q.w * v.y;
    let v11 = (v.x * q.y - q.x * v.y) + q.w * v.z;
    let v12 = q.z * v10 - q.y * v11;
    let v13 = q.y * v9 - q.x * v10;
    let v14 = q.x * v11 - q.z * v9;
    Vec3::new((v12 + v.x) + v12, (v14 + v.y) + v14, (v13 + v.z) + v13)
}

/// `sub_3509D460` `RigidBody::Add(pos, Ibody, q, V, M)`: merge a further part (centre of mass
/// `pos` in the world, principal inertia `ibody` per unit density at its scale, principal axes
/// `q` in the world, volume `v`, mass `m`) into the body.
///
/// * `m == 0` **or NaN** → nothing (`ucomiss`/`je`: the unordered result takes the branch).
/// * `!(|M + m| ≥ M·1e-4)` (NaN included) → the body is zeroed instead: `M`, `Minv`, `Ibody`,
///   `Ibody_inv`, `P`, `L`, `w`, `v` (volume, pose and `qfb`/`offsfb` are left alone).
/// * Otherwise, in the old body's principal frame: the new part's inertia
///   `C = R·diag((m/V)·Ibody)·Rᵀ` with `R` the matrix of `q_body* ⊗ q` (both products written
///   as full 3×3 multiplications, the `·0` terms included, only the upper triangle of `C`),
///   both parts shifted to the combined centre `(pos_b·M + pos·m)·(1/(M+m))` by the parallel
///   axis rule, summed, and diagonalised by the Jacobi solver ([`jacobi_sym`], threshold 0).
///   The eigenvalues become `Ibody`, `Ibody_inv` their inverse (kept as is when the product is
///   0 or NaN), `q_rot = mat2quat(V)` (rows of `V` = eigenvectors) gives `q ← q ⊗ q_rot*` and
///   `qfb ← q_rot ⊗ qfb`; `offsfb += q_ent⁻¹·(com − pos_b)` with `q_ent = q_b ⊗ qfb` of the old
///   body; `pos ← com`, `V += v`, `M ← M + m`, `Minv = 1/(M + m)`, then `UpdateState`.
#[allow(clippy::neg_cmp_op_on_partial_ord)]
pub fn body_add(b: &mut RigidBody, pos: Vec3, ibody: Vec3, q: Quat, v: f32, m: f32) {
    if m == 0.0 || m.is_nan() {
        return;
    }
    let m0 = b.m;
    let mt = m0 + m;
    if !(mt.abs() >= m0 * f32::from_bits(0x38D1_B717)) {
        // 1e-4
        b.m = 0.0;
        b.minv = 0.0;
        b.ibody = Vec3::ZERO;
        b.ibody_inv = Vec3::ZERO;
        b.p = Vec3::ZERO;
        b.l = Vec3::ZERO;
        b.w = Vec3::ZERO;
        b.v = Vec3::ZERO;
        return;
    }
    // the new part's inertia in the old principal frame: C = (R·D)·Rᵀ
    let r = Mat33::from_quat(b.q.conj().product(q)).m;
    let k = m / v;
    let dg = [
        [k * ibody.x, 0.0, 0.0],
        [0.0, k * ibody.y, 0.0],
        [0.0, 0.0, k * ibody.z],
    ];
    let mut a = [[0.0f32; 3]; 3];
    for i in 0..3 {
        for j in 0..3 {
            a[i][j] = (r[i][0] * dg[0][j] + r[i][1] * dg[1][j]) + r[i][2] * dg[2][j];
        }
    }
    let c = |i: usize, j: usize| (a[i][0] * r[j][0] + a[i][1] * r[j][1]) + a[i][2] * r[j][2];
    let (c00, c01, c02, c11, c12, c22) = (c(0, 0), c(0, 1), c(0, 2), c(1, 1), c(1, 2), c(2, 2));
    // combined centre of mass
    let inv = 1.0 / mt;
    let com = Vec3::new(
        (b.pos.x * m0 + pos.x * m) * inv,
        (b.pos.y * m0 + pos.y * m) * inv,
        (b.pos.z * m0 + pos.z * m) * inv,
    );
    // the old body shifted to the new centre
    let d = rotate_inv(b.q, com - b.pos);
    let o_xx = (d.y * d.y + d.z * d.z) * m0 + b.ibody.x;
    let o_yy = (d.x * d.x + d.z * d.z) * m0 + b.ibody.y;
    let o_zz = (d.x * d.x + d.y * d.y) * m0 + b.ibody.z;
    let o_xy = -((d.x * m0) * d.y);
    let o_xz = -((d.x * m0) * d.z);
    let o_yz = -((d.y * m0) * d.z);
    // the new part shifted to the new centre
    let d = rotate_inv(b.q, com - pos);
    let n_xx = (d.y * d.y + d.z * d.z) * m + c00;
    let n_yy = (d.x * d.x + d.z * d.z) * m + c11;
    let n_zz = (d.x * d.x + d.y * d.y) * m + c22;
    let n_xy = c01 - (d.x * m) * d.y;
    let n_xz = c02 - (d.x * m) * d.z;
    let n_yz = c12 - (d.y * m) * d.z;
    let (xx, yy, zz) = (o_xx + n_xx, o_yy + n_yy, o_zz + n_zz);
    let (xy, xz, yz) = (o_xy + n_xy, o_xz + n_xz, o_yz + n_yz);
    // `a5 + V` (commutative: same bits as `V + a5`)
    b.volume += v;
    b.m = mt;
    b.minv = 1.0 / mt;
    let q_ent = b.q.product(b.qfb);
    b.offsfb = rotate_inv(q_ent, com - b.pos) + b.offsfb;
    let eig = jacobi_sym(&[[xx, xy, xz], [xy, yy, yz], [xz, yz, zz]], 0.0);
    b.ibody = Vec3::new(eig.values[0], eig.values[1], eig.values[2]);
    // inline inverse: `ucomiss p, 0; je` keeps Ibody when the product is 0 or NaN
    b.ibody_inv = b.ibody;
    let (ix, iy, iz) = (b.ibody.x, b.ibody.y, b.ibody.z);
    let p = (ix * iy) * iz;
    if !(p == 0.0 || p.is_nan()) {
        let rp = 1.0 / p;
        b.ibody_inv = Vec3::new((iy * iz) * rp, (iz * ix) * rp, (iy * ix) * rp);
    }
    let qr = Quat::from_mat33(&Mat33 { m: eig.vectors });
    b.q = b.q.product(qr.conj());
    b.qfb = qr.product(b.qfb);
    b.pos = com;
    b.update_state();
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::geometry::primitives::{BoxPrim, Primitive};
    use crate::world::geoman::{create_primitive, register_geometry};

    fn unit_body(pos: Vec3, ibody: Vec3, q: Quat, v: f32, m: f32) -> RigidBody {
        let mut b = RigidBody::default();
        body_create(&mut b, pos, ibody, q, v, m, Quat::IDENTITY, Vec3::ZERO);
        b
    }

    /// Box of half-sizes `h`: volume and principal inertia per unit density.
    fn box_props(h: Vec3) -> (f32, Vec3) {
        let v = 8.0 * h.x * h.y * h.z;
        let k = v / 3.0;
        (
            v,
            Vec3::new(
                k * (h.y * h.y + h.z * h.z),
                k * (h.x * h.x + h.z * h.z),
                k * (h.x * h.x + h.y * h.y),
            ),
        )
    }

    /// `R·diag(d)·Rᵀ` in f64.
    fn rotated(q: Quat, d: [f64; 3]) -> [[f64; 3]; 3] {
        let r = Mat33::from_quat(q).m;
        let mut t = [[0.0; 3]; 3];
        for (i, ti) in t.iter_mut().enumerate() {
            for (j, tij) in ti.iter_mut().enumerate() {
                for k in 0..3 {
                    *tij += f64::from(r[i][k]) * d[k] * f64::from(r[j][k]);
                }
            }
        }
        t
    }

    #[test]
    fn first_part_through_add_matches_create() {
        // AddGeometry routes the first part to Create (the golden `one` entity pins that path);
        // Add on an empty body yields the same mass properties and frame (qfb/offsfb differ:
        // Create derives them from the entity pose, Add accumulates them).
        let (v, ib) = box_props(Vec3::new(0.5, 0.5, 0.5));
        let c = unit_body(Vec3::new(1.0, 2.0, 3.0), ib, Quat::IDENTITY, v, 10.0);
        let mut a = RigidBody::default();
        body_add(
            &mut a,
            Vec3::new(1.0, 2.0, 3.0),
            ib,
            Quat::IDENTITY,
            v,
            10.0,
        );
        assert_eq!(
            (a.m, a.minv, a.volume, a.pos),
            (c.m, c.minv, c.volume, c.pos)
        );
        assert_eq!(a.ibody, c.ibody);
        assert_eq!(a.ibody_inv, c.ibody_inv);
        assert_eq!(a.q, c.q);
        assert_eq!(a.iinv, c.iinv);
    }

    #[test]
    fn two_equal_boxes_side_by_side() {
        let (v, ib) = box_props(Vec3::new(0.5, 0.5, 0.5));
        let mut b = unit_body(Vec3::new(1.0, 2.0, 3.0), ib, Quat::IDENTITY, v, 10.0);
        body_add(
            &mut b,
            Vec3::new(2.0, 2.0, 3.0),
            ib,
            Quat::IDENTITY,
            v,
            10.0,
        );
        assert_eq!(b.m, 20.0);
        assert_eq!(b.minv, 1.0 / 20.0);
        assert_eq!(b.volume, 2.0);
        assert_eq!(b.pos, Vec3::new(1.5, 2.0, 3.0));
        // Ixx = 2·(10/6) = 10/3, Iyy = Izz = 10/3 + 2·10·0.25 = 25/3
        assert!((b.ibody.x - 10.0 / 3.0).abs() < 1e-5);
        assert!((b.ibody.y - 25.0 / 3.0).abs() < 1e-5);
        assert!((b.ibody.z - 25.0 / 3.0).abs() < 1e-5);
        // the summed tensor is diagonal: no rotation
        assert_eq!(b.q, Quat::IDENTITY);
        assert_eq!(b.qfb, Quat::IDENTITY);
        // entity origin (0,0,0, from body_create) → CoM
        assert_eq!(b.offsfb, Vec3::new(1.5, 2.0, 3.0));
        assert_eq!(b.ibody_inv, inv_diag(b.ibody));
    }

    #[test]
    #[allow(clippy::needless_range_loop)]
    fn two_rotated_boxes_match_a_double_reference() {
        let (v1, i1) = box_props(Vec3::new(0.5, 0.25, 0.75));
        let (v2, i2) = box_props(Vec3::new(1.0, 0.1, 0.3));
        let q1 = Quat::new(0.1, -0.2, 0.3, 0.9).normalized_xwyz();
        let q2 = Quat::new(-0.4, 0.3, 0.2, 0.8).normalized_xwyz();
        let (p1, p2) = (Vec3::new(0.2, -0.1, 0.5), Vec3::new(-1.0, 0.7, 0.3));
        let (m1, m2) = (12.0f32, 5.0f32);
        let mut b = unit_body(p1, i1, q1, v1, m1);
        body_add(&mut b, p2, i2, q2, v2, m2);
        // reference: Σ R·diag(I·m/V)·Rᵀ + parallel axis, in f64
        let part = |p: Vec3, i: Vec3, q: Quat, v: f32, m: f32| {
            let k = f64::from(m) / f64::from(v);
            let t = rotated(
                q,
                [f64::from(i.x) * k, f64::from(i.y) * k, f64::from(i.z) * k],
            );
            (
                t,
                [f64::from(p.x), f64::from(p.y), f64::from(p.z)],
                f64::from(m),
            )
        };
        let parts = [part(p1, i1, q1, v1, m1), part(p2, i2, q2, v2, m2)];
        let mt: f64 = parts.iter().map(|p| p.2).sum();
        let mut com = [0.0f64; 3];
        for p in &parts {
            for a in 0..3 {
                com[a] += p.1[a] * p.2 / mt;
            }
        }
        let mut t = [[0.0f64; 3]; 3];
        for p in &parts {
            let d = [p.1[0] - com[0], p.1[1] - com[1], p.1[2] - com[2]];
            let d2 = d[0] * d[0] + d[1] * d[1] + d[2] * d[2];
            for a in 0..3 {
                for c in 0..3 {
                    let delta = if a == c { d2 } else { 0.0 };
                    t[a][c] += p.0[a][c] + p.2 * (delta - d[a] * d[c]);
                }
            }
        }
        assert_eq!(b.m, 17.0);
        let pos = [b.pos.x, b.pos.y, b.pos.z];
        for a in 0..3 {
            assert!((f64::from(pos[a]) - com[a]).abs() < 1e-5);
        }
        let w = rotated(
            b.q,
            [
                f64::from(b.ibody.x),
                f64::from(b.ibody.y),
                f64::from(b.ibody.z),
            ],
        );
        for a in 0..3 {
            for c in 0..3 {
                assert!(
                    (w[a][c] - t[a][c]).abs() < 1e-4,
                    "[{a}][{c}] {} vs {}",
                    w[a][c],
                    t[a][c]
                );
            }
        }
        // the entity frame is preserved: q ⊗ qfb is the (identity) entity orientation
        let qe = b.q.product(b.qfb);
        assert!((qe.w.abs() - 1.0).abs() < 1e-5, "{qe:?}");
        // offsfb is the CoM in the entity frame (entity at the origin, unrotated)
        assert!((b.offsfb - b.pos).len() < 1e-5);
    }

    #[test]
    fn zero_or_nan_mass_is_ignored() {
        let (v, ib) = box_props(Vec3::new(0.5, 0.5, 0.5));
        let b0 = unit_body(Vec3::new(1.0, 0.0, 0.0), ib, Quat::IDENTITY, v, 10.0);
        for m in [0.0, -0.0, f32::NAN] {
            let mut b = b0;
            body_add(&mut b, Vec3::ZERO, ib, Quat::IDENTITY, v, m);
            assert_eq!(b, b0);
        }
    }

    #[test]
    fn cancelling_mass_zeroes_the_body() {
        let (v, ib) = box_props(Vec3::new(0.5, 0.5, 0.5));
        let mut b = unit_body(Vec3::new(1.0, 0.0, 0.0), ib, Quat::IDENTITY, v, 10.0);
        b.p = Vec3::new(1.0, 2.0, 3.0);
        b.l = Vec3::new(4.0, 5.0, 6.0);
        b.v = Vec3::new(7.0, 8.0, 9.0);
        b.w = Vec3::new(1.5, 2.5, 3.5);
        let before = b;
        // |10 + (−10)| = 0 < 10·1e-4
        body_add(&mut b, Vec3::ZERO, ib, Quat::IDENTITY, v, -10.0);
        assert_eq!((b.m, b.minv), (0.0, 0.0));
        assert_eq!((b.ibody, b.ibody_inv), (Vec3::ZERO, Vec3::ZERO));
        assert_eq!(
            (b.p, b.l, b.w, b.v),
            (Vec3::ZERO, Vec3::ZERO, Vec3::ZERO, Vec3::ZERO)
        );
        assert_eq!(
            (b.volume, b.pos, b.q, b.qfb),
            (before.volume, before.pos, before.q, before.qfb)
        );
        assert_eq!((b.offsfb, b.iinv), (before.offsfb, before.iinv));
        // just above the threshold the merge happens
        let mut b = before;
        body_add(&mut b, Vec3::ZERO, ib, Quat::IDENTITY, v, -9.99);
        assert!(b.m > 0.0 && b.m < 0.02);
    }

    fn breakable_world(
        flags: &[(usize, i32)],
        mapping: Option<&[i32]>,
    ) -> (World, EntIdx, Arc<PhysGeometry>) {
        let mut w = World::new();
        for &(i, f) in flags {
            w.surfaces.flags[i] = f;
        }
        let e = w.create_physical_entity(1, 0.0, None, 0, 0, -1).unwrap();
        let g = create_primitive(&Primitive::Box(BoxPrim {
            size: Vec3::new(0.5, 0.5, 0.5),
            ..BoxPrim::default()
        }))
        .unwrap();
        (w, e, register_geometry(g, 1, mapping))
    }

    fn auto_gp(x: f32, flags: u32) -> GeomParams {
        GeomParams {
            idmat_breakable: i32::MIN,
            pos: Vec3::new(x, 0.0, 0.0),
            flags,
            recalc_bbox: 0,
            ..GeomParams::default()
        }
    }

    #[test]
    fn idmat_auto_without_mapping_uses_the_surface_index() {
        let df = GeomParams::default().flags;
        // surface 1 (the geometry's default): flag 0x400 and no id → part flag 0x800000
        let (mut w, e, g) = breakable_world(&[(1, 0x400)], None);
        add_geometry(&mut w, e, &g, &auto_gp(0.0, df), -1);
        let p = &w.ent(e).parts[0];
        assert_eq!(p.surface_bits, 0xFF_FC01);
        assert_eq!(p.flags, df | 0x80_0000);
        // id field 2 on a non-mesh: reset to -1 and the 0x400 bit dropped
        let (mut w, e, g) = breakable_world(&[(1, 0x30400)], None);
        add_geometry(&mut w, e, &g, &auto_gp(0.0, df), -1);
        let p = &w.ent(e).parts[0];
        assert_eq!((p.surface_bits, p.flags), (0xFF_FC01, df));
        // flags outside 0xFFB: no automatic id, plain -1 and no flag
        let (mut w, e, g) = breakable_world(&[(1, 0x400)], None);
        add_geometry(&mut w, e, &g, &auto_gp(0.0, 0x4), -1);
        let p = &w.ent(e).parts[0];
        assert_eq!((p.surface_bits, p.flags), (0xFF_FC01, 0x4));
    }

    #[test]
    fn idmat_auto_with_a_mapping() {
        let df = GeomParams::default().flags;
        // mapped surface 9 has 0x400 without an id: the flag survives (id stays -1); the second
        // part enables the primitive pass (count > 1) and re-runs part 0
        let map = [9];
        let (mut w, e, g) = breakable_world(&[(9, 0x400)], Some(&map));
        add_geometry(&mut w, e, &g, &auto_gp(0.0, df), -1);
        assert_eq!(w.ent(e).parts[0].flags, df | 0x80_0000);
        add_geometry(&mut w, e, &g, &auto_gp(1.0, df), -1);
        for p in &w.ent(e).parts {
            assert_eq!((p.surface_bits, p.flags), (0xFF_FC01, df | 0x80_0000));
        }
        // the primitive pass maps the surface index 1 through map[min(1, nMats − 1)] = map[1]:
        // surface 4 brings 0x400 without an id, the first pass (surface 3, id field 6) does not
        let map = [3, 4];
        let (mut w, e, g) = breakable_world(&[(3, 0x60000), (4, 0x400)], Some(&map));
        add_geometry(&mut w, e, &g, &auto_gp(0.0, df), -1);
        // count 1: no primitive pass; id 5 ≥ 0 on a box → reset, and the 0x400 of surface 4
        // (seen in the first pass) is dropped with it
        assert_eq!(w.ent(e).parts[0].flags, df);
        add_geometry(&mut w, e, &g, &auto_gp(1.0, df), -1);
        // count 2: the field is >= 0 after the first pass → primitive pass: only map[1] = 4
        // contributes (0x400, id -1) → flag kept on both parts
        for p in &w.ent(e).parts {
            assert_eq!((p.surface_bits, p.flags), (0xFF_FC01, df | 0x80_0000));
        }
        // first-pass ids clamp to 0..511 (700 → 511)
        let map = [700];
        let (mut w, e, g) = breakable_world(&[(511, 0x400)], Some(&map));
        add_geometry(&mut w, e, &g, &auto_gp(0.0, df), -1);
        assert_eq!(w.ent(e).parts[0].flags, df | 0x80_0000);
    }
}
