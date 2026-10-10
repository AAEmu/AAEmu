//! P7: the entity parameter interface (`SetParams` / `GetParams` / `GetStatus` / `Action` of
//! `CPhysicalEntity` and `CRigidEntity`, [`aaemu_physics::entities::interface`]) replayed
//! against the oracle goldens `tests/golden/p7_params_*.trace` (regenerated and verified with
//! `AAEMU_PHYS_ORACLE`).
//!
//! The interpreter below runs the subset of the oracle's scenario language these scenarios
//! use — `grid`, `geom`, `entity` (static / rigid, with field setters on the creation
//! `pe_params_pos`), `part` (with field setters on the geomparams), `echo` and
//! `step` (without trace), `params` / `action` / `status` / `getparams` (templates, `ts`, `fN` / `iN` / `uN` / `bN` / `vN` /
//! `qN` / `pN` / `hex` setters, `dump=OFF,N`) — through the raw byte entry points, exactly as
//! the oracle calls the DLL, and must print the golden text.

use aaemu_physics::entities::base::add_geometry;
use aaemu_physics::entities::interface;
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use aaemu_physics::geometry::PhysGeometry;
use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::{check_scenario_golden, scenario_dir};
use aaemu_physics::params::{GeomParams, ParamsPos, PeStruct};
use aaemu_physics::world::{EntIdx, World};
use std::collections::HashMap;
use std::fmt::Write;
use std::sync::Arc;

const UF: u32 = 0xFFBF_FFFF;
const UI: u32 = 0x8000_0000;

fn parse_f(s: &str) -> f32 {
    if s == "U" {
        return f32::from_bits(UF);
    }
    if let Some(h) = s.strip_prefix("h:") {
        return f32::from_bits(u32::from_str_radix(h, 16).unwrap());
    }
    s.parse::<f64>().unwrap() as f32
}

fn parse_u(s: &str) -> u32 {
    if s == "U" {
        return UI;
    }
    if let Some(h) = s.strip_prefix("0x") {
        return u32::from_str_radix(h, 16).unwrap();
    }
    if let Some(h) = s.strip_prefix("-0x") {
        return (-(i64::from_str_radix(h, 16).unwrap())) as u32;
    }
    s.parse::<i64>().unwrap() as u32
}

fn parse_fv(s: &str, n: usize) -> Vec<f32> {
    let mut v: Vec<f32> = s.split(',').take(n).map(parse_f).collect();
    v.resize(n, 0.0);
    v
}

fn v3(s: &str) -> Vec3 {
    let v = parse_fv(s, 3);
    Vec3::new(v[0], v[1], v[2])
}

fn put(b: &mut [u8], o: usize, v: u32) {
    b[o..o + 4].copy_from_slice(&v.to_le_bytes());
}

fn put_p(b: &mut [u8], o: usize, v: u64) {
    b[o..o + 8].copy_from_slice(&v.to_le_bytes());
}

fn fill_unused(b: &mut [u8], from: usize, to: usize) {
    let mut o = from;
    while o + 4 <= to {
        put(b, o, UF);
        o += 4;
    }
}

/// `tmpl_params` of the oracle.
fn tmpl_params(ty: i32, b: &mut [u8]) -> bool {
    b.fill(0);
    put(b, 0, ty as u32);
    match ty {
        0 => {
            fill_unused(b, 4, 40);
            put(b, 56, UI);
            put(b, 60, 1);
        }
        6 => {
            put(b, 4, UI);
            put(b, 8, UI);
            put(b, 12, 1);
            fill_unused(b, 16, 48);
            put(b, 64, UI);
            put(b, 72, u32::MAX);
            put(b, 80, u32::MAX);
            fill_unused(b, 84, 96);
            put_p(b, 96, u64::MAX);
            put_p(b, 104, u64::MAX);
            put(b, 112, UI);
            put_p(b, 120, u64::MAX);
            put(b, 128, UI);
            put_p(b, 136, u64::MAX);
            put(b, 156, UI);
        }
        10 => {
            fill_unused(b, 4, 84);
            put(b, 4, UI);
            put(b, 60, UI);
            put(b, 64, UI);
        }
        12 => {
            fill_unused(b, 4, 80);
            put(b, 72, UI);
        }
        14 => fill_unused(b, 4, 28),
        15 => {
            put(b, 4, UI);
            put(b, 8, UI);
            put(b, 12, UI);
        }
        23 => fill_unused(b, 4, 12),
        _ => return false,
    }
    true
}

/// `tmpl_action` of the oracle.
fn tmpl_action(ty: i32, b: &mut [u8]) -> bool {
    b.fill(0);
    put(b, 0, ty as u32);
    match ty {
        2 => {
            fill_unused(b, 4, 64);
            put(b, 64, UI);
            put(b, 68, UI);
        }
        4 => {}
        8 => {
            put(b, 4, 1);
            fill_unused(b, 8, 12);
        }
        10 => {
            put(b, 4, UI);
            put(b, 8, UI);
            fill_unused(b, 12, 36);
        }
        _ => return false,
    }
    true
}

struct Port {
    w: World,
    geoms: HashMap<String, Arc<PhysGeometry>>,
    ents: HashMap<String, EntIdx>,
    out: String,
    step: u32,
}

impl Port {
    /// `apply_fields` of the oracle (tokens in order).
    fn apply_fields(&self, toks: &[(&str, Option<&str>)], b: &mut [u8]) {
        for &(k, v) in toks.iter().skip(1) {
            let Some(v) = v else { continue };
            if k == "hex" {
                for j in 0..v.len() / 2 {
                    b[j] = u8::from_str_radix(&v[2 * j..2 * j + 2], 16).unwrap();
                }
                continue;
            }
            let mut ch = k.chars();
            let c = ch.next().unwrap();
            let rest = ch.as_str();
            if !"fiuvqpb".contains(c) || !rest.starts_with(|x: char| x.is_ascii_digit()) {
                continue;
            }
            let off: usize = rest.parse().unwrap();
            match c {
                'f' => put(b, off, parse_f(v).to_bits()),
                'i' | 'u' => put(b, off, parse_u(v)),
                'b' => b[off] = parse_u(v) as u8,
                'v' => {
                    for (i, x) in parse_fv(v, 3).into_iter().enumerate() {
                        put(b, off + 4 * i, x.to_bits());
                    }
                }
                'q' => {
                    for (i, x) in parse_fv(v, 4).into_iter().enumerate() {
                        put(b, off + 4 * i, x.to_bits());
                    }
                }
                'p' => match v {
                    "null" => put_p(b, off, 0),
                    "unused" => put_p(b, off, u64::MAX),
                    _ => panic!("pointer value {v} not supported by the test interpreter"),
                },
                _ => unreachable!(),
            }
        }
    }

    fn run_line(&mut self, line: &str) {
        let line = line.split('#').next().unwrap().trim();
        if line.is_empty() {
            return;
        }
        let toks: Vec<(&str, Option<&str>)> = line
            .split_whitespace()
            .map(|t| match t.split_once('=') {
                Some((k, v)) => (k, Some(v)),
                None => (t, None),
            })
            .collect();
        let get = |k: &str| {
            toks.iter()
                .skip(1)
                .find(|t| t.0 == k)
                .map(|t| t.1.unwrap_or(""))
        };
        let cmd = toks[0].0;
        match cmd {
            "grid" => {
                let n = parse_fv(get("n").unwrap_or("64,64"), 2);
                let st = parse_fv(get("step").unwrap_or("4,4"), 2);
                self.w.setup_entity_grid(
                    parse_u(get("axisz").unwrap_or("2")) as usize,
                    v3(get("org").unwrap_or("0,0,-100")),
                    n[0] as i32,
                    n[1] as i32,
                    st[0],
                    st[1],
                    0,
                );
            }
            "geom" => {
                let center = v3(get("center").unwrap_or("0,0,0"));
                let prim = match get("type").unwrap() {
                    "box" => Primitive::Box(BoxPrim {
                        size: v3(get("size").unwrap()),
                        center,
                        ..BoxPrim::default()
                    }),
                    "sphere" => Primitive::Sphere(SpherePrim {
                        center,
                        r: parse_f(get("r").unwrap()),
                    }),
                    t @ ("cylinder" | "capsule") => {
                        let c = CylinderPrim {
                            center,
                            axis: v3(get("axis").unwrap_or("0,0,1")),
                            r: parse_f(get("r").unwrap()),
                            hh: parse_f(get("hh").unwrap()),
                        };
                        if t == "cylinder" {
                            Primitive::Cylinder(c)
                        } else {
                            Primitive::Capsule(c)
                        }
                    }
                    t => panic!("geom type {t}"),
                };
                let g = self.w.register_primitive(&prim).unwrap();
                self.geoms.insert(get("name").unwrap().to_string(), g);
            }
            "entity" => {
                let mut pp = [0u8; 512];
                tmpl_params(0, &mut pp);
                for (i, x) in parse_fv(get("pos").unwrap_or("0,0,0"), 3)
                    .into_iter()
                    .enumerate()
                {
                    put(&mut pp, 4 + 4 * i, x.to_bits());
                }
                let mut q = [0.0, 0.0, 0.0, 1.0];
                if let Some(s) = get("q") {
                    for (i, x) in parse_fv(s, 4).into_iter().enumerate() {
                        q[i] = x;
                    }
                }
                for (i, x) in q.into_iter().enumerate() {
                    put(&mut pp, 16 + 4 * i, x.to_bits());
                }
                self.apply_fields(&toks, &mut pp);
                let ty = match get("type").unwrap() {
                    "static" => 1,
                    "rigid" => 2,
                    t => t.parse().unwrap(),
                };
                let id = get("id").map_or(-1, |s| parse_u(s) as i32);
                let e = self
                    .w
                    .create_physical_entity(ty, 0.0, Some(&ParamsPos::read(&pp)), 0, 0, id)
                    .unwrap();
                self.ents.insert(get("name").unwrap().to_string(), e);
            }
            "part" => {
                let e = self.ents[get("ent").unwrap()];
                let mut gp = [0u8; 512];
                put(&mut gp, 4, parse_f(get("density").unwrap_or("0")).to_bits());
                put(&mut gp, 8, parse_f(get("mass").unwrap_or("0")).to_bits());
                for (i, x) in parse_fv(get("pos").unwrap_or("0,0,0"), 3)
                    .into_iter()
                    .enumerate()
                {
                    put(&mut gp, 12 + 4 * i, x.to_bits());
                }
                let mut q = [0.0, 0.0, 0.0, 1.0];
                if let Some(s) = get("q") {
                    for (i, x) in parse_fv(s, 4).into_iter().enumerate() {
                        q[i] = x;
                    }
                }
                for (i, x) in q.into_iter().enumerate() {
                    put(&mut gp, 24 + 4 * i, x.to_bits());
                }
                put(&mut gp, 40, parse_f(get("scale").unwrap_or("1")).to_bits());
                put(&mut gp, 64, UI);
                put(&mut gp, 68, parse_u(get("flags").unwrap_or("0x1809F")));
                put(&mut gp, 72, parse_u(get("collider").unwrap_or("0x1")));
                put(
                    &mut gp,
                    76,
                    parse_f(get("mincontact").unwrap_or("U")).to_bits(),
                );
                put(&mut gp, 80, u32::MAX);
                put(&mut gp, 108, 1);
                self.apply_fields(&toks, &mut gp);
                let gpr = GeomParams::read(&gp);
                let id = get("id").map_or(-1, |s| parse_u(s) as i32);
                let r = add_geometry(&mut self.w, e, &self.geoms[get("geom").unwrap()], &gpr, id);
                writeln!(self.out, "ret part {} {r}", get("ent").unwrap()).unwrap();
            }
            "step" => {
                let n: usize = get("n").unwrap_or("1").parse().unwrap();
                let dt = parse_f(get("dt").unwrap_or("h:3d088889"));
                let flags = parse_u(get("flags").unwrap_or("0x9F")) as i32;
                assert!(get("every").is_none() && get("trace").is_none());
                for _ in 0..n {
                    self.w.time_step(dt, flags);
                    self.step += 1;
                }
                writeln!(self.out, "step {}", self.step).unwrap();
            }
            "echo" => {
                // the oracle's tokenizer cuts at '=' and prints the keys only
                self.out.push_str("echo");
                for t in &toks[1..] {
                    self.out.push(' ');
                    self.out.push_str(t.0);
                }
                self.out.push('\n');
            }
            "params" | "action" | "status" | "getparams" => {
                let tname = get("target").unwrap();
                let e = self.ents[tname];
                let ty: i32 = get("type").unwrap().parse().unwrap();
                let ts = get("ts").map_or(1, |s| s.parse::<i32>().unwrap());
                let mut b = vec![0u8; 1024];
                let mut ok = false;
                if get("tmpl").unwrap_or("auto") == "auto" {
                    ok = match cmd {
                        "action" => tmpl_action(ty, &mut b[..512]),
                        "params" => tmpl_params(ty, &mut b[..512]),
                        _ => false,
                    };
                }
                if !ok {
                    b.fill(0);
                    put(&mut b, 0, ty as u32);
                }
                self.apply_fields(&toks, &mut b[..512]);
                let r = match cmd {
                    "params" => interface::set_params(&mut self.w, e, &mut b, ts),
                    "action" => interface::action(&mut self.w, e, &mut b, ts),
                    "getparams" => interface::get_params(&mut self.w, e, &mut b),
                    _ => interface::get_status(&mut self.w, e, &mut b),
                };
                write!(self.out, "ret {cmd} {tname} {ty} {r}").unwrap();
                if let Some(d) = get("dump") {
                    let (o, n) = d.split_once(',').unwrap();
                    let (o, n): (usize, usize) = (o.parse().unwrap(), n.parse().unwrap());
                    for i in 0..n {
                        if o + 4 * i >= 512 {
                            break;
                        }
                        let x = u32::from_le_bytes(b[o + 4 * i..o + 4 * i + 4].try_into().unwrap());
                        write!(self.out, " {x:08x}").unwrap();
                    }
                }
                self.out.push('\n');
            }
            c => panic!("command {c} not supported by the test interpreter"),
        }
    }
}

fn run_port(text: &str) -> String {
    let mut p = Port {
        w: World::new(),
        geoms: HashMap::new(),
        ents: HashMap::new(),
        out: String::from("physoracle 1\n"),
        step: 0,
    };
    for line in text.lines() {
        p.run_line(line);
    }
    p.out
}

fn check(name: &str) {
    let golden = check_scenario_golden(name).unwrap_or_else(|e| panic!("{e}"));
    let scn = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn"))).unwrap();
    let port = run_port(&scn);
    for (i, (a, b)) in port.lines().zip(golden.lines()).enumerate() {
        assert_eq!(a, b, "{name} line {}", i + 1);
    }
    assert_eq!(
        port.lines().count(),
        golden.lines().count(),
        "{name}: line count"
    );
}

#[test]
fn base_entity_interface_matches_the_dll() {
    check("p7_params_base");
}

#[test]
fn rigid_params_match_the_dll() {
    check("p7_params_rigid");
}

#[test]
fn rigid_status_and_actions_match_the_dll() {
    check("p7_params_rigid_act");
}

#[test]
fn deferred_calls_match_the_dll() {
    check("p7_params_queue");
}
