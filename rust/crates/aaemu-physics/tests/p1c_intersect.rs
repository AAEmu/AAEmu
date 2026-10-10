//! P1c: the generic `CGeometry::Intersect` (0x3514FD70) and the mesh protocol against the DLL.
//!
//! `tests/p1c_isect_probe.c` builds meshes with the DLL's constructor, creates primitive
//! geometries, runs `Intersect` on generated cases and prints every contact as raw bits. The
//! port reads the DLL's mesh state from the same output (`p1c_common::parse_mesh_dump`), runs
//! the same cases and prints the same text.
//!
//! | env | effect |
//! |---|---|
//! | `AAEMU_PHYS_P1C_PROBE_DIR` | directory with `p1c_isect_probe.exe`: goldens regenerated and compared |
//! | `AAEMU_PHYS_BLESS=1` | rewrite mismatching goldens |

mod p1c_common;

use aaemu_physics::collision::contact::{GeomContact, IntersectionParams};
use aaemu_physics::collision::pairs::PairTables;
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, RayPrim, SpherePrim};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use aaemu_physics::geometry::trimesh::intersect::{intersect, IntersectExtra, Tables};
use aaemu_physics::geometry::trimesh::protocol::{CallerArena, GeomProtocol};
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
    fn v(&mut self, a: f32, b: f32) -> Vec3 {
        Vec3::new(self.r(a, b), self.r(a, b), self.r(a, b))
    }
    fn chance(&mut self, p: f32) -> bool {
        self.f() < p
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

/// A primitive of a case: probe type id and words, plus the port object.
#[derive(Clone, Debug)]
#[allow(dead_code)] // solid primitives: cases come with CTriMesh slot 54
enum Prim {
    Box(BoxPrim),
    Sphere(SpherePrim),
    Cyl(CylinderPrim),
    Cap(CylinderPrim),
    Ray(RayPrim),
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
            Prim::Ray(r) => {
                let mut v = vw(r.origin).to_vec();
                v.extend(vw(r.dir));
                (3, v)
            }
        }
    }

    fn geom(&self) -> Box<dyn GeomProtocol> {
        match self {
            Prim::Box(b) => Box::new(BoxGeom::new(b)),
            Prim::Sphere(s) => Box::new(SphereGeom::new(s)),
            Prim::Cyl(c) => Box::new(CylinderGeom::new(c)),
            Prim::Cap(c) => Box::new(CapsuleGeom::new(c)),
            Prim::Ray(r) => Box::new(RayGeom::new(r)),
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

fn ip_words(ip: &IntersectionParams) -> String {
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
        | u32::from(ip.no_area_contacts) << 24;
    d[27] = u32::from(ip.no_intersection);
    d.iter()
        .map(|x| format!("{x:08x}"))
        .collect::<Vec<_>>()
        .join(" ")
}

/// The probe's contact text for a port contact.
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
}

struct Case {
    a: String,
    b: String,
    fnk: i32,
    ga: Option<GeomWorldData>,
    gb: Option<GeomWorldData>,
    ip: Option<IntersectionParams>,
}

fn rand_gwd(rng: &mut Rng, near: Vec3) -> GeomWorldData {
    GeomWorldData {
        offset: near + rng.v(-0.5, 0.5),
        r: if rng.chance(0.3) {
            Mat33::IDENTITY
        } else {
            rng.rot()
        },
        scale: if rng.chance(0.7) {
            1.0
        } else {
            rng.r(0.5, 2.0)
        },
        v: rng.v(-2.0, 2.0),
        w: rng.v(-1.0, 1.0),
        center_of_mass: near + rng.v(-1.0, 1.0),
    }
}

/// Runs a scenario: meshes (files + flags), primitives, cases. Returns the text the probe
/// prints (port side) given the DLL mesh dumps.
fn run(name: &str, meshes: &[(Vec<Vec3>, Vec<i32>, u32)], prims: &[Prim], cases: &[Case]) {
    let mut file = String::new();
    for (k, (v, idx, flags)) in meshes.iter().enumerate() {
        let p = p1c_common::write_mesh_file(&format!("{name}_{k}"), v, idx);
        let _ = writeln!(file, "mesh {} {flags:#x} 2 4", p.display());
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
            "isect {} {} {} {} {} {}",
            c.a,
            c.b,
            c.fnk,
            c.ga.as_ref().map_or("-".into(), gwd_words),
            c.gb.as_ref().map_or("-".into(), gwd_words),
            c.ip.as_ref().map_or("-".into(), ip_words)
        );
    }
    let cf = std::env::temp_dir().join(format!("aaemu-p1c-{}-{name}.cases", std::process::id()));
    std::fs::write(&cf, &file).unwrap();
    let golden = p1c_common::golden(name, "p1c_isect_probe", &[&cf.to_string_lossy()]);
    let Some(golden) = golden else {
        eprintln!("p1c: no golden {name} and no probe — skipped");
        return;
    };
    // port: meshes from the DLL dumps, then the cases
    let lines: Vec<&str> = golden.lines().collect();
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
            IntersectExtra::default(),
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
        for (k, cc) in r.contacts.iter().enumerate() {
            let kind = match r.border_kinds.get(k) {
                Some(aaemu_physics::geometry::trimesh::protocol::BorderKind::Center) => "center",
                Some(aaemu_physics::geometry::trimesh::protocol::BorderKind::Pool) => "pool",
                _ => "pt",
            };
            print_contact(&mut out, cc, kind);
        }
    }
    p1c_common::assert_same_text(name, &out, &golden);
}

fn cube() -> (Vec<Vec3>, Vec<i32>) {
    let v = (0..8)
        .map(|i| {
            Vec3::new(
                if i & 1 != 0 { 1.0 } else { -1.0 },
                if i & 2 != 0 { 1.0 } else { -1.0 },
                if i & 4 != 0 { 1.0 } else { -1.0 },
            )
        })
        .collect();
    let idx = vec![
        0, 4, 6, 6, 2, 0, 1, 3, 7, 7, 5, 1, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 2, 3, 3, 1, 0,
        4, 5, 7, 7, 6, 4,
    ];
    (v, idx)
}

/// A bumpy grid `n×n` of cell size `step`, heights from the generator.
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

#[test]
fn rays_vs_meshes() {
    let mut rng = Rng(12345);
    let meshes = vec![
        {
            let (v, i) = cube();
            (v, i, 0x40)
        },
        {
            let (v, i) = grid(&mut rng, 8, 0.5, 0.2);
            (v, i, 0x40)
        },
        {
            let (v, i) = grid(&mut rng, 6, 0.7, 0.4);
            (v, i, 0x80)
        },
    ];
    let mut prims = Vec::new();
    let mut cases = Vec::new();
    for k in 0..60 {
        let o = rng.v(-2.0, 2.0) + Vec3::new(0.0, 0.0, 3.0);
        let d = Vec3::new(rng.r(-0.5, 0.5), rng.r(-0.5, 0.5), -rng.r(2.0, 8.0));
        prims.push(Prim::Ray(RayPrim { origin: o, dir: d }));
        let m = k % 3;
        let ga = rand_gwd(&mut rng, Vec3::ZERO);
        let gb = rand_gwd(&mut rng, Vec3::ZERO);
        let ip = rng.chance(0.5).then(IntersectionParams::default);
        let (a, b) = if rng.chance(0.5) {
            (format!("m{m}"), format!("p{k}"))
        } else {
            (format!("p{k}"), format!("m{m}"))
        };
        let ident = rng.chance(0.3);
        cases.push(Case {
            a,
            b,
            fnk: 0,
            ga: (!ident).then_some(ga),
            gb: (!ident).then_some(gb),
            ip,
        });
    }
    run("p1c_isect_rays", &meshes, &prims, &cases);
}
