//! Query tests against the oracle goldens: a small scenario interpreter that builds the world
//! through the port (static entities only — the first `step` is emulated by flushing the
//! command queue, which is what the oracle's first step does for static worlds) and prints
//! the `ray` / `pwi` / `rte` / `cewp` / `beam` result lines in the oracle's format.

use super::entity::PosOverride;
use super::pwi::PwiParams;
use super::rwi::{HitLast, RayHit, RwiParams};
use super::QueryIntersectionParams;
use crate::entities::base::add_geometry;
use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use crate::geometry::PhysGeometry;
use crate::math::{Mat33, Quat, Vec3};
use crate::params::{GeomParams, ParamsPos};
use crate::sentinel::{UNUSED_F32, UNUSED_I32};
use crate::world::{EntIdx, World};
use std::collections::HashMap;
use std::fmt::Write as _;
use std::sync::Arc;

fn pf(s: &str) -> f32 {
    if s == "U" {
        return UNUSED_F32;
    }
    if let Some(h) = s.strip_prefix("h:") {
        return f32::from_bits(u32::from_str_radix(h, 16).expect("hex float"));
    }
    s.parse::<f64>().expect("float") as f32
}

fn pv(s: &str) -> Vec<f32> {
    s.split(',').map(pf).collect()
}

fn pu(s: &str) -> u32 {
    if s == "U" {
        return UNUSED_I32 as u32;
    }
    if let Some(h) = s.strip_prefix("0x").or_else(|| s.strip_prefix("0X")) {
        return u32::from_str_radix(h, 16).expect("hex");
    }
    s.parse::<i64>().expect("int") as u32
}

fn v3(s: &str) -> Vec3 {
    let v = pv(s);
    Vec3::new(v[0], v[1], v[2])
}

fn bits(v: Vec3) -> String {
    format!("{:08x} {:08x} {:08x}", v.x.to_bits(), v.y.to_bits(), v.z.to_bits())
}

/// A scenario interpreter for query tests.
pub(crate) struct Runner {
    pub w: World,
    geoms: HashMap<String, Arc<PhysGeometry>>,
    prims: HashMap<String, Primitive>,
    names: Vec<(EntIdx, String)>,
    pub out: String,
    hit_last: HashMap<String, HitLast>,
    /// Hit buffers that persist between `ray` commands with `keep=NAME`.
    bufs: HashMap<String, Vec<RayHit>>,
}

impl Runner {
    pub fn new() -> Self {
        Self {
            w: World::new(),
            geoms: HashMap::new(),
            prims: HashMap::new(),
            names: Vec::new(),
            out: String::new(),
            hit_last: HashMap::new(),
            bufs: HashMap::new(),
        }
    }

    fn name_of(&self, e: EntIdx) -> String {
        if e.is_none() {
            return "-".into();
        }
        for (x, n) in &self.names {
            if *x == e {
                return n.clone();
            }
        }
        for t in &self.w.terrain {
            if *t == e {
                return "terrain".into();
            }
        }
        if e == self.w.global_area && !e.is_none() {
            for (x, n) in &self.names {
                if *x == e {
                    return n.clone();
                }
            }
        }
        "?".into()
    }

    fn ent(&self, name: &str) -> EntIdx {
        self.names
            .iter()
            .find(|(_, n)| n == name)
            .map(|(e, _)| *e)
            .unwrap_or_else(|| panic!("unknown entity {name}"))
    }

    fn print_hit(&mut self, i: usize, h: &RayHit) {
        let line = format!(
            "h {} d {:08x} e {} ip {} pid {} s {} p {} n {}\n",
            i,
            h.dist.to_bits(),
            self.name_of(h.collider),
            h.ipart,
            h.partid,
            h.foreign_idx,
            bits(h.pt),
            bits(h.n)
        );
        self.out.push_str(&line);
    }

    #[allow(clippy::too_many_lines)]
    pub fn run(&mut self, text: &str) {
        for raw in text.lines() {
            let line = raw.split('#').next().unwrap_or("").trim();
            if line.is_empty() {
                continue;
            }
            let mut it = line.split_whitespace();
            let cmd = it.next().unwrap();
            let kv: HashMap<&str, &str> = it.filter_map(|t| t.split_once('=')).collect();
            let get = |k: &str, d: &'static str| -> String { kv.get(k).map_or(d.to_string(), |s| (*s).to_string()) };
            match cmd {
                "grid" => {
                    let org = v3(&get("org", "0,0,-100"));
                    let n: Vec<i32> = get("n", "64,64").split(',').map(|x| x.parse().unwrap()).collect();
                    let st = pv(&get("step", "4,4"));
                    let axisz: usize = get("axisz", "2").parse().unwrap();
                    self.w.setup_entity_grid(axisz, org, n[0], n[1], st[0], st[1], get("pod", "0").parse().unwrap());
                }
                "env" => {
                    if let Some(v) = kv.get("raymax") {
                        self.w.queries.ray_dir_limit = Some(v.parse().unwrap());
                    }
                    if let Some(v) = kv.get("b289") {
                        self.w.queries.env_byte_289 = *v != "0";
                    }
                }
                "var" => {
                    let off: usize = get("off", "0").parse().unwrap();
                    match off {
                        236 => self.w.vars.grid_flags = pu(&get("i", "0")) as i32,
                        _ => panic!("var off {off} not supported by the query runner"),
                    }
                }
                "surface" => {
                    let idx: usize = get("idx", "0").parse().unwrap();
                    self.w.surfaces.flags[idx] = pu(&get("flags", "0")) as i32;
                }
                "geom" => {
                    let name = get("name", "");
                    let ty = get("type", "box");
                    let c = kv.get("center").map_or(Vec3::ZERO, |s| v3(s));
                    let prim = match ty.as_str() {
                        "box" => {
                            let mut b = BoxPrim {
                                basis: Mat33::IDENTITY,
                                oriented: 0,
                                center: c,
                                size: v3(&get("size", "1,1,1")),
                            };
                            if let Some(bs) = kv.get("basis") {
                                let a = pv(bs);
                                let mut arr = [0f32; 9];
                                arr.copy_from_slice(&a[..9]);
                                b.basis = Mat33::from_array(arr);
                                b.oriented = 1;
                            }
                            Primitive::Box(b)
                        }
                        "sphere" => Primitive::Sphere(SpherePrim { center: c, r: pf(&get("r", "1")) }),
                        "cylinder" | "capsule" => {
                            let cp = CylinderPrim {
                                center: c,
                                axis: v3(&get("axis", "0,0,1")),
                                r: pf(&get("r", "1")),
                                hh: pf(&get("hh", "1")),
                            };
                            if ty == "cylinder" {
                                Primitive::Cylinder(cp)
                            } else {
                                Primitive::Capsule(cp)
                            }
                        }
                        other => panic!("geom type {other} not supported by the query runner"),
                    };
                    let g = self.w.register_primitive(&prim).expect("primitive");
                    self.prims.insert(name.clone(), prim);
                    self.geoms.insert(name, g);
                }
                "entity" => {
                    let ty = match get("type", "static").as_str() {
                        "static" => 1,
                        "rigid" => 2,
                        o => o.parse().unwrap(),
                    };
                    let q = pv(&get("q", "0,0,0,1"));
                    let pp = ParamsPos {
                        pos: v3(&get("pos", "0,0,0")),
                        q: Quat::new(q[0], q[1], q[2], q[3]),
                        ..ParamsPos::default()
                    };
                    let id: i32 = get("id", "-1").parse().unwrap();
                    let e = self.w.create_physical_entity(ty, 0.0, Some(&pp), 0, 0, id).expect("entity");
                    if let Some(fd) = kv.get("fdata") {
                        self.w.ent_mut(e).foreign_data = u64::from(pu(fd));
                    }
                    self.names.push((e, get("name", "")));
                }
                "part" => {
                    let e = self.ent(&get("ent", ""));
                    let g = self.geoms.get(&get("geom", "")).expect("geom").clone();
                    let q = pv(&get("q", "0,0,0,1"));
                    let gp = GeomParams {
                        density: pf(&get("density", "0")),
                        mass: pf(&get("mass", "0")),
                        pos: v3(&get("pos", "0,0,0")),
                        q: Quat::new(q[0], q[1], q[2], q[3]),
                        scale: pf(&get("scale", "1")),
                        surface_idx: kv.get("i64").map_or(UNUSED_I32, |s| pu(s) as i32),
                        flags: pu(&get("flags", "0x1809F")),
                        flags_collider: pu(&get("collider", "0x1")),
                        min_contact_dist: pf(&get("mincontact", "U")),
                        idmat_breakable: -1,
                        recalc_bbox: 1,
                        ..GeomParams::default()
                    };
                    let id: i32 = get("id", "-1").parse().unwrap();
                    let r = add_geometry(&mut self.w, e, &g, &gp, id);
                    let _ = writeln!(self.out, "ret part {} {}", get("ent", ""), r);
                }
                "step" => {
                    // static worlds: the first TimeStep flushes the creation queue (grid
                    // insertion, pool allocation) and makes the main thread physics thread 0
                    self.w.flush_command_queue();
                    self.w.tls_thread = 0;
                }
                "ray" | "rayx" => self.cmd_ray(&kv),
                "fdata" => {
                    let e = self.ent(&get("target", ""));
                    self.w.ent_mut(e).foreign_data = u64::from(pu(&get("v", "0")));
                    self.w.ent_mut(e).i_foreign_data = get("i", "0").parse().unwrap();
                }
                "pwi" => self.cmd_pwi(&kv),
                "rte" => self.cmd_rte(&kv),
                "cewp" => self.cmd_cewp(&kv),
                "beam" => self.cmd_beam(&kv),
                "trace" | "print" | "echo" => {}
                other => panic!("command {other} not supported by the query runner"),
            }
        }
    }

    fn skip_list(&self, kv: &HashMap<&str, &str>) -> Vec<EntIdx> {
        kv.get("skip").map_or_else(Vec::new, |s| {
            s.split(',')
                .map(|n| if n == "-" { EntIdx::NONE } else { self.ent(n) })
                .collect()
        })
    }

    fn cmd_ray(&mut self, kv: &HashMap<&str, &str>) {
        let get = |k: &str, d: &'static str| -> String { kv.get(k).map_or(d.to_string(), |s| (*s).to_string()) };
        let nmax: usize = get("max", "1").parse::<usize>().unwrap().clamp(1, 16);
        let p = RwiParams {
            org: v3(&get("org", "0,0,0")),
            dir: v3(&get("dir", "0,0,0")),
            objtypes: pu(&get("types", "0x13F")),
            flags: pu(&get("flags", "0")),
            skip: self.skip_list(kv),
            ..RwiParams::default()
        };
        let keep = kv.get("keep").map(|s| (*s).to_string());
        let mut hits = keep
            .as_ref()
            .and_then(|k| self.bufs.get(k).cloned())
            .unwrap_or_else(|| vec![RayHit::default(); nmax]);
        hits.resize(nmax, RayHit::default());
        let hl_name = kv.get("hitlast").map(|s| (*s).to_string());
        let mut hl = hl_name.as_ref().map(|n| {
            self.hit_last.get(n).copied().unwrap_or(HitLast {
                ent: EntIdx::NONE,
                ipart: 0,
                inode: 0,
            })
        });
        let caller: i32 = get("caller", "4").parse().unwrap();
        let n = self.w.rwi(&p, &mut hits, hl.as_mut(), caller);
        let _ = writeln!(self.out, "ray {} n {}", get("name", "-"), n);
        for (i, h) in hits.clone().iter().enumerate() {
            if h.dist < 0.0 && i > 0 {
                continue;
            }
            self.print_hit(i, h);
        }
        if let (Some(name), Some(h)) = (hl_name, hl) {
            let _ = writeln!(self.out, "hl {} e {} ip {} nd {}", name, self.name_of(h.ent), h.ipart, h.inode);
            self.hit_last.insert(name, h);
        }
        if let Some(k) = keep {
            self.bufs.insert(k, hits);
        }
    }

    fn prim_of(&self, kv: &HashMap<&str, &str>) -> Primitive {
        let g = kv.get("geom").expect("geom=");
        let mut p = self.prims.get(*g).expect("prim").clone();
        if let Some(c) = kv.get("at") {
            let c = v3(c);
            match &mut p {
                Primitive::Box(b) => b.center = c,
                Primitive::Sphere(s) => s.center = c,
                Primitive::Cylinder(cy) | Primitive::Capsule(cy) => cy.center = c,
                _ => {}
            }
        }
        p
    }

    fn print_contact_line(&mut self, c: &crate::collision::contact::GeomContact) {
        let line = format!(
            "c t {:016x} p {} n {} id {} {} pr {} {}\n",
            c.t.to_bits(),
            bits(c.pt),
            bits(c.n),
            c.id[0],
            c.id[1],
            c.i_prim[0],
            c.i_prim[1]
        );
        self.out.push_str(&line);
    }

    fn cmd_pwi(&mut self, kv: &HashMap<&str, &str>) {
        let get = |k: &str, d: &'static str| -> String { kv.get(k).map_or(d.to_string(), |s| (*s).to_string()) };
        let p = PwiParams {
            prim: self.prim_of(kv),
            sweep_dir: v3(&get("sweep", "0,0,0")),
            ent_types: pu(&get("types", "0x13F")),
            geom_flags_all: pu(&get("all", "0")),
            geom_flags_any: pu(&get("any", "0xFFFFFFFF")),
            ip: kv.get("stop").map(|s| QueryIntersectionParams {
                stop_at_first: *s != "0",
                ..QueryIntersectionParams::default()
            }),
            skip: self.skip_list(kv),
            entities_only: kv.contains_key("only"),
            want_contacts: true,
            ..PwiParams::default()
        };
        let p = if p.entities_only {
            PwiParams {
                skip: kv.get("only").map_or_else(Vec::new, |s| s.split(',').map(|n| self.ent(n)).collect()),
                ..p
            }
        } else {
            p
        };
        let r = self.w.pwi(&p);
        let _ = writeln!(self.out, "pwi {} d {:08x}", get("name", "-"), r.dist.to_bits());
        if r.dist != 0.0 {
            if let Some(c) = r.contacts.first() {
                self.print_contact_line(&c.clone());
            }
        }
    }

    fn cmd_rte(&mut self, kv: &HashMap<&str, &str>) {
        let get = |k: &str, d: &'static str| -> String { kv.get(k).map_or(d.to_string(), |s| (*s).to_string()) };
        let e = self.ent(&get("ent", ""));
        let mut h = RayHit::default();
        let pos = kv.get("pos").map(|s| PosOverride {
            pos: v3(s),
            q: {
                let q = pv(&get("q", "0,0,0,1"));
                Quat::new(q[0], q[1], q[2], q[3])
            },
            scale: None,
        });
        let r = self.w.ray_trace_entity(
            e,
            v3(&get("org", "0,0,0")),
            v3(&get("dir", "0,0,0")),
            &mut h,
            pos,
            pu(&get("any", "0xFFFFFFFF")),
            get("backfaces", "0") != "0",
        );
        let _ = writeln!(self.out, "rte {} r {}", get("name", "-"), r);
        if r != 0 {
            self.print_hit(0, &h);
        }
    }

    fn cmd_cewp(&mut self, kv: &HashMap<&str, &str>) {
        let get = |k: &str, d: &'static str| -> String { kv.get(k).map_or(d.to_string(), |s| (*s).to_string()) };
        let e = self.ent(&get("ent", ""));
        let prim = self.prim_of(kv);
        let mut h = RayHit::default();
        let r = self.w.collide_entity_with_primitive(
            e,
            &prim,
            v3(&get("sweep", "0,0,0")),
            &mut h,
            None,
            pu(&get("any", "0xFFFFFFFF")),
            get("inside", "0") != "0",
        );
        let _ = writeln!(self.out, "cewp {} r {}", get("name", "-"), r);
        if r != 0 {
            self.print_hit(0, &h);
        }
    }

    fn cmd_beam(&mut self, kv: &HashMap<&str, &str>) {
        let get = |k: &str, d: &'static str| -> String { kv.get(k).map_or(d.to_string(), |s| (*s).to_string()) };
        let e = self.ent(&get("ent", ""));
        let mut h = RayHit::default();
        let r = self.w.collide_entity_with_beam(
            e,
            v3(&get("org", "0,0,0")),
            v3(&get("dir", "0,0,0")),
            pf(&get("r", "0.5")),
            &mut h,
            pu(&get("any", "0xFFFFFFFF")),
        );
        let _ = writeln!(self.out, "beam {} r {}", get("name", "-"), r);
        if r != 0 {
            self.print_hit(0, &h);
        }
    }
}

/// The query result lines of an oracle trace.
fn result_lines(t: &str) -> Vec<String> {
    const KEYS: [&str; 9] = ["ray ", "h ", "hl ", "pwi ", "c t", "rte ", "cewp ", "beam ", "ret part"];
    t.lines()
        .map(str::trim_end)
        .filter(|l| KEYS.iter().any(|k| l.starts_with(k)))
        .map(str::to_string)
        .collect()
}

/// The golden text of a scenario (regenerated through the oracle when `AAEMU_PHYS_ORACLE` is
/// set; `AAEMU_PHYS_BLESS=1` rewrites mismatching goldens).
pub(crate) fn golden(name: &str) -> String {
    let gpath = crate::oracle::golden_dir().join(format!("{name}.trace"));
    let stored = std::fs::read_to_string(&gpath).ok().map(|s| s.replace("\r\n", "\n"));
    if let Some(exe) = crate::oracle::oracle_path() {
        let fresh = crate::oracle::run_scenario(&exe, &crate::oracle::scenario_dir().join(format!("{name}.scn")))
            .expect("oracle")
            .replace("\r\n", "\n");
        let bless = std::env::var_os(crate::oracle::BLESS_ENV).is_some_and(|v| v != "0");
        match &stored {
            Some(s) if *s == fresh => {}
            _ if bless || stored.is_none() => std::fs::write(&gpath, &fresh).expect("write golden"),
            _ => panic!("{} differs from the oracle; set AAEMU_PHYS_BLESS=1", gpath.display()),
        }
        return fresh;
    }
    stored.unwrap_or_else(|| panic!("golden {} missing", gpath.display()))
}

/// Run scenario `name` through the port and compare its query lines with the golden.
pub(crate) fn check_scenario(name: &str) -> (usize, usize) {
    let g = golden(name);
    let scn = std::fs::read_to_string(crate::oracle::scenario_dir().join(format!("{name}.scn"))).expect("scenario");
    let mut r = Runner::new();
    r.run(&scn);
    let want = result_lines(&g);
    let got = result_lines(&r.out);
    let mut bad = 0;
    let mut first = None;
    for (i, (a, b)) in want.iter().zip(got.iter()).enumerate() {
        if a != b {
            bad += 1;
            if std::env::var_os("P3_DIFF").is_some() {
                eprintln!("DIFF {i}
  o: {a}
  p: {b}");
            }
            if first.is_none() {
                first = Some(format!("line {i}:\n  oracle: {a}\n  port:   {b}"));
            }
        }
    }
    assert!(bad == 0 && want.len() == got.len(), "{name}: {bad} of {} lines differ ({} port lines); first {}", want.len(), got.len(), first.unwrap_or_default());
    (want.len(), bad)
}

#[test]
fn golden_ray_box() {
    check_scenario("ray_box");
}

#[test]
fn golden_p2a1_rwi_box() {
    check_scenario("p2a1_rwi_box");
}

#[test]
fn golden_p2a4_ray_round() {
    check_scenario("p2a4_ray_round");
}

#[test]
fn golden_p3q_basic() {
    check_scenario("p3q_basic");
}
