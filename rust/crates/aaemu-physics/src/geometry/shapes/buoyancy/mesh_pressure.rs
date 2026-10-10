//! `CTriMesh` slot 12 `CalcVolumetricPressure` (0x351CBE20) over a [`TriMeshView`]; see
//! [`volumetric_pressure`].

use super::super::super::INC_MOD3;
use super::TriMeshView;
use crate::geometry::GeomWorldData;
use crate::math::{Mat33, Vec3};

/// Number of vertices of the subdivision stack (21⅓ triangles; `rbp+0x40 .. rbp+0x340`).
const STACK_VERTS: usize = 64;
/// A popped triangle whose first stack vertex index is `<= SPLIT_MAX` may be split around the
/// epicenter's projection (`cmp r11d, 0x37; ja`).
const SPLIT_MAX: usize = 0x37;
/// A popped triangle whose first stack vertex index is `<= SUBDIV_MAX` may be subdivided 4-way
/// (`cmp r11d, 0x34; ja`); deeper ones become leaves.
const SUBDIV_MAX: usize = 0x34;
/// At most this many stack pops per mesh triangle (`cmp [rsp+0x4c], 0x12c; jl`); whatever is
/// left on the stack is dropped.
const MAX_ITERS: i32 = 300;

/// `Rᵀ·(p − offset)` with the binary's association `((dx·R0j + dy·R1j) + dz·R2j)`.
#[inline]
fn to_local(r: &Mat33, offset: Vec3, p: Vec3) -> Vec3 {
    let m = &r.m;
    let dx = p.x - offset.x;
    let dy = p.y - offset.y;
    let dz = p.z - offset.z;
    Vec3::new(
        (dx * m[0][0] + dy * m[1][0]) + dz * m[2][0],
        (dx * m[0][1] + dy * m[1][1]) + dz * m[2][1],
        (dx * m[0][2] + dy * m[1][2]) + dz * m[2][2],
    )
}

/// `R·v + acc` with the binary's association `((vx·Ri0 + vy·Ri1) + vz·Ri2) + acc_i`.
#[inline]
fn add_world(r: &Mat33, v: Vec3, acc: &mut Vec3) {
    let m = &r.m;
    *acc = Vec3::new(
        ((v.x * m[0][0] + v.y * m[0][1]) + v.z * m[0][2]) + acc.x,
        ((v.x * m[1][0] + v.y * m[1][1]) + v.z * m[1][2]) + acc.y,
        ((v.x * m[2][0] + v.y * m[2][1]) + v.z * m[2][2]) + acc.z,
    );
}

/// `|v − e|²` as `((dx·dx + dy·dy) + dz·dz)`.
#[inline]
fn dist2(v: Vec3, e: Vec3) -> f32 {
    let dx = v.x - e.x;
    let dy = v.y - e.y;
    let dz = v.z - e.z;
    (dx * dx + dy * dy) + dz * dz
}

/// Midpoint `(a + b)·0.5` per component, with the operand order given per component (the
/// binary's `addss` destination first; it only matters for NaN payloads).
#[inline]
fn mid(ax: f32, bx: f32, ay: f32, by: f32, az: f32, bz: f32) -> Vec3 {
    Vec3::new((ax + bx) * 0.5, (ay + by) * 0.5, (az + bz) * 0.5)
}

/// `CTriMesh::CalcVolumetricPressure` (vtable slot 12, 0x351CBE20; x86 1.2: 0x351E24D0).
///
/// The binary takes the mesh's read lock (`lock xadd [this+8], 1`, spin while the writer byte
/// `[this+10]` is set, `lock xadd −1` on exit); the port has no lock.
///
/// Frame: `e = Rᵀ·(epicenter − offset)` and `c = Rᵀ·(centerOfMass − offset)` — **not divided
/// by the scale** (quirk kept: for `s ≠ 1` the epicenter and the vertices are in different
/// units), `k' = k·(1/s)²`, `rmin'² = ((1/s)·rmin)²`.
///
/// Every triangle `t` with `n_t·(v0 − e) > 0` (`comiss`, NaN skips; i.e. triangles facing
/// *away* from the epicenter) is pushed on a 64-vertex stack and processed for at most 300
/// pops. A popped triangle `(A, B, C)` whose first stack slot is `b`:
/// 1. **Projection split** — if for every edge `(V_j, V_{j+1})` the triple product
///    `n·((V_{j+1} − V_j) × (V_{j+1} − e))` (computed as `(Y·n_y + X·n_x) + Z·n_z`) is
///    `> 0.0001` **in double** (`cvtps2pd; comisd`, NaN fails), i.e. the epicenter projects
///    strictly inside, and `b <= 55`: `p = e − n·(n·(e − A))` and the stack gets
///    `(p, B, C), (A, p, C), (A, B, p)`.
///    **Quirk kept:** the sum of the three triple products is `−2·n·area(A, B, C)`, so for a
///    mesh whose normals agree with the winding (every box with positive sizes and a proper
///    rotation) this test never passes — the split only happens for triangles wound against
///    their normal (oracle-tested with negative box sizes and mirrored bases). The `b > 55`
///    fallback is practically unreachable: a sub-triangle that contains the projection has an
///    ancestor that contained it with larger margins and was split instead.
/// 2. Otherwise, an edge `(V_j, V_{j+1})` "needs refinement" when
///    `(d_j > rmin ∨ d_{j+1} > rmin) ∧ (¬(d_j >= 0.49·d_{j+1}) ∨ d_j > 2.0408163·d_{j+1})` with
///    `d` = squared distances to `e` — compared against the **raw, unsquared, unscaled `rmin`
///    argument** (quirk kept). If an edge needs refinement and `b <= 52`, the triangle is
///    split 4-way into `(AB, BC, CA), (A, AB, AC), (B, BC, BA), (C, CA, CB)` (pushed in that
///    order, so the `C` corner is popped next).
/// 3. Otherwise it is a leaf: centroid `g = (C + (B + A))·⅓`, `d² = |g − e|²`,
///    `mag = (((|(B−A)×(C−A)|·(n·(g − e)))·0.5)·k') / ((max(d², rmin'²))·√d²)` (max idiom),
///    `F = n·mag`, `P += F`, `L += (g − c) × F` — all single precision, accumulated in the mesh
///    frame and rotated back with `R` (no scale) at the end.
///
/// Depth limit: a stack overflow is impossible (`b <= 55` / `b <= 52` keep every write below
/// vertex 64); the 300-pop limit silently drops the rest of the stack.
#[allow(clippy::too_many_arguments, clippy::many_single_char_names)]
pub fn volumetric_pressure(
    m: &TriMeshView,
    gwd: &GeomWorldData,
    epicenter: Vec3,
    k: f32,
    rmin: f32,
    com: Vec3,
    p_out: &mut Vec3,
    l_out: &mut Vec3,
) {
    let r = &gwd.r;
    let e = to_local(r, gwd.offset, epicenter);
    let cm = to_local(r, gwd.offset, com);
    let inv = 1.0 / gwd.scale;
    let rm = inv * rmin;
    let rmin2 = rm * rm;
    let kk = k * (inv * inv);
    let lo = f32::from_bits(0x3EFA_E147); // 0.49
    let hi = f32::from_bits(0x4002_9CBC); // 2.0408163 = 1/0.49
    let third = f32::from_bits(0x3EAA_AAAB);

    let (mut px, mut py, mut pz) = (0.0f32, 0.0f32, 0.0f32);
    let (mut lx, mut ly, mut lz) = (0.0f32, 0.0f32, 0.0f32);
    let mut s = [Vec3::ZERO; STACK_VERTS];

    for t in 0..m.n_tris() {
        let n = m.normals[t];
        let v0 = m.vertices[m.indices[3 * t] as usize];
        let facing = (n.x * (v0.x - e.x) + n.y * (v0.y - e.y)) + n.z * (v0.z - e.z);
        if !(facing > 0.0) {
            continue;
        }
        s[0] = v0;
        s[1] = m.vertices[m.indices[3 * t + 1] as usize];
        s[2] = m.vertices[m.indices[3 * t + 2] as usize];
        let mut cnt: usize = 3;
        let mut iter = 0;
        loop {
            let b = cnt - 3;
            // 1. does the epicenter project strictly inside?
            let mut j = 0;
            while j < 3 {
                let a = s[b + j];
                let v = s[b + INC_MOD3[j]];
                let ex = v.x - e.x;
                let ey = v.y - e.y;
                let ez = v.z - e.z;
                let ux = v.x - a.x;
                let uy = v.y - a.y;
                let uz = v.z - a.z;
                let x = uy * ez - uz * ey;
                let y = uz * ex - ez * ux;
                let z = ey * ux - uy * ex;
                let tp = (y * n.y + x * n.x) + z * n.z;
                if !(f64::from(tp) > 0.0001) {
                    break;
                }
                j += 1;
            }
            if j == 3 && b <= SPLIT_MAX {
                let a = s[b];
                let tt = (n.x * (e.x - a.x) + n.y * (e.y - a.y)) + n.z * (e.z - a.z);
                let pp = Vec3::new(e.x - n.x * tt, e.y - n.y * tt, e.z - n.z * tt);
                s[b + 3] = a;
                s[b + 5] = s[b + 2];
                s[b + 6] = a;
                s[b + 7] = s[b + 1];
                s[b] = pp;
                s[b + 4] = pp;
                s[b + 8] = pp;
                cnt = b + 9;
            } else {
                // 2. does an edge need refinement?
                let mut j = 0;
                while j < 3 {
                    let d1 = dist2(s[b + j], e);
                    let d2 = dist2(s[b + INC_MOD3[j]], e);
                    if (d1 > rmin || d2 > rmin) && (!(d1 >= d2 * lo) || d1 > d2 * hi) {
                        break;
                    }
                    j += 1;
                }
                if j < 3 && b <= SUBDIV_MAX {
                    let (a, bb, c) = (s[b], s[b + 1], s[b + 2]);
                    s[b + 3] = a;
                    s[b + 4] = mid(a.x, bb.x, bb.y, a.y, bb.z, a.z);
                    s[b + 5] = mid(c.x, a.x, c.y, a.y, c.z, a.z);
                    s[b + 6] = bb;
                    s[b + 7] = mid(bb.x, c.x, c.y, bb.y, c.z, bb.z);
                    s[b + 8] = mid(bb.x, a.x, a.y, bb.y, a.z, bb.z);
                    s[b + 9] = c;
                    s[b + 10] = mid(c.x, a.x, a.y, c.y, a.z, c.z);
                    s[b + 11] = mid(bb.x, c.x, bb.y, c.y, bb.z, c.z);
                    let (v3, v6, v9) = (s[b + 3], s[b + 6], s[b + 9]);
                    s[b] = mid(v3.x, v6.x, v6.y, v3.y, v6.z, v3.z);
                    s[b + 1] = mid(v9.x, v6.x, v9.y, v6.y, v9.z, v6.z);
                    s[b + 2] = mid(v9.x, v3.x, v3.y, v9.y, v3.z, v9.z);
                    cnt = b + 12;
                } else {
                    // 3. leaf
                    let (a, bb, c) = (s[b], s[b + 1], s[b + 2]);
                    let g = Vec3::new(
                        (c.x + (bb.x + a.x)) * third,
                        (c.y + (bb.y + a.y)) * third,
                        (c.z + (bb.z + a.z)) * third,
                    );
                    let dx = g.x - e.x;
                    let dy = g.y - e.y;
                    let dz = g.z - e.z;
                    let d2 = (dx * dx + dy * dy) + dz * dz;
                    let dot = (dy * n.y + n.x * dx) + dz * n.z;
                    let (ux, uy, uz) = (bb.x - a.x, bb.y - a.y, bb.z - a.z);
                    let (wx, wy, wz) = (c.x - a.x, c.y - a.y, c.z - a.z);
                    let cx = uy * wz - uz * wy;
                    let cy = uz * wx - wz * ux;
                    let cz = wy * ux - uy * wx;
                    let area = ((cx * cx + cy * cy) + cz * cz).sqrt();
                    let den = (((d2 - rmin2).abs() + (d2 + rmin2)) * 0.5) * d2.sqrt();
                    let mag = (((area * dot) * 0.5) * kk) / den;
                    let fy = n.y * mag;
                    let fz = n.z * mag;
                    let fx = n.x * mag;
                    pz += fz;
                    py += fy;
                    px += fx;
                    let rx = g.x - cm.x;
                    let ry = g.y - cm.y;
                    let rz = g.z - cm.z;
                    ly += rz * fx - fz * rx;
                    lz += fy * rx - ry * fx;
                    lx += ry * fz - rz * fy;
                    cnt = b;
                }
            }
            if cnt == 0 {
                break;
            }
            iter += 1;
            if iter >= MAX_ITERS {
                break;
            }
        }
    }
    add_world(r, Vec3::new(px, py, pz), p_out);
    add_world(r, Vec3::new(lx, ly, lz), l_out);
}
