//! `CTriMesh` bounding box, mass properties, volume / centre / extent caches, features,
//! per-triangle accessors and the small vertex-array maintenance slots.
//!
//! Every routine is a bit-exact port of the x64 `cryphysics_dedicate.dll` 10.0.2.13:
//!
//! | slot | address | method |
//! |---|---|---|
//! | 6 `GetBBox` | 0x35103340 | [`TriMesh::get_bbox`] |
//! | 7 `CalcPhysicalProperties` | 0x351DA290 | [`TriMesh::calc_physical_properties`] (+ [`mass_integrals`] 0x3512CE40, [`face_integrals`] 0x3512CA60, [`projection_integrals`] 0x3512BB90, [`jacobi_eigen3`] 0x35122FA0) |
//! | 16 `GetPrimitiveMatId` | 0x350FE1B0 | [`TriMesh::get_primitive_mat_id`] |
//! | 17 `GetPrimitive` | 0x3514DB60 | [`TriMesh::get_primitive`] |
//! | 18 `GetForeignIdx` | 0x350FE1D0 | [`TriMesh::get_foreign_idx`] |
//! | 19 `GetNormal` | 0x350FE1F0 | [`TriMesh::get_normal`] |
//! | 20 `GetFeature` | 0x351BE700 | [`TriMesh::get_feature`] |
//! | 28 `GetPrimitiveCount` | 0x350FE220 | [`TriMesh::get_primitive_count`] |
//! | 31 `GetVolume` | 0x351DB0D0 | [`TriMesh::get_volume`] |
//! | 32 `GetCenter` | 0x351BE500 | [`TriMesh::get_center`] |
//! | 34 (error count, +588) | 0x350FE230 | [`TriMesh::get_error_count`] |
//! | 35 `GetForeignData` | 0x350FE2B0 | [`TriMesh::get_foreign_data`] |
//! | 37 `SetForeignData` | 0x350FE2D0 | [`TriMesh::set_foreign_data`] |
//! | 38 (bad-edge count, +540) | 0x350FE2A0 | [`TriMesh::get_bad_edge_count`] |
//! | 39 `DestroyAuxData` | 0x350FE300 | [`TriMesh::destroy_aux_data`] |
//! | 40 `RemapForeignIdx` | 0x351CD640 | [`TriMesh::remap_foreign_idx`] |
//! | 41 `AppendVertices` | 0x351CDD20 (+ 0x351BF300) | [`TriMesh::append_vertices`] |
//! | 42 `GetExtent` | 0x351C2040 | [`TriMesh::get_extent`] |
//! | 43 `GetRandomPos` | 0x351D3460 (+ 0x351DB5A0) | [`TriMesh::get_random_pos`] with [`CryRandom`] (xlcommon MT19937) |
//! | 45 `CompactVertices` | 0x351BF180 | [`TriMesh::compact_vertices`] |
//! | 50 `GetTriMesh` | 0x350FE240 (+ copy 0x351BF5A0) | [`TriMesh::get_tri_mesh`] |
//!
//! Caches: `+516` volume, `+520` area, `+524` edge length, `+528` centre hold the unused
//! sentinel 0xFFBFFFFF until computed; "unused" is tested as `(bits & 0xFFA00000) == 0xFFA00000`
//! (so any NaN with those bits set counts as not computed). The getters that fill a cache take
//! `&mut self`, as the original writes it on read.
//!
//! Vertex access: the original reads vertices through the strided pointer `+80` / stride `+88`;
//! the port keeps them as a packed `Vec<Vec3>` (stride 12, the only stride the engine ever
//! creates for a mesh it owns).

use super::TriMesh;
use crate::geometry::primitives::{BoxPrim, Triangle};
use crate::geometry::PhysProperties;
use crate::math::{Mat33, Quat, Vec3};
use crate::sentinel::RawPtr;

/// The engine's "unused" test on a float word: `(bits & 0xFFA00000) == 0xFFA00000`.
#[inline]
#[must_use]
pub fn is_unused_bits(x: f32) -> bool {
    x.to_bits() & 0xFFA0_0000 == 0xFFA0_0000
}

/// `inc_mod3` (0x35209A10 / 0x3520A770) followed by the bytes the binary has behind it: the
/// feature routine indexes it with `iFeature & 0x1F`, so codes above 2 read on into the
/// neighbouring table and padding (words 28.. are relocated pointers in the binary, only the
/// low 28 entries are reproduced).
const INC_MOD3_EXT: [i32; 28] = [
    1, 2, 0, 0, 2, 0, 1, 0, -1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
];
/// `dec_mod3` (0x35209A20 / 0x3520A780) = `INC_MOD3_EXT[4..]`.
const DEC_MOD3: [usize; 3] = [2, 0, 1];
const INC_MOD3: [usize; 3] = [1, 2, 0];

#[inline]
fn d(bits: u64) -> f64 {
    f64::from_bits(bits)
}

/// `(double)1e-20f` (0x351F1CB8): the squared-normal threshold of [`mass_integrals`].
const NORMAL_EPS2: u64 = 0x3BC7_9CA1_0000_0000;
const ONE_THIRD: u64 = 0x3FD5_5555_5555_5555;
const ONE_SIXTH: u64 = 0x3FC5_5555_5555_5555;
const ONE_TWELFTH: u64 = 0x3FB5_5555_5555_5555;
const ONE_TWENTIETH: u64 = 0x3FA9_9999_9999_999A;
const ONE_24TH: u64 = 0x3FA5_5555_5555_5555;
const ONE_60TH: u64 = 0x3F91_1111_1111_1111;
/// Jacobi: `1e-20` (0x351EF338), `1e-6` (0x351EDAA0), `1e20` (0x351EF6F0).
const JACOBI_BUMP: u64 = 0x3BC7_9CA1_0C92_4223;
const JACOBI_ZERO: u64 = 0x3EB0_C6F7_A0B5_ED8D;
const JACOBI_HUGE: u64 = 0x4415_AF1D_78B5_8C40;
/// `0.16666667f` (0x351F2D1C).
const SIXTH_F32: u32 = 0x3E2A_AAAB;
/// Volume written by `CalcPhysicalProperties` for meshes with flag 0x40000000 (≈ −1e10).
pub const FORCED_VOLUME_BITS: u32 = 0xD015_02F9;

/// Projection integrals of one triangle, `sub_3512BB90` (Mirtich's `P1, Pa, Paa, Paaa, Pb,
/// Pbb, Pbbb, Pab, Paab, Pabb` in the binary's storage order). `v` holds the triangle in the
/// permuted frame `(α, β, γ)` with the first vertex repeated (`v[3] = v[0]`).
#[must_use]
pub fn projection_integrals(v: &[[f64; 3]; 4]) -> [f64; 10] {
    let mut p = [0.0f64; 10];
    for e in 0..3 {
        let a0 = v[e][0];
        let b0 = v[e][1];
        let a1 = v[e + 1][0];
        let b1 = v[e + 1][1];
        let a1_2 = a1 * a1;
        let a0_2 = a0 * a0;
        let a1_3 = a1 * a1_2;
        let a0_3 = a0 * a0_2;
        let a1_3x4 = a1_3 * 4.0;
        let a0_4 = a0 * a0_3;
        let b0_2 = b0 * b0;
        let c_a = (a1 * a0 + a1_2) + a0_2;
        let b0_3 = b0 * b0_2;
        let b1_2 = b1 * b1;
        let b0_4 = b0 * b0_3;
        let b1_3 = b1 * b1_2;
        let c_a2 = c_a * a1 + a0_3;
        let c_a3 = c_a2 * a1 + a0_4;
        let da = a1 - a0;
        let c_b = (b1 * b0 + b1_2) + b0_2;
        let c_b2 = c_b * b1 + b0_3;
        let c_b3 = c_b2 * b1 + b0_4;
        let db = b1 - b0;
        let k_a = ((a1 * 2.0) * a0 + a1_2 * 3.0) + a0_2;
        let v47 = k_a * a0 + a1_3x4;
        let v48 = (((b1_2 * 3.0) * b0 + b1_3 * 4.0) + (b1 * 2.0) * b0_2) + b0_3;
        let v26 = ((a0 * 2.0) * a1 + a0_2 * 3.0) + a1_2;
        let v27 = v26 * a1 + a0_3 * 4.0;
        let v28 = ((((b0_2 * 3.0) * b1 + b0_3 * 4.0) + (b0 * 2.0) * b1_2) + b1_3) * a0;
        p[0] += (a1 + a0) * db;
        let v29 = (v47 * b1 + v27 * b0) * db;
        p[2] += c_a2 * db;
        p[3] += c_a3 * db;
        p[1] += db * c_a;
        p[5] += c_b2 * da;
        p[6] += c_b3 * da;
        p[4] += da * c_b;
        let v33 = (k_a * b1 + v26 * b0) * db;
        p[8] += v29;
        p[9] += (v48 * a1 + v28) * da;
        p[7] += v33;
    }
    p[8] *= d(ONE_60TH);
    p[0] *= 0.5;
    p[1] *= d(ONE_SIXTH);
    p[7] *= d(ONE_24TH);
    p[2] *= d(ONE_TWELFTH);
    p[3] *= d(ONE_TWENTIETH);
    p[9] *= -d(ONE_60TH);
    p[4] *= -d(ONE_SIXTH);
    p[5] *= -d(ONE_TWELFTH);
    p[6] *= -d(ONE_TWENTIETH);
    p
}

/// Face integrals of one triangle, `sub_3512CA60` (Mirtich's `Fa, Fb, Fc, Faa, Fbb, Fcc,
/// Faaa, Fbbb, Fccc, Faab, Fbbc, Fcca`). `v` as for [`projection_integrals`], `n` the unit
/// normal in the same permuted frame.
#[must_use]
pub fn face_integrals(v: &[[f64; 3]; 4], n: [f64; 3]) -> [f64; 12] {
    let p = projection_integrals(v);
    let (na, nb, nc) = (n[0], n[1], n[2]);
    let k1 = 1.0 / nc;
    let k2 = k1 * k1;
    let k3 = k2 * k1;
    let k4 = k3 * k1;
    let w = -((v[0][1] * nb + v[0][0] * na) + nc * v[0][2]);
    let mut f = [0.0f64; 12];
    f[3] = p[2] * k1;
    f[1] = p[4] * k1;
    f[0] = p[1] * k1;
    f[6] = p[3] * k1;
    f[4] = p[5] * k1;
    f[7] = p[6] * k1;
    f[9] = p[8] * k1;
    f[10] = -(((p[6] * nb + p[9] * na) + p[5] * w) * k2);
    let ww = w * w;
    {
        let t_a = ((na * w) * p[2] + (nb * na) * p[8]) + (nb * w) * p[7];
        let t_b = ((nb * nb) * p[9] + (na * na) * p[3]) + p[1] * ww;
        f[11] = (t_a * 2.0 + t_b) * k3;
    }
    let s = p[4] * nb + p[1] * na;
    let na2 = na * na;
    let t1 = p[2] * na2;
    let nab = na * nb;
    let na3 = na * na2;
    let p9_ab_b = (p[9] * nab) * nb;
    let p8_ab_a = (p[8] * nab) * na;
    let t2 = p[3] * na3;
    let p7_ab2 = (p[7] * nab) * 2.0;
    let nb2 = nb * nb;
    let nb3 = nb * nb2;
    let p5_b2 = p[5] * nb2;
    let p0w = p[0] * w;
    let p6_b3 = p[6] * nb3;
    f[2] = -((p0w + s) * k2);
    let sw = s * w;
    {
        let x = (s * 2.0 + p0w) * w;
        let y = (p7_ab2 + t1) + p5_b2;
        f[5] = (x + y) * k3;
    }
    {
        let x = ((((t1 + p5_b2) + p7_ab2) + sw) * 3.0 + p[0] * ww) * w;
        let y = (((p9_ab_b + p8_ab_a) * 3.0) + t2) + p6_b3;
        f[8] = -((x + y) * k4);
    }
    f
}

/// Volume, centre of mass and inertia tensor (per unit density, about the centre) of a
/// triangle soup: `sub_3512CE40`, Mirtich's divergence-theorem integrals in double precision.
///
/// Returns `(volume, centre, tensor)` with the tensor row-major. Triangles whose cross product
/// has zero or NaN squared length are skipped; the dominant-axis choice is **kept from the
/// previous triangle** when every squared normal component is NaN (the original's register is
/// not reset). The centre is divided by `2·V` only when `V > 0`.
#[must_use]
pub fn mass_integrals(
    vertices: &[Vec3],
    indices: &[i32],
    n_tris: i32,
) -> (f64, [f64; 3], [f64; 9]) {
    let mut c = [0.0f64; 3];
    let mut t = [0.0f64; 9];
    let mut vol = 0.0f64;
    let (mut sxx, mut syy, mut szz) = (0.0f64, 0.0f64, 0.0f64);
    let mut k = 0usize;
    for tri in 0..n_tris.max(0) as usize {
        let v0 = vertices[indices[3 * tri] as usize];
        let v1 = vertices[indices[3 * tri + 1] as usize];
        let v2 = vertices[indices[3 * tri + 2] as usize];
        let e2 = Vec3::new(v2.x - v0.x, v2.y - v0.y, v2.z - v0.z);
        let a = Vec3::new(v1.x - v0.x, v1.y - v0.y, v1.z - v0.z);
        let nx = a.y * e2.z - a.z * e2.y;
        let ny = a.z * e2.x - e2.z * a.x;
        let nz = e2.y * a.x - a.y * e2.x;
        let (mut nx, mut ny, mut nz) = (f64::from(nx), f64::from(ny), f64::from(nz));
        let len2 = (nx * nx + ny * ny) + nz * nz;
        // ucomisd + je: zero and NaN both skip the triangle.
        if len2 == 0.0 || len2.is_nan() {
            continue;
        }
        if len2 > d(NORMAL_EPS2) {
            let inv = 1.0 / len2.sqrt();
            ny *= inv;
            nz *= inv;
            nx *= inv;
        } else {
            nx = 0.0;
            ny = 0.0;
            nz = 1.0;
        }
        let mut best = -1.0f64;
        let nx2 = nx * nx;
        if nx2 > -1.0 {
            best = nx2;
            k = 0;
        }
        let ny2 = ny * ny;
        if ny2 > best {
            best = ny2;
            k = 1;
        }
        if nz * nz > best {
            k = 2;
        }
        let (ia, ib) = (INC_MOD3[k], DEC_MOD3[k]);
        let perm = |p: Vec3| -> [f64; 3] {
            let q = [p.x, p.y, p.z];
            [f64::from(q[ia]), f64::from(q[ib]), f64::from(q[k])]
        };
        let w0 = perm(v0);
        let verts = [w0, perm(v1), perm(v2), w0];
        let nn = [nx, ny, nz];
        let np = [nn[ia], nn[ib], nn[k]];
        let f = face_integrals(&verts, np);
        let m = k ^ ((k ^ 2) >> 1);
        let (x, y, z) = (INC_MOD3[m], DEC_MOD3[m], m);
        let (t1, t2, t3, t4) = (&f[0..3], &f[3..6], &f[6..9], &f[9..12]);
        let (nx, ny, nz) = (np[x], np[y], np[z]);
        c[0] += nx * t2[x];
        vol += nx * t1[x];
        sxx += nx * t3[x];
        c[1] += ny * t2[y];
        syy += ny * t3[y];
        szz += nz * t3[z];
        c[2] += nz * t2[z];
        t[1] += nx * t4[x];
        t[5] += ny * t4[y];
        t[2] += nz * t4[z];
    }
    if vol > 0.0 {
        let r = 1.0 / (vol * 2.0);
        c[2] *= r;
        c[0] *= r;
        c[1] *= r;
    }
    let third = d(ONE_THIRD);
    t[0] = (szz + syy) * third - (c[2] * c[2] + c[1] * c[1]) * vol;
    t[4] = (szz + sxx) * third - (c[2] * c[2] + c[0] * c[0]) * vol;
    t[8] = (syy + sxx) * third - (c[1] * c[1] + c[0] * c[0]) * vol;
    let v = (vol * c[0]) * c[1] - t[1] * 0.5;
    t[1] = v;
    t[3] = v;
    let v = (vol * c[0]) * c[2] - t[2] * 0.5;
    t[2] = v;
    t[6] = v;
    let v = (vol * c[1]) * c[2] - t[5] * 0.5;
    t[5] = v;
    t[7] = v;
    (vol, c, t)
}

/// Cyclic Jacobi eigen-decomposition of a symmetric 3×3 matrix, `sub_35122FA0` (with the
/// matrix helpers sub_3511D910 / sub_3511D9F0 inlined). `a` is consumed in place (row-major);
/// returns `(eigenvalues, eigenvectors)` with the eigenvectors as the **rows** of the second
/// matrix, cleaned the way the binary does (`|x| < 1e-6` → 0, `||x| − 1| < 1e-20` → ±1;
/// a NaN component becomes 0).
///
/// Each sweep step picks the strictly largest squared upper off-diagonal element above the
/// threshold `thr` (0 from `CalcPhysicalProperties`; raised once by 1e-20 at step `n² + 1`),
/// rotates columns, then rows, then the eigenvector rows, and zeroes `a[p][q]`; at most
/// `10·n² = 90` steps.
#[must_use]
pub fn jacobi_eigen3(a: &mut [f64; 9], mut thr: f64) -> ([f64; 3], [f64; 9]) {
    const N: usize = 3;
    let mut v = [0.0f64; 9];
    for i in 0..N {
        v[i * (N + 1)] = 1.0;
    }
    let mut iter = 0usize;
    while iter < 10 * N * N {
        let mut best = thr;
        let mut pq: Option<(usize, usize)> = None;
        for i in 0..N - 1 {
            for j in i + 1..N {
                let x = a[i * N + j] * a[i * N + j];
                if x > best {
                    best = x;
                    pq = Some((i, j));
                }
            }
        }
        let Some((p, q)) = pq else {
            break;
        };
        let theta = ((a[q * N + q] - a[p * N + p]) * 0.5) / a[p * N + q];
        // comisd |θ|, 1e20; jae: a NaN θ still rotates.
        let at = theta.abs();
        if at < d(JACOBI_HUGE) || at.is_nan() {
            let r = (theta * theta + 1.0).sqrt();
            let t = if theta > 0.0 { -theta - r } else { r - theta };
            let c = 1.0 / (t * t + 1.0).sqrt();
            let s = c * t;
            for k in 0..N {
                let akp = a[k * N + p];
                let akq = a[k * N + q];
                a[k * N + p] = akp * c - akq * s;
                a[k * N + q] = akp * s + akq * c;
            }
            for k in 0..N {
                let apk = a[p * N + k];
                let aqk = a[q * N + k];
                a[p * N + k] = apk * c - aqk * s;
                a[q * N + k] = apk * s + aqk * c;
            }
            for k in 0..N {
                let vpk = v[p * N + k];
                let vqk = v[q * N + k];
                v[p * N + k] = vpk * c - vqk * s;
                v[q * N + k] = vpk * s + vqk * c;
            }
        }
        a[p * N + q] = 0.0;
        if iter == N * N + 1 {
            thr += d(JACOBI_BUMP);
        }
        iter += 1;
    }
    for x in &mut v {
        let ax = x.abs();
        // comisd |x|, 1e-6; jae keeps: smaller or NaN → 0.
        if ax < d(JACOBI_ZERO) || ax.is_nan() {
            *x = 0.0;
        } else if (ax - 1.0).abs() < d(JACOBI_BUMP) {
            // sign from the bits of (float)x: ((bits >> 30) & ~1) + 1.
            let b = (*x as f32).to_bits() as i32;
            *x = f64::from(((b >> 30) & !1) + 1);
        }
    }
    ([a[0], a[4], a[8]], v)
}

/// The MT19937 generator behind xlcommon's `CryRandom` / `CryRandomFloat` /
/// `CryRandomSeed` (per-thread `XLRandom`, 624 words + index at `+0x9C4`). The engine seeds it
/// with `time(0)` on first use; the port takes the seed explicitly.
#[derive(Clone, Debug)]
pub struct CryRandom {
    mt: [u32; 624],
    index: usize,
}

impl CryRandom {
    /// `CryRandomSeed(seed)` (0x3301A940): Knuth's initialisation, index 624.
    #[must_use]
    pub fn new(seed: u32) -> Self {
        let mut mt = [0u32; 624];
        mt[0] = seed;
        for i in 1..624 {
            mt[i] = (mt[i - 1] ^ (mt[i - 1] >> 30))
                .wrapping_mul(0x6C07_8965)
                .wrapping_add(i as u32);
        }
        Self { mt, index: 624 }
    }

    fn twist(&mut self) {
        for i in 0..624 {
            let u = self.mt[i];
            let v = self.mt[(i + 1) % 624];
            let y = ((u ^ ((u ^ v) & 0x7FFF_FFFE)) >> 1) ^ if v & 1 != 0 { 0x9908_B0DF } else { 0 };
            self.mt[i] = y ^ self.mt[(i + 397) % 624];
        }
        self.index = 0;
    }

    /// `CryRandom()` (0x3301ADF0): the tempered next word.
    pub fn next_u32(&mut self) -> u32 {
        if self.index >= 624 {
            self.twist();
        }
        let mut y = self.mt[self.index];
        self.index += 1;
        y ^= y >> 11;
        y ^= (y & 0xFF3A_58AD) << 7;
        y ^= (y & 0xFFFF_DF8C) << 15;
        y ^ (y >> 18)
    }

    /// `CryRandomFloat()` (0x3301B030): `(float)u32 · 2⁻³²` (can round to 1.0).
    pub fn next_f32(&mut self) -> f32 {
        (self.next_u32() as f32) * f32::from_bits(0x2F80_0000)
    }
}

/// Barycentric weights of `GetRandomPos` (`sub_351DB5A0`): form 0 a random vertex, 1 a random
/// point on a random edge, 2 / 3 a random point inside the triangle. Other forms leave the
/// original's stack array uninitialised; the port returns zeros there.
pub fn random_barycentric(rng: &mut CryRandom, form: i32) -> [f32; 3] {
    let mut a = [0.0f32; 3];
    match form {
        0 => {
            let k = ((3u64 * u64::from(rng.next_u32())) >> 32) as usize;
            a[k] = 1.0;
            a[(k + 2) % 3] = 0.0;
            a[(k + 1) % 3] = 0.0;
        }
        1 => {
            let k = ((3u64 * u64::from(rng.next_u32())) >> 32) as usize;
            let f = rng.next_f32();
            a[k] = f;
            a[(k + 1) % 3] = 1.0 - f;
            a[(k + 2) % 3] = 0.0;
        }
        2 | 3 => {
            let mut lo = rng.next_f32();
            let f2 = rng.next_f32();
            let mut hi = f2;
            if lo > f2 {
                hi = lo;
                lo = f2;
            }
            a[0] = lo;
            a[2] = 1.0 - hi;
            a[1] = hi - lo;
        }
        _ => {}
    }
    a
}

impl TriMesh {
    #[inline]
    fn tri_vertex(&self, tri: usize, k: usize) -> Vec3 {
        self.vertices[self.indices[3 * tri + k] as usize]
    }

    /// Slot 6 `GetBBox` (0x35103340): the BV tree's root box (tree slot 2), under the read lock.
    ///
    /// # Panics
    /// When the mesh has no tree (the original dereferences a null pointer).
    #[must_use]
    pub fn get_bbox(&self) -> BoxPrim {
        self.tree
            .as_ref()
            .expect("CTriMesh::GetBBox on a mesh without a BV tree")
            .as_dyn()
            .bbox()
    }

    /// Slot 7 `CalcPhysicalProperties` (0x351DA290). Always returns 1 in the original.
    ///
    /// * Flag 0x100000: all zero, `q` identity (the cache is not touched).
    /// * Otherwise: [`mass_integrals`] in double, `volume = (float)V`; the cached volume
    ///   (`+516`) takes this value when it holds the unused sentinel; the tensor goes through
    ///   [`jacobi_eigen3`] (threshold 0): inertia = `(float)` eigenvalues, origin = `(float)`
    ///   centre, `q = Quat::from_mat33((float)Vᵀ)` (eigenvectors as columns).
    /// * Flag 0x40000000 (after either path): the volume becomes 0xD01502F9 (≈ −1e10); the
    ///   cache keeps the computed value.
    pub fn calc_physical_properties(&mut self) -> PhysProperties {
        let mut out = PhysProperties {
            volume: 0.0,
            inertia: Vec3::ZERO,
            q: Quat::IDENTITY,
            origin: Vec3::ZERO,
        };
        if self.flags & 0x10_0000 == 0 {
            let (vol, c, mut t) = mass_integrals(&self.vertices, &self.indices, self.n_tris);
            let v = vol as f32;
            out.volume = v;
            if is_unused_bits(self.cached_volume) {
                self.cached_volume = v;
            }
            let (eig, ev) = jacobi_eigen3(&mut t, 0.0);
            out.origin = Vec3::new(c[0] as f32, c[1] as f32, c[2] as f32);
            out.inertia = Vec3::new(eig[0] as f32, eig[1] as f32, eig[2] as f32);
            let m = Mat33::from_array([
                ev[0] as f32,
                ev[3] as f32,
                ev[6] as f32,
                ev[1] as f32,
                ev[4] as f32,
                ev[7] as f32,
                ev[2] as f32,
                ev[5] as f32,
                ev[8] as f32,
            ]);
            out.q = Quat::from_mat33(&m);
        }
        if self.flags & 0x4000_0000 != 0 {
            out.volume = f32::from_bits(FORCED_VOLUME_BITS);
        }
        out
    }

    /// Slot 31 `GetVolume` (0x351DB0D0): slot 42 with form 3.
    pub fn get_volume(&mut self) -> f32 {
        self.get_extent(3)
    }

    /// Slot 32 `GetCenter` (0x351BE500): the mean of the triangles' vertex references
    /// (`Σ v / (float)(3·nTris)`, accumulated in single precision from 0), cached at `+528`.
    /// With no triangles the cache becomes `(0, 0, 0)`.
    pub fn get_center(&mut self) -> Vec3 {
        if is_unused_bits(self.cached_center.x) {
            let mut c = Vec3::ZERO;
            for tri in 0..self.n_tris.max(0) as usize {
                for k in 0..3 {
                    let v = self.tri_vertex(tri, k);
                    c.x += v.x;
                    c.y += v.y;
                    c.z += v.z;
                }
            }
            if self.n_tris > 0 {
                let r = 1.0 / ((3 * self.n_tris) as f32);
                c = Vec3::new(r * c.x, r * c.y, r * c.z);
            }
            self.cached_center = c;
        }
        self.cached_center
    }

    /// Slot 42 `GetExtent(form)` (0x351C2040): 0 → `(float)nVertices`; 1 → half the summed
    /// triangle perimeters (cache `+524`); 2 → the surface area (cache `+520`); 3 → the signed
    /// volume `Σ ((v2−v0).y·(v1−v0).x − (v1−v0).y·(v2−v0).x)·((v1.z+v0.z)+v2.z) · 0.16666667f`
    /// (cache `+516`); any other form → `(float)nVertices`. All single precision.
    pub fn get_extent(&mut self, form: i32) -> f32 {
        match form {
            1 => {
                if is_unused_bits(self.cached_extent[1]) {
                    let mut acc = 0.0f32;
                    for tri in 0..self.n_tris.max(0) as usize {
                        let (v0, v1, v2) = (
                            self.tri_vertex(tri, 0),
                            self.tri_vertex(tri, 1),
                            self.tri_vertex(tri, 2),
                        );
                        let e01 = edge_len(v1, v0);
                        let e20 = edge_len(v0, v2);
                        let e12 = edge_len(v2, v1);
                        acc = ((acc + e01) + e12) + e20;
                    }
                    self.cached_extent[1] = acc * 0.5;
                }
                self.cached_extent[1]
            }
            2 => {
                if is_unused_bits(self.cached_extent[0]) {
                    let mut acc = 0.0f32;
                    for tri in 0..self.n_tris.max(0) as usize {
                        let (v0, v1, v2) = (
                            self.tri_vertex(tri, 0),
                            self.tri_vertex(tri, 1),
                            self.tri_vertex(tri, 2),
                        );
                        let a = v2.y - v0.y;
                        let b = v1.z - v0.z;
                        let c = v1.y - v0.y;
                        let dd = v2.x - v0.x;
                        let e = v1.x - v0.x;
                        let f = v2.z - v0.z;
                        let x = c * f - b * a;
                        let y = b * dd - f * e;
                        let z = a * e - c * dd;
                        acc += ((y * y) + (x * x) + (z * z)).sqrt();
                    }
                    self.cached_extent[0] = acc * 0.5;
                }
                self.cached_extent[0]
            }
            3 => {
                if is_unused_bits(self.cached_volume) {
                    let mut acc = 0.0f32;
                    for tri in 0..self.n_tris.max(0) as usize {
                        let (v0, v1, v2) = (
                            self.tri_vertex(tri, 0),
                            self.tri_vertex(tri, 1),
                            self.tri_vertex(tri, 2),
                        );
                        let ex = v1.x - v0.x;
                        let fy = v2.y - v0.y;
                        let fx = v2.x - v0.x;
                        let ey = v1.y - v0.y;
                        let h = (v1.z + v0.z) + v2.z;
                        acc += (fy * ex - ey * fx) * h;
                    }
                    self.cached_volume = acc * f32::from_bits(SIXTH_F32);
                }
                self.cached_volume
            }
            _ => self.n_verts as f32,
        }
    }

    /// Slot 20 `GetFeature(iPrim, iFeature, pts)` (0x351BE700): the vertices of a feature of
    /// triangle `i_prim`. `iFeature` is truncated to a byte; bits `0x60` select vertex (0, one
    /// point), edge (0x20, two) or face (0x40, three); bits `0..4` give the starting corner `j`:
    /// `pts[0] = vtx[j]`, `pts[1] = vtx[inc(j)]`, `pts[2] = vtx[dec(j)]`. Code 0x60 returns 0.
    ///
    /// The corner index is not range-checked by the original (`j > 2` reads the next
    /// triangles' indices, and `inc`/`dec` read past their 3-entry tables); the port reproduces
    /// the table bytes for `j < 24` and panics on an out-of-range index.
    #[must_use]
    pub fn get_feature(&self, i_prim: i32, i_feature: i32) -> (i32, [Vec3; 3]) {
        let code = i_feature as u8;
        let j = (code & 0x1F) as i64;
        let base = 3 * i64::from(i_prim);
        let vtx =
            |slot: i64| -> Vec3 { self.vertices[self.indices[(base + slot) as usize] as usize] };
        let mut pts = [Vec3::ZERO; 3];
        match code & 0x60 {
            0 => {
                pts[0] = vtx(j);
                (1, pts)
            }
            0x20 => {
                pts[1] = vtx(i64::from(INC_MOD3_EXT[j as usize]));
                pts[0] = vtx(j);
                (2, pts)
            }
            0x40 => {
                pts[2] = vtx(i64::from(INC_MOD3_EXT[4 + j as usize]));
                pts[1] = vtx(i64::from(INC_MOD3_EXT[j as usize]));
                pts[0] = vtx(j);
                (3, pts)
            }
            _ => (0, pts),
        }
    }

    /// Slot 17 `GetPrimitive(iPrim)` (0x3514DB60): the triangle (vertices + stored normal);
    /// `None` (size 0) when `(unsigned)iPrim >= nTris` (negative indices included).
    #[must_use]
    pub fn get_primitive(&self, i_prim: i32) -> Option<Triangle> {
        if (i_prim as u32) >= (self.n_tris as u32) {
            return None;
        }
        let t = i_prim as usize;
        Some(Triangle {
            pt: [
                self.tri_vertex(t, 0),
                self.tri_vertex(t, 1),
                self.tri_vertex(t, 2),
            ],
            n: self.normals[t],
        })
    }

    /// Slot 16 `GetPrimitiveMatId(iPrim)` (0x350FE1B0): `pMats[iPrim]` sign-extended, −1 without
    /// materials. Not range-checked by the original (the port panics out of range).
    #[must_use]
    pub fn get_primitive_mat_id(&self, i_prim: i32) -> i32 {
        match &self.mats {
            Some(m) => i32::from(m[i_prim as usize]),
            None => -1,
        }
    }

    /// Slot 18 `GetForeignIdx(iPrim)` (0x350FE1D0): `pForeignIdx[iPrim]`, −1 without the array.
    #[must_use]
    pub fn get_foreign_idx(&self, i_prim: i32) -> i32 {
        match &self.foreign_idx {
            Some(f) => f[i_prim as usize],
            None => -1,
        }
    }

    /// Slot 19 `GetNormal(iPrim)` (0x350FE1F0): the stored triangle normal.
    #[must_use]
    pub fn get_normal(&self, i_prim: i32) -> Vec3 {
        self.normals[i_prim as usize]
    }

    /// Slot 28 `GetPrimitiveCount` (0x350FE220): `nTris`.
    #[must_use]
    pub fn get_primitive_count(&self) -> i32 {
        self.n_tris
    }

    /// Slot 34 (0x350FE230): the error / invalid counter `+588`.
    #[must_use]
    pub fn get_error_count(&self) -> i32 {
        self.n_errors
    }

    /// Slot 38 (0x350FE2A0): `+540`, the open / non-manifold edge count of the last topology
    /// pass.
    #[must_use]
    pub fn get_bad_edge_count(&self) -> i32 {
        self.n_bad_edges
    }

    /// Slot 35 `GetForeignData(iForeignData)` (0x350FE2B0): id −1 → `+544`, the current id
    /// (`+40`) → `+32`, else null.
    #[must_use]
    pub fn get_foreign_data(&self, i_foreign_data: i32) -> RawPtr {
        if i_foreign_data == -1 {
            self.foreign_data_mesh
        } else if i_foreign_data == self.i_foreign_data {
            self.foreign_data
        } else {
            RawPtr::NULL
        }
    }

    /// Slot 37 `SetForeignData(data, id)` (0x350FE2D0): id −1 sets `+544` (and, for a null
    /// pointer, resets the dword `+552` to 0x8000000, a field the port does not keep); any
    /// other id sets `+32` / `+40`.
    pub fn set_foreign_data(&mut self, data: RawPtr, i_foreign_data: i32) {
        if i_foreign_data == -1 {
            self.foreign_data_mesh = data;
        } else {
            self.foreign_data = data;
            self.i_foreign_data = i_foreign_data;
        }
    }

    /// Slot 39 `DestroyAuxData(mask)` (0x350FE300): bit 1 drops the materials, bit 2 the foreign
    /// ids, bit 4 the vertex map (shared arrays — flags 4 / 8 — are only detached; the flags
    /// themselves are not changed).
    pub fn destroy_aux_data(&mut self, mask: i32) {
        if mask & 1 != 0 {
            self.mats = None;
        }
        if mask & 2 != 0 {
            self.foreign_idx = None;
        }
        if mask & 4 != 0 {
            self.vtx_map = None;
        }
    }

    /// Slot 50 `GetTriMesh(bClone)` (0x350FE240): `None` stands for "this mesh" (bClone = 0);
    /// otherwise a new mesh built by the copy constructor sub_351BF5A0 with flags 0, which copies
    /// `flags & !0xF` (the ownership bits are cleared), `nTris`, `nVertices`, the vertices (the
    /// first `nVertices`), indices, materials and foreign ids (when present), topology, normals,
    /// vertex map (when present), max valency, the IsConvex cache, `+512`, `+16`, `+20`,
    /// `+24`, the foreign data `+32`/`+40`, `+588`, the BV tree (saved and reloaded — a field
    /// copy here) and the islands with their triangle links when the link array exists. Fields
    /// left at their constructor values: the aux array `+160`, the volume / extent / centre
    /// caches, the ray grid, `+540`, `+544`, `+600`.
    #[must_use]
    pub fn get_tri_mesh(&self, clone: bool) -> Option<TriMesh> {
        if !clone {
            return None;
        }
        let mut m = TriMesh {
            flags: self.flags & 0xFFFF_FFF0,
            n_tris: self.n_tris,
            n_verts: self.n_verts,
            vertices: self.vertices[..self.n_verts.max(0) as usize].to_vec(),
            indices: self.indices[..3 * self.n_tris.max(0) as usize].to_vec(),
            mats: self.mats.clone(),
            foreign_idx: self.foreign_idx.clone(),
            topology: self.topology.clone(),
            normals: self.normals.clone(),
            vtx_map: self.vtx_map.clone(),
            max_vertex_valency: self.max_vertex_valency,
            convex_cache: self.convex_cache,
            convex_tol: self.convex_tol,
            multi_island: self.multi_island,
            min_vtx_dist: self.min_vtx_dist,
            priority: self.priority,
            is_convex: self.is_convex,
            foreign_data: self.foreign_data,
            i_foreign_data: self.i_foreign_data,
            n_errors: self.n_errors,
            tree: self.tree.clone(),
            ..TriMesh::default()
        };
        if self.tri_island.is_some() {
            if let Some(isl) = &self.islands {
                if !isl.is_empty() {
                    m.islands = Some(isl.clone());
                    m.tri_island = self.tri_island.clone();
                }
            }
        }
        Some(m)
    }

    /// Slot 45 `CompactVertices` (0x351BF180): unless the vertices are shared (flag 1), numbers
    /// the referenced vertices in order of first reference; when that drops any, the vertex
    /// array is rebuilt in the new order, the indices remapped, the vertex map dropped,
    /// flag 0x1000000 set and `+600` zeroed.
    pub fn compact_vertices(&mut self) {
        if self.flags & 1 != 0 {
            return;
        }
        let nv = self.n_verts.max(0) as usize;
        let mut map = vec![-1i32; nv];
        let mut n = 0i32;
        let ni = 3 * self.n_tris.max(0) as usize;
        for &i in &self.indices[..ni] {
            if map[i as usize] == -1 {
                map[i as usize] = n;
                n += 1;
            }
        }
        if n < self.n_verts {
            let mut nvtx = vec![Vec3::ZERO; n as usize];
            for k in 0..ni {
                let i = self.indices[k] as usize;
                nvtx[map[i] as usize] = self.vertices[i];
                self.indices[k] = map[i];
            }
            self.vtx_map = None;
            self.vertices = nvtx;
            self.flags |= 0x100_0000;
            self.n_verts = n;
            self.i_vtx_min = 0;
        }
    }

    /// Slot 40 `RemapForeignIdx(pCurForeignIdx, pNewForeignIdx, nTris)` (0x351CD640): rewrites
    /// the per-triangle foreign ids through the map `cur[k] → new[k]` (later pairs win). Ids are
    /// two classes split by bit 27 (`id >> 27` = 0 or 1, local part `id & 0xF7FFFFFF`); an id
    /// keeps its value when its local part is outside `[0, max local + 1)` of its class or maps
    /// to a negative value. Nothing happens without a foreign-id array.
    ///
    /// The original also rewrites the ids held by the boolean-op update records (the `+544`
    /// chain and the `+560` thunk list); the port has no such records. Ids whose class is
    /// neither 0 nor 1 (negative ids) make the original index its stack out of bounds; the port
    /// leaves them unchanged.
    pub fn remap_foreign_idx(&mut self, cur: &[i32], new: &[i32]) {
        let Some(fid) = self.foreign_idx.as_mut() else {
            return;
        };
        let n = cur.len().min(new.len());
        let mut top = [0i32; 2];
        for &id in &cur[..n] {
            let c = (id >> 27) as usize;
            let local = id & !0x0800_0000;
            if c < 2 {
                // x -= (x - v) & ((x - v) >> 31)
                let diff = top[c].wrapping_sub(local);
                top[c] = top[c].wrapping_sub(diff & (diff >> 31));
            }
        }
        let size = [top[0].wrapping_add(1), top[1].wrapping_add(1)];
        let mut tables = [
            vec![-1i32; size[0].max(0) as usize],
            vec![-1i32; size[1].max(0) as usize],
        ];
        for k in 0..n {
            let id = cur[k];
            let c = (id >> 27) as usize;
            if c < 2 {
                tables[c][(id & !0x0800_0000) as usize] = new[k];
            }
        }
        for v in fid.iter_mut() {
            let id = *v;
            let c = id >> 27;
            if !(0..2).contains(&c) {
                continue;
            }
            let c = c as usize;
            let local = id & !0x0800_0000;
            // Branchless range test of the original (32-bit wrapping):
            // |n − 2·local − 1| < |−1 − n|  ⟺  0 ≤ local < n for sane ids.
            let n = size[c];
            let r8 = n.wrapping_sub(local.wrapping_mul(2));
            let s1 = r8.wrapping_sub(1) >> 31;
            let a1 = s1.wrapping_add(r8).wrapping_sub(1) ^ s1;
            let e = (-1i32).wrapping_sub(n);
            let s2 = e >> 31;
            let a2 = e.wrapping_add(s2) ^ s2;
            if (a1.wrapping_sub(a2) as u32) >> 31 == 1 {
                let m = tables[c][local as usize];
                if m >= 0 {
                    *v = m;
                }
            }
        }
    }

    /// Slot 41 `AppendVertices(pVtx, pVtxMap, nVtx)` (0x351CDD20): first restores the vertices
    /// that `Load` trimmed (sub_351BF300: when `+600 > 0`, `+600` slots are put in front of the
    /// array — **uninitialised** in the original, zero here — and every index and vertex-map
    /// entry is shifted by `+600`); then appends `vtx`, clears flag 1 and, when the mesh has a
    /// vertex map and `vtx_map` is given, appends the map entries (without `vtx_map` the new
    /// map entries are uninitialised in the original, zero here). No-op for an empty `vtx`.
    pub fn append_vertices(&mut self, vtx: &[Vec3], vtx_map: Option<&[i32]>) {
        if vtx.is_empty() {
            return;
        }
        if self.i_vtx_min > 0 {
            let k = self.i_vtx_min;
            let mut nv = vec![Vec3::ZERO; k as usize];
            nv.extend_from_slice(&self.vertices[..self.n_verts.max(0) as usize]);
            self.vertices = nv;
            let ni = 3 * self.n_tris.max(0) as usize;
            for i in &mut self.indices[..ni] {
                *i += k;
            }
            if let Some(map) = self.vtx_map.as_mut() {
                // Original bug: the shift loop runs over the first nVertices entries of the
                // new array (the k uninitialised slots and all but the last k copied
                // entries), so the last k copied entries stay unshifted.
                let nv = self.n_verts.max(0) as usize;
                let mut nm = vec![0i32; k as usize];
                nm.extend_from_slice(&map[..nv]);
                for v in &mut nm[..nv] {
                    *v = v.wrapping_add(k);
                }
                *map = nm;
            }
            self.n_verts += k;
            self.i_vtx_min = 0;
        }
        let nv = self.n_verts.max(0) as usize;
        self.vertices.truncate(nv);
        self.vertices.extend_from_slice(vtx);
        if let Some(map) = self.vtx_map.as_mut() {
            map.truncate(nv);
            match vtx_map {
                Some(src) => map.extend_from_slice(&src[..vtx.len()]),
                None => map.resize(nv + vtx.len(), 0),
            }
        }
        self.flags &= !1;
        self.n_verts += vtx.len() as i32;
    }

    /// Slot 43 `GetRandomPos(PosNorm&, seed, form)` (0x351D3460): a random triangle
    /// (`CryRandom() % nTris`, unsigned), then [`random_barycentric`]; returns
    /// `((v0·a0 + v1·a1) + v2·a2, normal)`.
    ///
    /// # Panics
    /// With no triangles (the original divides by zero).
    pub fn get_random_pos(&self, rng: &mut CryRandom, form: i32) -> (Vec3, Vec3) {
        let tri = (rng.next_u32() % (self.n_tris as u32)) as usize;
        let a = random_barycentric(rng, form);
        let (v0, v1, v2) = (
            self.tri_vertex(tri, 0),
            self.tri_vertex(tri, 1),
            self.tri_vertex(tri, 2),
        );
        let p = Vec3::new(
            (v0.x * a[0] + v1.x * a[1]) + v2.x * a[2],
            (v0.y * a[0] + v1.y * a[1]) + v2.y * a[2],
            (v0.z * a[0] + v1.z * a[1]) + v2.z * a[2],
        );
        (p, self.normals[tri])
    }
}

/// `sqrtss(((b−a).y² + (b−a).x²) + (b−a).z²)` as slot 42 form 1 evaluates its edges.
#[inline]
fn edge_len(b: Vec3, a: Vec3) -> f32 {
    let dy = b.y - a.y;
    let dx = b.x - a.x;
    let dz = b.z - a.z;
    ((dy * dy + dx * dx) + dz * dz).sqrt()
}
