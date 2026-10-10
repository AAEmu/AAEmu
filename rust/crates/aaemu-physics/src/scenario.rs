//! A port-side interpreter of the oracle's scenario language (`oracle/physoracle.c`,
//! `tests/scenarios/*.scn`): runs a scenario on the Rust port and prints the same text the
//! oracle prints, so the two traces can be compared line by line.
//!
//! Supported commands: `grid`, `var`, `geom` (box, sphere, cylinder, capsule), `entity`
//! (static, rigid, wheeled, living, particle), `part` (field setters on the geomparams image), `surface`, `area kind=global`, `params` (ids handled by
//! [`crate::entities::set_params`]), `action` (ids handled by [`crate::entities::action`]),
//! `trace`, `print`, `step`, `echo`. Unsupported commands return an error naming them.

use crate::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use crate::geometry::PhysGeometry;
use crate::math::{Mat33, Quat, Vec3};
use crate::params::ParamsPos;
use crate::sentinel::UNUSED_I32;
use crate::world::{EntIdx, World};
use std::collections::HashMap;
use std::fmt::Write;
use std::sync::Arc;

/// `(float)strtod(s)` / `h:XXXXXXXX` / `U` (unused), as the oracle parses floats.
#[must_use]
pub fn pf(s: &str) -> f32 {
    if let Some(h) = s.strip_prefix("h:") {
        return f32::from_bits(u32::from_str_radix(h, 16).unwrap_or(0));
    }
    if s == "U" {
        return crate::sentinel::UNUSED_F32;
    }
    s.parse::<f64>().unwrap_or(0.0) as f32
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

/// Integer argument (`0x..`, decimal, `U` = unused).
#[must_use]
pub fn pu(s: &str) -> u32 {
    if s == "U" {
        return UNUSED_I32 as u32;
    }
    s.strip_prefix("0x").map_or_else(
        || s.parse::<i64>().unwrap_or(0) as u32,
        |h| u32::from_str_radix(h, 16).unwrap_or(0),
    )
}

/// The scenario interpreter state.
pub struct Scenario {
    pub world: World,
    pub geoms: HashMap<String, Arc<PhysGeometry>>,
    pub ents: HashMap<String, EntIdx>,
    pub names: HashMap<EntIdx, String>,
    pub trace: Vec<EntIdx>,
    pub step: i32,
    pub out: String,
}

impl Default for Scenario {
    fn default() -> Self {
        Self::new()
    }
}

impl Scenario {
    #[must_use]
    pub fn new() -> Self {
        Self {
            world: World::new(),
            geoms: HashMap::new(),
            ents: HashMap::new(),
            names: HashMap::new(),
            trace: Vec::new(),
            step: 0,
            out: String::from("physoracle 1\n"),
        }
    }

    /// Run a whole scenario text; returns the output.
    pub fn run(text: &str) -> Result<String, String> {
        let mut s = Self::new();
        for line in text.lines() {
            s.line(line)?;
        }
        Ok(s.out)
    }

    fn target(&self, n: &str) -> Result<EntIdx, String> {
        self.ents
            .get(n)
            .copied()
            .ok_or_else(|| format!("unknown target {n}"))
    }

    /// The oracle's `print_entity`.
    fn print_entity(&mut self, e: EntIdx) {
        if matches!(
            self.world.ent(e).kind,
            crate::entities::base::EntityKind::Living(_)
                | crate::entities::base::EntityKind::Particle(_)
        ) {
            self.print_entity_status(e);
            return;
        }
        let w = &self.world;
        let ent = w.ent(e);
        let name = self.names.get(&e).cloned().unwrap_or_else(|| "?".into());
        let f = |x: f32| format!(" {:08x}", x.to_bits());
        let mut s = format!("e {name} p");
        let st = status_pos(w, e);
        for x in [st.0.x, st.0.y, st.0.z] {
            s += &f(x);
        }
        s += " q";
        for x in [st.1.x, st.1.y, st.1.z, st.1.w] {
            s += &f(x);
        }
        if let Some((v, wv)) = status_dynamics(w, e) {
            s += " v";
            for x in [v.x, v.y, v.z] {
                s += &f(x);
            }
            s += " w";
            for x in [wv.x, wv.y, wv.z] {
                s += &f(x);
            }
        }
        let aw = crate::entities::is_awake(w, e);
        let _ = writeln!(s, " sc {} aw {aw}", ent.sim_class());
        self.out += &s;
    }

    /// `print_entity` through the class's GetStatus (ids 1, 8 and 7, as the oracle calls them).
    fn print_entity_status(&mut self, e: EntIdx) {
        use crate::entities::interface::status::get_status;
        let rd = |b: &[u8], o: usize| u32::from_le_bytes(b[o..o + 4].try_into().unwrap_or([0; 4]));
        let mut sp = vec![0u8; 256];
        put(&mut sp, 0, 1);
        put(&mut sp, 4, u32::MAX);
        put(&mut sp, 8, u32::MAX);
        get_status(&mut self.world, e, &mut sp);
        let mut sd = vec![0u8; 256];
        put(&mut sd, 0, 8);
        put(&mut sd, 4, u32::MAX);
        put(&mut sd, 8, u32::MAX);
        let dynamics = get_status(&mut self.world, e, &mut sd);
        let mut sa = vec![0u8; 16];
        put(&mut sa, 0, 7);
        let aw = get_status(&mut self.world, e, &mut sa);
        let name = self.names.get(&e).cloned().unwrap_or_else(|| "?".into());
        let mut s = format!("e {name} p");
        for o in [24, 28, 32] {
            let _ = write!(s, " {:08x}", rd(&sp, o));
        }
        s += " q";
        for o in [60, 64, 68, 72] {
            let _ = write!(s, " {:08x}", rd(&sp, o));
        }
        if dynamics != 0 {
            s += " v";
            for o in [12, 16, 20] {
                let _ = write!(s, " {:08x}", rd(&sd, o));
            }
            s += " w";
            for o in [24, 28, 32] {
                let _ = write!(s, " {:08x}", rd(&sd, o));
            }
        }
        let _ = writeln!(s, " sc {} aw {aw}", rd(&sp, 80) as i32);
        self.out += &s;
    }

    fn print_trace(&mut self) {
        let _ = writeln!(self.out, "step {}", self.step);
        for e in self.trace.clone() {
            self.print_entity(e);
        }
    }

    /// Interpret one line.
    #[allow(clippy::too_many_lines)]
    pub fn line(&mut self, line: &str) -> Result<(), String> {
        let line = line.split('#').next().unwrap_or("").trim();
        if line.is_empty() {
            return Ok(());
        }
        let mut it = line.split_whitespace();
        let cmd = it.next().unwrap_or("");
        let toks: Vec<&str> = it.collect();
        let kv: HashMap<&str, &str> = toks.iter().filter_map(|t| t.split_once('=')).collect();
        let get = |k: &str, d: &str| -> String { kv.get(k).copied().unwrap_or(d).to_string() };
        let need = |k: &str| -> Result<String, String> {
            kv.get(k)
                .map(|s| (*s).to_string())
                .ok_or_else(|| format!("{cmd}: missing {k}"))
        };
        let w = &mut self.world;
        match cmd {
            "grid" => {
                let org = v3(&get("org", "0,0,-100"));
                let n = pv(&get("n", "64,64"));
                let st = pv(&get("step", "4,4"));
                w.setup_entity_grid(
                    pu(&get("axisz", "2")) as usize,
                    org,
                    n[0] as i32,
                    n[1] as i32,
                    st[0],
                    st[1],
                    0,
                );
            }
            "var" => {
                let off = pu(&need("off")?) as usize;
                let mut b = w.vars.to_bytes();
                let o = off - crate::physvars::WORLD_OFFSET;
                let word = if let Some(f) = kv.get("f") {
                    pf(f).to_bits()
                } else {
                    pu(&need("i")?)
                };
                b[o..o + 4].copy_from_slice(&word.to_le_bytes());
                w.vars = crate::physvars::PhysVars::from_bytes(&b);
            }
            "geom" => {
                let center = v3(&get("center", "0,0,0"));
                let prim = match need("type")?.as_str() {
                    "box" => {
                        let mut b = BoxPrim {
                            size: v3(&need("size")?),
                            center,
                            ..BoxPrim::default()
                        };
                        if let Some(bs) = kv.get("basis") {
                            let m = pv(bs);
                            b.basis = Mat33::from_rows(
                                Vec3::new(m[0], m[1], m[2]),
                                Vec3::new(m[3], m[4], m[5]),
                                Vec3::new(m[6], m[7], m[8]),
                            );
                            b.oriented = 1;
                        }
                        Primitive::Box(b)
                    }
                    "sphere" => Primitive::Sphere(SpherePrim {
                        center,
                        r: pf(&need("r")?),
                    }),
                    t @ ("cylinder" | "capsule") => {
                        let c = CylinderPrim {
                            center,
                            axis: v3(&get("axis", "0,0,1")),
                            r: pf(&need("r")?),
                            hh: pf(&need("hh")?),
                        };
                        if t == "cylinder" {
                            Primitive::Cylinder(c)
                        } else {
                            Primitive::Capsule(c)
                        }
                    }
                    t => return Err(format!("geom type {t} not supported")),
                };
                let g = w.register_primitive(&prim).ok_or("register failed")?;
                self.geoms.insert(need("name")?, g);
            }
            "entity" => {
                let ty = match need("type")?.as_str() {
                    "static" => 1,
                    "rigid" => 2,
                    "wheeled" => 3,
                    "living" => 4,
                    "particle" => 5,
                    t => t.parse::<i32>().map_err(|_| format!("entity type {t} not supported"))?,
                };
                // the oracle's pe_params_pos image: template, pos, q, then the field setters
                let mut b = vec![0u8; 512];
                template(crate::params::PeKind::Params, 0, &mut b);
                let p = v3(&get("pos", "0,0,0"));
                let q = q4(&get("q", "0,0,0,1"));
                for (i, x) in [p.x, p.y, p.z, q.x, q.y, q.z, q.w].into_iter().enumerate() {
                    put(&mut b, 4 + 4 * i, x.to_bits());
                }
                apply_fields(&toks, &mut b)?;
                let pp = <ParamsPos as crate::params::PeStruct>::read(&b);
                let id = get("id", "-1").parse::<i32>().unwrap_or(-1);
                let e = w
                    .create_physical_entity(ty, 0.0, Some(&pp), 0, 0, id)
                    .ok_or("create failed")?;
                let n = need("name")?;
                self.names.insert(e, n.clone());
                self.ents.insert(n, e);
            }
            "part" => {
                let en = need("ent")?;
                let e = self.target(&en)?;
                let g = self
                    .geoms
                    .get(&need("geom")?)
                    .cloned()
                    .ok_or("unknown geom")?;
                // the oracle's pe_geomparams image (type 0 unless `i0=` says otherwise)
                let mut b = vec![0u8; 512];
                let p = v3(&get("pos", "0,0,0"));
                let q = q4(&get("q", "0,0,0,1"));
                put(&mut b, 4, pf(&get("density", "0")).to_bits());
                put(&mut b, 8, pf(&get("mass", "0")).to_bits());
                for (i, x) in [p.x, p.y, p.z, q.x, q.y, q.z, q.w].into_iter().enumerate() {
                    put(&mut b, 12 + 4 * i, x.to_bits());
                }
                put(&mut b, 40, pf(&get("scale", "1")).to_bits());
                put(&mut b, 64, UI);
                put(&mut b, 68, pu(&get("flags", "0x1809F")));
                put(&mut b, 72, pu(&get("collider", "0x1")));
                put(&mut b, 76, pf(&get("mincontact", "U")).to_bits());
                put(&mut b, 80, u32::MAX);
                put(&mut b, 108, 1);
                apply_fields(&toks, &mut b)?;
                let id = get("id", "-1").parse::<i32>().unwrap_or(-1);
                let r = crate::entities::base::geometry::add_geometry_raw(
                    &mut self.world,
                    e,
                    &g,
                    &b,
                    id,
                );
                let _ = writeln!(self.out, "ret part {en} {r}");
            }
            "surface" => {
                let idx = pu(&need("idx")?) as usize;
                w.surfaces.bounciness[idx] = pf(&get("bounce", "0"));
                let fr = pf(&get("friction", "h:3f99999a"));
                w.surfaces.friction[idx] = fr;
                w.surfaces.dyn_friction[idx] = fr * (2.0 / 3.0);
                w.surfaces.flags[idx] = pu(&get("flags", "0")) as i32;
                let _ = writeln!(self.out, "ret surface 1");
            }
            "area" => {
                if get("kind", "global") != "global" {
                    return Err("only global areas".into());
                }
                let a = w.add_global_area();
                let n = need("name")?;
                self.names.insert(a, n.clone());
                self.ents.insert(n, a);
            }
            "params" | "action" | "status" | "getparams" => {
                let tn = need("target")?;
                let e = self.target(&tn)?;
                let ty = need("type")?.parse::<i32>().map_err(|e| e.to_string())?;
                let mut b = vec![0u8; 1024];
                let fam = match cmd {
                    "params" => crate::params::PeKind::Params,
                    "action" => crate::params::PeKind::Action,
                    "status" => crate::params::PeKind::Status,
                    _ => crate::params::PeKind::Params,
                };
                let tmpl = get("tmpl", "auto") == "auto" && (cmd == "params" || cmd == "action");
                if !(tmpl && template(fam, ty, &mut b[..512])) {
                    b[..4].copy_from_slice(&ty.to_le_bytes());
                }
                apply_fields(&toks, &mut b)?;
                let ts = get("ts", "1").parse::<i32>().unwrap_or(1);
                let is_area = matches!(
                    self.world.ent(e).kind,
                    crate::entities::base::EntityKind::Area(_)
                );
                let r = match (cmd, is_area) {
                    ("params", true) => area_set_params(&mut self.world, e, ty, &b)?,
                    ("params", false) => {
                        crate::entities::interface::set::set_params(&mut self.world, e, &mut b, ts)
                    }
                    ("action", false) => {
                        crate::entities::interface::action::action(&mut self.world, e, &mut b, ts)
                    }
                    ("status", false) => {
                        crate::entities::interface::status::get_status(&mut self.world, e, &mut b)
                    }
                    ("getparams", false) => {
                        crate::entities::interface::get::get_params(&mut self.world, e, &mut b)
                    }
                    (c, _) => return Err(format!("{c} type {ty} on an area not supported")),
                };
                let mut s = format!("ret {cmd} {tn} {ty} {r}");
                if let Some(d) = kv.get("dump") {
                    let (o, n) = d.split_once(',').ok_or("dump=OFF,N")?;
                    let (o, n) = (
                        o.parse::<usize>().unwrap_or(0),
                        n.parse::<usize>().unwrap_or(0),
                    );
                    for i in 0..n {
                        let k = o + 4 * i;
                        if k + 4 <= 512 {
                            let _ = write!(
                                s,
                                " {:08x}",
                                u32::from_le_bytes(b[k..k + 4].try_into().unwrap_or([0; 4]))
                            );
                        }
                    }
                }
                let _ = writeln!(self.out, "{s}");
            }
            "trace" => {
                self.trace.clear();
                for n in need("ents")?.split(',') {
                    let e = self.target(n)?;
                    self.trace.push(e);
                }
            }
            "step" => {
                let n = get("n", "1").parse::<i32>().unwrap_or(1);
                let dt = pf(&get("dt", "h:3d088889"));
                let flags = pu(&get("flags", "0x9F")) as i32;
                let every = get("every", "1").parse::<i32>().unwrap_or(1);
                for i in 0..n {
                    self.world.time_step(dt, flags);
                    self.step += 1;
                    if every > 0 && (self.step % every == 0 || i == n - 1) {
                        self.print_trace();
                    }
                }
            }
            "print" => self.print_trace(),
            "peek" => {
                let tn = need("target")?;
                let e = self.target(&tn)?;
                let off = pu(&need("off")?) as usize;
                let n = pu(&get("n", "1")) as usize;
                let mut s = format!("peek {tn} {off}");
                for i in 0..n {
                    match entity_word(&self.world, e, off + 4 * i) {
                        Some(x) => {
                            let _ = write!(s, " {x:08x}");
                        }
                        None => s += " ????????",
                    }
                }
                let _ = writeln!(self.out, "{s}");
            }
            "contacts" => {
                let tn = need("target")?;
                let e = self.target(&tn)?;
                let w = &self.world;
                let head = w.ent(e).rigid().ok_or("not rigid")?.contacts_head;
                let mut k = w.contacts.get(head).next;
                let mut n = 0;
                let h = |x: f32| format!(" {:08x}", x.to_bits());
                let mut s = String::new();
                while k != head && n < 1000 {
                    let c = w.contacts.get(k);
                    let _ = write!(s, "c {n}");
                    for v in [c.pt[0], c.pt[1], c.n] {
                        s += &(h(v.x) + &h(v.y) + &h(v.z));
                    }
                    s += " 00000000";
                    let nm = |x: EntIdx| self.names.get(&x).cloned().unwrap_or_else(|| "?".into());
                    let _ = write!(s, " e {} {}", nm(c.pent[0]), nm(c.pent[1]));
                    let _ = write!(s, " {:08x} {:08x}", c.ipart[0] as u32, c.ipart[1] as u32);
                    s += &(h(c.nloc.x) + &h(c.nloc.y) + &h(c.nloc.z) + &h(c.friction));
                    let _ = write!(s, " {:08x}", c.flags);
                    s += &(h(c.vn)
                        + &h(c.vreq.x)
                        + &h(c.vreq.y)
                        + &h(c.vreq.z)
                        + &h(c.pn)
                        + &h(c.penetration));
                    let _ = writeln!(
                        s,
                        " {:08x} {:08x} {:08x}",
                        c.id_bits, c.idx_bits, c.group_n as u32
                    );
                    k = c.next;
                    n += 1;
                }
                self.out += &s;
                let _ = writeln!(self.out, "contacts {tn} {n}");
            }
            "echo" => {
                let _ = writeln!(self.out, "echo {}", toks.join(" "));
            }
            // P3 queries: RayWorldIntersection (oracle `cmd_rwi`) and the fake-gEnv knobs
            "ray" => self.cmd_ray(&kv)?,
            "env" => {
                if let Some(v) = kv.get("raymax") {
                    self.world.queries.ray_dir_limit =
                        Some(v.parse().map_err(|e| format!("raymax: {e}"))?);
                }
                if let Some(v) = kv.get("b289") {
                    self.world.queries.env_byte_289 = *v != "0";
                }
                // b290 only affects TimeStep's vars (+328) in the original; not modelled here
            }
            c => return Err(format!("command {c} not supported by the port interpreter")),
        }
        Ok(())
    }
}

impl Scenario {
    /// The oracle's name of an object (`-` null, `terrain` for the terrain entities, `?`
    /// unknown).
    fn obj_name(&self, e: EntIdx) -> String {
        if e.is_none() {
            return "-".into();
        }
        if let Some(n) = self.names.get(&e) {
            return n.clone();
        }
        if self.world.terrain.contains(&e) {
            return "terrain".into();
        }
        "?".into()
    }

    /// `ray name= org= dir= [types=0x13F] [flags=0] [max=1] [caller=4]` (oracle `cmd_rwi`):
    /// prints `ray NAME n N` and one `h` line per hit slot (empty slots after the first
    /// skipped).
    fn cmd_ray(&mut self, kv: &HashMap<&str, &str>) -> Result<(), String> {
        let get = |k: &str, d: &str| -> String { kv.get(k).copied().unwrap_or(d).to_string() };
        let nmax = get("max", "1")
            .parse::<i32>()
            .map_err(|e| e.to_string())?
            .clamp(1, 16) as usize;
        let p = crate::world::queries::RwiParams {
            org: v3(&get("org", "0,0,0")),
            dir: v3(&get("dir", "0,0,0")),
            objtypes: pu(&get("types", "0x13F")),
            flags: pu(&get("flags", "0")),
            ..crate::world::queries::RwiParams::default()
        };
        let mut hits = vec![crate::world::queries::RayHit::default(); nmax];
        let caller = get("caller", "4")
            .parse::<i32>()
            .map_err(|e| e.to_string())?;
        let n = self.world.rwi(&p, &mut hits, None, caller);
        let _ = writeln!(self.out, "ray {} n {}", get("name", "-"), n);
        for (i, h) in hits.iter().enumerate() {
            if h.dist < 0.0 && i > 0 {
                continue;
            }
            let line = format!(
                "h {} d {:08x} e {} ip {} pid {} s {} p {:08x} {:08x} {:08x} n {:08x} {:08x} {:08x}",
                i,
                h.dist.to_bits(),
                self.obj_name(h.collider),
                h.ipart,
                h.partid,
                h.foreign_idx,
                h.pt.x.to_bits(),
                h.pt.y.to_bits(),
                h.pt.z.to_bits(),
                h.n.x.to_bits(),
                h.n.y.to_bits(),
                h.n.z.to_bits()
            );
            let _ = writeln!(self.out, "{line}");
        }
        Ok(())
    }
}

/// The oracle's `apply_fields` in token order: `fOFF=`, `iOFF=`, `uOFF=`, `bOFF=`,
/// `vOFF=x,y,z`, `qOFF=`, `pOFF=null|unused`, `hex=`.
fn apply_fields(toks: &[&str], b: &mut [u8]) -> Result<(), String> {
    for t in toks {
        let Some((k, v)) = t.split_once('=') else {
            continue;
        };
        if k == "hex" {
            let bytes: Vec<u8> = (0..v.len() / 2)
                .map(|i| u8::from_str_radix(&v[2 * i..2 * i + 2], 16).unwrap_or(0))
                .collect();
            b[..bytes.len()].copy_from_slice(&bytes);
            continue;
        }
        let (kind, off) = k.split_at(1);
        let Ok(off) = off.parse::<usize>() else {
            continue;
        };
        match kind {
            "f" => put(b, off, pf(v).to_bits()),
            "i" | "u" => put(b, off, pu(v)),
            "b" => b[off] = pu(v) as u8,
            "v" | "q" => {
                for (i, x) in pv(v).into_iter().enumerate() {
                    put(b, off + 4 * i, x.to_bits());
                }
            }
            "p" => match v {
                "null" => put_p(b, off, 0),
                "unused" => put_p(b, off, u64::MAX),
                _ => return Err(format!("pointer value {v} not supported")),
            },
            _ => {}
        }
    }
    Ok(())
}

const UF: u32 = crate::sentinel::UNUSED_F32_BITS;
const UI: u32 = crate::sentinel::UNUSED_I32 as u32;

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

/// The oracle's `tmpl_params` / `tmpl_action` (constructor images of the structs).
fn template(fam: crate::params::PeKind, ty: i32, b: &mut [u8]) -> bool {
    use crate::params::PeKind;
    b.fill(0);
    put(b, 0, ty as u32);
    match (fam, ty) {
        (PeKind::Params, 0) => {
            fill_unused(b, 4, 40);
            put(b, 56, UI);
            put(b, 60, 1);
        }
        (PeKind::Params, 6) => {
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
        (PeKind::Params, 10) => {
            fill_unused(b, 4, 84);
            put(b, 4, UI);
            put(b, 60, UI);
            put(b, 64, UI);
        }
        (PeKind::Params, 12) => {
            fill_unused(b, 4, 80);
            put(b, 72, UI);
        }
        (PeKind::Params, 14) => fill_unused(b, 4, 28),
        (PeKind::Params, 15) => {
            put(b, 4, UI);
            put(b, 8, UI);
            put(b, 12, UI);
        }
        (PeKind::Params, 23) => fill_unused(b, 4, 12),
        (PeKind::Action, 2) => {
            fill_unused(b, 4, 64);
            put(b, 64, UI);
            put(b, 68, UI);
        }
        (PeKind::Action, 4) => {}
        (PeKind::Action, 8) => {
            put(b, 4, 1);
            fill_unused(b, 8, 12);
        }
        (PeKind::Action, 10) => {
            put(b, 4, UI);
            put(b, 8, UI);
            fill_unused(b, 12, 36);
        }
        _ => return false,
    }
    true
}

/// Area SetParams (ids 0, 10, 12, 18; 0x35045B50) for the interpreter.
fn area_set_params(w: &mut World, e: EntIdx, ty: i32, b: &[u8]) -> Result<i32, String> {
    use crate::params::PeStruct;
    Ok(match ty {
        0 => w.area_set_params_pos(e, &crate::params::ParamsPos::read(b)),
        12 => w.area_set_params_buoyancy(e, &crate::params::ParamsBuoyancy::read(b)),
        t => {
            return Err(format!(
                "area params type {t} not supported by the interpreter"
            ))
        }
    })
}

/// `pe_status_pos` position/orientation.
fn status_pos(w: &World, e: EntIdx) -> (Vec3, Quat) {
    let ent = w.ent(e);
    (ent.pos, ent.q)
}

/// `pe_status_dynamics` velocities (rigid: the body's `v`, `w`).
fn status_dynamics(w: &World, e: EntIdx) -> Option<(Vec3, Vec3)> {
    // 0x350B1B80 id 8: zero velocities while asleep
    w.ent(e).rigid().map(|r| {
        let b = w.bodies.get(r.body);
        if r.awake() {
            (b.v, b.w)
        } else {
            (Vec3::ZERO, Vec3::ZERO)
        }
    })
}

/// The modelled words of a rigid entity object (`E+off`), for `peek` comparisons; `None` for
/// offsets the port does not model (pointers, padding, unported members).
#[must_use]
pub fn entity_word(w: &World, e: EntIdx, off: usize) -> Option<u32> {
    let ent = w.ent(e);
    let f = |x: f32| Some(x.to_bits());
    let v = |x: Vec3, o: usize| match o {
        0 => Some(x.x.to_bits()),
        4 => Some(x.y.to_bits()),
        _ => Some(x.z.to_bits()),
    };
    match off {
        8..=28 => return v(ent.bbox[(off - 8) / 12], (off - 8) % 12),
        408..=416 => return v(ent.pos, off - 408),
        420..=432 => return f(ent.q.to_array()[(off - 420) / 4]),
        252 => return f(ent.time_idle),
        256 => return f(ent.max_time_idle),
        92 => return Some(ent.flags),
        68 => return Some(ent.id_bits),
        _ => {}
    }
    if let Some(l) = ent.living() {
        return crate::entities::living::member_word(l, off);
    }
    let r = ent.rigid()?;
    if (744..956).contains(&off) {
        let b = w.bodies.get(r.body);
        let words = crate::solver::probe::body_words(b);
        return Some(words[(off - 744) / 4]);
    }
    match off {
        448..=456 => v(r.pos_new, off - 448),
        460..=472 => f(r.q_new.to_array()[(off - 460) / 4]),
        476..=496 => v(r.bbox_new[(off - 476) / 12], (off - 476) % 12),
        500 => Some(r.part_bbox_threshold as u32),
        504 => Some(r.flags2),
        508 => Some(r.awake_bits),
        512 => Some(r.bits512),
        552 => Some(r.n_contacts as u32),
        580 => Some(r.awake_history),
        584 => Some(r.n_constraints as u32),
        588 => f(r.ccd_vel),
        592 => f(r.ccd_size),
        596 => f(r.time_step_full),
        600 => f(r.time_step_performed),
        604 => f(r.last_dt),
        608 => f(r.min_awake_time),
        612 => f(r.forced_dt),
        616..=624 => v(r.gravity, off - 616),
        628..=636 => v(r.gravity_freefall, off - 628),
        640..=648 => v(r.ang_accel, off - 640),
        652 => f(r.min_energy),
        656 => f(r.max_time_step),
        660..=668 => v(r.v_accum, off - 660),
        672..=680 => v(r.w_accum, off - 672),
        684 => f(r.damping),
        688 => f(r.damping_freefall),
        692 => f(r.water_damping_eff),
        696 => f(r.max_w),
        700 => f(r.min_friction),
        704 => f(r.max_friction),
        708..=716 => v(r.v_sleep, off - 708),
        720..=728 => v(r.w_sleep, off - 720),
        956..=964 => v(r.p_deferred, off - 956),
        968..=976 => v(r.l_deferred, off - 968),
        980..=988 => v(r.prev_pos, off - 980),
        992..=1000 => v(r.prev_v, off - 992),
        1004..=1012 => v(r.prev_w, off - 1004),
        1016..=1028 => f(r.prev_q.to_array()[(off - 1016) / 4]),
        1032 => f(r.e_cached),
        1036 => f(r.e_clamp),
        1040 => f(r.unk1040),
        1044 => Some(r.unk1044),
        1048..=1056 => v(r.f_ext, off - 1048),
        1060..=1068 => v(r.t_ext, off - 1060),
        1072..=1080 => v(r.pos_correction, off - 1072),
        1096 => Some(r.stamp1096 as u32),
        1100 => f(r.f1100),
        1104 => Some(r.last_ext_contact_step as u32),
        1120 => f(r.water_damping),
        1124 => f(r.k_water_density),
        1128 => f(r.k_water_resistance),
        1132 => f(r.min_energy_water),
        1136 => f(r.submerged_fraction),
        1140 => f(r.k_air_resistance),
        _ => None,
    }
}
