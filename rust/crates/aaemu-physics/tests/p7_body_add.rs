//! P7: multi-part rigid bodies — `RigidBody::Add` (0x3509D460, with the Jacobi solver
//! 0x3511EAA0) and the automatic `idmatBreakable` (0x35052CC0), replayed against the oracle
//! golden `tests/golden/p7_body_add.trace` (regenerated and verified with `AAEMU_PHYS_ORACLE`).
//!
//! The test interprets the subset of the scenario language the scenario uses (`grid`, `surface`,
//! `geom` box/sphere/cylinder, `entity` rigid, `part`, `peek` of the body at `E+744`,
//! `getparams type=6 dump=68,1` = the part flags) on the port and must print the golden text.

use aaemu_physics::entities::base::add_geometry;
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use aaemu_physics::geometry::PhysGeometry;
use aaemu_physics::math::{Quat, Vec3};
use aaemu_physics::oracle::{check_scenario_golden, oracle_path, run_scenario, scenario_dir};
use aaemu_physics::params::{GeomParams, ParamsPos};
use aaemu_physics::sentinel::UNUSED_I32;
use aaemu_physics::solver::RigidBody;
use aaemu_physics::world::{EntIdx, World};
use std::collections::HashMap;
use std::fmt::Write;
use std::sync::Arc;

/// `(float)strtod(s)` / `h:XXXXXXXX` / `U`, as the oracle parses floats.
fn pf(s: &str) -> f32 {
    if let Some(h) = s.strip_prefix("h:") {
        return f32::from_bits(u32::from_str_radix(h, 16).unwrap());
    }
    assert_ne!(s, "U");
    s.parse::<f64>().unwrap() as f32
}

fn pv(s: &str) -> Vec<f32> {
    s.split(',').map(pf).collect()
}

fn v3(s: &str) -> Vec3 {
    let v = pv(s);
    Vec3::new(v[0], v[1], v[2])
}

fn q4(s: &str) -> Quat {
    let v = pv(s);
    Quat::new(v[0], v[1], v[2], v[3])
}

fn pu(s: &str) -> u32 {
    if s == "U" {
        return UNUSED_I32 as u32;
    }
    s.strip_prefix("0x").map_or_else(
        || s.parse::<i64>().unwrap() as u32,
        |h| u32::from_str_radix(h, 16).unwrap(),
    )
}

/// The body as the 53 words of the binary's 212-byte `RigidBody`.
fn body_words(b: &RigidBody) -> Vec<u32> {
    let mut w = Vec::with_capacity(53);
    let v = |w: &mut Vec<u32>, v: Vec3| w.extend([v.x.to_bits(), v.y.to_bits(), v.z.to_bits()]);
    v(&mut w, b.pos);
    w.extend(b.q.to_bits());
    v(&mut w, b.p);
    v(&mut w, b.l);
    v(&mut w, b.w);
    v(&mut w, b.v);
    w.extend([b.m.to_bits(), b.minv.to_bits(), b.volume.to_bits()]);
    v(&mut w, b.ibody);
    v(&mut w, b.ibody_inv);
    w.extend(b.qfb.to_bits());
    v(&mut w, b.offsfb);
    w.extend(b.iinv.to_bits());
    v(&mut w, b.fcoll);
    v(&mut w, b.tcoll);
    let s = b.solver_idx.map(|x| x as u16 as u32);
    w.extend([s[0] | (s[1] << 16), s[2] | (s[3] << 16), b.e_aux.to_bits()]);
    w
}

fn run_port(text: &str) -> String {
    let mut w = World::new();
    let mut geoms: HashMap<String, Arc<PhysGeometry>> = HashMap::new();
    let mut ents: HashMap<String, EntIdx> = HashMap::new();
    let mut out = String::from("physoracle 1\n");
    for line in text.lines() {
        let line = line.split('#').next().unwrap().trim();
        if line.is_empty() {
            continue;
        }
        let mut it = line.split_whitespace();
        let cmd = it.next().unwrap();
        let kv: HashMap<&str, &str> = it.map(|t| t.split_once('=').unwrap()).collect();
        let get =
            |k: &str, d: &'static str| -> String { kv.get(k).copied().unwrap_or(d).to_string() };
        match cmd {
            "grid" => {
                let org = v3(kv["org"]);
                let n = pv(kv["n"]);
                let st = pv(kv["step"]);
                w.setup_entity_grid(
                    pu(kv["axisz"]) as usize,
                    org,
                    n[0] as i32,
                    n[1] as i32,
                    st[0],
                    st[1],
                    0,
                );
            }
            "surface" => {
                let idx = pu(kv["idx"]) as usize;
                w.surfaces.flags[idx] = pu(&get("flags", "0")) as i32;
                writeln!(out, "ret surface 1").unwrap();
            }
            "geom" => {
                let center = v3(&get("center", "0,0,0"));
                let prim = match kv["type"] {
                    "box" => Primitive::Box(BoxPrim {
                        size: v3(kv["size"]),
                        center,
                        ..BoxPrim::default()
                    }),
                    "sphere" => Primitive::Sphere(SpherePrim {
                        center,
                        r: pf(kv["r"]),
                    }),
                    t @ ("cylinder" | "capsule") => {
                        let c = CylinderPrim {
                            center,
                            axis: v3(&get("axis", "0,0,1")),
                            r: pf(kv["r"]),
                            hh: pf(kv["hh"]),
                        };
                        if t == "cylinder" {
                            Primitive::Cylinder(c)
                        } else {
                            Primitive::Capsule(c)
                        }
                    }
                    t => panic!("geom type {t}"),
                };
                geoms.insert(kv["name"].to_string(), w.register_primitive(&prim).unwrap());
            }
            "entity" => {
                assert_eq!(kv["type"], "rigid");
                let pp = ParamsPos {
                    pos: v3(&get("pos", "0,0,0")),
                    q: q4(&get("q", "0,0,0,1")),
                    ..ParamsPos::default()
                };
                let e = w
                    .create_physical_entity(2, 0.0, Some(&pp), 0, 0, -1)
                    .unwrap();
                ents.insert(kv["name"].to_string(), e);
            }
            "part" => {
                let e = ents[kv["ent"]];
                let mut gp = GeomParams {
                    density: pf(&get("density", "0")),
                    mass: pf(&get("mass", "0")),
                    pos: v3(&get("pos", "0,0,0")),
                    q: q4(&get("q", "0,0,0,1")),
                    scale: pf(&get("scale", "1")),
                    flags: pu(&get("flags", "0x1809F")),
                    flags_collider: pu(&get("collider", "0x1")),
                    ..GeomParams::default()
                };
                if let Some(v) = kv.get("i80") {
                    gp.idmat_breakable = pu(v) as i32;
                }
                if let Some(v) = kv.get("i64") {
                    gp.surface_idx = pu(v) as i32;
                }
                let id = add_geometry(&mut w, e, &geoms[kv["geom"]], &gp, -1);
                writeln!(out, "ret part {} {id}", kv["ent"]).unwrap();
            }
            "peek" => {
                assert_eq!((kv["off"], kv["n"]), ("744", "53"));
                let e = ents[kv["target"]];
                let body = w.ent(e).rigid().unwrap().body;
                write!(out, "peek {} 744", kv["target"]).unwrap();
                for x in body_words(w.bodies.get(body)) {
                    write!(out, " {x:08x}").unwrap();
                }
                out.push('\n');
            }
            "getparams" => {
                assert_eq!((kv["type"], kv["dump"]), ("6", "68,1"));
                let e = ents[kv["target"]];
                let ip = pu(kv["i8"]) as usize;
                let flags = w.ent(e).parts[ip].flags;
                writeln!(out, "ret getparams {} 6 1 {flags:08x}", kv["target"]).unwrap();
            }
            c => panic!("command {c} not supported by the test interpreter"),
        }
    }
    out
}

#[test]
fn multi_part_bodies_match_the_dll() {
    let golden = check_scenario_golden("p7_body_add").unwrap_or_else(|e| panic!("{e}"));
    let scn = std::fs::read_to_string(scenario_dir().join("p7_body_add.scn")).unwrap();
    let port = run_port(&scn);
    for (i, (a, b)) in port.lines().zip(golden.lines()).enumerate() {
        assert_eq!(a, b, "line {}", i + 1);
    }
    assert_eq!(port.lines().count(), golden.lines().count());
}

/// Random multi-part bodies (oracle only: skipped without `AAEMU_PHYS_ORACLE`): 40 entities
/// with 2–6 parts of random shapes, placements (raw float bits), masses or densities/scales.
#[test]
fn random_multi_part_bodies_match_the_dll() {
    let Some(exe) = oracle_path() else {
        return;
    };
    let mut seed = 0x2545_F491_4F6C_DD1Du64;
    let mut rnd = move || {
        seed ^= seed << 13;
        seed ^= seed >> 7;
        seed ^= seed << 17;
        (seed >> 40) as f32 / (1u64 << 24) as f32
    };
    let h = |x: f32| format!("h:{:08x}", x.to_bits());
    let mut scn = String::from(
        "grid axisz=2 org=0,0,-100 n=64,64 step=4,4
         geom name=g0 type=box size=0.5,0.5,0.5
         geom name=g1 type=box size=1.3,0.2,0.45
         geom name=g2 type=sphere r=0.35
         geom name=g3 type=cylinder r=0.15 hh=0.9 axis=0,1,0
         geom name=g4 type=capsule r=0.25 hh=0.6 axis=1,0,0
         geom name=g5 type=box size=2,1,0.1 center=0.3,0,0.2
",
    );
    for e in 0..40 {
        let q = |rnd: &mut dyn FnMut() -> f32| {
            let v = [rnd() - 0.5, rnd() - 0.5, rnd() - 0.5, rnd() - 0.5];
            let l = (v.iter().map(|x| x * x).sum::<f32>()).sqrt();
            v.map(|x| x / l)
        };
        let qe = q(&mut rnd);
        let qs = |q: [f32; 4]| q.map(h).join(",");
        scn += &format!(
            "entity name=e{e} type=rigid pos={},{},{} q={}
",
            h(8.0 + rnd() * 240.0),
            h(8.0 + rnd() * 240.0),
            h(rnd() * 20.0),
            qs(qe)
        );
        let np = 2 + (rnd() * 5.0) as usize;
        for _ in 0..np {
            let g = (rnd() * 6.0) as usize;
            let qp = q(&mut rnd);
            let pos = [rnd() * 4.0 - 2.0, rnd() * 4.0 - 2.0, rnd() * 4.0 - 2.0]
                .map(h)
                .join(",");
            let mass = if rnd() < 0.3 {
                format!(
                    "density={} scale={}",
                    h(rnd() * 2000.0 + 1.0),
                    h(0.3 + rnd() * 1.5)
                )
            } else {
                format!("mass={}", h((rnd() * 8.0 - 3.0).exp2() * 10.0))
            };
            scn += &format!(
                "part ent=e{e} geom=g{g} {mass} pos={pos} q={}
",
                qs(qp)
            );
            scn += &format!(
                "peek target=e{e} off=744 n=53
"
            );
        }
    }
    let dir = std::env::temp_dir().join(format!("aaemu-p7-body-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    let path = dir.join("random.scn");
    std::fs::write(&path, &scn).unwrap();
    let oracle = run_scenario(&exe, &path).unwrap();
    let _ = std::fs::remove_dir_all(&dir);
    let port = run_port(&scn);
    let mut bad = 0;
    for (i, (a, b)) in port.lines().zip(oracle.lines()).enumerate() {
        if a != b {
            bad += 1;
            if bad <= 5 {
                eprintln!(
                    "line {}:
  port:   {a}
  oracle: {b}",
                    i + 1
                );
            }
        }
    }
    assert_eq!(port.lines().count(), oracle.lines().count());
    assert_eq!(bad, 0, "{bad} mismatching lines");
    eprintln!("{} lines identical", port.lines().count());
}
