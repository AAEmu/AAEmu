//! P3 areas / P4a water test support: replays an oracle scenario (`oracle/p3_areas.c`
//! commands plus the scenario basics) on the port and prints the same lines, so a test can
//! diff the port's output with the golden trace token by token.

#![allow(dead_code)]

use aaemu_physics::entities::base::add_geometry;
use aaemu_physics::entities::rigid::buoyancy;
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use aaemu_physics::geometry::PhysGeometry;
use aaemu_physics::math::{Mat33, Quat, Vec3};
use aaemu_physics::oracle::scenario_trace;
use aaemu_physics::params::{GeomParams, ParamsArea, ParamsBuoyancy, ParamsPos, PeStruct};
use aaemu_physics::world::{EntIdx, World};
use std::collections::HashMap;
use std::fmt::Write as _;
use std::sync::{Arc, Mutex};

const U: u32 = 0xFFBF_FFFF;
const UI: u32 = 0x8000_0000;

fn parse_f(s: &str) -> f32 {
    if s == "U" {
        return f32::from_bits(U);
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
    s.parse::<i64>().unwrap() as u32
}
fn parse_fv(s: &str) -> Vec<f32> {
    s.split(',').map(parse_f).collect()
}
fn v3(s: &str) -> Vec3 {
    let v = parse_fv(s);
    Vec3::new(v[0], v[1], v[2])
}
fn q4(s: &str) -> Quat {
    let v = parse_fv(s);
    Quat::new(v[0], v[1], v[2], v[3])
}
fn hx(f: f32) -> String {
    format!("{:08x}", f.to_bits())
}
fn out_v(o: &mut String, v: Vec3) {
    let _ = write!(o, " {} {} {}", hx(v.x), hx(v.y), hx(v.z));
}

struct Toks<'a> {
    kv: Vec<(&'a str, Option<&'a str>)>,
}
impl<'a> Toks<'a> {
    fn get(&self, k: &str) -> Option<&'a str> {
        self.kv.iter().skip(1).find(|(kk, _)| *kk == k).and_then(|(_, v)| *v)
    }
    fn need(&self, k: &str) -> &'a str {
        self.get(k).unwrap_or_else(|| panic!("missing {k}"))
    }
    fn or(&self, k: &str, d: &'a str) -> &'a str {
        self.get(k).unwrap_or(d)
    }
}

/// The interpreter state.
pub struct Replay {
    pub w: World,
    geoms: HashMap<String, Arc<PhysGeometry>>,
    names: HashMap<String, EntIdx>,
    rnames: HashMap<EntIdx, String>,
    buf: Vec<ParamsBuoyancy>,
    nbuoy: i32,
    pub out: Arc<Mutex<String>>,
}

const BUF_RECS: usize = 8;

fn fill_rec() -> ParamsBuoyancy {
    let mut b = [0u8; 80];
    for o in (0..80).step_by(4) {
        b[o..o + 4].copy_from_slice(&0x7F7F_7F7Fu32.to_le_bytes());
    }
    b[0..4].copy_from_slice(&12u32.to_le_bytes());
    ParamsBuoyancy::read(&b)
}

fn apply_fields(t: &Toks, b: &mut [u8]) {
    for (k, v) in t.kv.iter().skip(1) {
        let Some(v) = v else { continue };
        let c = k.as_bytes()[0];
        if !b"fiuvq".contains(&c) || k.len() < 2 || !k.as_bytes()[1].is_ascii_digit() {
            continue;
        }
        let off: usize = k[1..].parse().unwrap();
        match c {
            b'f' => b[off..off + 4].copy_from_slice(&parse_f(v).to_bits().to_le_bytes()),
            b'i' | b'u' => b[off..off + 4].copy_from_slice(&parse_u(v).to_le_bytes()),
            b'v' | b'q' => {
                for (i, f) in parse_fv(v).iter().enumerate() {
                    b[off + 4 * i..off + 4 * i + 4].copy_from_slice(&f.to_bits().to_le_bytes());
                }
            }
            _ => {}
        }
    }
}

fn fill_unused(b: &mut [u8], from: usize, to: usize) {
    for o in (from..to).step_by(4) {
        b[o..o + 4].copy_from_slice(&U.to_le_bytes());
    }
}
fn rd_f(b: &[u8], o: usize) -> f32 {
    f32::from_le_bytes(b[o..o + 4].try_into().unwrap())
}
fn rd_v(b: &[u8], o: usize) -> Vec3 {
    Vec3::new(rd_f(b, o), rd_f(b, o + 4), rd_f(b, o + 8))
}

impl Default for Replay {
    fn default() -> Self {
        Self::new()
    }
}

impl Replay {
    pub fn new() -> Self {
        let mut r = Replay {
            w: World::new(),
            geoms: HashMap::new(),
            names: HashMap::new(),
            rnames: HashMap::new(),
            buf: Vec::new(),
            nbuoy: 0,
            out: Arc::new(Mutex::new(String::from("physoracle 1\n"))),
        };
        r.buf = vec![fill_rec(); BUF_RECS];
        r
    }

    fn name(&self, e: EntIdx) -> String {
        if e.is_none() {
            return "-".into();
        }
        self.rnames.get(&e).cloned().unwrap_or_else(|| "?".into())
    }

    fn add(&mut self, n: &str, e: EntIdx) {
        self.names.insert(n.to_string(), e);
        self.rnames.entry(e).or_insert_with(|| n.to_string());
    }

    pub fn target(&self, n: &str) -> EntIdx {
        *self.names.get(n).unwrap_or_else(|| panic!("unknown target {n}"))
    }

    fn line(&self, s: String) {
        let mut o = self.out.lock().unwrap();
        o.push_str(&s);
        o.push('\n');
    }

    fn state(&self, e: EntIdx) -> String {
        let ent = self.w.ent(e);
        let r = ent.rigid().unwrap();
        let b = self.w.bodies.get(r.body);
        let mut o = String::from(" S");
        for v in [b.p, b.l, b.w, b.v, r.p_deferred, r.l_deferred] {
            out_v(&mut o, v);
        }
        let _ = write!(
            o,
            " {} {} {:08x} {} {} {} {} {:08x}",
            hx(r.water_damping_eff),
            hx(r.submerged_fraction),
            r.flags2 & 0x1FFF_FFFF,
            hx(r.min_awake_time),
            hx(ent.time_idle),
            ent.sim_class(),
            r.awake_bits & 0xFF,
            r.last_ext_contact_step as u32
        );
        o
    }

    fn buf_words(&self, n: usize) -> String {
        let mut o = String::from(" B");
        for b in self.buf.iter().take(n) {
            let img = b.to_bytes();
            for k in (0..80).step_by(4) {
                let _ = write!(o, " {:08x}", u32::from_le_bytes(img[k..k + 4].try_into().unwrap()));
            }
        }
        o
    }

    fn poke(&mut self, e: EntIdx, off: usize, vals: &[f32], ival: Option<u32>) {
        let w = &mut self.w;
        if let Some(i) = ival {
            match off {
                92 => w.ent_mut(e).flags = i,
                504 => w.ent_mut(e).rigid_mut().unwrap().flags2 = i,
                _ => panic!("poke int offset {off} not mapped"),
            }
            return;
        }
        let v = |k: usize| vals[k];
        let vec = || Vec3::new(vals[0], vals[1], vals[2]);
        let body = w.ent(e).rigid().map(|r| r.body);
        match off {
            408 => w.ent_mut(e).pos = vec(),
            420 => w.ent_mut(e).q = Quat::new(v(0), v(1), v(2), v(3)),
            168 => w.ent_mut(e).pos2 = vec(),
            8 => w.ent_mut(e).bbox[0] = vec(),
            20 => w.ent_mut(e).bbox[1] = vec(),
            448 => w.ent_mut(e).rigid_mut().unwrap().pos_new = vec(),
            460 => w.ent_mut(e).rigid_mut().unwrap().q_new = Quat::new(v(0), v(1), v(2), v(3)),
            476 => w.ent_mut(e).rigid_mut().unwrap().bbox_new[0] = vec(),
            488 => w.ent_mut(e).rigid_mut().unwrap().bbox_new[1] = vec(),
            684 => w.ent_mut(e).rigid_mut().unwrap().damping = v(0),
            692 => w.ent_mut(e).rigid_mut().unwrap().water_damping_eff = v(0),
            1120 => w.ent_mut(e).rigid_mut().unwrap().water_damping = v(0),
            1124 => w.ent_mut(e).rigid_mut().unwrap().k_water_density = v(0),
            1128 => w.ent_mut(e).rigid_mut().unwrap().k_water_resistance = v(0),
            1140 => w.ent_mut(e).rigid_mut().unwrap().k_air_resistance = v(0),
            1192 => w.ent_mut(e).rigid_mut().unwrap().water_level_offset = v(0),
            628 => w.ent_mut(e).rigid_mut().unwrap().gravity_freefall = vec(),
            744 => w.bodies.get_mut(body.unwrap()).pos = vec(),
            772 => w.bodies.get_mut(body.unwrap()).p = vec(),
            784 => w.bodies.get_mut(body.unwrap()).l = vec(),
            796 => w.bodies.get_mut(body.unwrap()).w = vec(),
            808 => w.bodies.get_mut(body.unwrap()).v = vec(),
            _ => panic!("poke offset {off} not mapped"),
        }
    }

    /// Run one scenario line.
    pub fn run_line(&mut self, line: &str) {
        let line = line.split('#').next().unwrap();
        let kv: Vec<(&str, Option<&str>)> = line
            .split_whitespace()
            .map(|s| match s.split_once('=') {
                Some((k, v)) => (k, Some(v)),
                None => (s, None),
            })
            .collect();
        if kv.is_empty() {
            return;
        }
        let t = Toks { kv };
        let c = t.kv[0].0;
        match c {
            "grid" => {
                let org = v3(t.or("org", "0,0,-100"));
                let n: Vec<i32> = t.or("n", "64,64").split(',').map(|s| s.parse().unwrap()).collect();
                let st = parse_fv(t.or("step", "4,4"));
                self.w.setup_entity_grid(
                    t.or("axisz", "2").parse().unwrap(),
                    org,
                    n[0],
                    n[1],
                    st[0],
                    st[1],
                    t.or("pod", "0").parse().unwrap(),
                );
            }
            "geom" => {
                let c = t.get("center").map(v3).unwrap_or(Vec3::ZERO);
                let prim = match t.need("type") {
                    "box" => {
                        let mut basis = Mat33::IDENTITY;
                        let mut oriented = 0;
                        if let Some(bs) = t.get("basis") {
                            let f = parse_fv(bs);
                            basis = Mat33::from_array(f.try_into().unwrap());
                            oriented = 1;
                        }
                        Primitive::Box(BoxPrim {
                            basis,
                            oriented,
                            center: c,
                            size: v3(t.need("size")),
                        })
                    }
                    "sphere" => Primitive::Sphere(SpherePrim {
                        center: c,
                        r: parse_f(t.need("r")),
                    }),
                    ty @ ("cylinder" | "capsule") => {
                        let p = CylinderPrim {
                            center: c,
                            axis: t.get("axis").map(v3).unwrap_or(Vec3::Z),
                            r: parse_f(t.need("r")),
                            hh: parse_f(t.need("hh")),
                        };
                        if ty == "cylinder" {
                            Primitive::Cylinder(p)
                        } else {
                            Primitive::Capsule(p)
                        }
                    }
                    other => panic!("geom type {other}"),
                };
                let g = self.w.register_primitive(&prim).unwrap();
                self.geoms.insert(t.need("name").to_string(), g);
            }
            "entity" => {
                let ty = match t.need("type") {
                    "static" => 1,
                    "rigid" => 2,
                    o => o.parse().unwrap(),
                };
                let p = ParamsPos {
                    pos: v3(t.or("pos", "0,0,0")),
                    q: q4(t.or("q", "0,0,0,1")),
                    ..ParamsPos::default()
                };
                let e = self
                    .w
                    .create_physical_entity(ty, 0.0, Some(&p), 0, 0, t.or("id", "-1").parse().unwrap())
                    .unwrap();
                self.add(t.need("name"), e);
            }
            "part" => {
                let e = self.target(t.need("ent"));
                let g = self.geoms[t.need("geom")].clone();
                let gp = GeomParams {
                    density: parse_f(t.or("density", "0")),
                    mass: parse_f(t.or("mass", "0")),
                    pos: v3(t.or("pos", "0,0,0")),
                    q: q4(t.or("q", "0,0,0,1")),
                    scale: parse_f(t.or("scale", "1")),
                    flags: parse_u(t.or("flags", "0x1809F")),
                    flags_collider: parse_u(t.or("collider", "0x1")),
                    min_contact_dist: parse_f(t.or("mincontact", "U")),
                    ..GeomParams::default()
                };
                let id = add_geometry(&mut self.w, e, &g, &gp, t.or("id", "-1").parse().unwrap());
                self.line(format!("ret part {} {id}", t.need("ent")));
            }
            "area" => {
                let n = t.need("name");
                let a = match t.or("kind", "global") {
                    "global" => {
                        let a = self.w.add_global_area();
                        self.add(n, a);
                        return;
                    }
                    "spline" => {
                        let pts: Vec<Vec3> = t.need("pts").split(';').map(v3).collect();
                        self.w
                            .add_area_spline(
                                &pts,
                                parse_f(t.need("r")),
                                v3(t.or("pos", "0,0,0")),
                                q4(t.or("q", "0,0,0,1")),
                                parse_f(t.or("scale", "1")),
                            )
                            .unwrap()
                    }
                    "geom" => {
                        let g = self.geoms[t.need("geom")].geom.clone();
                        self.w.add_area_geometry(
                            g,
                            v3(t.or("pos", "0,0,0")),
                            q4(t.or("q", "0,0,0,1")),
                            parse_f(t.or("scale", "1")),
                        )
                    }
                    k => panic!("area kind {k}"),
                };
                self.add(n, a);
                let b = self.w.ent(a).bbox;
                let mut o = format!("area {n} bbox");
                out_v(&mut o, b[0]);
                out_v(&mut o, b[1]);
                self.line(o);
            }
            "rmarea" => {
                let a = self.target(t.need("target"));
                self.w.remove_area(a);
            }
            "params" => {
                let name = t.need("target");
                let e = self.target(name);
                let ty: i32 = t.need("type").parse().unwrap();
                let mut b = vec![0u8; 512];
                b[0..4].copy_from_slice(&(ty as u32).to_le_bytes());
                match ty {
                    0 => {
                        fill_unused(&mut b, 4, 40);
                        b[56..60].copy_from_slice(&UI.to_le_bytes());
                        b[60..64].copy_from_slice(&1u32.to_le_bytes());
                    }
                    10 => {
                        fill_unused(&mut b, 4, 84);
                        b[4..8].copy_from_slice(&UI.to_le_bytes());
                        b[60..64].copy_from_slice(&UI.to_le_bytes());
                        b[64..68].copy_from_slice(&UI.to_le_bytes());
                    }
                    12 => {
                        fill_unused(&mut b, 4, 80);
                        b[72..76].copy_from_slice(&UI.to_le_bytes());
                    }
                    18 => {
                        fill_unused(&mut b, 4, 32);
                        b[32..36].copy_from_slice(&UI.to_le_bytes());
                        b[36..40].copy_from_slice(&UI.to_le_bytes());
                        fill_unused(&mut b, 40, 44);
                        b[48..56].copy_from_slice(&u64::MAX.to_le_bytes());
                        fill_unused(&mut b, 56, 72);
                    }
                    _ => panic!("params type {ty}"),
                }
                apply_fields(&t, &mut b);
                let is_area = self.w.is_area(e);
                let r = match (ty, is_area) {
                    (0, true) => self.w.area_set_params_pos(e, &ParamsPos::read(&b)),
                    (10, true) => self.w.area_set_params_simulation(e, rd_f(&b, 16), rd_v(&b, 20)),
                    (12, true) => self.w.area_set_params_buoyancy(e, &ParamsBuoyancy::read(&b)),
                    (18, true) => {
                        let geom = t.get("p48").map(|g| {
                            if g == "null" {
                                None
                            } else {
                                Some(self.geoms[g.strip_prefix("geom:").unwrap()].geom.clone())
                            }
                        });
                        self.w.area_set_params_area(e, &ParamsArea::read(&b), geom)
                    }
                    (12, false) => buoyancy::set_params_buoyancy(&mut self.w, e, &ParamsBuoyancy::read(&b)),
                    (0, false) => aaemu_physics::entities::base::params::set_params_pos(
                        &mut self.w,
                        e,
                        &ParamsPos::read(&b),
                        true,
                    ),
                    _ => panic!("params {ty} on {name}"),
                };
                self.line(format!("ret params {name} {ty} {r}"));
            }
            "checkareas" => {
                let pt = v3(t.need("pt"));
                let vel = v3(t.or("vel", "0,0,0"));
                let ent = t.get("ent").map(|n| self.target(n));
                let nmax = t.or("nmax", "4").parse::<usize>().unwrap().min(BUF_RECS);
                let nareas = t.or("nareas", "0").parse::<usize>().unwrap().min(16);
                let mut areas = vec![EntIdx::NONE; nareas];
                self.buf = vec![fill_rec(); BUF_RECS];
                let mut g = Vec3::new(1.0, 2.0, 3.0);
                let im = parse_u(t.or("imedium", "-1")) as i32;
                let r = self.w.check_areas(
                    pt,
                    &mut g,
                    &mut self.buf[..nmax],
                    im,
                    vel,
                    ent,
                    parse_f(t.or("r", "0")),
                    t.or("caller", "4").parse().unwrap(),
                    &mut areas,
                );
                self.nbuoy = r;
                let mut o = format!("ca {} {r} G", t.or("tag", "-"));
                out_v(&mut o, g);
                o.push_str(&self.buf_words(nmax));
                o.push_str(" A");
                for a in areas {
                    let _ = write!(o, " {}", self.name(a));
                }
                self.line(o);
            }
            "gather" => {
                let e = self.target(t.need("ent"));
                let nmax = t.or("nmax", "4").parse::<usize>().unwrap().min(BUF_RECS);
                self.buf = vec![fill_rec(); BUF_RECS];
                let r = buoyancy::gather_areas_raw(&mut self.w, e, parse_f(t.need("dt")), &mut self.buf[..nmax]);
                self.nbuoy = r;
                let o = format!("ga {} {r}{}{}", t.or("tag", "-"), self.buf_words(nmax), self.state(e));
                self.line(o);
            }
            "abuoy" => {
                let e = self.target(t.need("ent"));
                let g = v3(t.or("g", "0,0,-9.81"));
                let mut n: i32 = t.or("n", "-1").parse().unwrap();
                if n < 0 {
                    n = self.nbuoy;
                }
                let n = n.max(0) as usize;
                let recs: Vec<ParamsBuoyancy> = self.buf[..n].to_vec();
                buoyancy::apply_buoyancy(&mut self.w, e, parse_f(t.need("dt")), g, &recs);
                let o = format!("ab {}{}", t.or("tag", "-"), self.state(e));
                self.line(o);
            }
            "aair" => {
                let e = self.target(t.need("ent"));
                buoyancy::apply_air_resistance(&mut self.w, e, parse_f(t.need("dt")));
                let o = format!("aa {}{}", t.or("tag", "-"), self.state(e));
                self.line(o);
            }
            "estate" => {
                let e = self.target(t.need("ent"));
                let o = format!("es {}{}", t.or("tag", "-"), self.state(e));
                self.line(o);
            }
            "poke" => {
                let e = self.target(t.need("target"));
                let off: usize = t.need("off").parse().unwrap();
                if let Some(v) = t.get("f") {
                    self.poke(e, off, &[parse_f(v)], None);
                } else if let Some(v) = t.get("i") {
                    self.poke(e, off, &[], Some(parse_u(v)));
                } else if let Some(v) = t.get("v").or(t.get("q")) {
                    self.poke(e, off, &parse_fv(v), None);
                }
            }
            "areacb" => {
                let mode: i32 = t.or("mode", "0").parse().unwrap();
                let out = self.out.clone();
                let rn = self.rnames.clone();
                self.w.add_area_callback(
                    parse_f(t.or("prio", "0")),
                    Box::new(move |ev| {
                        if mode == 1 {
                            ev.gravity = Vec3::new(0.0, 0.0, -3.0);
                            ev.buoyancy.water_density = 500.0;
                            ev.buoyancy.water_plane_n = Vec3::new(0.0, 0.0, 1.0);
                            ev.buoyancy.water_plane_origin = Vec3::new(0.0, 0.0, 2.0);
                        } else if mode == 2 {
                            ev.buoyancy.water_flow.x += 1.0;
                        }
                        let mut o = String::from("cb11 pt");
                        out_v(&mut o, ev.pt);
                        o.push_str(" c");
                        out_v(&mut o, ev.closest);
                        o.push_str(" d");
                        out_v(&mut o, ev.dir);
                        let en = ev.ent.map_or("-".to_string(), |e| rn.get(&e).cloned().unwrap_or("?".into()));
                        let _ = write!(o, " e {en}");
                        let mut s = out.lock().unwrap();
                        s.push_str(&o);
                        s.push('\n');
                        1
                    }),
                );
            }
            "areanext" => {
                let a = t.get("after").map(|n| self.target(n));
                let r = self.w.get_next_area(a).unwrap_or(EntIdx::NONE);
                self.line(format!("areanext {}", self.name(r)));
            }
            "areavalid" => {
                let n = t.need("target");
                let a = self.target(n);
                self.line(format!("areavalid {n} {}", i32::from(self.w.is_area_valid(a))));
            }
            "echo" => {
                let mut o = String::from("echo");
                for (k, _) in t.kv.iter().skip(1) {
                    let _ = write!(o, " {k}");
                }
                self.line(o);
            }
            other => panic!("replay: unsupported command {other}"),
        }
    }

    pub fn run(text: &str) -> String {
        let mut r = Replay::new();
        for l in text.lines() {
            r.run_line(l);
        }
        let s = r.out.lock().unwrap().clone();
        s
    }
}

/// Replay `tests/scenarios/<name>.scn` on the port and compare every output line with the
/// golden (regenerated with the oracle when configured). Returns (lines, mismatches).
pub fn check_scenario(name: &str) -> (usize, Vec<String>) {
    let golden = scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"));
    let path = aaemu_physics::oracle::scenario_dir().join(format!("{name}.scn"));
    let text = std::fs::read_to_string(&path).unwrap();
    let port = Replay::run(&text);
    let port = aaemu_physics::trace::Trace::parse(&port).unwrap();
    let mut fails = Vec::new();
    let g: Vec<&Vec<String>> = golden.rets.iter().chain(golden.other.iter()).collect();
    let p: Vec<&Vec<String>> = port.rets.iter().chain(port.other.iter()).collect();
    if g.len() != p.len() {
        fails.push(format!("{name}: {} golden lines vs {} port lines", g.len(), p.len()));
    }
    for (a, b) in g.iter().zip(p.iter()) {
        if a != b {
            let diff: Vec<String> = a
                .iter()
                .zip(b.iter())
                .enumerate()
                .filter(|(_, (x, y))| x != y)
                .take(8)
                .map(|(i, (x, y))| format!("[{i}] {x}/{y}"))
                .collect();
            fails.push(format!("{name}: {} {}: {}", a[0], a.get(1).cloned().unwrap_or_default(), diff.join(" ")));
        }
    }
    (g.len(), fails)
}
