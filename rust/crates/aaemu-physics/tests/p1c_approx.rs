//! `CreateMesh`'s primitive approximation (`geometry::trimesh::approx`: `CGeomManager` slot 6
//! 0x35098490, sub_350975D0, sub_3512D650) against the DLL.
//!
//! * Always: the `a_*` meshes of `tests/golden/p1c_mesh_gen.trace` (scenario
//!   `tests/scenarios/p1c_mesh_gen.scn`) are rebuilt bit for bit, run through
//!   [`try_primitive`], `create_primitive` and `register_geometry`, and every `geominfo` word
//!   must match.
//! * `tests/golden/p1c_approx_rand.trace`: a seeded set of randomised meshes (boxes, cylinders,
//!   spheres, capsules, rotated, scaled, noisy, with various tolerances and flags, including
//!   tolerances placed on the decision thresholds the port computes) run through slot 6 of the
//!   DLL by `tests/p1c_approx_probe.c`; the port must reproduce every returned type and every
//!   primitive word.
//!
//! | env | effect |
//! |---|---|
//! | none | the golden is replayed |
//! | `AAEMU_PHYS_P1C_PROBE_DIR` (with `p1c_approx_probe.exe`) | golden regenerated from the DLL (must match) + a larger live run (`AAEMU_PHYS_APPROX_CASES`, default 6000) |

mod p1c_common;

use aaemu_physics::geometry::primitives::Primitive;
use aaemu_physics::geometry::trimesh::approx::{
    principal_axes, try_primitive, try_primitive_traced, FitTrace, KIND_MESH,
};
use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::scenario_trace;
use aaemu_physics::world::geoman::{create_primitive, register_geometry};
use std::fmt::Write as _;
use std::path::PathBuf;

const PROBE: &str = "p1c_approx_probe";

// ------------------------------------------------------------------ generators (p1c_mesh.c)

/// The oracle's generators, as in `tests/p1c_scenarios.rs` (`gen`).
mod gen {
    pub type Mesh = (Vec<[f32; 3]>, Vec<[i32; 3]>);

    fn wrap(mut a: f64) -> f64 {
        let tau = std::f64::consts::TAU;
        while a > std::f64::consts::PI {
            a -= tau;
        }
        while a < -std::f64::consts::PI {
            a += tau;
        }
        a
    }
    pub fn sin(x: f64) -> f64 {
        let (x2, mut term, mut sum) = (x * x, x, x);
        for k in 1..=12 {
            term = term * -x2 / f64::from((2 * k) * (2 * k + 1));
            sum += term;
        }
        sum
    }
    pub fn cos(x: f64) -> f64 {
        let (x2, mut term, mut sum) = (x * x, 1.0f64, 1.0f64);
        for k in 1..=12 {
            term = term * -x2 / f64::from((2 * k - 1) * (2 * k));
            sum += term;
        }
        sum
    }
    pub fn boxm(h: [f32; 3]) -> Mesh {
        let v = (0..8)
            .map(|k| {
                [
                    if k & 1 != 0 { h[0] } else { -h[0] },
                    if k & 2 != 0 { h[1] } else { -h[1] },
                    if k & 4 != 0 { h[2] } else { -h[2] },
                ]
            })
            .collect();
        let t = vec![
            [0, 2, 3],
            [0, 3, 1],
            [4, 5, 7],
            [4, 7, 6],
            [0, 1, 5],
            [0, 5, 4],
            [2, 6, 7],
            [2, 7, 3],
            [0, 4, 6],
            [0, 6, 2],
            [1, 3, 7],
            [1, 7, 5],
        ];
        (v, t)
    }
    fn unit(v: &mut [f32; 3]) {
        let n2 = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
        let inv = 1.0f32 / n2.sqrt();
        for c in v.iter_mut() {
            *c *= inv;
        }
    }
    pub fn icosphere(r: f32, sub: i32) -> Mesh {
        #[allow(clippy::approx_constant)] // the oracle's literal
        let tt = 1.618034f32;
        let base = [
            [-1.0, tt, 0.0],
            [1.0, tt, 0.0],
            [-1.0, -tt, 0.0],
            [1.0, -tt, 0.0],
            [0.0, -1.0, tt],
            [0.0, 1.0, tt],
            [0.0, -1.0, -tt],
            [0.0, 1.0, -tt],
            [tt, 0.0, -1.0],
            [tt, 0.0, 1.0],
            [-tt, 0.0, -1.0],
            [-tt, 0.0, 1.0],
        ];
        let mut v: Vec<[f32; 3]> = base
            .iter()
            .map(|b| {
                let mut p = *b;
                unit(&mut p);
                p
            })
            .collect();
        let mut t: Vec<[i32; 3]> = vec![
            [0, 11, 5],
            [0, 5, 1],
            [0, 1, 7],
            [0, 7, 10],
            [0, 10, 11],
            [1, 5, 9],
            [5, 11, 4],
            [11, 10, 2],
            [10, 7, 6],
            [7, 1, 8],
            [3, 9, 4],
            [3, 4, 2],
            [3, 2, 6],
            [3, 6, 8],
            [3, 8, 9],
            [4, 9, 5],
            [2, 4, 11],
            [6, 2, 10],
            [8, 6, 7],
            [9, 8, 1],
        ];
        for _ in 0..sub {
            let old = std::mem::take(&mut t);
            let mut edges: Vec<(i32, i32, i32)> = Vec::new();
            for tri in &old {
                let mut mid = [0i32; 3];
                for e in 0..3 {
                    let (a, b) = (tri[e], tri[(e + 1) % 3]);
                    let (lo, hi) = (a.min(b), a.max(b));
                    let found = edges.iter().find(|x| x.0 == lo && x.1 == hi).map(|x| x.2);
                    mid[e] = found.unwrap_or_else(|| {
                        let (va, vb) = (v[a as usize], v[b as usize]);
                        let mut m = [
                            (va[0] + vb[0]) * 0.5f32,
                            (va[1] + vb[1]) * 0.5f32,
                            (va[2] + vb[2]) * 0.5f32,
                        ];
                        unit(&mut m);
                        v.push(m);
                        let id = v.len() as i32 - 1;
                        edges.push((lo, hi, id));
                        id
                    });
                }
                let [a, b, c] = *tri;
                t.push([a, mid[0], mid[2]]);
                t.push([b, mid[1], mid[0]]);
                t.push([c, mid[2], mid[1]]);
                t.push([mid[0], mid[1], mid[2]]);
            }
        }
        for p in &mut v {
            for c in p.iter_mut() {
                *c *= r;
            }
        }
        (v, t)
    }
    pub fn cylinder(r: f32, hh: f32, seg: i32) -> Mesh {
        let cs: Vec<(f32, f32)> = (0..seg)
            .map(|k| {
                let a = wrap(f64::from(k) * (std::f64::consts::TAU / f64::from(seg)));
                (cos(a) as f32, sin(a) as f32)
            })
            .collect();
        let mut v = Vec::new();
        for z in [-hh, hh] {
            for (c, s) in &cs {
                v.push([r * c, r * s, z]);
            }
        }
        v.push([0.0, 0.0, -hh]);
        v.push([0.0, 0.0, hh]);
        let (cb, ct) = (2 * seg, 2 * seg + 1);
        let mut t = Vec::new();
        for k in 0..seg {
            let k1 = (k + 1) % seg;
            t.push([k, k1, seg + k1]);
            t.push([k, seg + k1, seg + k]);
            t.push([cb, k1, k]);
            t.push([ct, seg + k, seg + k1]);
        }
        (v, t)
    }
}

fn to_input(m: &gen::Mesh) -> (Vec<Vec3>, Vec<u16>) {
    let v = m.0.iter().map(|p| Vec3::new(p[0], p[1], p[2])).collect();
    let i = m.1.iter().flatten().map(|&k| k as u16).collect();
    (v, i)
}

/// The `geominfo` line of a geometry made by the port (format of `oracle/p1c_mesh.c`).
fn geominfo_of(name: &str, prim: &Primitive) -> String {
    let g = create_primitive(prim).expect("create_primitive");
    let pg = register_geometry(g.clone(), 0, None);
    let b = g.bbox();
    let w = |f: f32| format!("{:08x}", f.to_bits());
    let mut s = format!(
        "geominfo {name} type {} nprims {} nv 0 flags 00000000 bbox",
        g.geom_type() as i32,
        g.primitive_count()
    );
    for r in &b.basis.m {
        for x in r {
            let _ = write!(s, " {}", w(*x));
        }
    }
    let _ = write!(s, " {:08x}", b.oriented as u32);
    for x in [
        b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z,
    ] {
        let _ = write!(s, " {}", w(x));
    }
    let _ = write!(s, " vol {} pg {}", w(g.volume()), w(pg.volume));
    for x in [
        pg.inertia.x,
        pg.inertia.y,
        pg.inertia.z,
        pg.q.x,
        pg.q.y,
        pg.q.z,
        pg.q.w,
        pg.origin.x,
        pg.origin.y,
        pg.origin.z,
    ] {
        let _ = write!(s, " {}", w(x));
    }
    let _ = write!(s, " {}", pg.surface_idx);
    s
}

#[test]
fn mesh_gen_goldens() {
    let t = scenario_trace("p1c_mesh_gen").expect("p1c_mesh_gen golden");
    let golden = std::fs::read_to_string(p1c_common::golden_dir().join("p1c_mesh_gen.trace"))
        .unwrap()
        .replace("\r\n", "\n");
    let line = |n: &str| {
        golden
            .lines()
            .find(|l| l.starts_with(&format!("geominfo {n} ")))
            .unwrap_or_else(|| panic!("no geominfo {n}"))
            .to_string()
    };
    let bx = gen::boxm([0.5, 0.7, 0.9]);
    let cases: Vec<(&str, gen::Mesh, u32, f32)> = vec![
        ("a_box", bx.clone(), 0x1040, 0.05),
        ("a_cyl", gen::cylinder(0.5, 1.0, 32), 0x840, 0.05),
        ("a_sph", gen::icosphere(0.5, 2), 0x2040, 0.05),
        ("a_none", bx.clone(), 0x40, 0.05),
        ("a_wrong", bx.clone(), 0x2840, 0.05),
        ("a_tol0", bx, 0x0020_3840, 0.0),
    ];
    for (name, mesh, flags, tol) in cases {
        let (v, i) = to_input(&mesh);
        let n = mesh.1.len() as i32;
        let got = try_primitive(&v, &i, None, n, flags, tol);
        let gi = t.geom_info(name).unwrap();
        match got {
            None => assert_eq!(gi.gtype, 1, "{name}: the DLL made a primitive"),
            Some(p) => assert_eq!(geominfo_of(name, &p), line(name), "{name}"),
        }
    }
}

#[test]
fn material_and_tolerance_gates() {
    let (v, i) = to_input(&gen::boxm([0.5, 0.7, 0.9]));
    let uni = [3i8; 12];
    let mut mixed = [3i8; 12];
    mixed[11] = 4;
    let f = 0x1040;
    assert!(try_primitive(&v, &i, Some(&uni), 12, f, 0.05).is_some());
    assert!(try_primitive(&v, &i, Some(&mixed), 12, f, 0.05).is_none());
    assert!(try_primitive(&v, &i, Some(&mixed), 12, f, 0.5).is_none());
    // non-uniform materials with tol > 0.5 still reach the fitter (quirk)
    assert!(try_primitive(&v, &i, Some(&mixed), 12, f, 0.51).is_some());
    assert!(try_primitive(&v, &i, None, 12, f, 0.0).is_none());
    assert!(try_primitive(&v, &i, None, 12, f, f32::NAN).is_none());
}

// ------------------------------------------------------------------ randomised cases

/// splitmix64.
struct Rng(u64);
impl Rng {
    fn next(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }
    fn f(&mut self) -> f32 {
        (self.next() >> 40) as f32 * (1.0 / 16_777_216.0)
    }
    fn range(&mut self, a: f32, b: f32) -> f32 {
        a + (b - a) * self.f()
    }
    fn below(&mut self, n: u64) -> u64 {
        self.next() % n
    }
}

struct Case {
    v: Vec<Vec3>,
    i: Vec<u16>,
    mats: Option<Vec<i8>>,
    flags: u32,
    tol: f32,
}

fn capsule(r: f32, hh: f32, seg: i32, rings: i32) -> gen::Mesh {
    // hemispheres of `rings` latitude rings + the cylindrical part, poles as single vertices
    let mut v: Vec<[f32; 3]> = Vec::new();
    let mut ringz: Vec<(f32, f32)> = Vec::new(); // (z, radius)
    for k in (1..=rings).rev() {
        let a = f64::from(k) * std::f64::consts::FRAC_PI_2 / f64::from(rings + 1);
        ringz.push((-hh - r * gen::sin(a) as f32, r * gen::cos(a) as f32));
    }
    ringz.push((-hh, r));
    ringz.push((hh, r));
    for k in 1..=rings {
        let a = f64::from(k) * std::f64::consts::FRAC_PI_2 / f64::from(rings + 1);
        ringz.push((hh + r * gen::sin(a) as f32, r * gen::cos(a) as f32));
    }
    for (z, rr) in &ringz {
        for s in 0..seg {
            let a = f64::from(s) * std::f64::consts::TAU / f64::from(seg);
            let a = if a > std::f64::consts::PI {
                a - std::f64::consts::TAU
            } else {
                a
            };
            v.push([rr * gen::cos(a) as f32, rr * gen::sin(a) as f32, *z]);
        }
    }
    let nr = ringz.len() as i32;
    let bot = v.len() as i32;
    v.push([0.0, 0.0, -hh - r]);
    v.push([0.0, 0.0, hh + r]);
    let top = bot + 1;
    let mut t = Vec::new();
    for j in 0..nr - 1 {
        for s in 0..seg {
            let s1 = (s + 1) % seg;
            let (a, b, c, d) = (
                j * seg + s,
                j * seg + s1,
                (j + 1) * seg + s1,
                (j + 1) * seg + s,
            );
            t.push([a, b, c]);
            t.push([a, c, d]);
        }
    }
    for s in 0..seg {
        let s1 = (s + 1) % seg;
        t.push([bot, s1, s]);
        t.push([top, (nr - 1) * seg + s, (nr - 1) * seg + s1]);
    }
    (v, t)
}

/// Rotation from a random unit quaternion (single precision).
fn random_rot(rng: &mut Rng) -> [[f32; 3]; 3] {
    let (mut x, mut y, mut z, mut w);
    loop {
        x = rng.range(-1.0, 1.0);
        y = rng.range(-1.0, 1.0);
        z = rng.range(-1.0, 1.0);
        w = rng.range(-1.0, 1.0);
        let n = x * x + y * y + z * z + w * w;
        if n > 0.01 && n <= 1.0 {
            let k = 1.0 / n.sqrt();
            x *= k;
            y *= k;
            z *= k;
            w *= k;
            break;
        }
    }
    [
        [
            1.0 - 2.0 * (y * y + z * z),
            2.0 * (x * y - z * w),
            2.0 * (x * z + y * w),
        ],
        [
            2.0 * (x * y + z * w),
            1.0 - 2.0 * (x * x + z * z),
            2.0 * (y * z - x * w),
        ],
        [
            2.0 * (x * z - y * w),
            2.0 * (y * z + x * w),
            1.0 - 2.0 * (x * x + y * y),
        ],
    ]
}

const FLAG_SETS: &[u32] = &[
    0x800, 0x1000, 0x2000, 0x20_0000, 0x1800, 0x20_2800, 0x20_3800, 0x3000, 0x20_1000, 0x2800,
    0x20_0800,
];

fn random_case(rng: &mut Rng) -> Case {
    let kind = rng.below(10);
    let mut mesh = match kind {
        6 => {
            // flat grid (the box pass sees a zero extent)
            let (nx, ny) = (1 + rng.below(4) as i32, 1 + rng.below(4) as i32);
            let (sx, sy) = (rng.range(0.05, 1.0), rng.range(0.05, 1.0));
            let mut v = Vec::new();
            for j in 0..=ny {
                for i in 0..=nx {
                    v.push([sx * i as f32, sy * j as f32, 0.0]);
                }
            }
            let mut t = Vec::new();
            for j in 0..ny {
                for i in 0..nx {
                    let a = j * (nx + 1) + i;
                    t.push([a, a + 1, a + nx + 2]);
                    t.push([a, a + nx + 2, a + nx + 1]);
                }
            }
            (v, t)
        }
        7 => {
            // one to three random triangles
            let n = 1 + rng.below(3) as i32;
            let v = (0..3 * n)
                .map(|_| {
                    [
                        rng.range(-1.0, 1.0),
                        rng.range(-1.0, 1.0),
                        rng.range(-1.0, 1.0),
                    ]
                })
                .collect();
            let t = (0..n).map(|k| [3 * k, 3 * k + 1, 3 * k + 2]).collect();
            (v, t)
        }
        8 => {
            // a closed mesh with some collapsed triangles (zero normals)
            let mut m = if rng.below(2) == 0 {
                gen::cylinder(
                    rng.range(0.1, 1.0),
                    rng.range(0.1, 1.0),
                    6 + rng.below(20) as i32,
                )
            } else {
                gen::boxm([
                    rng.range(0.1, 1.0),
                    rng.range(0.1, 1.0),
                    rng.range(0.1, 1.0),
                ])
            };
            let n = m.1.len();
            for _ in 0..1 + rng.below(3) {
                let k = rng.below(n as u64) as usize;
                m.1[k][2] = m.1[k][1];
            }
            m
        }
        0 => gen::boxm([
            rng.range(0.05, 2.0),
            rng.range(0.05, 2.0),
            rng.range(0.05, 2.0),
        ]),
        1 => gen::cylinder(
            rng.range(0.05, 1.5),
            rng.range(0.05, 2.0),
            3 + rng.below(40) as i32,
        ),
        2 => gen::icosphere(rng.range(0.05, 2.0), rng.below(3) as i32),
        3 => capsule(
            rng.range(0.05, 1.0),
            rng.range(0.02, 1.5),
            4 + rng.below(24) as i32,
            1 + rng.below(5) as i32,
        ),
        4 => {
            // a subdivided box: every face split into k×k quads
            let h = [
                rng.range(0.05, 2.0),
                rng.range(0.05, 2.0),
                rng.range(0.05, 2.0),
            ];
            let k = 1 + rng.below(4) as i32;
            boxgrid(h, k)
        }
        _ => {
            // random blob: a noisy icosphere, scaled per axis
            let mut m = gen::icosphere(1.0, rng.below(2) as i32 + 1);
            let s = [
                rng.range(0.1, 2.0),
                rng.range(0.1, 2.0),
                rng.range(0.1, 2.0),
            ];
            for p in &mut m.0 {
                for k in 0..3 {
                    p[k] *= s[k];
                }
            }
            m
        }
    };
    // per-axis stretch (sometimes), noise (sometimes), rotation (often), offset
    if rng.below(4) == 0 {
        let s = [
            rng.range(0.5, 1.5),
            rng.range(0.5, 1.5),
            rng.range(0.5, 1.5),
        ];
        for p in &mut mesh.0 {
            for k in 0..3 {
                p[k] *= s[k];
            }
        }
    }
    if rng.below(2) == 0 {
        let amp = [0.0005, 0.005, 0.02, 0.08][rng.below(4) as usize];
        for p in &mut mesh.0 {
            for c in p.iter_mut() {
                *c += rng.range(-amp, amp);
            }
        }
    }
    if rng.below(4) != 0 {
        let r = random_rot(rng);
        for p in &mut mesh.0 {
            let q = *p;
            for k in 0..3 {
                p[k] = (r[k][0] * q[0] + r[k][1] * q[1]) + r[k][2] * q[2];
            }
        }
    }
    if rng.below(2) == 0 {
        let o = [
            rng.range(-5.0, 5.0),
            rng.range(-5.0, 5.0),
            rng.range(-5.0, 5.0),
        ];
        for p in &mut mesh.0 {
            for k in 0..3 {
                p[k] += o[k];
            }
        }
    }
    // shuffle triangle order / rotate vertex order sometimes
    if rng.below(3) == 0 {
        let n = mesh.1.len();
        for a in (1..n).rev() {
            let b = rng.below(a as u64 + 1) as usize;
            mesh.1.swap(a, b);
        }
        for t in &mut mesh.1 {
            let r = rng.below(3) as usize;
            t.rotate_left(r);
        }
    }
    let flags = FLAG_SETS[rng.below(FLAG_SETS.len() as u64) as usize] | 0x40;
    let tol = [0.002, 0.01, 0.02, 0.05, 0.1, 0.2, 0.4, 0.8][rng.below(8) as usize];
    let mats = match rng.below(8) {
        0 => Some(vec![2i8; mesh.1.len()]),
        1 => {
            let mut m = vec![1i8; mesh.1.len()];
            let k = rng.below(m.len() as u64) as usize;
            m[k] = -3;
            Some(m)
        }
        _ => None,
    };
    let (v, i) = to_input(&mesh);
    Case {
        v,
        i,
        mats,
        flags,
        tol,
    }
}

fn boxgrid(h: [f32; 3], k: i32) -> gen::Mesh {
    let mut v: Vec<[f32; 3]> = Vec::new();
    let mut t = Vec::new();
    for axis in 0..3 {
        for side in [-1.0f32, 1.0] {
            let (u, w) = ((axis + 1) % 3, (axis + 2) % 3);
            let base = v.len() as i32;
            for j in 0..=k {
                for i in 0..=k {
                    let mut p = [0.0f32; 3];
                    p[axis] = side * h[axis];
                    p[u] = h[u] * (2.0 * i as f32 / k as f32 - 1.0);
                    p[w] = h[w] * (2.0 * j as f32 / k as f32 - 1.0);
                    v.push(p);
                }
            }
            for j in 0..k {
                for i in 0..k {
                    let a = base + j * (k + 1) + i;
                    let (b, c, d) = (a + 1, a + k + 2, a + k + 1);
                    if side > 0.0 {
                        t.push([a, b, c]);
                        t.push([a, c, d]);
                    } else {
                        t.push([a, c, b]);
                        t.push([a, d, c]);
                    }
                }
            }
        }
    }
    (v, t)
}

/// The tolerances that sit on the port's own decision values (so the DLL and the port must
/// agree on which side every error falls).
fn threshold_tols(tr: &FitTrace) -> Vec<f32> {
    let mut vals: Vec<f64> = Vec::new();
    for e in [tr.cyl_err, tr.cap_err] {
        vals.extend_from_slice(&e[..3]);
    }
    for e in [tr.cyl_max, tr.cap_max] {
        vals.extend(e.iter().map(|x| x / 1.5));
    }
    vals.extend_from_slice(&tr.box_err);
    vals.extend(tr.box_max.iter().map(|x| x / 1.5));
    vals.push(tr.sph_err);
    vals.push(tr.sph_max / 1.5);
    let mut out = Vec::new();
    for x in vals {
        if !(x > 0.0 && x < 1.0e3) {
            continue;
        }
        let f = x as f32;
        for t in [
            f,
            f32::from_bits(f.to_bits() + 1),
            f32::from_bits(f.to_bits() - 1),
        ] {
            if t > 0.0 {
                out.push(t);
            }
        }
    }
    out
}

fn cases(seed: u64, n: usize) -> Vec<Case> {
    let mut rng = Rng(seed);
    let mut out = Vec::new();
    while out.len() < n {
        let c = random_case(&mut rng);
        // half of the base cases also spawn threshold variants
        if rng.below(2) == 0 {
            let mut tr = FitTrace::default();
            let _ = try_primitive_traced(
                &c.v,
                &c.i,
                None,
                (c.i.len() / 3) as i32,
                c.flags,
                c.tol,
                &mut tr,
            );
            let tols = threshold_tols(&tr);
            if !tols.is_empty() {
                let k = rng.below(tols.len() as u64) as usize;
                let t2 = tols[k];
                out.push(Case {
                    v: c.v.clone(),
                    i: c.i.clone(),
                    mats: None,
                    flags: c.flags,
                    tol: t2,
                });
            }
        }
        out.push(c);
    }
    out.truncate(n);
    out
}

/// The probe's input file: per case `u32 nv, u32 nt, u32 flags, f32 tol, u32 hasMats`,
/// `f32 v[3nv]`, `u16 idx[3nt]`, `i8 mats[nt]` (if any).
fn write_cases(path: &PathBuf, cs: &[Case]) {
    let mut b: Vec<u8> = Vec::new();
    b.extend_from_slice(&(cs.len() as u32).to_le_bytes());
    for c in cs {
        let nt = (c.i.len() / 3) as u32;
        b.extend_from_slice(&(c.v.len() as u32).to_le_bytes());
        b.extend_from_slice(&nt.to_le_bytes());
        b.extend_from_slice(&c.flags.to_le_bytes());
        b.extend_from_slice(&c.tol.to_bits().to_le_bytes());
        b.extend_from_slice(&u32::from(c.mats.is_some()).to_le_bytes());
        for p in &c.v {
            for x in [p.x, p.y, p.z] {
                b.extend_from_slice(&x.to_bits().to_le_bytes());
            }
        }
        for i in &c.i {
            b.extend_from_slice(&i.to_le_bytes());
        }
        if let Some(m) = &c.mats {
            b.extend(m.iter().map(|&x| x as u8));
        }
    }
    std::fs::write(path, b).unwrap();
}

/// What the probe prints for a case: `case k tol T ret R [w …]` (the primitive's words) followed by
/// `axes` (sub_350975D0's nine axes and centre as double bits).
fn port_lines(k: usize, c: &Case) -> String {
    let nt = (c.i.len() / 3) as i32;
    let mut tr = FitTrace::default();
    let (ret, prim) =
        try_primitive_traced(&c.v, &c.i, c.mats.as_deref(), nt, c.flags, c.tol, &mut tr);
    let mut s = format!("case {k} tol {:08x} ret {ret}", c.tol.to_bits());
    let w = |s: &mut String, f: f32| {
        let _ = write!(s, " {:08x}", f.to_bits());
    };
    match prim {
        None => assert_eq!(ret, KIND_MESH),
        Some(Primitive::Box(b)) => {
            for r in &b.basis.m {
                for x in r {
                    w(&mut s, *x);
                }
            }
            let _ = write!(s, " {:08x}", b.oriented as u32);
            for x in [
                b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z,
            ] {
                w(&mut s, x);
            }
        }
        Some(Primitive::Cylinder(p) | Primitive::Capsule(p)) => {
            for x in [
                p.center.x, p.center.y, p.center.z, p.axis.x, p.axis.y, p.axis.z, p.r, p.hh,
            ] {
                w(&mut s, x);
            }
        }
        Some(Primitive::Sphere(p)) => {
            for x in [p.center.x, p.center.y, p.center.z, p.r] {
                w(&mut s, x);
            }
        }
        Some(other) => panic!("unexpected {other:?}"),
    }
    s.push('\n');
    let pa = principal_axes(&c.v, &c.i, nt);
    s += "axes";
    for r in &pa.axes {
        for x in r {
            let _ = write!(s, " {:016x}", x.to_bits());
        }
    }
    for x in pa.center {
        let _ = write!(s, " {:016x}", x.to_bits());
    }
    s.push('\n');
    s
}

fn case_file(tag: &str, cs: &[Case]) -> PathBuf {
    let dir = std::env::temp_dir().join("aaemu_p1c_approx");
    std::fs::create_dir_all(&dir).unwrap();
    let p = dir.join(format!("{tag}_{}.bin", std::process::id()));
    write_cases(&p, cs);
    p
}

fn compare(probe_out: &str, cs: &[Case]) -> (usize, usize, Vec<String>) {
    let port: String = cs
        .iter()
        .enumerate()
        .map(|(k, c)| port_lines(k, c))
        .collect();
    let (mut ok, mut bad, mut first) = (0usize, 0usize, Vec::new());
    let pl: Vec<&str> = probe_out.lines().filter(|l| !l.is_empty()).collect();
    let ql: Vec<&str> = port.lines().collect();
    assert_eq!(pl.len(), ql.len(), "line count");
    for (a, b) in pl.chunks(2).zip(ql.chunks(2)) {
        if a == b {
            ok += 1;
        } else {
            bad += 1;
            if first.len() < 8 {
                first.push(format!(
                    "probe: {}\n       {}\nport:  {}\n       {}",
                    a[0], a[1], b[0], b[1]
                ));
            }
        }
    }
    (ok, bad, first)
}

const GOLDEN_SEED: u64 = 0x5E_EDA9_9A0C;
const GOLDEN_CASES: usize = 400;

#[test]
fn randomised_against_the_dll() {
    let mut cs = cases(GOLDEN_SEED, GOLDEN_CASES);
    // The threshold variants take their tolerance from the port's decision values; once the
    // golden exists its recorded tolerances are used, so the input no longer depends on the port.
    if let Ok(stored) =
        std::fs::read_to_string(p1c_common::golden_dir().join("p1c_approx_rand.trace"))
    {
        for l in stored.lines().filter(|l| l.starts_with("case ")) {
            let w: Vec<&str> = l.split_whitespace().collect();
            let k: usize = w[1].parse().unwrap();
            if k < cs.len() {
                cs[k].tol = f32::from_bits(u32::from_str_radix(w[3], 16).unwrap());
            }
        }
    }
    let file = case_file("golden", &cs);
    let text = p1c_common::golden("p1c_approx_rand", PROBE, &[file.to_str().unwrap()])
        .expect("tests/golden/p1c_approx_rand.trace");
    let _ = std::fs::remove_file(&file);
    let (ok, bad, first) = compare(&text, &cs);
    assert!(
        bad == 0,
        "{bad} of {} cases differ:\n{}",
        ok + bad,
        first.join("\n")
    );
    // the set must exercise every outcome
    let kinds: std::collections::BTreeSet<&str> = text
        .lines()
        .filter_map(|l| l.split_whitespace().nth(5))
        .collect();
    for k in ["0", "1", "4", "5", "6"] {
        assert!(kinds.contains(k), "no case returned {k}");
    }
}

#[test]
fn randomised_live_run() {
    let Some(exe) = p1c_common::probe(PROBE) else {
        return;
    };
    let n: usize = std::env::var("AAEMU_PHYS_APPROX_CASES")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(6000);
    let mut total = (0usize, 0usize);
    let mut firsts = Vec::new();
    let mut hist = std::collections::BTreeMap::<String, usize>::new();
    for (chunk, seed) in [0x1234_5678u64, 0xDEAD_BEEF, 0x0BAD_F00D, 0xFEED_FACE]
        .iter()
        .enumerate()
    {
        let cs = cases(*seed, n / 4);
        let file = case_file(&format!("live{chunk}"), &cs);
        let out = p1c_common::run_probe(&exe, &[file.to_str().unwrap()]).unwrap();
        let _ = std::fs::remove_file(&file);
        for l in out.lines().filter(|l| l.starts_with("case")) {
            *hist
                .entry(l.split_whitespace().nth(5).unwrap().to_string())
                .or_default() += 1;
        }
        let (ok, bad, first) = compare(&out, &cs);
        total.0 += ok;
        total.1 += bad;
        firsts.extend(first);
    }
    eprintln!(
        "approx live run: {} match, {} differ; DLL results {hist:?}",
        total.0, total.1
    );
    assert!(
        total.1 == 0,
        "{} cases differ:\n{}",
        total.1,
        firsts.join("\n")
    );
}
