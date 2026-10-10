//! P1c-register: `CTriMesh::RegisterIntersection` (0x351D3950) and its slots 57/58/59 through
//! the generic `CGeometry::Intersect`, against the DLL (`tests/p1c_isect_probe.c`), plus the
//! contact-area polygon clipper sub_35110F50 directly.
//!
//! | env | effect |
//! |---|---|
//! | `AAEMU_PHYS_P1C_PROBE_DIR` | directory with `p1c_isect_probe.exe`: goldens regenerated and compared |
//! | `AAEMU_PHYS_BLESS=1` | rewrite mismatching goldens |
//! | `AAEMU_PHYS_P1C_REG_N=<k>` | (live only, no golden) `k` extra random rounds per test |
//! | `AAEMU_PHYS_CGF_DIR` | corpus meshes for `corpus_meshes` (live only) |

mod p1c_common;

use aaemu_physics::collision::contact::{GeomContact, IntersectionParams};
use aaemu_physics::collision::pairs::PairTables;
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, SpherePrim};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, SphereGeom};
use aaemu_physics::geometry::trimesh::intersect::{intersect, IntersectExtra, Tables};
use aaemu_physics::geometry::trimesh::protocol::{BorderKind, CallerArena, GeomProtocol};
use aaemu_physics::geometry::trimesh::register::polyclip::{
    intersect_polygons, PolyOut, PolyScratch,
};
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::geometry::GeomWorldData;
use aaemu_physics::math::{Mat33, Quat, Vec3};
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
    fn i(&mut self, n: u32) -> u32 {
        (self.f() * n as f32) as u32 % n.max(1)
    }
    fn v(&mut self, a: f32, b: f32) -> Vec3 {
        Vec3::new(self.r(a, b), self.r(a, b), self.r(a, b))
    }
    fn chance(&mut self, p: f32) -> bool {
        self.f() < p
    }
    fn unit(&mut self) -> Vec3 {
        loop {
            let v = self.v(-1.0, 1.0);
            let l = (v.x * v.x + v.y * v.y + v.z * v.z).sqrt();
            if l > 0.1 && l < 1.0 {
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

#[derive(Clone, Debug)]
enum Prim {
    Box(BoxPrim),
    Sphere(SpherePrim),
    Cyl(CylinderPrim),
    Cap(CylinderPrim),
}

impl Prim {
    fn words(&self) -> (i32, Vec<f32>) {
        match self {
            Prim::Box(b) => {
                let mut v = b.basis.to_array().to_vec();
                v.push(f32::from_bits(b.oriented as u32));
                v.extend(vw(b.center));
                v.extend(vw(b.size));
                (0, v)
            }
            Prim::Sphere(s) => {
                let mut v = vw(s.center).to_vec();
                v.push(s.r);
                (4, v)
            }
            Prim::Cyl(c) | Prim::Cap(c) => {
                let mut v = vw(c.center).to_vec();
                v.extend(vw(c.axis));
                v.push(c.r);
                v.push(c.hh);
                (if matches!(self, Prim::Cyl(_)) { 5 } else { 6 }, v)
            }
        }
    }

    fn geom(&self) -> Box<dyn GeomProtocol> {
        match self {
            Prim::Box(b) => Box::new(BoxGeom::new(b)),
            Prim::Sphere(s) => Box::new(SphereGeom::new(s)),
            Prim::Cyl(c) => Box::new(CylinderGeom::new(c)),
            Prim::Cap(c) => Box::new(CapsuleGeom::new(c)),
        }
    }
}

fn gwd_words(g: &GeomWorldData) -> String {
    let mut v = vw(g.offset).to_vec();
    v.extend(g.r.to_array());
    v.push(g.scale);
    v.extend(vw(g.v));
    v.extend(vw(g.w));
    v.extend(vw(g.center_of_mass));
    let mut s: Vec<String> = v.into_iter().map(w).collect();
    s.push("00000000".into());
    s.join(" ")
}

fn ip_words(ip: &IntersectionParams, first: bool) -> String {
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
    let p = ip.unproj_plane_normal;
    d[11..14].copy_from_slice(&[f(p.x), f(p.y), f(p.z)]);
    let u = ip.unproj_dir;
    d[14..17].copy_from_slice(&[f(u.x), f(u.y), f(u.z)]);
    d[17] = f(ip.max_unproj);
    for k in 0..2 {
        let p = ip.pt_outside_pivot[k];
        d[18 + 3 * k..21 + 3 * k].copy_from_slice(&[f(p.x), f(p.y), f(p.z)]);
    }
    d[24] = u32::from(ip.sweep_test)
        | u32::from(ip.keep_prev_contacts) << 8
        | u32::from(first) << 16
        | u32::from(ip.no_area_contacts) << 24;
    d[25] = u32::from(ip.no_border_walk);
    d[26] = ip.border_by_tri_pairs as u32;
    d[27] = u32::from(ip.no_intersection);
    d.iter()
        .map(|x| format!("{x:08x}"))
        .collect::<Vec<_>>()
        .join(" ")
}

/// The probe's contact text for a port contact (`print_contact` + the verbose `x` line).
fn print_contact(s: &mut String, c: &GeomContact, kind: &str) {
    let _ = write!(s, "c {:016x}", c.t.to_bits());
    for v in [c.pt, c.n, c.dir] {
        for x in vw(v) {
            let _ = write!(s, " {}", w(x));
        }
    }
    let _ = write!(
        s,
        " {} {} {} {} {} {} {} {} {} {}",
        c.i_unproj_mode,
        w(c.vel),
        c.id[0],
        c.id[1],
        c.i_prim[0],
        c.i_prim[1],
        c.i_feature[0],
        c.i_feature[1],
        c.i_node[0],
        c.i_node[1]
    );
    let nb = c.border.len();
    let _ = writeln!(
        s,
        " center {} {} {} cons {} nb {} {}",
        w(c.center.x),
        w(c.center.y),
        w(c.center.z),
        u8::from(c.border_consecutive),
        nb,
        kind
    );
    if nb > 1 || kind != "pt" {
        s.push('b');
        for p in &c.border {
            for x in vw(*p) {
                let _ = write!(s, " {}", w(x));
            }
        }
        s.push('\n');
        if !c.idx_border.is_empty() {
            s.push_str("bi");
            for e in &c.idx_border {
                let _ = write!(s, " {} {}", e[0], e[1]);
            }
            s.push('\n');
        }
    }
    if let Some(a) = &c.area {
        let _ = writeln!(
            s,
            "a {} {} {} {} n {} {} {}",
            a.i_type,
            a.npt,
            a.nmaxpt,
            w(a.min_edge),
            w(a.n.x),
            w(a.n.y),
            w(a.n.z)
        );
        for i in 0..a.npt.max(0) as usize {
            let _ = writeln!(
                s,
                "ap {} {} {} {} {} {} {}",
                w(a.pt[i].x),
                w(a.pt[i].y),
                w(a.pt[i].z),
                a.prim[0][i],
                a.feature[0][i],
                a.prim[1][i],
                a.feature[1][i]
            );
        }
    }
    let _ = writeln!(s, "x {}", c.border_closed);
}

#[derive(Clone)]
struct MeshSpec {
    v: Vec<Vec3>,
    idx: Vec<i32>,
    flags: u32,
    mats: Option<Vec<i8>>,
}

struct Case {
    a: String,
    b: String,
    ga: Option<GeomWorldData>,
    gb: Option<GeomWorldData>,
    ip: Option<IntersectionParams>,
    /// `ip+98` (stop after the first contact; needs an `ip`).
    first: bool,
}

/// Runs a scenario through the probe (golden) and the port; panics on the first differing
/// line.
fn run(name: &str, meshes: &[MeshSpec], prims: &[Prim], cases: &[Case], live_only: bool) {
    let mut file = String::from("verbose 1\n");
    for (k, m) in meshes.iter().enumerate() {
        let p = p1c_common::write_mesh_file(&format!("{name}_{k}"), &m.v, &m.idx);
        match &m.mats {
            None => {
                let _ = writeln!(file, "mesh {} {:#x} 2 4", p.display(), m.flags);
            }
            Some(mm) => {
                let _ = write!(file, "meshmat {} {:#x} 2 4", p.display(), m.flags);
                for x in mm {
                    let _ = write!(file, " {x}");
                }
                file.push('\n');
            }
        }
    }
    for p in prims {
        let (t, ws) = p.words();
        let _ = writeln!(
            file,
            "prim {t} {}",
            ws.into_iter().map(w).collect::<Vec<_>>().join(" ")
        );
    }
    for c in cases {
        let _ = writeln!(
            file,
            "isect {} {} 0 {} {} {}",
            c.a,
            c.b,
            c.ga.as_ref().map_or("-".into(), gwd_words),
            c.gb.as_ref().map_or("-".into(), gwd_words),
            c.ip.as_ref().map_or("-".into(), |p| ip_words(p, c.first))
        );
    }
    let cf = std::env::temp_dir().join(format!("aaemu-p1creg-{}-{name}.cases", std::process::id()));
    std::fs::write(&cf, &file).unwrap();
    let dll = if live_only {
        let Some(exe) = p1c_common::probe("p1c_isect_probe") else {
            eprintln!("p1c_reg: {name} needs the probe — skipped");
            return;
        };
        p1c_common::run_probe(&exe, &[&cf.to_string_lossy()]).unwrap_or_else(|e| panic!("{e}"))
    } else {
        let Some(g) = p1c_common::golden(name, "p1c_isect_probe", &[&cf.to_string_lossy()]) else {
            eprintln!("p1c_reg: no golden {name} and no probe — skipped");
            return;
        };
        g
    };
    let lines: Vec<&str> = dll.lines().collect();
    let mut pos = 0;
    let mut port_meshes: Vec<TriMesh> = Vec::new();
    let mut out = String::new();
    for _ in meshes {
        let (m, used) = p1c_common::parse_mesh_dump(&lines[pos..]);
        for l in &lines[pos..pos + used] {
            out.push_str(l);
            out.push('\n');
        }
        pos += used;
        port_meshes.push(m);
    }
    let port_prims: Vec<Box<dyn GeomProtocol>> = prims.iter().map(Prim::geom).collect();
    let pairs = PairTables::build();
    let unproj = UnprojTables::build();
    let tables = Tables {
        pairs: &pairs,
        unproj: &unproj,
    };
    let mut arena = CallerArena::new();
    for c in cases {
        let obj = |s: &str| -> &dyn GeomProtocol {
            let k: usize = s[1..].parse().unwrap();
            if s.starts_with('m') {
                &port_meshes[k]
            } else {
                port_prims[k].as_ref()
            }
        };
        let mut ga = c.ga;
        let mut ip = c.ip;
        let r = intersect(
            tables,
            &mut arena,
            obj(&c.a),
            obj(&c.b),
            ga.as_mut(),
            c.gb.as_ref(),
            ip.as_mut(),
            IntersectExtra {
                first_only: c.first && c.ip.is_some(),
                ..IntersectExtra::default()
            },
        );
        let gv = ga.map_or(Vec3::ZERO, |g| g.v);
        let _ = writeln!(
            out,
            "res {} convex {} gwdav {} {} {} ipv {} sweep {}",
            r.contacts.len(),
            if ip.is_some() { r.ip_convex } else { -1 },
            w(gv.x),
            w(gv.y),
            w(gv.z),
            w(ip.map_or(0.0, |p| p.vrel_min)),
            ip.map_or(-1, |p| i32::from(p.sweep_test))
        );
        match ip {
            Some(p) => {
                let a = p.axis_of_rotation;
                let _ = writeln!(out, "ipx {} {} {} {}", w(a.x), w(a.y), w(a.z), r.ip_convex);
            }
            None => out.push_str("ipx -\n"),
        }
        for (k, cc) in r.contacts.iter().enumerate() {
            let kind = match r.border_kinds.get(k) {
                Some(BorderKind::Center) => "center",
                Some(BorderKind::Pool) => "pool",
                _ => "pt",
            };
            print_contact(&mut out, cc, kind);
        }
    }
    p1c_common::assert_same_text(name, &out, &dll);
}

// ------------------------------------------------------------------------------------------
// meshes

fn cube(h: f32) -> (Vec<Vec3>, Vec<i32>) {
    let v = (0..8)
        .map(|i| {
            Vec3::new(
                if i & 1 != 0 { h } else { -h },
                if i & 2 != 0 { h } else { -h },
                if i & 4 != 0 { h } else { -h },
            )
        })
        .collect();
    let idx = vec![
        0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1, 0,
        4, 5, 7, 7, 6, 4,
    ];
    (v, idx)
}

/// A grid `n×n` of cell size `step`, heights `±amp` (0 = flat; open mesh).
fn grid(rng: &mut Rng, n: usize, step: f32, amp: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for j in 0..=n {
        for i in 0..=n {
            v.push(Vec3::new(
                (i as f32 - n as f32 * 0.5) * step,
                (j as f32 - n as f32 * 0.5) * step,
                if amp > 0.0 { rng.r(-amp, amp) } else { 0.0 },
            ));
        }
    }
    let mut idx = Vec::new();
    let s = (n + 1) as i32;
    for j in 0..n as i32 {
        for i in 0..n as i32 {
            let a = j * s + i;
            if (i + j) % 2 == 0 {
                idx.extend([a, a + 1, a + s + 1, a, a + s + 1, a + s]);
            } else {
                idx.extend([a, a + 1, a + s, a + 1, a + s + 1, a + s]);
            }
        }
    }
    (v, idx)
}

/// An icosphere of radius `r` with `sub` subdivisions (closed, curved).
fn icosphere(r: f32, sub: usize) -> (Vec<Vec3>, Vec<i32>) {
    let t = (1.0 + 5f32.sqrt()) / 2.0;
    let mut v: Vec<Vec3> = [
        [-1.0, t, 0.0],
        [1.0, t, 0.0],
        [-1.0, -t, 0.0],
        [1.0, -t, 0.0],
        [0.0, -1.0, t],
        [0.0, 1.0, t],
        [0.0, -1.0, -t],
        [0.0, 1.0, -t],
        [t, 0.0, -1.0],
        [t, 0.0, 1.0],
        [-t, 0.0, -1.0],
        [-t, 0.0, 1.0],
    ]
    .iter()
    .map(|p| Vec3::new(p[0], p[1], p[2]))
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
        let mut cache = std::collections::HashMap::new();
        let mut mid = |a: i32, b: i32, v: &mut Vec<Vec3>| -> i32 {
            let k = (a.min(b), a.max(b));
            *cache.entry(k).or_insert_with(|| {
                let (p, q) = (v[a as usize], v[b as usize]);
                v.push(Vec3::new(
                    (p.x + q.x) * 0.5,
                    (p.y + q.y) * 0.5,
                    (p.z + q.z) * 0.5,
                ));
                v.len() as i32 - 1
            })
        };
        let mut nf = Vec::new();
        for t3 in &f {
            let a = mid(t3[0], t3[1], &mut v);
            let b = mid(t3[1], t3[2], &mut v);
            let c = mid(t3[2], t3[0], &mut v);
            nf.extend([[t3[0], a, c], [t3[1], b, a], [t3[2], c, b], [a, b, c]]);
        }
        f = nf;
    }
    let v = v
        .into_iter()
        .map(|p| {
            let l = (p.x * p.x + p.y * p.y + p.z * p.z).sqrt();
            Vec3::new(p.x / l * r, p.y / l * r, p.z / l * r)
        })
        .collect();
    (v, f.into_iter().flatten().collect())
}

fn spec(m: (Vec<Vec3>, Vec<i32>), flags: u32, mats: Option<Vec<i8>>) -> MeshSpec {
    MeshSpec {
        v: m.0,
        idx: m.1,
        flags,
        mats,
    }
}

fn rand_mats(rng: &mut Rng, nt: usize, holes: bool) -> Vec<i8> {
    (0..nt)
        .map(|_| {
            if holes && rng.chance(0.1) {
                -1
            } else {
                rng.i(5) as i8
            }
        })
        .collect()
}

fn meshes(rng: &mut Rng) -> Vec<MeshSpec> {
    let mut out = vec![
        spec(cube(1.0), 0x40, None),
        spec(grid(rng, 8, 0.5, 0.0), 0x40, None),
        spec(grid(rng, 8, 0.5, 0.15), 0x40, None),
        spec(icosphere(1.0, 1), 0x40, None),
        spec(grid(rng, 6, 0.7, 0.4), 0x80, None),
    ];
    let g = grid(rng, 6, 0.6, 0.1);
    let nt = g.1.len() / 3;
    let m = rand_mats(rng, nt, true);
    out.push(spec(g, 0x40, Some(m)));
    let c = cube(0.8);
    let m = rand_mats(rng, 12, false);
    out.push(spec(c, 0x40, Some(m)));
    out
}

fn rand_prim(rng: &mut Rng, near: Vec3) -> Prim {
    let c = near + rng.v(-0.6, 0.6);
    match rng.i(4) {
        0 => {
            let basis = if rng.chance(0.4) {
                Mat33::IDENTITY
            } else {
                rng.rot()
            };
            Prim::Box(BoxPrim {
                basis,
                oriented: i32::from(basis != Mat33::IDENTITY),
                center: c,
                size: rng.v(0.15, 0.7),
            })
        }
        1 => Prim::Sphere(SpherePrim {
            center: c,
            r: rng.r(0.15, 0.8),
        }),
        2 => Prim::Cyl(CylinderPrim {
            center: c,
            axis: if rng.chance(0.4) {
                Vec3::new(0.0, 0.0, 1.0)
            } else {
                rng.unit()
            },
            r: rng.r(0.15, 0.6),
            hh: rng.r(0.15, 0.7),
        }),
        _ => Prim::Cap(CylinderPrim {
            center: c,
            axis: if rng.chance(0.4) {
                Vec3::new(0.0, 0.0, 1.0)
            } else {
                rng.unit()
            },
            r: rng.r(0.15, 0.5),
            hh: rng.r(0.15, 0.6),
        }),
    }
}

fn rand_gwd(rng: &mut Rng, near: Vec3, rot: bool) -> GeomWorldData {
    GeomWorldData {
        offset: near + rng.v(-0.3, 0.3),
        r: if !rot || rng.chance(0.4) {
            Mat33::IDENTITY
        } else {
            rng.rot()
        },
        scale: if rng.chance(0.7) {
            1.0
        } else {
            rng.r(0.6, 1.6)
        },
        v: rng.v(-2.0, 2.0),
        w: if rng.chance(0.5) {
            Vec3::ZERO
        } else {
            rng.v(-1.5, 1.5)
        },
        center_of_mass: near + rng.v(-1.0, 1.0),
    }
}

/// An `intersection_params` variant.
fn rand_ip(rng: &mut Rng) -> Option<IntersectionParams> {
    let k = rng.i(10);
    let mut ip = IntersectionParams::default();
    match k {
        0 => return None,
        1 => {}
        2 => ip.no_area_contacts = true,
        3 => ip.vrel_min = 1e10,
        4 => {
            ip.i_unprojection_mode = 1;
            ip.center_of_rotation = rng.v(-1.0, 1.0);
            ip.axis_of_rotation = if rng.chance(0.5) {
                Vec3::ZERO
            } else {
                rng.unit()
            };
            ip.min_axis_dist = if rng.chance(0.5) {
                0.0
            } else {
                rng.r(0.0, 0.5)
            };
        }
        5 => {
            ip.pt_outside_pivot = [rng.v(-3.0, 3.0), rng.v(-3.0, 3.0)];
        }
        6 => {
            ip.max_surface_gap_angle = rng.r(0.0, 0.3);
            ip.time_interval = rng.r(0.01, 3.0);
        }
        7 => ip.unproj_plane_normal = rng.unit(),
        8 => ip.border_by_tri_pairs = 1,
        _ => ip.no_border_walk = true,
    }
    if rng.chance(0.15) {
        ip.keep_prev_contacts = true;
    }
    if rng.chance(0.2) {
        ip.unproj_dir = rng.unit();
    }
    Some(ip)
}

fn mesh_prim_cases(seed: u32, n: usize, name: &str, live_only: bool) {
    let mut rng = Rng(seed);
    let ms = meshes(&mut rng);
    let mut prims = Vec::new();
    let mut cases = Vec::new();
    for k in 0..n {
        let m = rng.i(ms.len() as u32) as usize;
        let near = Vec3::new(rng.r(-1.2, 1.2), rng.r(-1.2, 1.2), rng.r(-0.6, 0.9));
        prims.push(rand_prim(&mut rng, near));
        let ident = rng.chance(0.25);
        let ga = rand_gwd(&mut rng, Vec3::ZERO, true);
        let gb = rand_gwd(&mut rng, Vec3::ZERO, true);
        let ip = rand_ip(&mut rng);
        let (a, b) = if rng.chance(0.5) {
            (format!("m{m}"), format!("p{k}"))
        } else {
            (format!("p{k}"), format!("m{m}"))
        };
        cases.push(Case {
            a,
            b,
            ga: (!ident).then_some(ga),
            gb: (!ident).then_some(gb),
            first: rng.chance(0.05),
            ip,
        });
    }
    run(name, &ms, &prims, &cases, live_only);
}

fn mesh_mesh_cases(seed: u32, n: usize, name: &str, live_only: bool) {
    let mut rng = Rng(seed);
    let ms = meshes(&mut rng);
    let mut cases = Vec::new();
    for _ in 0..n {
        let ma = rng.i(ms.len() as u32);
        let mb = rng.i(ms.len() as u32);
        let ga = rand_gwd(&mut rng, Vec3::ZERO, true);
        let near = Vec3::new(rng.r(-1.0, 1.0), rng.r(-1.0, 1.0), rng.r(-1.5, 1.5));
        let gb = rand_gwd(&mut rng, near, true);
        let ip = rand_ip(&mut rng);
        cases.push(Case {
            a: format!("m{ma}"),
            b: format!("m{mb}"),
            ga: Some(ga),
            gb: Some(gb),
            first: rng.chance(0.05),
            ip,
        });
    }
    run(name, &ms, &[], &cases, live_only);
}

fn extra_rounds() -> u32 {
    std::env::var("AAEMU_PHYS_P1C_REG_N")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(0)
}

#[test]
fn mesh_vs_primitives() {
    mesh_prim_cases(0xC0FFEE, 300, "p1c_reg_mesh_prim", false);
    for k in 0..extra_rounds() {
        mesh_prim_cases(1000 + k, 400, "p1c_reg_mesh_prim_live", true);
    }
}

#[test]
fn mesh_vs_mesh() {
    mesh_mesh_cases(0xBEEF, 150, "p1c_reg_mesh_mesh", false);
    for k in 0..extra_rounds() {
        mesh_mesh_cases(5000 + k, 200, "p1c_reg_mesh_mesh_live", true);
    }
}

// ------------------------------------------------------------------------------------------
// sub_35110F50 directly

fn rand_poly(rng: &mut Rng, n: usize, c: [f32; 2], r: f32, convex: bool) -> Vec<[f32; 2]> {
    let mut ang: Vec<f32> = (0..n).map(|_| rng.r(0.0, std::f32::consts::TAU)).collect();
    ang.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let mut v: Vec<[f32; 2]> = ang
        .iter()
        .map(|a| {
            let rr = if convex { r } else { r * rng.r(0.4, 1.0) };
            [c[0] + rr * a.cos(), c[1] + rr * a.sin()]
        })
        .collect();
    if rng.chance(0.5) {
        v.reverse();
    }
    v
}

fn poly_case(rng: &mut Rng) -> (i32, Vec<[f32; 2]>, Vec<[f32; 2]>) {
    let closed = i32::from(rng.chance(0.6));
    let kind = rng.i(10);
    let n0 = 3 + rng.i(if kind == 9 { 40 } else { 6 }) as usize;
    let cv = rng.chance(0.7);
    let mut p0 = if kind == 0 {
        // axis-aligned square (collinear overlaps with the other square)
        vec![[-1.0, -1.0], [1.0, -1.0], [1.0, 1.0], [-1.0, 1.0]]
    } else {
        rand_poly(rng, n0, [0.0, 0.0], 1.0, cv)
    };
    let p1 = if closed != 0 {
        match kind {
            0 => {
                let o = [rng.i(5) as f32 * 0.5 - 1.0, rng.i(5) as f32 * 0.5 - 1.0];
                let h = 0.5 + rng.i(3) as f32 * 0.5;
                vec![
                    [o[0] - h, o[1] - h],
                    [o[0] + h, o[1] - h],
                    [o[0] + h, o[1] + h],
                    [o[0] - h, o[1] + h],
                ]
            }
            1 => p0.iter().map(|q| [q[0] * 0.5, q[1] * 0.5]).collect(),
            2 => p0.iter().map(|q| [q[0] * 2.0, q[1] * 2.0]).collect(),
            3 => p0.clone(),
            4 => {
                // shares a vertex / an edge with p0
                let nn = 3 + rng.i(4) as usize;
                let mut p = rand_poly(rng, nn, [1.0, 0.0], 0.8, true);
                p[0] = p0[0];
                p
            }
            _ => {
                let n1 = 3 + rng.i(if kind == 9 { 40 } else { 6 }) as usize;
                let c = [rng.r(-1.5, 1.5), rng.r(-1.5, 1.5)];
                let rr = rng.r(0.3, 1.5);
                let cv = rng.chance(0.7);
                rand_poly(rng, n1, c, rr, cv)
            }
        }
    } else {
        let k = 2 + rng.i(3) as usize;
        match kind {
            0 => vec![[-1.0, rng.r(-1.5, 1.5)], [1.0, rng.r(-1.5, 1.5)]],
            1 => vec![p0[0], p0[1 % p0.len()]],
            2 => vec![[0.0, 0.0], [rng.r(-0.5, 0.5), rng.r(-0.5, 0.5)]],
            _ => (0..k)
                .map(|_| [rng.r(-1.6, 1.6), rng.r(-1.6, 1.6)])
                .collect(),
        }
    };
    if rng.chance(0.03) {
        p0.truncate(2);
    }
    (closed, p0, p1)
}

fn poly_cases(seed: u32, n: usize, name: &str, live_only: bool) {
    let mut rng = Rng(seed);
    let mut cases = Vec::new();
    for _ in 0..n {
        cases.push(poly_case(&mut rng));
    }
    let mut file = String::new();
    for (closed, p0, p1) in &cases {
        let _ = write!(file, "poly {closed}");
        for p in [p0, p1] {
            let _ = write!(file, " {}", p.len());
            for q in p.iter().chain(std::iter::once(&p[0])) {
                let _ = write!(file, " {} {}", w(q[0]), w(q[1]));
            }
        }
        file.push('\n');
    }
    let cf = std::env::temp_dir().join(format!("aaemu-p1creg-{}-{name}.cases", std::process::id()));
    std::fs::write(&cf, &file).unwrap();
    let dll = if live_only {
        let Some(exe) = p1c_common::probe("p1c_isect_probe") else {
            return;
        };
        p1c_common::run_probe(&exe, &[&cf.to_string_lossy()]).unwrap_or_else(|e| panic!("{e}"))
    } else {
        let Some(g) = p1c_common::golden(name, "p1c_isect_probe", &[&cf.to_string_lossy()]) else {
            eprintln!("p1c_reg: no golden {name} and no probe — skipped");
            return;
        };
        g
    };
    let mut scr = PolyScratch::default();
    let mut out = String::new();
    for (closed, p0, p1) in &cases {
        let a: Vec<[f32; 2]> = p0.iter().chain(std::iter::once(&p0[0])).copied().collect();
        let b: Vec<[f32; 2]> = p1.iter().chain(std::iter::once(&p1[0])).copied().collect();
        let (r, which) = intersect_polygons(
            &mut scr,
            [&a, &b],
            [p0.len() as i32, p1.len() as i32],
            *closed,
        );
        let (src, pts) = match which {
            PolyOut::Input0 => (0, a.clone()),
            PolyOut::Input1 => (1, b.clone()),
            PolyOut::Own(v) => (2, v),
        };
        let _ = writeln!(out, "poly {r} {src}");
        for (q, id) in pts.iter().zip(&scr.ids).take(r.max(0) as usize) {
            let _ = writeln!(out, "pp {} {} {}", w(q[0]), w(q[1]), id);
        }
    }
    if out != dll {
        let split = |t: &str| -> Vec<String> {
            let mut v: Vec<String> = Vec::new();
            for l in t.lines() {
                if l.starts_with("poly ") {
                    v.push(String::new());
                }
                let last = v.last_mut().unwrap();
                last.push_str(l);
                last.push('\n');
            }
            v
        };
        let (po, dl) = (split(&out), split(&dll));
        for (k, (a, b)) in po.iter().zip(&dl).enumerate() {
            if a != b {
                panic!("{name} case {k}: {:?} port: {a} dll: {b}", cases[k]);
            }
        }
        p1c_common::assert_same_text(name, &out, &dll);
    }
}

#[test]
fn polygon_clipper() {
    poly_cases(77, 3000, "p1c_reg_polyclip", false);
    for k in 0..extra_rounds() {
        poly_cases(9000 + k, 5000, "p1c_reg_polyclip_live", true);
    }
}

// ------------------------------------------------------------------------------------------
// resting contacts (face–face / face–edge areas, walks around flat patches)

fn resting_meshes(rng: &mut Rng) -> Vec<MeshSpec> {
    let g = grid(rng, 6, 0.6, 0.0);
    let nt = g.1.len() / 3;
    let m = rand_mats(rng, nt, true);
    vec![
        spec(grid(rng, 8, 0.5, 0.0), 0x40, None),
        spec(cube(1.0), 0x40, None),
        spec(grid(rng, 8, 0.5, 0.03), 0x40, None),
        spec(cube(1.0), 0x80, None),
        spec(g, 0x40, Some(m)),
        spec(icosphere(1.2, 2), 0x40, None),
    ]
}

/// A primitive lying on the plane z = top (slightly penetrating), small tilts.
fn resting_prim(rng: &mut Rng, top: f32) -> Prim {
    let tilt = |rng: &mut Rng| -> Mat33 {
        if rng.chance(0.5) {
            Mat33::IDENTITY
        } else {
            let a = rng.r(-0.02, 0.02);
            let b = rng.r(-0.02, 0.02);
            let c = rng.r(-3.0, 3.0);
            let q = Quat {
                x: a,
                y: b,
                z: (c * 0.5).sin(),
                w: (c * 0.5).cos(),
            };
            let l = ((q.x * q.x + q.y * q.y) + q.z * q.z + q.w * q.w).sqrt();
            Mat33::from_quat(Quat {
                x: q.x / l,
                y: q.y / l,
                z: q.z / l,
                w: q.w / l,
            })
        }
    };
    let pen = rng.r(-0.01, 0.06);
    let xy = Vec3::new(rng.r(-1.2, 1.2), rng.r(-1.2, 1.2), 0.0);
    match rng.i(4) {
        0 => {
            let size = rng.v(0.1, 0.6);
            let basis = tilt(rng);
            Prim::Box(BoxPrim {
                basis,
                oriented: i32::from(basis != Mat33::IDENTITY),
                center: Vec3::new(xy.x, xy.y, top + size.z - pen),
                size,
            })
        }
        1 => {
            // cylinder standing or lying
            let r = rng.r(0.1, 0.5);
            let hh = rng.r(0.1, 0.6);
            let lying = rng.chance(0.5);
            let axis = if lying {
                let a = rng.r(0.0, std::f32::consts::TAU);
                Vec3::new(a.cos(), a.sin(), rng.r(-0.01, 0.01))
            } else {
                Vec3::new(rng.r(-0.01, 0.01), rng.r(-0.01, 0.01), 1.0)
            };
            let l = (axis.x * axis.x + axis.y * axis.y + axis.z * axis.z).sqrt();
            let axis = Vec3::new(axis.x / l, axis.y / l, axis.z / l);
            let h = if lying { r } else { hh };
            Prim::Cyl(CylinderPrim {
                center: Vec3::new(xy.x, xy.y, top + h - pen),
                axis,
                r,
                hh,
            })
        }
        2 => {
            let r = rng.r(0.1, 0.5);
            Prim::Sphere(SpherePrim {
                center: Vec3::new(xy.x, xy.y, top + r - pen),
                r,
            })
        }
        _ => {
            let r = rng.r(0.1, 0.4);
            let hh = rng.r(0.1, 0.5);
            let a = rng.r(0.0, std::f32::consts::TAU);
            Prim::Cap(CylinderPrim {
                center: Vec3::new(xy.x, xy.y, top + r - pen),
                axis: Vec3::new(a.cos(), a.sin(), 0.0),
                r,
                hh,
            })
        }
    }
}

fn resting_ip(rng: &mut Rng) -> Option<IntersectionParams> {
    let mut ip = IntersectionParams::default();
    match rng.i(8) {
        0 => return None,
        1 => ip.max_surface_gap_angle = rng.r(0.05, 0.35),
        2 => ip.vrel_min = 1e10,
        3 => {
            ip.vrel_min = 1e10;
            ip.max_surface_gap_angle = rng.r(0.05, 0.35);
        }
        4 => {
            ip.i_unprojection_mode = 1;
            ip.center_of_rotation = rng.v(-1.0, 1.0);
            ip.axis_of_rotation = if rng.chance(0.5) {
                Vec3::ZERO
            } else {
                rng.unit()
            };
        }
        5 => ip.border_by_tri_pairs = 1,
        6 => ip.no_border_walk = true,
        _ => {}
    }
    if rng.chance(0.1) {
        ip.keep_prev_contacts = true;
    }
    Some(ip)
}

fn resting_cases(seed: u32, n: usize, name: &str, live_only: bool) {
    let mut rng = Rng(seed);
    let ms = resting_meshes(&mut rng);
    let tops = [0.0f32, 1.0, 0.0, 1.0, 0.0, 1.2];
    let mut prims = Vec::new();
    let mut cases = Vec::new();
    for k in 0..n {
        let m = rng.i(ms.len() as u32) as usize;
        prims.push(resting_prim(&mut rng, tops[m]));
        let still = rng.chance(0.5);
        let g = |rng: &mut Rng| GeomWorldData {
            offset: Vec3::ZERO,
            r: Mat33::IDENTITY,
            scale: 1.0,
            v: if still { Vec3::ZERO } else { rng.v(-1.0, 1.0) },
            w: if still || rng.chance(0.5) {
                Vec3::ZERO
            } else {
                rng.v(-1.0, 1.0)
            },
            center_of_mass: rng.v(-1.0, 1.0),
        };
        let ga = g(&mut rng);
        let gb = g(&mut rng);
        let ip = resting_ip(&mut rng);
        let (a, b) = if rng.chance(0.5) {
            (format!("m{m}"), format!("p{k}"))
        } else {
            (format!("p{k}"), format!("m{m}"))
        };
        let first = rng.chance(0.05);
        cases.push(Case {
            a,
            b,
            ga: Some(ga),
            gb: Some(gb),
            ip,
            first,
        });
    }
    run(name, &ms, &prims, &cases, live_only);
}

/// Mesh on mesh: a cube resting on a grid or another cube.
fn resting_mesh_cases(seed: u32, n: usize, name: &str, live_only: bool) {
    let mut rng = Rng(seed);
    let ms = resting_meshes(&mut rng);
    let mut cases = Vec::new();
    for _ in 0..n {
        let base = [0usize, 2, 4, 1, 5][rng.i(5) as usize];
        let top = [0.0f32, 1.0, 0.0, 1.0, 0.0, 1.2][base];
        let s = rng.r(0.2, 0.7);
        let pen = rng.r(-0.01, 0.05);
        let a = rng.r(-3.0, 3.0);
        let rot = if rng.chance(0.4) {
            Mat33::IDENTITY
        } else {
            Mat33::from_quat(Quat {
                x: 0.0,
                y: 0.0,
                z: (a * 0.5).sin(),
                w: (a * 0.5).cos(),
            })
        };
        let still = rng.chance(0.5);
        let gb = GeomWorldData {
            offset: Vec3::new(rng.r(-1.0, 1.0), rng.r(-1.0, 1.0), top + s - pen),
            r: rot,
            scale: s,
            v: if still { Vec3::ZERO } else { rng.v(-1.0, 1.0) },
            w: Vec3::ZERO,
            center_of_mass: rng.v(-1.0, 1.0),
        };
        let ga = GeomWorldData {
            offset: Vec3::ZERO,
            r: Mat33::IDENTITY,
            scale: 1.0,
            v: Vec3::ZERO,
            w: Vec3::ZERO,
            center_of_mass: Vec3::ZERO,
        };
        let ip = resting_ip(&mut rng);
        let swap = rng.chance(0.5);
        let first = rng.chance(0.05);
        let (a, b, ga, gb) = if swap {
            ("m1".to_string(), format!("m{base}"), gb, ga)
        } else {
            (format!("m{base}"), "m1".to_string(), ga, gb)
        };
        cases.push(Case {
            a,
            b,
            ga: Some(ga),
            gb: Some(gb),
            ip,
            first,
        });
    }
    run(name, &ms, &[], &cases, live_only);
}

#[test]
fn resting_contacts() {
    resting_cases(31337, 300, "p1c_reg_resting", false);
    resting_mesh_cases(4242, 120, "p1c_reg_resting_mesh", false);
    for k in 0..extra_rounds() {
        resting_cases(20000 + k, 400, "p1c_reg_resting_live", true);
        resting_mesh_cases(30000 + k, 150, "p1c_reg_resting_mesh_live", true);
    }
}
