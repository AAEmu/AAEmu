//! Slot 43 `GetRandomPos(PosNorm&, CRndGen&, EGeomForm)` and its random source.
//!
//! ## The random source
//!
//! The geometry code calls `CryRandomFloat` / `CryRandom`, which are **imports from
//! `xlcommon.dll`** (IAT 0x351ED360 / 0x351ED358), not engine code. In xlcommon they are a
//! per-thread MT19937 (TLS instance created on first use and seeded with `_time64(0)`):
//!
//! * `CryRandomSeed(s)` (0x3301AEA0 → 0x3301A940): the standard `init_genrand` (`mt[0] = s`,
//!   `mt[i] = 0x6C078965·(mt[i−1] ^ (mt[i−1] >> 30)) + i`), index 624;
//! * the twist 0x3301AC20 and the standard tempering (`>> 11`, `<< 7 & 0x9D2C5680`,
//!   `<< 15 & 0xEFC60000`, `>> 18`) — `CryRandom` (0x3301ADF0) returns the tempered word;
//! * `CryRandomFloat` (0x3301AE70 → 0x3301B030): `(float)(u64)word · 2⁻³²` (`cvtsi2ss` of the
//!   zero-extended word, round to nearest, so the result can be exactly 1.0).
//!
//! [`CryRandom`] ports that generator with its state as an explicit value; the caller owns it
//! (the engine's is thread-local and time-seeded). The third argument of slot 43 (a generator
//! reference in later engine versions) is ignored by every primitive.
//!
//! ## Quirks kept
//!
//! * The base (box, ray) and the cylinder rotate the local sample by `Basis` itself, not
//!   `Basisᵀ` (the box/tree basis rows are the local axes, so the correct transform would be
//!   `Basisᵀ·p`): for an oriented box or a cylinder whose axis is not `z` the points land in
//!   the wrong place. The position is then offset by the centre, the normal only rotated.
//! * The capsule uses the cylinder's function, so its end caps are flat discs at `±hh`.
//! * The sphere's "volume" form scales the rejection sample by `r` (uniform in the ball), but
//!   its normal is the normalised sample; forms 0, 1 and anything outside 2..3 give the centre
//!   with a zero normal.

use crate::geometry::primitives::BoxPrim;
use crate::geometry::shapes::cylinder::CylinderData;
use crate::geometry::shapes::SphereGeom;
use crate::math::{crt, Vec3};

/// `PosNorm`: a position and a normal (24 bytes).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct PosNorm {
    pub pt: Vec3,
    pub n: Vec3,
}

/// xlcommon's MT19937 behind `CryRandom` / `CryRandomFloat` / `CryRandomSeed` (see the
/// module docs).
#[derive(Clone)]
pub struct CryRandom {
    mt: [u32; 624],
    index: i32,
}

impl std::fmt::Debug for CryRandom {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("CryRandom")
            .field("index", &self.index)
            .finish_non_exhaustive()
    }
}

impl CryRandom {
    /// A generator after `CryRandomSeed(seed)`.
    #[must_use]
    pub fn new(seed: u32) -> Self {
        let mut r = Self {
            mt: [0; 624],
            index: 0,
        };
        r.seed(seed);
        r
    }

    /// `CryRandomSeed` (0x3301A940): `init_genrand(seed)`, index 624.
    pub fn seed(&mut self, seed: u32) {
        self.mt[0] = seed;
        for i in 1..624 {
            let p = self.mt[i - 1];
            self.mt[i] = (p ^ (p >> 30))
                .wrapping_mul(0x6C07_8965)
                .wrapping_add(i as u32);
        }
        self.index = 624;
    }

    /// The twist (0x3301AC20); resets the index to 0.
    fn twist(&mut self) {
        let mt = &mut self.mt;
        let step = |a: u32, b: u32, m: u32| -> u32 {
            let y = ((a ^ b) & 0x7FFF_FFFE) ^ a;
            let mag = if b & 1 != 0 { 0x9908_B0DF } else { 0 };
            (y >> 1) ^ mag ^ m
        };
        for i in 0..227 {
            mt[i] = step(mt[i], mt[i + 1], mt[i + 397]);
        }
        for i in 227..623 {
            mt[i] = step(mt[i], mt[i + 1], mt[i - 227]);
        }
        mt[623] = step(mt[623], mt[0], mt[396]);
        self.index = 0;
    }

    /// The next tempered word (`CryRandom`, 0x3301ADF0).
    pub fn next_u32(&mut self) -> u32 {
        if self.index >= 624 {
            self.twist();
        }
        let mut y = self.mt[self.index as usize];
        self.index += 1;
        y ^= y >> 11;
        y ^= (y & 0xFF3A_58AD) << 7;
        y ^= (y & 0xFFFF_DF8C) << 15;
        y ^ (y >> 18)
    }

    /// `CryRandomFloat` (0x3301B030): `(float)word · 2⁻³²`, in `[0, 1]`.
    pub fn next_f32(&mut self) -> f32 {
        self.next_u32() as f32 * f32::from_bits(0x2F80_0000)
    }
}

/// `2π` as the cylinder code's float constant (0x40C90FDB).
const TWO_PI_F32: f32 = f32::from_bits(0x40C9_0FDB);

/// `sub_35153C30`: a random point of the axis-aligned box `±size` and its normal, by form
/// (0 vertex, 1 edge, 2 surface, 3 volume, anything else: a volume point with a vertex
/// normal). Draw order: z, y, x of the volume point, then (forms ≠ 3) one `CryRandom` word for
/// the vertex signs (bits 29/30/31 → x/y/z) and, for forms 1/2, one more float to pick the
/// edge/face, weighted by length/area. The normal is normalised with `+FLT_MIN` under the root.
pub fn box_random_pos(size: Vec3, form: i32, rng: &mut CryRandom) -> PosNorm {
    let (sx, sy, sz) = (size.x, size.y, size.z);
    let (nx, ny, nz) = (-sx, -sy, -sz);
    let z = rng.next_f32() * (sz - nz) + nz;
    let y = rng.next_f32() * (sy - ny) + ny;
    let x = rng.next_f32() * (sx - nx) + nx;
    let mut pt = Vec3::new(x, y, z);
    let mut n = pt;
    if form != 3 {
        let w = rng.next_u32() >> 29;
        n.x = (2 * (w & 1) as i32 - 1) as f32 * sx;
        n.y = ((w & 2) as i32 - 1) as f32 * sy;
        n.z = ((((w as i32) >> 1) & 2) - 1) as f32 * sz;
        match form {
            0 => pt = n,
            2 => {
                let t = (sz + sx) * sy + sz * sx;
                let r = rng.next_f32() * t - sz * sy;
                if !(r >= 0.0) {
                    pt.x = n.x;
                    n.y = 0.0;
                    n.z = 0.0;
                } else if !(r - sz * sx >= 0.0) {
                    pt.y = n.y;
                    n.x = 0.0;
                    n.z = 0.0;
                } else {
                    pt.z = n.z;
                    n.x = 0.0;
                    n.y = 0.0;
                }
            }
            1 => {
                let t = (sy + sx) + sz;
                let r = rng.next_f32() * t - sx;
                if !(r >= 0.0) {
                    pt.y = n.y;
                    pt.z = n.z;
                    n.x = 0.0;
                } else {
                    let r = r - sy;
                    pt.x = n.x;
                    if !(r >= 0.0) {
                        pt.z = n.z;
                        n.y = 0.0;
                    } else {
                        pt.y = n.y;
                        n.z = 0.0;
                    }
                }
            }
            _ => {}
        }
    }
    let inv = 1.0 / (((n.x * n.x + n.y * n.y) + n.z * n.z) + f32::MIN_POSITIVE).sqrt();
    PosNorm {
        pt,
        n: Vec3::new(n.x * inv, n.y * inv, n.z * inv),
    }
}

/// `CGeometry` slot 43 (0x35151A10), used by the box and the ray: [`box_random_pos`] on the
/// half-sizes of the slot-6 bounding box, rotated by its `Basis` when `bOriented` (the quirk in
/// the module docs) and offset by its centre.
pub fn base_get_random_pos(bbox: &BoxPrim, out: &mut PosNorm, rng: &mut CryRandom, form: i32) {
    let mut r = box_random_pos(bbox.size, form, rng);
    if bbox.oriented != 0 {
        r.pt = bbox.basis.mul_vec(r.pt);
        r.n = bbox.basis.mul_vec(r.n);
    }
    r.pt = Vec3::new(
        bbox.center.x + r.pt.x,
        bbox.center.y + r.pt.y,
        bbox.center.z + r.pt.z,
    );
    *out = r;
}

/// Sphere slot 43 (0x351AF140). Forms 2 (surface) and 3 (volume) draw `z, y, x` in
/// `[-1, 1]` (`rf·2 − 1`) until `(x² + y²) + z² ≤ 1` (a NaN sum also stops); the normal is the
/// sample normalised with `+FLT_MIN`; the point is `n·r` (form 2) or `r·sample` (form 3) plus
/// the centre. Any other form: the centre and a zero normal, no draws.
pub fn sphere_get_random_pos(g: &SphereGeom, out: &mut PosNorm, rng: &mut CryRandom, form: i32) {
    let c = g.prim.center;
    if !(2..=3).contains(&form) {
        *out = PosNorm {
            pt: c,
            n: Vec3::ZERO,
        };
        return;
    }
    let (mut x, mut y, mut z);
    loop {
        z = rng.next_f32() * 2.0 - 1.0;
        y = rng.next_f32() * 2.0 - 1.0;
        x = rng.next_f32() * 2.0 - 1.0;
        if !((x * x + y * y) + z * z > 1.0) {
            break;
        }
    }
    let inv = 1.0 / (((x * x + y * y) + z * z) + f32::MIN_POSITIVE).sqrt();
    let n = Vec3::new(x * inv, y * inv, z * inv);
    let r = g.prim.r;
    let p = if form == 2 {
        Vec3::new(n.x * r, n.y * r, n.z * r)
    } else {
        Vec3::new(r * x, r * y, r * z)
    };
    *out = PosNorm {
        pt: Vec3::new(c.x + p.x, c.y + p.y, c.z + p.z),
        n,
    };
}

/// `sub_350210A0`: `v·(1/sqrtss(|v|²))`, or `(0,0,1)` when `|v|² ≤ 1e-20` (or NaN).
fn normalize_or_z(v: Vec3) -> Vec3 {
    let l2 = (v.x * v.x + v.y * v.y) + v.z * v.z;
    if l2 > 1e-20 {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(v.x * inv, inv * v.y, inv * v.z)
    } else {
        Vec3::Z
    }
}

/// `sub_35148930`: `rf·(b − a) + a`.
fn rand_range(rng: &mut CryRandom, a: f32, b: f32) -> f32 {
    rng.next_f32() * (b - a) + a
}

/// Cylinder slot 43 (0x35145120), also the capsule's. Local frame: axis `z`, radius `r`,
/// half-height `hh`; angles are `rf·2π` (float) through MSVCR100 `sinf`/`cosf`
/// ([`crt::sinf`]/[`crt::cosf`]); disc radii are `sqrtss(rf)·r`.
///
/// * form 3 (volume): disc point at `z = rand(−hh, hh)`, normal = normalised point;
/// * form 2 (surface): `rf·(2hh + r) ≥ r` → side (`n = (cos, sin, 0)`, `pt = n·r`,
///   `z = rand(−hh, hh)`), else a cap (`CryRandom() & 1` picks `n.z = ±1`, disc point,
///   `z = n.z·hh`);
/// * anything else: a point on one of the two rims (`CryRandom() & 1` picks the end), normal =
///   normalised point.
///
/// Then rotated by the tree box `Basis` (quirk: not transposed) when `bOriented`, and offset by
/// the tree box centre.
pub fn cylinder_get_random_pos(
    d: &CylinderData,
    out: &mut PosNorm,
    rng: &mut CryRandom,
    form: i32,
) {
    let (r, hh) = (d.prim.r, d.prim.hh);
    let mut o = PosNorm::default();
    if form == 3 {
        let a = rng.next_f32() * TWO_PI_F32;
        let s = crt::sinf(a);
        let t = rng.next_f32().sqrt() * r;
        o.pt.x = crt::cosf(a) * t;
        o.pt.y = s * t;
        o.pt.z = rand_range(rng, -hh, hh);
        o.n = normalize_or_z(o.pt);
    } else if form == 2 {
        let t = hh * 2.0 + r;
        if rng.next_f32() * t >= r {
            let a = rng.next_f32() * TWO_PI_F32;
            let s = crt::sinf(a);
            let c = crt::cosf(a);
            o.n = Vec3::new(c, s, 0.0);
            o.pt = Vec3::new(o.n.x * r, o.n.y * r, o.n.z * r);
            o.pt.z = rand_range(rng, -hh, hh);
        } else {
            let u = rng.next_u32() & 1;
            o.n = Vec3::new(0.0, 0.0, u as f32 * 2.0 - 1.0);
            let a = rng.next_f32() * TWO_PI_F32;
            let s = crt::sinf(a);
            let t = rng.next_f32().sqrt() * r;
            o.pt.x = crt::cosf(a) * t;
            o.pt.y = s * t;
            o.pt.z = o.n.z * hh;
        }
    } else {
        let a = rng.next_f32() * TWO_PI_F32;
        let s = crt::sinf(a);
        let c = crt::cosf(a);
        o.pt.y = s * r;
        o.pt.x = c * r;
        let u = rng.next_u32() & 1;
        o.pt.z = (u as f32 * 2.0) * hh - hh;
        o.n = normalize_or_z(o.pt);
    }
    let bx = &d.bv.box_;
    if bx.oriented != 0 {
        o.pt = bx.basis.mul_vec(o.pt);
        o.n = bx.basis.mul_vec(o.n);
    }
    o.pt = Vec3::new(
        bx.center.x + o.pt.x,
        bx.center.y + o.pt.y,
        bx.center.z + o.pt.z,
    );
    *out = o;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mt19937_reference() {
        // the standard MT19937 check value for seed 5489
        let mut r = CryRandom::new(5489);
        assert_eq!(r.next_u32(), 3_499_211_612);
        let mut r = CryRandom::new(5489);
        for _ in 0..9999 {
            r.next_u32();
        }
        assert_eq!(r.next_u32(), 4_123_659_995);
    }

    #[test]
    fn float_can_reach_one() {
        assert_eq!(u32::MAX as f32 * f32::from_bits(0x2F80_0000), 1.0);
    }

    #[test]
    fn box_forms_stay_on_the_box() {
        let mut rng = CryRandom::new(7);
        let s = Vec3::new(1.0, 2.0, 3.0);
        for _ in 0..200 {
            let v = box_random_pos(s, 0, &mut rng);
            assert_eq!(v.pt.abs(), s);
            let f = box_random_pos(s, 2, &mut rng);
            let on = (f.pt.x.abs() == 1.0) as u32
                + (f.pt.y.abs() == 2.0) as u32
                + (f.pt.z.abs() == 3.0) as u32;
            assert!(on >= 1);
            assert!((f.n.len() - 1.0).abs() < 1e-6);
        }
    }
}
