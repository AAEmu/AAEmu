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
        Ok(Hit {
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
