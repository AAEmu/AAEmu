//! Parser for `physoracle` traces (scenario mode and `--math` mode).
//!
//! Every float is printed as its raw IEEE-754 bits (8 hex digits; doubles 16), so traces are
//! compared bit for bit. Scenario trace lines:
//!
//! ```text
//! physoracle 1
//! step <n>
//! e <name> p <x> <y> <z> q <x> <y> <z> <w> [v <x> <y> <z> w <x> <y> <z>] sc <simclass> aw <awake>
//! ray <name> n <nhits>
//! h <i> d <dist> e <entity|-|?> ip <ipart> pid <partid> s <surface> p <x> <y> <z> n <x> <y> <z>
//!     [x <i16 +24> <i16 +26> <int +32> <int +60> <iprim +64>]   (P1c, `ray ... ext=1`)
//! geominfo <name> type <t> nprims <n> nv <n> flags <hex> bbox <16 words> vol <bits>
//!     pg <V> <Ibody 3> <q 4> <origin 3> <surface>             (P1c, `geom type=mesh`, `geominfo`)
//! meshv <name> <3*nv words> / mesht <name> <3*nt ints> [/ meshf <name> <nt ints>]  (P1c, dump=1)
//! ret <command> <args...>          (return values of part/params/action/status)
//! vars <world offset> <132 words>  (dumpvars)
//! peek / peekworld / peekdll / listwhere / echo ...
//! ```

use crate::math::{Quat, Vec3};

/// One traced entity at one step.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct EntityState {
    pub name: String,
    pub pos: [u32; 3],
    pub q: [u32; 4],
    /// Present for entities that answer `pe_status_dynamics`.
    pub v: Option<[u32; 3]>,
    pub w: Option<[u32; 3]>,
    pub sim_class: i32,
    pub awake: i32,
}

impl EntityState {
    #[must_use]
    pub fn pos_f(&self) -> Vec3 {
        Vec3::from_bits(self.pos)
    }
    #[must_use]
    pub fn q_f(&self) -> Quat {
        Quat::from_bits(self.q)
    }
    #[must_use]
    pub fn v_f(&self) -> Option<Vec3> {
        self.v.map(Vec3::from_bits)
    }
    #[must_use]
    pub fn w_f(&self) -> Option<Vec3> {
        self.w.map(Vec3::from_bits)
    }
}

/// States printed after a world step (or by `print`).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct StepRecord {
    pub step: u32,
    pub entities: Vec<EntityState>,
}

/// One RayWorldIntersection hit.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Hit {
    pub index: i32,
    pub dist: u32,
    pub entity: String,
    pub ipart: i32,
    pub part_id: i32,
    pub surface: i32,
    pub pt: [u32; 3],
    pub n: [u32; 3],
    /// The extra hit fields printed with `ray ... ext=1` (P1c).
    pub ext: Option<HitExt>,
}

/// Raw hit-record fields beyond the classic ones (`ray ... ext=1`, added by P1c).
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct HitExt {
    /// int16 at +24 (corpus meshes: the triangle's material id; synthetic meshes 0; terrain 0).
    pub w24: i16,
    /// int16 at +26 (corpus meshes: the triangle's material id; synthetic meshes −1; terrain 0).
    pub w26: i16,
    /// int at +32 (not identified; varies per hit triangle).
    pub w32: i32,
    /// int at +60 (order / next index; 1 for terrain hits, 0 for entity hits).
    pub w60: i32,
    /// int at +64: the primitive (triangle) index in the geometry's **stored** order (trimesh:
    /// after the BV-tree build permuted the triangles; see `mesht` of `dump=1`).
    pub iprim: i32,
}

/// The `geominfo` summary of a registered geometry (P1c).
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct GeomInfo {
    pub name: String,
    /// Geometry type id (0 box, 1 trimesh, 4 sphere, …).
    pub gtype: i32,
    /// GetPrimitiveCount (slot 28): triangles of a trimesh, 1 for primitives.
    pub nprims: i32,
    /// CTriMesh +124 (0 for other types).
    pub nverts: i32,
    /// CTriMesh +152 (0 for other types).
    pub flags: u32,
    /// GetBBox (slot 6) as `primitives::box`: Basis (9), bOriented, center (3), size (3).
    pub bbox: [u32; 16],
    /// GetVolume (slot 31).
    pub volume: u32,
    /// `phys_geometry` +48 V, +8 Ibody, +20 q, +36 origin, +56 surface_idx.
    pub pg_volume: u32,
    pub pg_inertia: [u32; 3],
    pub pg_q: [u32; 4],
    pub pg_origin: [u32; 3],
    pub pg_surface: i32,
}

impl GeomInfo {
    /// bbox centre (geometry frame).
    #[must_use]
    pub fn bbox_center(&self) -> Vec3 {
        Vec3::from_bits([self.bbox[10], self.bbox[11], self.bbox[12]])
    }
    /// bbox half-size.
    #[must_use]
    pub fn bbox_size(&self) -> Vec3 {
        Vec3::from_bits([self.bbox[13], self.bbox[14], self.bbox[15]])
    }
}

/// A ray query and its hit slots.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct RayRecord {
    pub name: String,
    pub n_hits: i32,
    pub hits: Vec<Hit>,
}

/// A parsed scenario trace.
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct Trace {
    pub steps: Vec<StepRecord>,
    pub rays: Vec<RayRecord>,
    /// `ret ...` lines, tokenised (without the leading `ret`).
    pub rets: Vec<Vec<String>>,
    /// Other lines (`vars`, `peek*`, `listwhere`, `echo`), tokenised.
    pub other: Vec<Vec<String>>,
}

fn hex32(s: &str) -> Result<u32, String> {
    u32::from_str_radix(s, 16).map_err(|e| format!("bad hex '{s}': {e}"))
}

fn int(s: &str) -> Result<i32, String> {
    s.parse::<i32>().map_err(|e| format!("bad int '{s}': {e}"))
}

fn hex_n<const N: usize>(t: &[&str]) -> Result<[u32; N], String> {
    if t.len() < N {
        return Err(format!("expected {N} values in {t:?}"));
    }
    let mut out = [0u32; N];
    for (o, s) in out.iter_mut().zip(t) {
        *o = hex32(s)?;
    }
    Ok(out)
}

impl Trace {
    /// Parse scenario-mode output.
    pub fn parse(text: &str) -> Result<Trace, String> {
        let mut tr = Trace::default();
        let mut lines = text.lines().peekable();
        match lines.next() {
            Some(l) if l.trim() == "physoracle 1" => {}
            other => return Err(format!("not a physoracle v1 trace (first line {other:?})")),
        }
        for (ln, line) in lines.enumerate() {
            let t: Vec<&str> = line.split_whitespace().collect();
            let ctx = |e: String| format!("line {}: {e}: {line}", ln + 2);
            match t.first().copied() {
                None => {}
                Some("ERROR") | Some("EXCEPTION") => {
                    return Err(ctx("oracle reported an error".into()))
                }
                Some("step") => {
                    let step = t.get(1).ok_or_else(|| ctx("missing step".into()))?;
                    let step = step.parse::<u32>().map_err(|e| ctx(e.to_string()))?;
                    tr.steps.push(StepRecord {
                        step,
                        entities: Vec::new(),
                    });
                }
                Some("e") => {
                    let rec = tr
                        .steps
                        .last_mut()
                        .ok_or_else(|| ctx("entity before any step".into()))?;
                    rec.entities.push(Self::parse_entity(&t).map_err(ctx)?);
                }
                Some("ray") => {
                    if t.len() < 4 {
                        return Err(ctx("short ray line".into()));
                    }
                    tr.rays.push(RayRecord {
                        name: t[1].to_string(),
                        n_hits: int(t[3]).map_err(ctx)?,
                        hits: Vec::new(),
                    });
                }
                Some("h") => {
                    let ray = tr
                        .rays
                        .last_mut()
                        .ok_or_else(|| ctx("hit before any ray".into()))?;
                    ray.hits.push(Self::parse_hit(&t).map_err(ctx)?);
                }
                Some("ret") => tr.rets.push(t[1..].iter().map(|s| s.to_string()).collect()),
                Some(_) => tr.other.push(t.iter().map(|s| s.to_string()).collect()),
            }
        }
        Ok(tr)
    }

    fn parse_entity(t: &[&str]) -> Result<EntityState, String> {
        // e NAME p x y z q x y z w [v x y z w x y z] sc N aw N
        let name = t.get(1).ok_or("missing name")?.to_string();
        let find = |key: &str| t.iter().position(|s| *s == key);
        let p = find("p").ok_or("missing p")?;
        let q = find("q").ok_or("missing q")?;
        let sc = find("sc").ok_or("missing sc")?;
        let aw = find("aw").ok_or("missing aw")?;
        let (v, w) = match (find("v"), find("w")) {
            (Some(vi), Some(wi)) => (
                Some(hex_n::<3>(&t[vi + 1..])?),
                Some(hex_n::<3>(&t[wi + 1..])?),
            ),
            _ => (None, None),
        };
        Ok(EntityState {
            name,
            pos: hex_n::<3>(&t[p + 1..])?,
            q: hex_n::<4>(&t[q + 1..])?,
            v,
            w,
            sim_class: int(t.get(sc + 1).ok_or("missing sc value")?)?,
            awake: int(t.get(aw + 1).ok_or("missing aw value")?)?,
        })
    }

    fn parse_hit(t: &[&str]) -> Result<Hit, String> {
        // h I d D e NAME ip I pid I s I p x y z n x y z
        if t.len() < 20 {
            return Err("short hit line".into());
        }
        let ext = match t.get(20) {
            Some(&"x") if t.len() >= 26 => Some(HitExt {
                w24: t[21].parse::<i16>().map_err(|e| e.to_string())?,
                w26: t[22].parse::<i16>().map_err(|e| e.to_string())?,
                w32: int(t[23])?,
                w60: int(t[24])?,
                iprim: int(t[25])?,
            }),
            _ => None,
        };
        Ok(Hit {
            ext,
            index: int(t[1])?,
            dist: hex32(t[3])?,
            entity: t[5].to_string(),
            ipart: int(t[7])?,
            part_id: int(t[9])?,
            surface: int(t[11])?,
            pt: hex_n::<3>(&t[13..])?,
            n: hex_n::<3>(&t[17..])?,
        })
    }

    /// The last record of a step number.
    #[must_use]
    pub fn step(&self, n: u32) -> Option<&StepRecord> {
        self.steps.iter().rev().find(|s| s.step == n)
    }

    /// All states of one entity, in trace order.
    #[must_use]
    pub fn entity_states(&self, name: &str) -> Vec<(u32, &EntityState)> {
        self.steps
            .iter()
            .flat_map(|s| {
                s.entities
                    .iter()
                    .filter(|e| e.name == name)
                    .map(move |e| (s.step, e))
            })
            .collect()
    }

    /// A ray by name.
    #[must_use]
    pub fn ray(&self, name: &str) -> Option<&RayRecord> {
        self.rays.iter().find(|r| r.name == name)
    }

    /// The first `other` line starting with `key`.
    #[must_use]
    pub fn other_line(&self, key: &str) -> Option<&Vec<String>> {
        self.other
            .iter()
            .find(|l| l.first().map(String::as_str) == Some(key))
    }

    /// The (last) `geominfo` line of a geometry (P1c).
    #[must_use]
    pub fn geom_info(&self, name: &str) -> Option<GeomInfo> {
        let l = self
            .other
            .iter()
            .rev()
            .find(|l| l.len() >= 2 && l[0] == "geominfo" && l[1] == name)?;
        Self::parse_geom_info(l).ok()
    }

    fn parse_geom_info(l: &[String]) -> Result<GeomInfo, String> {
        let t: Vec<&str> = l.iter().map(String::as_str).collect();
        let find = |key: &str| {
            t.iter()
                .position(|s| *s == key)
                .ok_or_else(|| format!("geominfo: missing {key}"))
        };
        let (ty, np, nv, fl) = (find("type")?, find("nprims")?, find("nv")?, find("flags")?);
        let (bb, vol, pg) = (find("bbox")?, find("vol")?, find("pg")?);
        Ok(GeomInfo {
            name: t[1].to_string(),
            gtype: int(t.get(ty + 1).ok_or("type")?)?,
            nprims: int(t.get(np + 1).ok_or("nprims")?)?,
            nverts: int(t.get(nv + 1).ok_or("nv")?)?,
            flags: hex32(t.get(fl + 1).ok_or("flags")?)?,
            bbox: hex_n::<16>(&t[bb + 1..])?,
            volume: hex32(t.get(vol + 1).ok_or("vol")?)?,
            pg_volume: hex32(t.get(pg + 1).ok_or("pg")?)?,
            pg_inertia: hex_n::<3>(&t[pg + 2..])?,
            pg_q: hex_n::<4>(&t[pg + 5..])?,
            pg_origin: hex_n::<3>(&t[pg + 9..])?,
            pg_surface: int(t.get(pg + 12).ok_or("surface")?)?,
        })
    }

    /// The stored vertices and triangle indices of a mesh geometry printed with `dump=1` (P1c).
    #[must_use]
    pub fn mesh_data(&self, name: &str) -> Option<(Vec<Vec3>, Vec<[i32; 3]>)> {
        let line = |key: &str| {
            self.other
                .iter()
                .rev()
                .find(|l| l.len() >= 2 && l[0] == key && l[1] == name)
        };
        let v = line("meshv")?[2..]
            .iter()
            .map(|s| hex32(s))
            .collect::<Result<Vec<u32>, _>>()
            .ok()?;
        let i = line("mesht")?[2..]
            .iter()
            .map(|s| int(s))
            .collect::<Result<Vec<i32>, _>>()
            .ok()?;
        Some((
            v.as_chunks::<3>()
                .0
                .iter()
                .map(|c| Vec3::from_bits(*c))
                .collect(),
            i.as_chunks::<3>().0.to_vec(),
        ))
    }
}

/// A `--math` probe trace: rows of input and output words (u32 for floats, u64 for doubles).
#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct MathTrace {
    pub name: String,
    pub count: usize,
    pub seed: u64,
    pub rows: Vec<(Vec<u64>, Vec<u64>)>,
}

impl MathTrace {
    /// Parse `--math` output.
    pub fn parse(text: &str) -> Result<MathTrace, String> {
        let mut lines = text.lines();
        let head: Vec<&str> = lines.next().unwrap_or("").split_whitespace().collect();
        if head.len() < 5 || head[0] != "physoracle-math" || head[1] != "1" {
            return Err(format!("not a physoracle-math v1 trace: {head:?}"));
        }
        let mut tr = MathTrace {
            name: head[2].to_string(),
            count: head[3].parse().map_err(|e| format!("count: {e}"))?,
            seed: head[4].parse().map_err(|e| format!("seed: {e}"))?,
            rows: Vec::new(),
        };
        for line in lines {
            let t: Vec<&str> = line.split_whitespace().collect();
            if t.is_empty() {
                continue;
            }
            if t[0] != "in" {
                return Err(format!("unexpected line: {line}"));
            }
            let out_at = t
                .iter()
                .position(|s| *s == "out")
                .ok_or_else(|| format!("no 'out': {line}"))?;
            let parse =
                |s: &&str| u64::from_str_radix(s, 16).map_err(|e| format!("bad hex '{s}': {e}"));
            let ins = t[1..out_at]
                .iter()
                .map(parse)
                .collect::<Result<Vec<_>, _>>()?;
            let outs = t[out_at + 1..]
                .iter()
                .map(parse)
                .collect::<Result<Vec<_>, _>>()?;
            tr.rows.push((ins, outs));
        }
        Ok(tr)
    }
}

/// `f32` from a trace word.
#[inline]
#[must_use]
pub fn f(w: u64) -> f32 {
    f32::from_bits(w as u32)
}

#[cfg(test)]
mod tests {
    use super::*;

    const SAMPLE: &str = "physoracle 1
ret part ground 0
step 0
e box p 42000000 42000000 40a00000 q 00000000 00000000 00000000 3f800000 v 00000000 00000000 00000000 w 00000000 00000000 00000000 sc 2 aw 1
ray down n 1
h 0 d 41080000 e crate ip 0 pid 0 s -1 p 42000000 42000000 3fc00000 n 00000000 00000000 3f800000
peek box 420 3dd2b33d
";

    #[test]
    fn parse_scenario_trace() {
        let t = Trace::parse(SAMPLE).unwrap();
        assert_eq!(t.steps.len(), 1);
        let e = &t.steps[0].entities[0];
        assert_eq!(e.name, "box");
        assert_eq!(e.pos_f(), Vec3::new(32.0, 32.0, 5.0));
        assert_eq!(e.q_f(), Quat::IDENTITY);
        assert_eq!(e.sim_class, 2);
        assert_eq!(t.ray("down").unwrap().hits[0].entity, "crate");
        assert_eq!(t.ray("down").unwrap().hits[0].dist, 0x4108_0000);
        assert_eq!(t.rets[0], vec!["part", "ground", "0"]);
        assert_eq!(t.other_line("peek").unwrap()[3], "3dd2b33d");
        assert!(Trace::parse("garbage").is_err());
        assert!(Trace::parse("physoracle 1\nERROR x\n").is_err());
    }

    #[test]
    fn parse_p1c_records() {
        let t = Trace::parse(
            "physoracle 1
geominfo bx type 1 nprims 12 nv 8 flags 00000040 bbox 3f800000 00000000 00000000 00000000 3f800000 00000000 00000000 00000000 3f800000 00000000 00000000 00000000 00000000 3f000000 3f000000 3f000000 vol 3f800000 pg 3f800000 3e2aaaab 3e2aaaab 3e2aaaab 00000000 00000000 00000000 3f800000 00000000 00000000 00000000 0
meshv bx 00000000 00000000 00000000 3f800000 00000000 00000000 00000000 3f800000 00000000
mesht bx 0 1 2
ray r n 1
h 0 d 41200000 e floor ip 0 pid 7 s -1 p 41580000 41580000 00000000 n 00000000 00000000 3f800000 x 0 -1 20 0 1
",
        )
        .unwrap();
        let g = t.geom_info("bx").unwrap();
        assert_eq!((g.gtype, g.nprims, g.nverts, g.flags), (1, 12, 8, 0x40));
        assert_eq!(g.bbox_size(), Vec3::new(0.5, 0.5, 0.5));
        assert_eq!(f32::from_bits(g.volume), 1.0);
        assert_eq!(g.pg_q[3], 0x3f80_0000);
        assert_eq!(g.pg_surface, 0);
        let (v, i) = t.mesh_data("bx").unwrap();
        assert_eq!((v.len(), i), (3, vec![[0, 1, 2]]));
        let x = t.ray("r").unwrap().hits[0].ext.unwrap();
        assert_eq!((x.w24, x.w26, x.w32, x.w60, x.iprim), (0, -1, 20, 0, 1));
        assert!(Trace::parse(SAMPLE).unwrap().rays[0].hits[0].ext.is_none());
    }

    #[test]
    fn parse_math_trace() {
        let m = MathTrace::parse(
            "physoracle-math 1 sinf 2 1\nin 3f800000 out 3f576aa4\nin 00000000 out 00000000\n",
        )
        .unwrap();
        assert_eq!(m.name, "sinf");
        assert_eq!(m.rows.len(), 2);
        assert_eq!(f(m.rows[0].1[0]), 1.0f32.sin());
    }
}
