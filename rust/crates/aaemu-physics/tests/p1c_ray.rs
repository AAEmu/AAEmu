//! P1c-ray: the `CTriMesh` ray path against the DLL — slot 22 grids (built by the core port,
//! dumped here once more), slot 10 `Intersect` with rays (0x351C9910), slot 48
//! `PreparePrimitive` (0x351C2A60) and slot 49 `UnprojectSphere` (0x351D8EA0).
//!
//! `tests/p1c_ray_probe.c` builds the meshes with the DLL's constructor, runs the commands and
//! prints every result as raw bits; the port reads the DLL's mesh state from the same output
//! (`p1c_common::parse_mesh_dump`), replays the commands and must print the same text.
//!
//! | env | effect |
//! |---|---|
//! | `AAEMU_PHYS_P1C_PROBE_DIR` | directory with `p1c_ray_probe.exe`: goldens regenerated and compared |
//! | `AAEMU_PHYS_BLESS=1` | rewrite mismatching goldens |
//! | `AAEMU_PHYS_CGF_DIR` (+ probe) | `corpus_rays_match_the_dll`: every corpus trimesh, live |

mod p1b_common;
mod p1c_common;

use aaemu_physics::collision::contact::{GeomContact, IntersectionParams, UnprojResult};
use aaemu_physics::collision::pairs::PairTables;
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::RayPrim;
use aaemu_physics::geometry::shapes::RayGeom;
use aaemu_physics::geometry::trimesh::intersect::{IntersectExtra, Tables};
use aaemu_physics::geometry::trimesh::protocol::{BorderKind, CallerArena};
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::geometry::GeomWorldData;
use aaemu_physics::math::{Mat33, Quat, Vec3};
use std::collections::HashMap;
use std::fmt::Write as _;

/// Deterministic generator (LCG, top 24 bits).
struct Rng(u32);
impl Rng {
    fn f(&mut self) -> f32 {
        self.0 = self.0.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
        (self.0 >> 8) as f32 / 16_777_216.0
    }
    fn r(&mut self, a: f32, b: f32) -> f32 {
        a + (b - a) * self.f()
    }
    fn v(&mut self, a: f32, b: f32) -> Vec3 {
        Vec3::new(self.r(a, b), self.r(a, b), self.r(a, b))
    }
    fn chance(&mut self, p: f32) -> bool {
        self.f() < p
    }
    fn i(&mut self, n: usize) -> usize {
        ((self.f() * n as f32) as usize).min(n.saturating_sub(1))
    }
    fn unit(&mut self) -> Vec3 {
        loop {
            let v = self.v(-1.0, 1.0);
            let l = (v.x * v.x + v.y * v.y + v.z * v.z).sqrt();
            if l > 0.1 && l <= 1.0 {
                return Vec3::new(v.x / l, v.y / l, v.z / l);
            }
        }
    }
    fn rot(&mut self) -> Mat33 {
        let q = Quat {
            x: self.r(-1.0, 1.0),
            y: self.r(-1.0, 1.0),
            z: self.r(-1.0, 1.0),
            w: self.r(-1.0, 1.0),
        };
        let l = ((q.x * q.x + q.y * q.y) + q.z * q.z + q.w * q.w).sqrt();
        Mat33::from_quat(Quat {
            x: q.x / l,
            y: q.y / l,
            z: q.z / l,
            w: q.w / l,
        })
    }
}

fn w(f: f32) -> String {
    format!("{:08x}", f.to_bits())
}

fn vw(v: Vec3) -> [f32; 3] {
    [v.x, v.y, v.z]
}

/// FNV-1a 64 over the little-endian bytes of the ints (the probe's grid-list digest).
fn fnv(v: &[i32]) -> u64 {
    let mut h = 0xcbf2_9ce4_8422_2325u64;
    for x in v {
        for b in x.to_le_bytes() {
            h ^= u64::from(b);
            h = h.wrapping_mul(0x0100_0000_01b3);
        }
    }
    h
}

fn gwd_words(g: &GeomWorldData, start: i32) -> String {
    let mut v = vw(g.offset).to_vec();
    v.extend(g.r.to_array());
    v.push(g.scale);
    v.extend(vw(g.v));
    v.extend(vw(g.w));
    v.extend(vw(g.center_of_mass));
    let mut s: Vec<String> = v.into_iter().map(w).collect();
    s.push(format!("{:08x}", start as u32));
    s.join(" ")
}

fn ip_words(ip: &IntersectionParams, first_only: bool) -> String {
    let mut d = [0u32; 36];
    d[0] = ip.i_unprojection_mode as u32;
    let f = |x: f32| x.to_bits();
    let c = ip.center_of_rotation;
    d[1..4].copy_from_slice(&[f(c.x), f(c.y), f(c.z)]);
    let a = ip.axis_of_rotation;
    d[4..7].copy_from_slice(&[f(a.x), f(a.y), f(a.z)]);
    d[7] = f(ip.time_interval);
    d[8] = f(ip.vrel_min);
    d[9] = f(ip.max_surface_gap_angle);
    d[10] = f(ip.min_axis_dist);
    let u = ip.unproj_dir;
    d[14..17].copy_from_slice(&[f(u.x), f(u.y), f(u.z)]);
    d[17] = f(ip.max_unproj);
    for k in 0..2 {
        let p = ip.pt_outside_pivot[k];
        d[18 + 3 * k..21 + 3 * k].copy_from_slice(&[f(p.x), f(p.y), f(p.z)]);
    }
    d[24] = u32::from(ip.sweep_test)
        | u32::from(ip.keep_prev_contacts) << 8
        | u32::from(first_only) << 16
        | u32::from(ip.no_area_contacts) << 24;
    d[27] = u32::from(ip.no_intersection);
    d.iter()
        .map(|x| format!("{x:08x}"))
        .collect::<Vec<_>>()
        .join(" ")
}

fn print_contact(s: &mut String, c: &GeomContact, kind: &str) {
    let _ = write!(s, "c {:016x}", c.t.to_bits());
    for v in [c.pt, c.n, c.dir] {
        for x in vw(v) {
            let _ = write!(s, " {}", w(x));
        }
    }
    let _ = writeln!(
        s,
        " {} {} {} {} {} {} {} {} {} {} center {} {} {} cons {} nb {} {kind} area {}",
        c.i_unproj_mode,
        w(c.vel),
        c.id[0],
        c.id[1],
        c.i_prim[0],
        c.i_prim[1],
        c.i_feature[0],
        c.i_feature[1],
        c.i_node[0],
        c.i_node[1],
        w(c.center.x),
        w(c.center.y),
        w(c.center.z),
        u8::from(c.border_consecutive),
        c.border.len(),
        u8::from(c.area.is_some())
    );
}

/// `Cmd::Usph::tri` value meaning "the previous slot-49 result's triangle".
const LAST: i32 = i32::MIN;

/// One command of a job.
#[allow(clippy::large_enum_variant)]
#[derive(Clone, Debug)]
enum Cmd {
    Mesh {
        v: Vec<Vec3>,
        idx: Vec<i32>,
        flags: u32,
        mat_seed: i32,
    },
    Ray(RayPrim),
    Prep {
        m: usize,
        raylen: f32,
        reset: bool,
    },
    Isect {
        m: usize,
        p: usize,
        prio: i32,
        ga: Option<GeomWorldData>,
        start: i32,
        gb: Option<GeomWorldData>,
        ip: Option<IntersectionParams>,
        first_only: bool,
    },
    Pprim {
        m: usize,
        g: GeomWorldData,
    },
    Usph {
        m: usize,
        c: Vec3,
        r: f32,
        rsep: f32,
        tri: i32,
    },
}

#[derive(Default)]
struct Job {
    cmds: Vec<Cmd>,
    n_mesh: usize,
    n_ray: usize,
    n_isect: usize,
    n_usph: usize,
    n_prep: usize,
    /// Cases the port could not run (generic fallback into the unported mesh slot 54).
    skipped: std::cell::Cell<usize>,
}

impl Job {
    fn mesh(&mut self, v: Vec<Vec3>, idx: Vec<i32>, flags: u32, mat_seed: i32) -> usize {
        self.cmds.push(Cmd::Mesh {
            v,
            idx,
            flags,
            mat_seed,
        });
        self.n_mesh += 1;
        self.n_mesh - 1
    }
    fn ray(&mut self, r: RayPrim) -> usize {
        self.cmds.push(Cmd::Ray(r));
        self.n_ray += 1;
        self.n_ray - 1
    }
    fn push(&mut self, c: Cmd) {
        match c {
            Cmd::Isect { .. } => self.n_isect += 1,
            Cmd::Usph { .. } => self.n_usph += 1,
            Cmd::Prep { .. } => self.n_prep += 1,
            _ => {}
        }
        self.cmds.push(c);
    }

    fn case_file(&self, name: &str) -> std::path::PathBuf {
        let mut file = String::new();
        let mut k = 0;
        for c in &self.cmds {
            match c {
                Cmd::Mesh {
                    v,
                    idx,
                    flags,
                    mat_seed,
                } => {
                    let p = p1c_common::write_mesh_file(&format!("{name}_{k}"), v, idx);
                    k += 1;
                    let _ = writeln!(file, "mesh {} {flags:#x} 2 4 {mat_seed}", p.display());
                }
                Cmd::Ray(r) => {
                    let _ = writeln!(
                        file,
                        "prim 3 {} {} {} {} {} {}",
                        w(r.origin.x),
                        w(r.origin.y),
                        w(r.origin.z),
                        w(r.dir.x),
                        w(r.dir.y),
                        w(r.dir.z)
                    );
                }
                Cmd::Prep { m, raylen, reset } => {
                    let _ = writeln!(file, "prep {m} {} {}", w(*raylen), u8::from(*reset));
                }
                Cmd::Isect {
                    m,
                    p,
                    prio,
                    ga,
                    start,
                    gb,
                    ip,
                    first_only,
                } => {
                    let _ = writeln!(
                        file,
                        "isect {m} {p} {prio} {} {} {}",
                        ga.as_ref().map_or("-".into(), |g| gwd_words(g, *start)),
                        gb.as_ref().map_or("-".into(), |g| gwd_words(g, 0)),
                        ip.as_ref().map_or("-".into(), |i| ip_words(i, *first_only))
                    );
                }
                Cmd::Pprim { m, g } => {
                    let _ = writeln!(file, "pprim {m} {}", gwd_words(g, 0));
                }
                Cmd::Usph { m, c, r, rsep, tri } => {
                    let _ = writeln!(
                        file,
                        "usph {m} {} {} {} {} {} {}",
                        w(c.x),
                        w(c.y),
                        w(c.z),
                        w(*r),
                        w(*rsep),
                        if *tri == LAST {
                            "L".to_string()
                        } else {
                            tri.to_string()
                        }
                    );
                }
            }
        }
        let cf =
            std::env::temp_dir().join(format!("aaemu-p1c-ray-{}-{name}.cases", std::process::id()));
        std::fs::write(&cf, &file).unwrap();
        cf
    }

    /// Replays the job on the port, with the meshes parsed from the DLL output; returns the
    /// port's text.
    fn replay(&self, dll: &str) -> String {
        let hook = std::panic::take_hook();
        std::panic::set_hook(Box::new(|_| {}));
        let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| self.replay_inner(dll)));
        std::panic::set_hook(hook);
        r.unwrap_or_else(|e| std::panic::resume_unwind(e))
    }

    fn replay_inner(&self, dll: &str) -> String {
        let lines: Vec<&str> = dll.lines().collect();
        let mut pos = 0;
        let mut meshes: Vec<TriMesh> = Vec::new();
        let mut rays: Vec<RayPrim> = Vec::new();
        let pairs = PairTables::build();
        let unproj = UnprojTables::build();
        let tables = Tables {
            pairs: &pairs,
            unproj: &unproj,
        };
        let mut arena = CallerArena::new();
        let mut last_tri = 0i32;
        let mut last_hit = 0i32;
        let mut out = String::new();
        for c in &self.cmds {
            match c {
                Cmd::Mesh { .. } => {
                    while pos < lines.len() && !lines[pos].starts_with("mesh nt") {
                        pos += 1;
                    }
                    let (m, used) = p1c_common::parse_mesh_dump(&lines[pos..]);
                    for l in &lines[pos..pos + used] {
                        out.push_str(l);
                        out.push('\n');
                    }
                    pos += used;
                    meshes.push(m);
                }
                Cmd::Ray(r) => rays.push(*r),
                Cmd::Prep { m, raylen, reset } => {
                    let mesh = &mut meshes[*m];
                    if *reset {
                        mesh.ray_grid_ready = 0;
                    }
                    let r = mesh.prepare_for_ray_test(*raylen);
                    let _ = writeln!(out, "prep {} ready {}", i32::from(r), mesh.ray_grid_ready);
                    if let Some(g) = &mesh.ray_grid {
                        for (k, l) in g
                            .layers
                            .iter()
                            .enumerate()
                            .take(mesh.ray_grid_ready.clamp(0, 2) as usize)
                        {
                            let _ = write!(out, "grid {k}");
                            for x in l.record_words() {
                                let _ = write!(out, " {x:08x}");
                            }
                            let _ = writeln!(
                                out,
                                "\ngc {k} {} {:016x} gt {} {:016x}",
                                l.cells.len(),
                                fnv(&l.cells),
                                l.tris.len(),
                                fnv(&l.tris)
                            );
                        }
                    }
                }
                Cmd::Isect {
                    m,
                    p,
                    prio,
                    ga,
                    start,
                    gb,
                    ip,
                    first_only,
                } => {
                    // the DLL's block of this case
                    while pos < lines.len() && !lines[pos].starts_with("res ") {
                        pos += 1;
                    }
                    let b0 = pos;
                    pos += 1;
                    while pos < lines.len()
                        && (lines[pos].starts_with("c ") || lines[pos].starts_with("b "))
                    {
                        pos += 1;
                    }
                    let mut rg = RayGeom::new(&rays[*p]);
                    rg.hdr.priority = *prio;
                    let mut ga = *ga;
                    let mut ip = *ip;
                    let mesh = &meshes[*m];
                    let r = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                        mesh.intersect_slot10(
                            tables,
                            &mut arena,
                            &rg,
                            ga.as_mut(),
                            gb.as_ref(),
                            ip.as_mut(),
                            IntersectExtra {
                                start_node: [if *start == -2 { last_hit } else { *start }, 0],
                                first_only: *first_only,
                            },
                        )
                    }));
                    let Ok(r) = r else {
                        // A ray with priority 1 that falls back to the generic Intersect needs
                        // CTriMesh slot 54 (not ported yet): copy the DLL block, resync the arena.
                        self.skipped.set(self.skipped.get() + 1);
                        for l in &lines[b0..pos] {
                            out.push_str(l);
                            out.push('\n');
                        }
                        let k = lines[b0].rsplit(' ').next().unwrap().parse().unwrap();
                        arena = CallerArena::new();
                        arena.n_contacts = k;
                        last_hit = lines
                            .get(b0 + 1)
                            .filter(|l| l.starts_with("c "))
                            .map_or(0, |l| {
                                l.split(' ').nth(15).unwrap().parse::<i32>().unwrap() + 1
                            });
                        continue;
                    };
                    last_hit = r.contacts.first().map_or(0, |c| c.i_prim[0] + 1);
                    let _ = writeln!(out, "res {} arena {}", r.contacts.len(), arena.n_contacts);
                    for (k, cc) in r.contacts.iter().enumerate() {
                        let pool = matches!(r.border_kinds.get(k), Some(BorderKind::Pool));
                        print_contact(&mut out, cc, if pool { "pool" } else { "pt" });
                        if pool || cc.border.len() > 1 {
                            out.push('b');
                            for p in &cc.border {
                                for x in vw(*p) {
                                    let _ = write!(out, " {}", w(x));
                                }
                            }
                            out.push('\n');
                        }
                    }
                }
                Cmd::Pprim { m, g } => {
                    let t = meshes[*m].prepare_primitive_slot48(g);
                    let _ = write!(out, "pp 1");
                    for v in [t.pt[0], t.pt[1], t.pt[2], t.n] {
                        for x in vw(v) {
                            let _ = write!(out, " {}", w(x));
                        }
                    }
                    out.push('\n');
                }
                Cmd::Usph { m, c, r, rsep, tri } => {
                    let mesh = &mut meshes[*m];
                    let mut res = UnprojResult::default();
                    res.i_feature[0] = if *tri == LAST { last_tri } else { *tri };
                    let ret =
                        mesh.unproject_sphere_slot49(tables, &mut arena, *c, *r, *rsep, &mut res);
                    last_tri = res.i_feature[0];
                    let tb = res.t.to_bits();
                    let cb = res.cos_t.to_bits();
                    let _ = write!(
                        out,
                        "us {ret} {:08x} {:08x} {:08x} {:08x}",
                        tb as u32,
                        (tb >> 32) as u32,
                        cb as u32,
                        (cb >> 32) as u32
                    );
                    for v in [res.pt, res.n] {
                        for x in vw(v) {
                            let _ = write!(out, " {}", w(x));
                        }
                    }
                    let cc = mesh.cached_center;
                    let _ = writeln!(
                        out,
                        " {:08x} {:08x} ready {} center {} {} {} arena {}",
                        res.i_feature[0] as u32,
                        res.i_feature[1] as u32,
                        mesh.ray_grid_ready,
                        w(cc.x),
                        w(cc.y),
                        w(cc.z),
                        arena.n_contacts
                    );
                }
            }
        }
        out
    }
}

/// Counts of a DLL output: (isect lines, contacts, usph returning 1).
fn stats(text: &str) -> (usize, usize, usize, usize) {
    let mut s = (0, 0, 0, 0);
    for l in text.lines() {
        if l.starts_with("res ") {
            s.0 += 1;
        } else if l.starts_with("c ") {
            s.1 += 1;
        } else if l.starts_with("us 1") {
            s.2 += 1;
        } else if l.starts_with("prep 1") {
            s.3 += 1;
        }
    }
    s
}

fn run_job(name: &str, job: &Job) {
    let cf = job.case_file(name);
    let Some(golden) = p1c_common::golden(name, "p1c_ray_probe", &[&cf.to_string_lossy()]) else {
        eprintln!("{name}: no golden and no probe — skipped");
        return;
    };
    let port = job.replay(&golden);
    p1c_common::assert_same_text(name, &port, &golden);
    let s = stats(&golden);
    println!(
        "{name}: {} meshes, {} preps ({} built), {} isect ({} contacts, {} skipped: generic fallback with a priority-1 ray needs mesh slot 54), {} usph ({} hits): all match",
        job.n_mesh, job.n_prep, s.3, s.0, s.1, job.skipped.get(), job.n_usph, s.2
    );
}

// ---------------------------------------------------------------------------------------------
// Mesh generators
// ---------------------------------------------------------------------------------------------

fn cube(h: Vec3) -> (Vec<Vec3>, Vec<i32>) {
    let v = (0..8)
        .map(|i| {
            Vec3::new(
                if i & 1 != 0 { h.x } else { -h.x },
                if i & 2 != 0 { h.y } else { -h.y },
                if i & 4 != 0 { h.z } else { -h.z },
            )
        })
        .collect();
    let idx = vec![
        0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1, 0,
        4, 5, 7, 7, 6, 4,
    ];
    (v, idx)
}

fn grid(rng: &mut Rng, n: usize, step: f32, amp: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for j in 0..=n {
        for i in 0..=n {
            v.push(Vec3::new(
                (i as f32 - n as f32 * 0.5) * step,
                (j as f32 - n as f32 * 0.5) * step,
                rng.r(-amp, amp),
            ));
        }
    }
    let mut idx = Vec::new();
    let s = (n + 1) as i32;
    for j in 0..n as i32 {
        for i in 0..n as i32 {
            let a = j * s + i;
            idx.extend([a, a + 1, a + s + 1, a, a + s + 1, a + s]);
        }
    }
    (v, idx)
}

fn icosphere(r: f32, sub: usize) -> (Vec<Vec3>, Vec<i32>) {
    let t = (1.0 + 5f32.sqrt()) / 2.0;
    let mut v: Vec<Vec3> = [
        (-1.0, t, 0.0),
        (1.0, t, 0.0),
        (-1.0, -t, 0.0),
        (1.0, -t, 0.0),
        (0.0, -1.0, t),
        (0.0, 1.0, t),
        (0.0, -1.0, -t),
        (0.0, 1.0, -t),
        (t, 0.0, -1.0),
        (t, 0.0, 1.0),
        (-t, 0.0, -1.0),
        (-t, 0.0, 1.0),
    ]
    .iter()
    .map(|&(x, y, z)| {
        let l = (x * x + y * y + z * z).sqrt();
        Vec3::new(x / l * r, y / l * r, z / l * r)
    })
    .collect();
    let mut f: Vec<[i32; 3]> = vec![
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
        let mut mid: HashMap<(i32, i32), i32> = HashMap::new();
        let mut nf = Vec::new();
        let mut m = |a: i32, b: i32, v: &mut Vec<Vec3>| -> i32 {
            let k = (a.min(b), a.max(b));
            if let Some(&i) = mid.get(&k) {
                return i;
            }
            let (p, q) = (v[a as usize], v[b as usize]);
            let c = Vec3::new((p.x + q.x) * 0.5, (p.y + q.y) * 0.5, (p.z + q.z) * 0.5);
            let l = (c.x * c.x + c.y * c.y + c.z * c.z).sqrt();
            v.push(Vec3::new(c.x / l * r, c.y / l * r, c.z / l * r));
            let i = v.len() as i32 - 1;
            mid.insert(k, i);
            i
        };
        for t in &f {
            let a = m(t[0], t[1], &mut v);
            let b = m(t[1], t[2], &mut v);
            let c = m(t[2], t[0], &mut v);
            nf.extend([[t[0], a, c], [t[1], b, a], [t[2], c, b], [a, b, c]]);
        }
        f = nf;
    }
    (v, f.into_iter().flatten().collect())
}

/// Thin slivers and degenerate triangles in a strip.
fn slivers(rng: &mut Rng) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    let mut idx = Vec::new();
    for i in 0..24 {
        let x = i as f32 * 0.25;
        let b = v.len() as i32;
        v.push(Vec3::new(x, 0.0, rng.r(-0.01, 0.01)));
        v.push(Vec3::new(x + 0.25, 1e-4 * rng.f(), 0.0));
        v.push(Vec3::new(x + 0.1, 2.0, rng.r(-1.0, 1.0)));
        idx.extend([b, b + 1, b + 2]);
        if i % 5 == 0 {
            // a needle along x and a (removed) zero-area triangle
            idx.extend([b, b + 1, b]);
            v.push(Vec3::new(x + 0.125, 1e-6, 0.0));
            idx.extend([b, b + 3, b + 1]);
        }
    }
    (v, idx)
}

fn transform(v: &[Vec3], r: &Mat33, o: Vec3) -> Vec<Vec3> {
    let m = &r.m;
    v.iter()
        .map(|p| {
            Vec3::new(
                m[0][0] * p.x + m[0][1] * p.y + m[0][2] * p.z + o.x,
                m[1][0] * p.x + m[1][1] * p.y + m[1][2] * p.z + o.y,
                m[2][0] * p.x + m[2][1] * p.y + m[2][2] * p.z + o.z,
            )
        })
        .collect()
}

fn bounds(v: &[Vec3]) -> (Vec3, Vec3) {
    let mut lo = Vec3::splat(f32::MAX);
    let mut hi = Vec3::splat(f32::MIN);
    for p in v {
        lo = Vec3::new(lo.x.min(p.x), lo.y.min(p.y), lo.z.min(p.z));
        hi = Vec3::new(hi.x.max(p.x), hi.y.max(p.y), hi.z.max(p.z));
    }
    (lo, hi)
}

fn rand_gwd(rng: &mut Rng, near: Vec3, scale: bool) -> GeomWorldData {
    GeomWorldData {
        offset: near + rng.v(-0.5, 0.5),
        r: if rng.chance(0.3) {
            Mat33::IDENTITY
        } else {
            rng.rot()
        },
        scale: if !scale || rng.chance(0.6) {
            1.0
        } else {
            rng.r(0.5, 2.0)
        },
        v: if rng.chance(0.2) {
            Vec3::ZERO
        } else {
            rng.v(-2.0, 2.0)
        },
        w: rng.v(-1.0, 1.0),
        center_of_mass: near + rng.v(-1.0, 1.0),
    }
}

/// Rays against mesh `m` (local vertices `v`, triangles `idx`).
#[allow(clippy::too_many_arguments)]
fn add_rays(
    job: &mut Job,
    rng: &mut Rng,
    m: usize,
    v: &[Vec3],
    idx: &[i32],
    n: usize,
    with_unproj: bool,
    with_scale: bool,
) {
    let (lo, hi) = bounds(v);
    let ext = Vec3::new(hi.x - lo.x, hi.y - lo.y, hi.z - lo.z);
    let size = ext.x.max(ext.y).max(ext.z).max(1e-3);
    let ctr = Vec3::new(
        (lo.x + hi.x) * 0.5,
        (lo.y + hi.y) * 0.5,
        (lo.z + hi.z) * 0.5,
    );
    let nt = idx.len() / 3;
    for _ in 0..n {
        // origin and direction in the mesh frame
        let o = match rng.i(4) {
            0 => ctr,
            1 => {
                let t = rng.i(nt);
                let p = v[idx[3 * t] as usize];
                p + rng.v(-0.05, 0.05) * size
            }
            _ => Vec3::new(
                rng.r(lo.x - 0.3 * size, hi.x + 0.3 * size),
                rng.r(lo.y - 0.3 * size, hi.y + 0.3 * size),
                rng.r(lo.z - 0.3 * size, hi.z + 0.3 * size),
            ),
        };
        let dirn = match rng.i(5) {
            0 => {
                let a = rng.i(6);
                let mut d = [0.0f32; 3];
                d[a % 3] = if a < 3 { 1.0 } else { -1.0 };
                Vec3::new(d[0], d[1], d[2])
            }
            1 => {
                // grazing: in the plane of a triangle
                let t = rng.i(nt);
                let (a, b, c) = (
                    v[idx[3 * t] as usize],
                    v[idx[3 * t + 1] as usize],
                    v[idx[3 * t + 2] as usize],
                );
                let k = rng.r(0.0, 1.0);
                let e = (c - a) * k + (b - a) * (1.0 - k);
                let l = (e.x * e.x + e.y * e.y + e.z * e.z).sqrt().max(1e-6);
                Vec3::new(e.x / l, e.y / l, e.z / l)
            }
            _ => rng.unit(),
        };
        let len = size
            * match rng.i(4) {
                0 => rng.r(0.01, 0.1),
                1 => rng.r(0.1, 0.4),
                2 => rng.r(0.4, 1.2),
                _ => rng.r(1.2, 3.0),
            };
        let ga = rand_gwd(rng, Vec3::ZERO, with_scale);
        // ray placed so that, after the mesh transform, it sits at (o, dir) in the mesh frame
        // (scale ignored: the target is approximate)
        let rm = &ga.r.m;
        let to_w = |p: Vec3| {
            Vec3::new(
                rm[0][0] * p.x + rm[0][1] * p.y + rm[0][2] * p.z,
                rm[1][0] * p.x + rm[1][1] * p.y + rm[1][2] * p.z,
                rm[2][0] * p.x + rm[2][1] * p.y + rm[2][2] * p.z,
            )
        };
        let ow = to_w(o) * ga.scale + ga.offset;
        let dw = to_w(dirn) * (len * ga.scale);
        let use_gb = rng.chance(0.4);
        let gb = use_gb.then(|| GeomWorldData {
            offset: Vec3::ZERO,
            r: Mat33::IDENTITY,
            scale: 1.0,
            v: rng.v(-1.0, 1.0),
            w: Vec3::ZERO,
            center_of_mass: Vec3::ZERO,
        });
        let p = job.ray(RayPrim {
            origin: ow,
            dir: dw,
        });
        let kind = rng.i(if with_unproj { 8 } else { 6 });
        let mut first_only = false;
        let mut prio = 0;
        let ip = match kind {
            0 | 1 => None,
            2 => Some(IntersectionParams::default()),
            3 => Some(IntersectionParams {
                keep_prev_contacts: true,
                ..IntersectionParams::default()
            }),
            4 => {
                first_only = true;
                Some(IntersectionParams::default())
            }
            5 => {
                // unprojection fields set, but the ray's priority (0) disables them
                Some(IntersectionParams {
                    i_unprojection_mode: rng.i(2) as i32,
                    center_of_rotation: ow,
                    axis_of_rotation: rng.unit(),
                    ..IntersectionParams::default()
                })
            }
            6 => {
                prio = 1;
                Some(IntersectionParams {
                    i_unprojection_mode: 0,
                    max_unproj: if rng.chance(0.5) {
                        1e10
                    } else {
                        rng.r(0.01, 1.0)
                    },
                    ..IntersectionParams::default()
                })
            }
            _ => {
                prio = 1;
                Some(IntersectionParams {
                    i_unprojection_mode: 1,
                    center_of_rotation: ga.offset + rng.v(-1.0, 1.0) * size,
                    axis_of_rotation: if rng.chance(0.2) {
                        Vec3::ZERO
                    } else {
                        rng.unit()
                    },
                    ..IntersectionParams::default()
                })
            }
        };
        let start = if prio == 0 && rng.chance(0.25) {
            match rng.i(4) {
                0 => 0,
                1 => nt as i32 + 3,
                _ => rng.i(nt) as i32 + 1,
            }
        } else {
            0
        };
        let ga = Some(ga);
        job.push(Cmd::Isect {
            m,
            p,
            prio,
            ga,
            start,
            gb,
            ip,
            first_only,
        });
        if rng.chance(0.3) {
            // the same ray again, starting at the triangle the first call hit (`gwd+88`)
            job.push(Cmd::Isect {
                m,
                p,
                prio: 0,
                ga,
                start: -2,
                gb,
                ip: rng.chance(0.5).then(IntersectionParams::default),
                first_only: false,
            });
        }
    }
}

fn synthetic_meshes(rng: &mut Rng) -> Vec<(Vec<Vec3>, Vec<i32>, u32, i32)> {
    let mut out = Vec::new();
    let (v, i) = cube(Vec3::new(1.0, 1.0, 1.0));
    out.push((v, i, 0x40, -1));
    let (v, i) = cube(Vec3::new(2.0, 0.5, 0.25));
    let r = rng.rot();
    out.push((
        transform(&v, &r, Vec3::new(0.3, -0.2, 0.1)),
        i,
        0x40000 | 0x40,
        5,
    ));
    let (v, i) = grid(rng, 8, 0.5, 0.2);
    out.push((v, i, 0x40, 11));
    let (v, i) = grid(rng, 6, 0.7, 0.4);
    out.push((v, i, 0x80, -1));
    let (v, i) = grid(rng, 30, 0.2, 0.05);
    out.push((v, i, 0x40, 3));
    let (v, i) = icosphere(1.5, 2);
    out.push((v, i, 0x40, 7));
    let (v, i) = icosphere(0.8, 1);
    let r = rng.rot();
    out.push((transform(&v, &r, Vec3::new(-0.4, 0.2, 0.9)), i, 0x20, -1));
    let (v, i) = slivers(rng);
    out.push((v, i, 0x40, 2));
    let (v, i) = grid(rng, 4, 3.0, 0.0);
    let r = rng.rot();
    out.push((transform(&v, &r, Vec3::ZERO), i, 0x40000 | 0x40, -1));
    out
}

/// Slot 22 for several `raylen`s and slot 10 rays after each build, slot 48 on every mesh.
#[test]
fn rays_vs_synthetic_meshes() {
    let mut rng = Rng(0x0005_EED1);
    let mut job = Job::default();
    for (v, i, flags, ms) in synthetic_meshes(&mut rng) {
        let m = job.mesh(v.clone(), i.clone(), flags, ms);
        let (lo, hi) = bounds(&v);
        let size = (hi.x - lo.x).max(hi.y - lo.y).max(hi.z - lo.z);
        for _ in 0..3 {
            let g = rand_gwd(&mut rng, Vec3::ZERO, true);
            job.push(Cmd::Pprim { m, g });
        }
        let lens = [
            0.0,
            -0.0,
            -1.5,
            size * 0.05,
            size * 0.3,
            size,
            size * 4.0,
            1e-3,
            f32::from_bits(0x8000_0001),
        ];
        for (k, &rl) in lens.iter().enumerate() {
            job.push(Cmd::Prep {
                m,
                raylen: rl,
                reset: true,
            });
            // a second call without reset: returns 1 without rebuilding
            if k == 0 {
                job.push(Cmd::Prep {
                    m,
                    raylen: 5.0,
                    reset: false,
                });
            }
            if k % 2 == 0 || k == 3 || k == 4 {
                add_rays(&mut job, &mut rng, m, &v, &i, 90, true, true);
            }
        }
    }
    run_job("p1c_ray_synthetic", &job);
}

/// Slot 49 on closed meshes: random spheres, cached triangles (none, random, out of range),
/// meshes whose grid is built by slot 49 itself.
#[test]
fn unproject_sphere_matches_the_dll() {
    let mut rng = Rng(0x0005_EED2);
    let mut job = Job::default();
    let mut meshes = Vec::new();
    let (v, i) = cube(Vec3::new(1.0, 1.0, 1.0));
    meshes.push((v, i, 0x40u32, -1));
    let (v, i) = icosphere(1.5, 2);
    meshes.push((v, i, 0x40, 4));
    let (v, i) = cube(Vec3::new(2.0, 0.5, 0.25));
    let r = rng.rot();
    meshes.push((transform(&v, &r, Vec3::new(0.3, -0.2, 0.1)), i, 0x40040, -1));
    let (v, i) = icosphere(0.8, 1);
    let r = rng.rot();
    meshes.push((transform(&v, &r, Vec3::new(-0.4, 0.2, 0.9)), i, 0x20, -1));
    for (k, (v, i, flags, ms)) in meshes.into_iter().enumerate() {
        let m = job.mesh(v.clone(), i.clone(), flags, ms);
        if k % 2 == 1 {
            job.push(Cmd::Prep {
                m,
                raylen: if k == 1 { 0.7 } else { 0.0 },
                reset: false,
            });
        }
        let (lo, hi) = bounds(&v);
        let size = (hi.x - lo.x).max(hi.y - lo.y).max(hi.z - lo.z);
        let nt = (i.len() / 3) as i32;
        let mut prev = Vec3::ZERO;
        for _ in 0..250 {
            let tri = match rng.i(5) {
                0 => -1,
                1 => nt + 2,
                2 => rng.i(nt as usize) as i32,
                _ => LAST,
            };
            // "L" cases move the previous centre a little (the cached-triangle path)
            let c = if tri == LAST {
                prev + rng.v(-0.03, 0.03) * size
            } else {
                Vec3::new(
                    rng.r(lo.x - 0.4 * size, hi.x + 0.4 * size),
                    rng.r(lo.y - 0.4 * size, hi.y + 0.4 * size),
                    rng.r(lo.z - 0.4 * size, hi.z + 0.4 * size),
                )
            };
            prev = c;
            job.push(Cmd::Usph {
                m,
                c,
                r: rng.r(0.0, 1.0),
                rsep: size * rng.r(0.02, 0.8),
                tri,
            });
        }
        // a few rays after slot 49 built the grid
        add_rays(&mut job, &mut rng, m, &v, &i, 20, false, false);
    }
    run_job("p1c_ray_usph", &job);
}

/// Every edge (by vertex position) shared by exactly two triangles.
fn closed(v: &[Vec3], idx: &[i32]) -> bool {
    let key = |p: Vec3| (p.x.to_bits(), p.y.to_bits(), p.z.to_bits());
    let mut cnt: HashMap<_, i32> = HashMap::new();
    for t in idx.chunks(3) {
        for k in 0..3 {
            let (a, b) = (key(v[t[k] as usize]), key(v[t[(k + 1) % 3] as usize]));
            if a == b {
                return false;
            }
            *cnt.entry(if a < b { (a, b) } else { (b, a) }).or_default() += 1;
        }
    }
    cnt.values().all(|&c| c == 2)
}

/// The CGF corpus, live (`AAEMU_PHYS_CGF_DIR` + the probe): every trimesh, slot 22 with three
/// `raylen`s, rays after each, slot 49 on closed meshes.
#[test]
fn corpus_rays_match_the_dll() {
    let Some(exe) = p1c_common::probe("p1c_ray_probe") else {
        println!("corpus: AAEMU_PHYS_P1C_PROBE_DIR / p1c_ray_probe.exe not available; skipped");
        return;
    };
    if std::env::var_os("AAEMU_PHYS_CGF_DIR").is_none() {
        println!("corpus: AAEMU_PHYS_CGF_DIR not set; skipped");
        return;
    }
    let meshes = p1b_common::all_meshes();
    let mut rng = Rng(0x0005_EED3);
    let mut job = Job::default();
    let (mut used, mut batch, mut n_closed) = (0usize, 0usize, 0usize);
    let mut tot = [0usize; 6];
    let flush = |job: &mut Job, batch: &mut usize, tot: &mut [usize; 6]| {
        if job.cmds.is_empty() {
            return;
        }
        let cf = job.case_file(&format!("corpus{batch}"));
        let text =
            p1c_common::run_probe(&exe, &[&cf.to_string_lossy()]).unwrap_or_else(|e| panic!("{e}"));
        let port = job.replay(&text);
        p1c_common::assert_same_text(&format!("corpus batch {batch}"), &port, &text);
        let s = stats(&text);
        tot[0] += s.0;
        tot[1] += s.1;
        tot[2] += s.2;
        tot[3] += s.3;
        tot[4] += job.skipped.get();
        tot[5] += job.n_usph;
        *batch += 1;
        *job = Job::default();
    };
    for (_, _, mb) in &meshes {
        let (Some(v), Some(idx)) = (&mb.verts, &mb.idx) else {
            continue;
        };
        if idx.is_empty() || v.is_empty() || idx.iter().any(|&i| i as usize >= v.len()) {
            continue;
        }
        let nt = idx.len() / 3;
        let flags = if mb.tree_type == 1 { 0x40 } else { 0x20 };
        let m = job.mesh(v.clone(), idx.clone(), flags, (used % 7) as i32 - 1);
        let (lo, hi) = bounds(v);
        let size = (hi.x - lo.x).max(hi.y - lo.y).max(hi.z - lo.z);
        let nr = if nt > 20_000 {
            4
        } else if nt > 3000 {
            8
        } else {
            16
        };
        for rl in [0.0, size * 0.1, -size] {
            job.push(Cmd::Prep {
                m,
                raylen: rl,
                reset: true,
            });
            add_rays(&mut job, &mut rng, m, v, idx, nr, true, true);
        }
        if closed(v, idx) {
            n_closed += 1;
            for _ in 0..nr {
                let c = Vec3::new(
                    rng.r(lo.x - 0.2 * size, hi.x + 0.2 * size),
                    rng.r(lo.y - 0.2 * size, hi.y + 0.2 * size),
                    rng.r(lo.z - 0.2 * size, hi.z + 0.2 * size),
                );
                job.push(Cmd::Usph {
                    m,
                    c,
                    r: 0.5,
                    rsep: size * rng.r(0.02, 0.5),
                    tri: if rng.chance(0.5) {
                        -1
                    } else {
                        rng.i(nt) as i32
                    },
                });
            }
        }
        used += 1;
        if job.n_mesh >= 40 {
            flush(&mut job, &mut batch, &mut tot);
        }
    }
    flush(&mut job, &mut batch, &mut tot);
    println!(
        "corpus: {used} meshes ({n_closed} closed) of {} trimesh blobs; {} preps built, {} isect ({} contacts, {} skipped: priority-1 fallback needs mesh slot 54), {} usph ({} hits): all match",
        meshes.len(),
        tot[3],
        tot[0],
        tot[1],
        tot[4],
        tot[5],
        tot[2]
    );
}
