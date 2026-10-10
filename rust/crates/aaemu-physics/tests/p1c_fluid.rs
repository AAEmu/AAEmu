//! P1c-fluid: `CTriMesh` slots 11 (FindClosestPoint 0x351C6780), 12 (CalcVolumetricPressure
//! 0x351CBE20), 13 (CalculateBuoyancy 0x351DA620), 14 (CalculateMediumResistance 0x351CD0C0)
//! and `CHeightfield` slots 11 / 12 (0x35155CB0 / 0x35154CB0) plus its inherited 13 / 14,
//! bit-exact against the DLL (probe `tests/p1c_fluid_probe.c`).
//!
//! Every test generates a job (meshes or heightfields followed by queries) with a fixed seed;
//! the probe runs the job on the DLL and prints the DLL's mesh dump and one result line per
//! query; the port rebuilds the mesh from the dump (`parse_mesh_dump`) and must print the same
//! lines bit for bit.
//!
//! * `synthetic_meshes_match_the_dll`, `heightfield_matches_the_dll`: goldens
//!   `tests/golden/p1c_fluid_{synth,hf}.trace` (regenerated and compared when
//!   `AAEMU_PHYS_P1C_PROBE_DIR` holds `p1c_fluid_probe.exe`).
//! * `corpus_meshes_match_the_dll`: every trimesh of the CGF corpus (`AAEMU_PHYS_CGF_DIR`), live
//!   only (needs the probe and the corpus; passes vacuously, saying so, without them).

mod p1b_common;
mod p1c_common;

use aaemu_physics::geometry::primitives::{HeightfieldPrim, Plane};
use aaemu_physics::geometry::trimesh::closest::{
    heightfield_calc_volumetric_pressure, heightfield_find_closest_point,
};
use aaemu_physics::geometry::trimesh::fluid::{
    mesh_buoyancy, mesh_medium_resistance, mesh_volumetric_pressure, MeshView,
};
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::geometry::GeomWorldData;
use aaemu_physics::math::{Mat33, Quat, Vec3};
use std::collections::BTreeMap;
use std::fmt::Write as _;
use std::path::PathBuf;

// ---------------------------------------------------------------------------------------------
// RNG and formatting
// ---------------------------------------------------------------------------------------------

struct Rng(u64);

impl Rng {
    fn next(&mut self) -> u64 {
        self.0 ^= self.0 << 13;
        self.0 ^= self.0 >> 7;
        self.0 ^= self.0 << 17;
        self.0
    }
    fn f(&mut self, lo: f32, hi: f32) -> f32 {
        let u = (self.next() >> 40) as f32 / (1u64 << 24) as f32;
        lo + (hi - lo) * u
    }
    fn i(&mut self, n: i32) -> i32 {
        (self.next() % n.max(1) as u64) as i32
    }
    fn chance(&mut self, p: f32) -> bool {
        self.f(0.0, 1.0) < p
    }
    fn unit(&mut self) -> Vec3 {
        loop {
            let v = Vec3::new(self.f(-1.0, 1.0), self.f(-1.0, 1.0), self.f(-1.0, 1.0));
            let l = v.len();
            if l > 0.1 && l <= 1.0 {
                return v * (1.0 / l);
            }
        }
    }
}

fn w(s: &mut String, f: f32) {
    let _ = write!(s, " {:08x}", f.to_bits());
}

fn wv(s: &mut String, v: Vec3) {
    w(s, v.x);
    w(s, v.y);
    w(s, v.z);
}

fn wgwd(s: &mut String, g: &GeomWorldData) {
    wv(s, g.offset);
    for r in &g.r.m {
        for &x in r {
            w(s, x);
        }
    }
    w(s, g.scale);
    wv(s, g.v);
    wv(s, g.w);
    wv(s, g.center_of_mass);
}

// ---------------------------------------------------------------------------------------------
// Queries
// ---------------------------------------------------------------------------------------------

#[derive(Clone, Debug)]
enum Query {
    B13(Plane, GeomWorldData),
    B14(Plane, GeomWorldData, Vec3, Vec3),
    B12(GeomWorldData, Vec3, f32, f32, Vec3, Vec3, Vec3),
    C11(GeomWorldData, i32, i32, Vec3, Vec3, i32, [Vec3; 2]),
}

impl Query {
    fn kind(&self) -> &'static str {
        match self {
            Query::B13(..) => "b13",
            Query::B14(..) => "b14",
            Query::B12(..) => "b12",
            Query::C11(..) => "c11",
        }
    }

    fn job_line(&self) -> String {
        let mut s = String::from(self.kind());
        match self {
            Query::B13(p, g) => {
                wv(&mut s, p.n);
                wv(&mut s, p.origin);
                wgwd(&mut s, g);
            }
            Query::B14(p, g, dp, dl) => {
                wv(&mut s, p.n);
                wv(&mut s, p.origin);
                wgwd(&mut s, g);
                wv(&mut s, *dp);
                wv(&mut s, *dl);
            }
            Query::B12(g, e, k, rmin, com, p, l) => {
                wgwd(&mut s, g);
                wv(&mut s, *e);
                w(&mut s, *k);
                w(&mut s, *rmin);
                wv(&mut s, *com);
                wv(&mut s, *p);
                wv(&mut s, *l);
            }
            Query::C11(g, ip, ifeat, p0, p1, nmax, res) => {
                wgwd(&mut s, g);
                let _ = write!(s, " {ip} {ifeat}");
                wv(&mut s, *p0);
                wv(&mut s, *p1);
                let _ = write!(s, " {nmax}");
                wv(&mut s, res[0]);
                wv(&mut s, res[1]);
            }
        }
        s
    }
}

/// What the port evaluates a query against.
enum Target<'a> {
    Mesh(&'a TriMesh),
    /// A heightfield: its primitive and its inherited `CTriMesh` triangle count (`+120`).
    Hf(&'a HeightfieldPrim, i32),
}

fn port_line(t: &Target, q: &Query) -> String {
    let empty: [Vec3; 0] = [];
    let no_idx: [i32; 0] = [];
    let view = match t {
        Target::Mesh(m) => m.view(),
        Target::Hf(_, nt) => MeshView {
            vertices: &empty,
            indices: &no_idx,
            normals: &empty,
            n_tris: *nt,
        },
    };
    let mut s = String::new();
    match q {
        Query::B13(p, g) => {
            let (v, c) = mesh_buoyancy(&view, p, g);
            s.push_str("r13");
            w(&mut s, v);
            wv(&mut s, c);
        }
        Query::B14(p, g, dp, dl) => {
            let (mut dp, mut dl) = (*dp, *dl);
            mesh_medium_resistance(&view, p, g, &mut dp, &mut dl);
            s.push_str("r14");
            wv(&mut s, dp);
            wv(&mut s, dl);
        }
        Query::B12(g, e, k, rmin, com, p, l) => {
            let (mut p, mut l) = (*p, *l);
            match t {
                Target::Mesh(_) => mesh_volumetric_pressure(&view, g, *e, *k, *rmin, *com, &mut p, &mut l),
                Target::Hf(hf, _) => {
                    heightfield_calc_volumetric_pressure(hf, g, *e, *k, *rmin, *com, &mut p, &mut l);
                }
            }
            s.push_str("r12");
            wv(&mut s, p);
            wv(&mut s, l);
        }
        Query::C11(g, ip, ifeat, p0, p1, nmax, res) => {
            let (mut ip, mut ifeat, mut res) = (*ip, *ifeat, *res);
            let r = match t {
                Target::Mesh(m) => m.find_closest_point(g, &mut ip, &mut ifeat, *p0, *p1, &mut res, *nmax),
                Target::Hf(hf, _) => {
                    heightfield_find_closest_point(hf, g, &mut ip, &mut ifeat, *p0, *p1, &mut res, *nmax)
                }
            };
            let _ = write!(s, "r11 {r} {ip} {ifeat}");
            wv(&mut s, res[0]);
            wv(&mut s, res[1]);
        }
    }
    s
}

// ---------------------------------------------------------------------------------------------
// Generators
// ---------------------------------------------------------------------------------------------

fn rand_rot(r: &mut Rng) -> Mat33 {
    if r.chance(0.2) {
        return Mat33::IDENTITY;
    }
    let (x, y, z, ww) = (r.f(-1.0, 1.0), r.f(-1.0, 1.0), r.f(-1.0, 1.0), r.f(-1.0, 1.0));
    let l = (x * x + y * y + z * z + ww * ww).sqrt().max(1e-3);
    Mat33::from_quat(Quat::new(x / l, y / l, z / l, ww / l))
}

fn rand_gwd(r: &mut Rng, extent: f32) -> GeomWorldData {
    let off = if r.chance(0.2) {
        Vec3::ZERO
    } else {
        Vec3::new(r.f(-20.0, 20.0), r.f(-20.0, 20.0), r.f(-20.0, 20.0))
    };
    let scale = if r.chance(0.5) { 1.0 } else { r.f(0.2, 3.0) };
    let e = extent.max(0.1);
    GeomWorldData {
        offset: off,
        r: rand_rot(r),
        scale,
        v: r.unit() * r.f(0.0, 10.0),
        w: if r.chance(0.2) { Vec3::ZERO } else { r.unit() * r.f(0.0, 3.0) },
        center_of_mass: off + r.unit() * (r.f(0.0, 0.5) * e * scale),
    }
}

/// World-space centre and radius of the vertices under `g` (approximate, for placing queries).
fn world_ball(v: &[Vec3], g: &GeomWorldData) -> (Vec3, f32) {
    if v.is_empty() {
        return (g.offset, 1.0);
    }
    let mut lo = Vec3::splat(f32::MAX);
    let mut hi = Vec3::splat(f32::MIN);
    for p in v {
        let q = g.r.mul_vec(*p) * g.scale + g.offset;
        for k in 0..3 {
            lo[k] = lo[k].min(q[k]);
            hi[k] = hi[k].max(q[k]);
        }
    }
    let c = (lo + hi) * 0.5;
    (c, ((hi - lo).len() * 0.5).max(0.05))
}

fn rand_plane(r: &mut Rng, c: Vec3, rad: f32) -> Plane {
    let n = match r.i(10) {
        0..=3 => Vec3::Z,
        4 => [Vec3::X, -Vec3::X, Vec3::Y, -Vec3::Y, -Vec3::Z][r.i(5) as usize],
        _ => r.unit(),
    };
    let depth = match r.i(10) {
        0 => r.f(1.1, 3.0) * rad,
        1 => -r.f(1.1, 3.0) * rad,
        _ => r.f(-1.0, 1.0) * rad,
    };
    Plane {
        n,
        origin: c + n * depth + r.unit() * r.f(0.0, rad),
    }
}

fn gen_queries(r: &mut Rng, verts: &[Vec3], nt: i32, n: [usize; 4], out: &mut Vec<Query>) {
    let extent = {
        let (_, rad) = world_ball(verts, &GeomWorldData::default());
        rad
    };
    for _ in 0..n[0] {
        let g = rand_gwd(r, extent);
        let (c, rad) = world_ball(verts, &g);
        out.push(Query::B13(rand_plane(r, c, rad), g));
    }
    for _ in 0..n[1] {
        let g = rand_gwd(r, extent);
        let (c, rad) = world_ball(verts, &g);
        let dp = if r.chance(0.5) { Vec3::ZERO } else { r.unit() * 3.0 };
        out.push(Query::B14(rand_plane(r, c, rad), g, dp, r.unit()));
    }
    for _ in 0..n[2] {
        let g = rand_gwd(r, extent);
        let (c, rad) = world_ball(verts, &g);
        let e = c + r.unit() * (rad * r.f(0.0, 3.0));
        let k = r.f(0.1, 100.0);
        let rmin = if r.chance(0.2) { r.f(0.0, 0.01) } else { r.f(0.001, 2.0) * rad };
        let (p, l) = if r.chance(0.3) {
            (Vec3::ZERO, Vec3::ZERO)
        } else {
            (r.unit(), r.unit())
        };
        out.push(Query::B12(g, e, k, rmin, g.center_of_mass, p, l));
    }
    for _ in 0..n[3] {
        let g = rand_gwd(r, extent);
        let (c, rad) = world_ball(verts, &g);
        let ip = if r.chance(0.02) {
            [nt, nt + 5, -1][r.i(3) as usize]
        } else {
            r.i(nt.max(1))
        };
        let ifeat = if r.chance(0.03) {
            [0x60, 3, 0x23, 0x5F, 0x45][r.i(5) as usize]
        } else {
            match r.i(3) {
                0 => 0x40,
                1 => 0x20 | r.i(3),
                _ => r.i(3),
            }
        };
        let p0 = c + r.unit() * (rad * r.f(0.0, 1.5));
        let p1 = match r.i(8) {
            0 | 1 => p0,
            2 => p0 + r.unit() * 0.003,
            _ => p0 + r.unit() * (rad * r.f(0.0, 2.0)),
        };
        let nmax = match r.i(10) {
            0 => [0, -1, 1, 2][r.i(4) as usize],
            _ => 1 + r.i(40),
        };
        let res = [r.unit(), r.unit()];
        out.push(Query::C11(g, ip, ifeat, p0, p1, nmax, res));
    }
}

// ---------------------------------------------------------------------------------------------
// Jobs
// ---------------------------------------------------------------------------------------------

/// A job: the text the probe reads and, per object, its queries.
#[derive(Default)]
struct Job {
    text: String,
    objects: Vec<(ObjKind, Vec<Query>)>,
}

enum ObjKind {
    Mesh,
    Hf(i32),
}

impl Job {
    fn add_mesh(&mut self, v: &[Vec3], idx: &[i32], flags: u32, qs: Vec<Query>) {
        let _ = writeln!(self.text, "mesh {} {} {flags:x}", v.len(), idx.len() / 3);
        self.text.push('v');
        for p in v {
            wv(&mut self.text, *p);
        }
        self.text.push_str("\ni");
        for i in idx {
            let _ = write!(self.text, " {i}");
        }
        self.text.push('\n');
        for q in &qs {
            self.text.push_str(&q.job_line());
            self.text.push('\n');
        }
        self.objects.push((ObjKind::Mesh, qs));
    }

    fn add_hf(&mut self, n: [i32; 2], step: [f32; 2], hscale: f32, mode: i32, qs: Vec<Query>) {
        let mut s = format!("hf {} {}", n[0], n[1]);
        w(&mut s, step[0]);
        w(&mut s, step[1]);
        w(&mut s, hscale);
        let _ = writeln!(s, " {mode}");
        self.text.push_str(&s);
        for q in &qs {
            self.text.push_str(&q.job_line());
            self.text.push('\n');
        }
        self.objects.push((ObjKind::Hf(mode), qs));
    }

    fn write(&self, tag: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("aaemu-p1c-fluid-{}", std::process::id()));
        std::fs::create_dir_all(&dir).unwrap();
        let p = dir.join(format!("{tag}.job"));
        std::fs::write(&p, &self.text).unwrap();
        p
    }
}

// heightfield callbacks of the probe's modes
fn pmod(a: i32, m: i32) -> i32 {
    a.rem_euclid(m)
}
fn hf_h0(_: i32, _: i32) -> f32 {
    0.5
}
fn hf_h1(ix: i32, iy: i32) -> f32 {
    pmod(ix.wrapping_mul(7).wrapping_add(iy.wrapping_mul(13)), 5) as f32 * 0.75 - 1.0
}
fn hf_h2(ix: i32, iy: i32) -> f32 {
    let u = (ix as u32).wrapping_mul(73_856_093) ^ (iy as u32).wrapping_mul(19_349_663);
    ((u >> 8) & 1023) as f32 * 0.01 - 5.0
}
fn hf_s0(_: i32, _: i32) -> u16 {
    0
}
fn hf_s3(ix: i32, iy: i32) -> u16 {
    if pmod(ix.wrapping_mul(3).wrapping_add(iy.wrapping_mul(5)), 7) == 0 {
        0x8000
    } else {
        0
    }
}

fn hf_prim(words: &[u32], mode: i32) -> HeightfieldPrim {
    let f = |i: usize| f32::from_bits(words[i]);
    HeightfieldPrim {
        step: [f(13), f(14)],
        stepr: [f(15), f(16)],
        size: [words[17] as i32, words[18] as i32],
        stride: [words[19] as i32, words[20] as i32],
        height_scale: f(21),
        type_mask: (words[22] & 0xFFFF) as u16,
        height_mask: (words[22] >> 16) as u16,
        type_hole: words[23] as i32,
        type_power: words[24] as i32,
        get_height: Some(match mode {
            1 => hf_h1,
            2 | 3 => hf_h2,
            _ => hf_h0,
        }),
        get_surf_type: Some(if mode == 3 { hf_s3 } else { hf_s0 }),
        ..HeightfieldPrim::default()
    }
}

/// Mismatch statistics per query kind.
#[derive(Default)]
struct Stats {
    count: BTreeMap<&'static str, usize>,
    fails: Vec<String>,
    /// For slot 11: return values / final features seen (branch coverage).
    feats: BTreeMap<String, usize>,
}

impl Stats {
    fn finish(&self, what: &str) {
        println!("{what}: {:?}", self.count);
        println!("{what}: slot 11 outcomes {:?}", self.feats);
        if !self.fails.is_empty() {
            for f in self.fails.iter().take(8) {
                println!("{f}");
            }
            panic!("{what}: {} mismatches", self.fails.len());
        }
    }
}

/// Replays a probe output against the job; returns the meshes parsed (for statistics).
fn check_output(job: &Job, text: &str, tag: &str, st: &mut Stats) {
    let lines: Vec<&str> = text.lines().collect();
    let mut i = 0;
    for (oi, (kind, qs)) in job.objects.iter().enumerate() {
        let mesh;
        let hf;
        let target = match kind {
            ObjKind::Mesh => {
                assert!(lines[i].starts_with("mesh "), "{tag}: object {oi}: expected a mesh dump at line {}", i + 1);
                let (m, used) = p1c_common::parse_mesh_dump(&lines[i..]);
                i += used;
                mesh = m;
                Target::Mesh(&mesh)
            }
            ObjKind::Hf(mode) => {
                let words: Vec<u32> = lines[i]
                    .split_whitespace()
                    .skip(1)
                    .map(|s| u32::from_str_radix(s, 16).unwrap())
                    .collect();
                let nt: i32 = lines[i + 1].split_whitespace().nth(1).unwrap().parse().unwrap();
                i += 2;
                hf = hf_prim(&words, *mode);
                Target::Hf(&hf, nt)
            }
        };
        for q in qs {
            let dll = lines.get(i).copied().unwrap_or("<eof>");
            i += 1;
            let port = port_line(&target, q);
            *st.count.entry(q.kind()).or_default() += 1;
            if let Query::C11(..) = q {
                let t: Vec<&str> = dll.split_whitespace().collect();
                if t.len() > 3 {
                    let kind = match t[1] {
                        "-1" => "ret-1".to_string(),
                        _ => {
                            let f: i32 = t[3].parse().unwrap_or(-1);
                            let h = matches!(target, Target::Hf(..));
                            format!("{}{}", if h { "hf-" } else { "" }, match f & 0x60 {
                                0x40 => "face",
                                0x20 => "edge",
                                0 => "vertex",
                                _ => "other",
                            })
                        }
                    };
                    *st.feats.entry(kind).or_default() += 1;
                }
            }
            if port != dll {
                st.fails.push(format!(
                    "{tag} object {oi} {}:\n  query: {}\n  port:  {port}\n  dll:   {dll}",
                    q.kind(),
                    q.job_line()
                ));
            }
        }
    }
    assert_eq!(i, lines.len(), "{tag}: trailing probe output");
}

// ---------------------------------------------------------------------------------------------
// Synthetic meshes
// ---------------------------------------------------------------------------------------------

fn cube(h: Vec3, c: Vec3) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for i in 0..8 {
        v.push(Vec3::new(
            c.x + if i & 1 != 0 { h.x } else { -h.x },
            c.y + if i & 2 != 0 { h.y } else { -h.y },
            c.z + if i & 4 != 0 { h.z } else { -h.z },
        ));
    }
    let idx = vec![
        0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1, 0, 4,
        5, 7, 7, 6, 4,
    ];
    (v, idx)
}

/// An octahedron subdivided `levels` times and pushed onto a sphere (outward winding).
fn sphere(levels: usize, r: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = vec![Vec3::X, -Vec3::X, Vec3::Y, -Vec3::Y, Vec3::Z, -Vec3::Z];
    let mut t: Vec<[i32; 3]> = vec![
        [0, 2, 4],
        [2, 1, 4],
        [1, 3, 4],
        [3, 0, 4],
        [2, 0, 5],
        [1, 2, 5],
        [3, 1, 5],
        [0, 3, 5],
    ];
    for _ in 0..levels {
        let mut nt = Vec::new();
        let mut mid = BTreeMap::new();
        let mut m = |a: i32, b: i32, v: &mut Vec<Vec3>| -> i32 {
            *mid.entry((a.min(b), a.max(b))).or_insert_with(|| {
                let p = (v[a as usize] + v[b as usize]) * 0.5;
                v.push(p * (1.0 / p.len()));
                v.len() as i32 - 1
            })
        };
        for [a, b, c] in t {
            let ab = m(a, b, &mut v);
            let bc = m(b, c, &mut v);
            let ca = m(c, a, &mut v);
            nt.extend([[a, ab, ca], [ab, b, bc], [ca, bc, c], [ab, bc, ca]]);
        }
        t = nt;
    }
    (v.iter().map(|p| *p * r).collect(), t.concat())
}

/// A torus (non-convex): `n` segments around, `m` around the tube.
fn torus(n: i32, m: i32, rr: f32, r: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for i in 0..n {
        let a = i as f32 / n as f32 * std::f32::consts::TAU;
        for j in 0..m {
            let b = j as f32 / m as f32 * std::f32::consts::TAU;
            let d = rr + r * b.cos();
            v.push(Vec3::new(d * a.cos(), d * a.sin(), r * b.sin()));
        }
    }
    let mut idx = Vec::new();
    for i in 0..n {
        for j in 0..m {
            let p = |i: i32, j: i32| (i.rem_euclid(n)) * m + j.rem_euclid(m);
            let (a, b, c, d) = (p(i, j), p(i + 1, j), p(i + 1, j + 1), p(i, j + 1));
            idx.extend([a, b, c, a, c, d]);
        }
    }
    (v, idx)
}

/// An open bumpy grid (open edges everywhere along the border).
fn open_grid(n: i32, s: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for j in 0..=n {
        for i in 0..=n {
            let z = ((i * 7 + j * 3) % 5) as f32 * 0.2 * s - 0.4 * s;
            v.push(Vec3::new(i as f32 * s, j as f32 * s, z));
        }
    }
    let mut idx = Vec::new();
    for j in 0..n {
        for i in 0..n {
            let p = |i: i32, j: i32| j * (n + 1) + i;
            idx.extend([p(i, j), p(i + 1, j), p(i + 1, j + 1), p(i, j), p(i + 1, j + 1), p(i, j + 1)]);
        }
    }
    (v, idx)
}

fn synthetic_job(seed: u64, nq: [usize; 4]) -> Job {
    let mut r = Rng(seed);
    let mut job = Job::default();
    let mut meshes: Vec<(Vec<Vec3>, Vec<i32>)> = vec![
        cube(Vec3::splat(1.0), Vec3::ZERO),
        cube(Vec3::new(3.0, 0.4, 1.2), Vec3::new(0.5, -1.0, 2.0)),
        sphere(1, 1.5),
        sphere(2, 0.8),
        torus(12, 6, 2.0, 0.6),
        open_grid(6, 0.7),
        (
            vec![Vec3::ZERO, Vec3::X, Vec3::Y, Vec3::Z],
            vec![0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3],
        ),
    ];
    // a cube with every other triangle flipped (normals against the volume)
    let (v, mut idx) = cube(Vec3::new(1.0, 2.0, 0.5), Vec3::ZERO);
    for t in (0..12).step_by(2) {
        idx.swap(3 * t + 1, 3 * t + 2);
    }
    meshes.push((v, idx));
    // an inverted sphere (all normals inward)
    let (v, mut idx) = sphere(1, 1.0);
    for t in 0..idx.len() / 3 {
        idx.swap(3 * t + 1, 3 * t + 2);
    }
    meshes.push((v, idx));
    for (k, (v, idx)) in meshes.into_iter().enumerate() {
        let mut qs = Vec::new();
        gen_queries(&mut r, &v, (idx.len() / 3) as i32, nq, &mut qs);
        let flags = 0x20000 | 2 | if k % 2 == 0 { 0x20 } else { 0x40 };
        job.add_mesh(&v, &idx, flags, qs);
    }
    job
}

#[test]
fn synthetic_meshes_match_the_dll() {
    let job = synthetic_job(0x51F1_C0DE_0001, [60, 50, 25, 220]);
    let path = job.write("synth");
    let Some(text) = p1c_common::golden("p1c_fluid_synth", "p1c_fluid_probe", &[path.to_str().unwrap()]) else {
        println!("p1c_fluid_synth: no golden and no probe; skipped");
        return;
    };
    let mut st = Stats::default();
    check_output(&job, &text, "synth", &mut st);
    st.finish("synthetic meshes");
}

// ---------------------------------------------------------------------------------------------
// Heightfield
// ---------------------------------------------------------------------------------------------

fn hf_job(seed: u64) -> Job {
    let mut r = Rng(seed);
    let mut job = Job::default();
    let cfgs: [([i32; 2], [f32; 2], f32, i32); 6] = [
        ([16, 16], [1.0, 1.0], 1.0, 0),
        ([16, 12], [1.0, 1.0], 1.0, 1),
        ([32, 32], [2.0, 2.0], 1.0, 2),
        ([20, 24], [0.75, 1.5], 0.5, 2),
        ([24, 24], [1.0, 1.0], 2.0, 3),
        ([2, 3], [1.0, 1.0], 1.0, 1),
    ];
    for (n, step, hs, mode) in cfgs {
        let mut qs = Vec::new();
        let ext = Vec3::new(n[0] as f32 * step[0], n[1] as f32 * step[1], 0.0);
        for _ in 0..400 {
            let g = if r.chance(0.5) {
                GeomWorldData::default()
            } else {
                let mut g = rand_gwd(&mut r, 10.0);
                if r.chance(0.5) {
                    g.r = Mat33::IDENTITY;
                }
                g
            };
            let l = Vec3::new(
                r.f(-0.1, 1.1) * ext.x,
                r.f(-0.1, 1.1) * ext.y,
                r.f(-7.0, 7.0) * hs.max(0.5),
            );
            let p0 = g.r.mul_vec(l) * g.scale + g.offset;
            let p1 = p0 + r.unit();
            qs.push(Query::C11(g, r.i(5), r.i(3), p0, p1, r.i(30), [r.unit(), r.unit()]));
        }
        for _ in 0..5 {
            let g = rand_gwd(&mut r, 10.0);
            let pl = rand_plane(&mut r, g.offset, 10.0);
            qs.push(Query::B13(pl, g));
            qs.push(Query::B14(pl, g, r.unit(), r.unit()));
            qs.push(Query::B12(g, r.unit() * 5.0, 3.0, 0.5, g.center_of_mass, r.unit(), r.unit()));
        }
        job.add_hf(n, step, hs, mode, qs);
    }
    job
}

#[test]
fn heightfield_matches_the_dll() {
    let job = hf_job(0x51F1_C0DE_0002);
    let path = job.write("hf");
    let Some(text) = p1c_common::golden("p1c_fluid_hf", "p1c_fluid_probe", &[path.to_str().unwrap()]) else {
        println!("p1c_fluid_hf: no golden and no probe; skipped");
        return;
    };
    let mut st = Stats::default();
    check_output(&job, &text, "hf", &mut st);
    st.finish("heightfield");
}

// ---------------------------------------------------------------------------------------------
// Corpus
// ---------------------------------------------------------------------------------------------

#[test]
fn corpus_meshes_match_the_dll() {
    let Some(exe) = p1c_common::probe("p1c_fluid_probe") else {
        println!("corpus: AAEMU_PHYS_P1C_PROBE_DIR / p1c_fluid_probe.exe not available; skipped");
        return;
    };
    if std::env::var_os("AAEMU_PHYS_CGF_DIR").is_none() {
        println!("corpus: AAEMU_PHYS_CGF_DIR not set; skipped");
        return;
    }
    let meshes = p1b_common::all_meshes();
    let mut r = Rng(0x51F1_C0DE_0003);
    let mut st = Stats::default();
    let mut job = Job::default();
    let mut used = 0usize;
    let mut batch = 0usize;
    let flush = |job: &mut Job, batch: &mut usize, st: &mut Stats| {
        if job.objects.is_empty() {
            return;
        }
        let path = job.write(&format!("corpus{batch}"));
        let text = p1c_common::run_probe(&exe, &[path.to_str().unwrap()]).unwrap_or_else(|e| panic!("{e}"));
        check_output(job, &text, &format!("corpus batch {batch}"), st);
        *batch += 1;
        *job = Job::default();
    };
    for (_, _, m) in &meshes {
        let (Some(v), Some(idx)) = (&m.verts, &m.idx) else {
            continue;
        };
        if idx.is_empty() || v.is_empty() || idx.iter().any(|&i| i as usize >= v.len()) {
            continue;
        }
        let nt = (idx.len() / 3) as i32;
        // keep the work per mesh bounded on huge meshes
        let n = if nt > 20_000 { [4, 2, 0, 10] } else if nt > 3000 { [8, 4, 1, 20] } else { [16, 8, 3, 40] };
        let mut qs = Vec::new();
        gen_queries(&mut r, v, nt, n, &mut qs);
        let flags = 0x20000 | 2 | if m.tree_type == 1 { 0x40 } else { 0x20 };
        job.add_mesh(v, idx, flags, qs);
        used += 1;
        if job.objects.len() >= 40 {
            flush(&mut job, &mut batch, &mut st);
        }
    }
    flush(&mut job, &mut batch, &mut st);
    println!("corpus: {used} meshes of {} trimesh blobs", meshes.len());
    st.finish("corpus");
}
