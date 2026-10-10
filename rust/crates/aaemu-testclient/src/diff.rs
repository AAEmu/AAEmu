//! Parity diff of two captures of the same scenario (e.g. the C# and the Rust server).
//!
//! S->C packets are compared step by step:
//! 1. the multiset of packet names per step,
//! 2. the bodies of same-named packets, after normalisation: run-specific values (account id,
//!    cookie, character id, object ids, item ids, ports — the scenario's `vars`) are replaced by
//!    placeholders, and the byte ranges listed in the mask file (timestamps, random values) are
//!    blanked.
//!
//! Bodies of repeated packets are compared as multisets; unmatched ones are paired in arrival
//! order to show where they differ.

use std::collections::{BTreeMap, HashMap};
use std::fmt::Write as _;

use serde::{Deserialize, Serialize};

use crate::record::{Dir, Event};

/// A blanked byte range `[start, end)` of a packet body; `end` = `None` means to the end.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Mask {
    pub start: usize,
    pub end: Option<usize>,
    /// Repeat the range every `stride` bytes (arrays of fixed-size records).
    #[serde(default)]
    pub stride: Option<usize>,
    /// Why the range is volatile.
    #[serde(default)]
    pub why: String,
}

/// Normalisation rules.
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct DiffConfig {
    /// Packets that are not compared at all (periodic / time driven).
    #[serde(default)]
    pub ignore: Vec<String>,
    /// Packets compared by name and count only.
    #[serde(default)]
    pub count_only: Vec<String>,
    /// Packets whose content and number are random (combat): only their layouts are compared,
    /// i.e. when both sides sent some, the sets of body lengths must be equal.
    #[serde(default)]
    pub len_only: Vec<String>,
    /// Packets that may be missing on either side (best-effort broadcasts), never reported.
    #[serde(default)]
    pub optional: Vec<String>,
    /// Per-packet volatile byte ranges.
    #[serde(default)]
    pub masks: HashMap<String, Vec<Mask>>,
    /// Steps not compared.
    #[serde(default)]
    pub ignore_steps: Vec<String>,
    /// Packets compared over the whole run instead of per step (world population that depends
    /// on region-activation timing: NPC / doodad spawns and despawns).
    #[serde(default)]
    pub whole_run: Vec<String>,
    /// Packets that carry no object ids: compared without object-id canonicalisation (their
    /// data could coincide with an object id of one run).
    #[serde(default)]
    pub raw_ids: Vec<String>,
    /// Blank every 4-byte value that looks like a current unix time (seconds, +-30 days).
    #[serde(default)]
    pub mask_unix_times: bool,
}

/// One difference.
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Finding {
    pub step: String,
    pub packet: String,
    pub detail: String,
}

#[derive(Debug, Default, Serialize, Deserialize)]
pub struct Report {
    pub compared_packets: usize,
    pub matched_packets: usize,
    pub findings: Vec<Finding>,
}

impl Report {
    pub fn render(&self, a_label: &str, b_label: &str) -> String {
        let mut s = String::new();
        let _ = writeln!(
            s,
            "parity {a_label} vs {b_label}: {} S->C packets compared, {} identical after normalisation, {} finding(s)",
            self.compared_packets,
            self.matched_packets,
            self.findings.len()
        );
        let mut by_step: BTreeMap<&str, Vec<&Finding>> = BTreeMap::new();
        for f in &self.findings {
            by_step.entry(&f.step).or_default().push(f);
        }
        for (step, fs) in by_step {
            let _ = writeln!(s, "\n## {step}");
            for f in fs {
                let _ = writeln!(s, "- {}: {}", f.packet, f.detail);
            }
        }
        s
    }
}

fn replace_all(body: &mut [u8], needle: &[u8], marker: u8) {
    if needle.is_empty() || needle.len() > body.len() {
        return;
    }
    let mut i = 0;
    while i + needle.len() <= body.len() {
        if &body[i..i + needle.len()] == needle {
            for b in &mut body[i..i + needle.len()] {
                *b = marker;
            }
            i += needle.len();
        } else {
            i += 1;
        }
    }
}

/// Replaces the run-specific values in `body`. Each variable gets its own marker byte so both
/// sides normalise to the same bytes.
pub fn normalise(
    body: &[u8],
    name: &str,
    vars: &BTreeMap<String, u64>,
    objs: &HashMap<u32, [u8; 3]>,
    items: &HashMap<[u8; 8], [u8; 8]>,
    cfg: &DiffConfig,
) -> Vec<u8> {
    let mut b = body.to_vec();
    if cfg.raw_ids.iter().any(|n| n == name) {
        // no object ids in this packet
    } else if name == "SCDoodadsCreatedPacket" {
        // Fixed 91-byte records: only the id slots (objId, ownerObjId, parentObjId), so that
        // positions / rotations that happen to equal an object id stay untouched.
        replace_doodad_record_ids(&mut b, objs);
    } else {
        replace_obj_ids(&mut b, objs);
    }
    replace_item_ids(&mut b, items);
    if name == "SCUnitStatePacket" {
        mask_unit_state_buff_elapsed(&mut b);
    }
    for (i, (k, &v)) in vars.iter().enumerate() {
        // Tiny values (< 256) would match unrelated bytes; ports are masked where they occur.
        if v < 256 || k.ends_with("_port") {
            continue;
        }
        let marker = 0xA0u8.wrapping_add(i as u8);
        if v > u64::from(u32::MAX) {
            replace_all(&mut b, &v.to_le_bytes(), marker);
        } else {
            let v32 = v as u32;
            if k.ends_with("_id") || k == "cookie" || k == "token" {
                // u64 forms (item ids, ids written as long) first, then u32.
                replace_all(&mut b, &u64::from(v32).to_le_bytes(), marker);
            }
            replace_all(&mut b, &v32.to_le_bytes(), marker);
        }
    }
    if cfg.mask_unix_times {
        mask_unix_times(&mut b);
    }
    if let Some(masks) = cfg.masks.get(name) {
        for m in masks {
            let mut start = m.start;
            let mut end = m.end.unwrap_or(b.len());
            let len = b.len();
            while start < len {
                for x in b.iter_mut().take(end.min(len)).skip(start) {
                    *x = 0;
                }
                match m.stride {
                    Some(st) if st > 0 => {
                        start += st;
                        end += st;
                    }
                    _ => break,
                }
            }
        }
    }
    b
}

/// Zeroes 4-byte little-endian values within 30 days of now (unix seconds).
fn mask_unix_times(b: &mut [u8]) {
    let now = std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .map_or(0, |d| d.as_secs()) as u32;
    let (lo, hi) = (
        now.saturating_sub(30 * 86_400),
        now.saturating_add(30 * 86_400),
    );
    let mut i = 0;
    while i + 4 <= b.len() {
        let v = u32::from_le_bytes([b[i], b[i + 1], b[i + 2], b[i + 3]]);
        if (lo..=hi).contains(&v) {
            b[i..i + 4].fill(0);
            i += 4;
        } else {
            i += 1;
        }
    }
}

fn first_diff(a: &[u8], b: &[u8]) -> String {
    let n = a.len().min(b.len());
    let off = (0..n).find(|&i| a[i] != b[i]).unwrap_or(n);
    let lo = off.saturating_sub(4);
    let ctx = |x: &[u8]| hex::encode(&x[lo.min(x.len())..(off + 12).min(x.len())]);
    format!(
        "len {} vs {}, first difference at byte {off}: ..{} vs ..{}",
        a.len(),
        b.len(),
        ctx(a),
        ctx(b)
    )
}

/// Stable replacement bytes for the object ids of one run: object ids are handed out by the
/// server in a non-deterministic order, so each unit / doodad id seen in `SCUnitStatePacket` /
/// `SCDoodadsCreatedPacket` is replaced by a hash of what it is (kind, template, position).
pub fn canonical_obj_ids(events: &[Event], vars: &BTreeMap<String, u64>) -> HashMap<u32, [u8; 3]> {
    let mut keys: Vec<(u32, String)> = Vec::new();
    for e in events.iter().filter(|e| e.dir == Dir::S2C && e.level == 1) {
        let b = e.body_bytes();
        let mut s = aaemu_commons::PacketStream::from_bytes(b);
        let parsed: Result<(), aaemu_commons::MarshalError> = (|| {
            match e.name.as_str() {
                "SCUnitStatePacket" => {
                    let obj = s.read_bc()?;
                    let _name = s.read_string()?;
                    let t = s.read_u8()?;
                    let id = match t {
                        0 => {
                            let id = s.read_u32()?;
                            s.read_u64()?;
                            id
                        }
                        1 => {
                            s.read_bc()?;
                            let tpl = s.read_u32()?;
                            s.read_u32()?;
                            s.read_u8()?;
                            tpl
                        }
                        _ => return Ok(()),
                    };
                    s.read_string()?;
                    let (x, y, _) = s.read_position()?;
                    // NPCs wander between their spawn and the moment they are seen: use a coarse
                    // grid (30 m) around where they first appear.
                    let (gx, gy) = ((x / 30.0).round(), (y / 30.0).round());
                    keys.push((obj, format!("unit{t}:{id}@{gx},{gy}")));
                }
                "SCDoodadsCreatedPacket" => {
                    let n = s.read_u8()?;
                    for _ in 0..n {
                        let obj = s.read_bc()?;
                        let tpl = s.read_u32()?;
                        s.read_bc()?;
                        s.read_bc()?;
                        s.read_u8()?;
                        let (x, y, _) = s.read_position()?;
                        s.read_bytes(91 - 23)?;
                        keys.push((obj, format!("doodad:{tpl}@{:.0},{:.0}", x, y)));
                    }
                }
                _ => {}
            }
            Ok(())
        })();
        let _ = parsed;
    }
    let mut out = HashMap::new();
    let mut seen: HashMap<String, u32> = HashMap::new();
    if let Some(&me) = vars.get("self_obj") {
        out.insert(me as u32, [0xFF, 0xFF, 0xFE]);
    }
    // First sighting of each object wins (later unit states carry moved positions).
    let mut first: HashMap<u32, String> = HashMap::new();
    for (obj, key) in keys {
        first.entry(obj).or_insert(key);
    }
    let mut keys: Vec<(u32, String)> = first.into_iter().collect();
    keys.sort_by(|a, b| a.1.cmp(&b.1).then(a.0.cmp(&b.0)));
    for (obj, key) in keys {
        if out.contains_key(&obj) || obj < 0x100 {
            continue;
        }
        let n = seen.entry(key.clone()).or_insert(0);
        let key = format!("{key}#{n}");
        *n += 1;
        // FNV-1a, 16 bits + a marker byte.
        let mut h: u32 = 0x811c_9dc5;
        for b in key.bytes() {
            h ^= u32::from(b);
            h = h.wrapping_mul(0x0100_0193);
        }
        out.insert(obj, [(h & 0xFF) as u8, ((h >> 8) & 0xFF) as u8, 0xFE]);
    }
    out
}

/// Item ids (`u64`, `0x01xxxxxx`) of one run, renumbered in order of first appearance: the id
/// allocator's history differs between servers (and restarts) while the items are the same.
pub fn canonical_item_ids(events: &[Event]) -> HashMap<[u8; 8], [u8; 8]> {
    const ITEM_PACKETS: [&str; 5] = [
        "SCCreateCharacterResponsePacket",
        "SCCharacterListPacket",
        "SCCharacterInvenContentsPacket",
        "SCItemTaskSuccessPacket",
        "SCUnitStatePacket",
    ];
    let mut out = HashMap::new();
    for e in events
        .iter()
        .filter(|e| e.dir == Dir::S2C && ITEM_PACKETS.contains(&e.name.as_str()))
    {
        let b = e.body_bytes();
        let mut i = 0;
        while i + 8 <= b.len() {
            let w: [u8; 8] = b[i..i + 8].try_into().unwrap_or_default();
            if is_item_id(&w) {
                let n = out.len() as u16;
                out.entry(w)
                    .or_insert([n as u8, (n >> 8) as u8, 0xEE, 0x01, 0, 0, 0, 0]);
                i += 8;
            } else {
                i += 1;
            }
        }
    }
    out
}

/// `0x0100_0000 + n` (n < 65536) as a `u64`.
fn is_item_id(w: &[u8; 8]) -> bool {
    w[2] == 0 && w[3] == 0x01 && w[4..] == [0, 0, 0, 0] && w[..2] != [0, 0]
}

fn replace_item_ids(b: &mut [u8], map: &HashMap<[u8; 8], [u8; 8]>) {
    let mut i = 0;
    while i + 8 <= b.len() {
        let w: [u8; 8] = b[i..i + 8].try_into().unwrap_or_default();
        if let Some(r) = map.get(&w) {
            b[i..i + 8].copy_from_slice(r);
            i += 8;
        } else {
            i += 1;
        }
    }
}

/// `SCUnitStatePacket` ends with the good / bad / hidden buff lists (`u8` count + 47-byte
/// records). Blanks each record's `elapsedTime` (timing dependent).
fn mask_unit_state_buff_elapsed(b: &mut [u8]) {
    const REC: usize = 47;
    const ELAPSED: usize = 23;
    let len = b.len();
    // Prefer the longest consistent split (a short tail of zero bytes also parses as "no buffs").
    for total in (0..=64usize).rev() {
        let Some(start) = len.checked_sub(3 + REC * total) else {
            continue;
        };
        let g = usize::from(b[start]);
        if g > total {
            continue;
        }
        let pb = start + 1 + REC * g;
        let Some(&bb) = b.get(pb) else { continue };
        let bb = usize::from(bb);
        if g + bb > total {
            continue;
        }
        let ph = pb + 1 + REC * bb;
        let Some(&h) = b.get(ph) else { continue };
        if g + bb + usize::from(h) != total {
            continue;
        }
        for (base, n) in [(start + 1, g), (pb + 1, bb), (ph + 1, usize::from(h))] {
            for k in 0..n {
                let o = base + k * REC + ELAPSED;
                b[o..o + 4].fill(0);
            }
        }
        return;
    }
}

fn replace_doodad_record_ids(b: &mut [u8], map: &HashMap<u32, [u8; 3]>) {
    const REC: usize = 91;
    let n = usize::from(b.first().copied().unwrap_or(0));
    for k in 0..n {
        let base = 1 + k * REC;
        if base + REC > b.len() {
            break;
        }
        for off in [0usize, 7, 10] {
            let o = base + off;
            let v = u32::from(b[o]) | (u32::from(b[o + 1]) << 8) | (u32::from(b[o + 2]) << 16);
            if let Some(r) = map.get(&v) {
                b[o..o + 3].copy_from_slice(r);
            }
        }
    }
}

fn replace_obj_ids(b: &mut [u8], map: &HashMap<u32, [u8; 3]>) {
    if map.is_empty() || b.len() < 3 {
        return;
    }
    let mut i = 0;
    while i + 3 <= b.len() {
        let v = u32::from(b[i]) | (u32::from(b[i + 1]) << 8) | (u32::from(b[i + 2]) << 16);
        if let Some(r) = map.get(&v) {
            b[i..i + 3].copy_from_slice(r);
            i += 3;
        } else {
            i += 1;
        }
    }
}

/// Compares the S->C packets of two runs.
pub fn diff(
    a: &[Event],
    a_vars: &BTreeMap<String, u64>,
    b: &[Event],
    b_vars: &BTreeMap<String, u64>,
    cfg: &DiffConfig,
) -> Report {
    // step -> name -> bodies (arrival order); steps in first-seen order of `a`.
    // Each body is kept in two normal forms: with every object id replaced, and without the
    // round ids (multiples of 100), which also occur as ordinary values (durations, ...). Two
    // bodies match when either form does.
    type Groups = Vec<(String, BTreeMap<String, Vec<(Vec<u8>, Vec<u8>)>>)>;
    let group = |evs: &[Event], vars: &BTreeMap<String, u64>| -> Groups {
        let objs = canonical_obj_ids(evs, vars);
        let relaxed: HashMap<u32, [u8; 3]> = objs
            .iter()
            .filter(|(id, _)| *id % 100 != 0)
            .map(|(k, v)| (*k, *v))
            .collect();
        let items = canonical_item_ids(evs);
        let mut out: Groups = Vec::new();
        for e in evs.iter().filter(|e| e.dir == Dir::S2C) {
            if cfg.ignore.contains(&e.name) || cfg.ignore_steps.contains(&e.step) {
                continue;
            }
            let step = if cfg.whole_run.contains(&e.name) {
                "(whole run)"
            } else {
                e.step.as_str()
            };
            let idx = match out.iter().position(|(s, _)| s == step) {
                Some(i) => i,
                None => {
                    out.push((step.to_string(), BTreeMap::new()));
                    out.len() - 1
                }
            };
            out[idx].1.entry(e.name.clone()).or_default().push({
                let body = e.body_bytes();
                (
                    normalise(&body, &e.name, vars, &objs, &items, cfg),
                    normalise(&body, &e.name, vars, &relaxed, &items, cfg),
                )
            });
        }
        out
    };
    let ga = group(a, a_vars);
    let gb = group(b, b_vars);
    let mut report = Report::default();
    let mut steps: Vec<String> = ga.iter().map(|(s, _)| s.clone()).collect();
    for (s, _) in &gb {
        if !steps.contains(s) {
            steps.push(s.clone());
        }
    }
    let empty = BTreeMap::new();
    for step in steps {
        let pa = ga
            .iter()
            .find(|(s, _)| *s == step)
            .map_or(&empty, |(_, m)| m);
        let pb = gb
            .iter()
            .find(|(s, _)| *s == step)
            .map_or(&empty, |(_, m)| m);
        let mut names: Vec<&String> = pa.keys().chain(pb.keys()).collect();
        names.sort();
        names.dedup();
        for name in names {
            let va = pa.get(name).map_or(&[][..], |v| v.as_slice());
            let vb = pb.get(name).map_or(&[][..], |v| v.as_slice());
            report.compared_packets += va.len().max(vb.len());
            let optional = cfg.optional.contains(name);
            if cfg.len_only.contains(name) {
                let la: std::collections::BTreeSet<usize> = va.iter().map(|x| x.0.len()).collect();
                let lb: std::collections::BTreeSet<usize> = vb.iter().map(|x| x.0.len()).collect();
                // A random event may simply not have happened on one side.
                if !la.is_empty() && !lb.is_empty() && la != lb {
                    report.findings.push(Finding {
                        step: step.clone(),
                        packet: name.clone(),
                        detail: format!("body lengths {la:?} vs {lb:?}"),
                    });
                } else {
                    report.matched_packets += va.len().min(vb.len());
                }
                continue;
            }
            if va.len() != vb.len() && !optional {
                report.findings.push(Finding {
                    step: step.clone(),
                    packet: name.clone(),
                    detail: format!("count {} vs {}", va.len(), vb.len()),
                });
            }
            if cfg.count_only.contains(name) || optional {
                report.matched_packets += va.len().min(vb.len());
                continue;
            }
            // Multiset match.
            let mut rest_b: Vec<&(Vec<u8>, Vec<u8>)> = vb.iter().collect();
            let mut rest_a: Vec<&(Vec<u8>, Vec<u8>)> = Vec::new();
            for x in va {
                if let Some(i) = rest_b.iter().position(|y| y.0 == x.0 || y.1 == x.1) {
                    rest_b.remove(i);
                    report.matched_packets += 1;
                } else {
                    rest_a.push(x);
                }
            }
            let pairs = rest_a.len().min(rest_b.len());
            for i in 0..pairs.min(3) {
                report.findings.push(Finding {
                    step: step.clone(),
                    packet: name.clone(),
                    detail: first_diff(&rest_a[i].0, &rest_b[i].0),
                });
            }
            if pairs > 3 {
                report.findings.push(Finding {
                    step: step.clone(),
                    packet: name.clone(),
                    detail: format!("... {} more differing bodies", pairs - 3),
                });
            }
        }
    }
    report
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::record::Channel;

    fn ev(step: &str, name: &str, body: &[u8]) -> Event {
        Event {
            t_ms: 0,
            channel: Channel::Game,
            dir: Dir::S2C,
            level: 1,
            opcode: 0,
            name: name.into(),
            step: step.into(),
            body: hex::encode(body),
        }
    }

    #[test]
    fn vars_and_masks_are_normalised() {
        let mut va = BTreeMap::new();
        va.insert("char_id".to_string(), 0x0102_0304u64);
        let mut vb = BTreeMap::new();
        vb.insert("char_id".to_string(), 0x0A0B_0C0Du64);
        let mut cfg = DiffConfig::default();
        cfg.masks.insert(
            "P".into(),
            vec![Mask {
                start: 4,
                end: Some(6),
                stride: None,
                why: "time".into(),
            }],
        );
        let a = [ev("s", "P", &[4, 3, 2, 1, 9, 9, 7])];
        let b = [ev("s", "P", &[0x0D, 0x0C, 0x0B, 0x0A, 8, 8, 7])];
        let r = diff(&a, &va, &b, &vb, &cfg);
        assert!(r.findings.is_empty(), "{:?}", r.findings);
        assert_eq!(r.matched_packets, 1);
    }

    #[test]
    fn reports_count_and_body_differences() {
        let v = BTreeMap::new();
        let a = [ev("s", "P", &[1]), ev("s", "Q", &[1])];
        let b = [ev("s", "P", &[2])];
        let r = diff(&a, &v, &b, &v, &DiffConfig::default());
        assert_eq!(r.findings.len(), 2);
        assert!(r.findings.iter().any(|f| f.packet == "Q"));
    }
}
