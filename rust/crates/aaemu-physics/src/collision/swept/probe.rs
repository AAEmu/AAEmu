//! Test support for the P2b cells: case encoding shared with the oracle's `--p2b` probe
//! (`oracle/p2b_probe.c`), running cases through the port and through the real DLL, and golden
//! files.
//!
//! A case is one text line (all values hex words, floats as raw bits):
//!
//! ```text
//! U <mode> <tA> <tB> <featA> <featB> P <13 words> A <prim words> B <prim words> R <12 words> [AREA <7 words>]
//! ```
//!
//! and its answer
//!
//! ```text
//! ret <word> P <13 words> R <12 words> [AREA <7 words> <npt × 7 words>]
//! ```
//!
//! (params after the call, result after the call, area header `+0, npt, nmaxpt, minedge, n` and
//! the first `min(npt, nmaxpt)` entries `pt xyz, prim0, prim1, feat0, feat1`). The probe and the
//! port pre-fill the area arrays with the same sentinels, so unwritten entries compare equal.
//!
//! Goldens: `tests/golden/<name>.trace`, records of two lines (`U …` / `= …`). [`check`]:
//! * without the oracle: run the port on every golden case and compare the answers bit for bit;
//! * with `AAEMU_PHYS_ORACLE`: regenerate the golden from the DLL (it must match the stored file,
//!   or is rewritten with `AAEMU_PHYS_BLESS=1`), then compare the port;
//! * `AAEMU_PHYS_P2B_LIVE=<n>` (with the oracle): additionally run `n` fresh cases (another seed)
//!   through both, without touching any golden;
//! * `AAEMU_PHYS_P2B_DUMP=<file>`: write every mismatch (case, oracle, port) to that file.

use super::{new_area, UnprojTables};
use crate::collision::contact::{ContactArea, UnprojParams, UnprojResult};
use crate::geometry::primitives::{
    BoxPrim, CylinderPrim, Primitive, RayPrim, SpherePrim, Triangle,
};
use crate::math::{Mat33, Quat, Vec3};
use crate::oracle::{golden_dir, oracle_path, BLESS_ENV};
use std::fmt::Write as _;
use std::io::Write as _;
use std::path::Path;
use std::process::{Command, Stdio};

/// Environment variable: number of extra live (oracle-only) cases per [`check`].
pub const LIVE_ENV: &str = "AAEMU_PHYS_P2B_LIVE";
/// Environment variable: file that receives all mismatches.
pub const DUMP_ENV: &str = "AAEMU_PHYS_P2B_DUMP";

// ------------------------------------------------------------------------------------- rng

/// splitmix64 with float helpers for case generators.
#[derive(Clone, Debug)]
pub struct Rng(pub u64);

impl Rng {
    /// Seeded generator.
    #[must_use]
    pub fn new(seed: u64) -> Self {
        Rng(seed)
    }
    /// Next 64 random bits.
    pub fn next_u64(&mut self) -> u64 {
        self.0 = self.0.wrapping_add(0x9E37_79B9_7F4A_7C15);
        let mut z = self.0;
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^ (z >> 31)
    }
    /// Uniform float in `[lo, hi)`.
    pub fn f(&mut self, lo: f32, hi: f32) -> f32 {
        lo + (hi - lo) * ((self.next_u64() >> 40) as f32 * (1.0 / 16_777_216.0))
    }
    /// Uniform integer in `0..n`.
    pub fn below(&mut self, n: u32) -> u32 {
        ((self.next_u64() >> 32) % u64::from(n.max(1))) as u32
    }
    /// True with probability `p`.
    pub fn chance(&mut self, p: f32) -> bool {
        self.f(0.0, 1.0) < p
    }
    /// One of the items.
    pub fn pick<T: Copy>(&mut self, items: &[T]) -> T {
        items[self.below(items.len() as u32) as usize]
    }
    /// Value rounded to a multiple of `step` (hits exact parallels / coincidences).
    pub fn quant(&mut self, lo: f32, hi: f32, step: f32) -> f32 {
        (self.f(lo, hi) / step).round() * step
    }
    /// Random vector with components in `[lo, hi)`.
    pub fn vec(&mut self, lo: f32, hi: f32) -> Vec3 {
        Vec3::new(self.f(lo, hi), self.f(lo, hi), self.f(lo, hi))
    }
    /// Random unit vector (float-normalised, not uniform on the sphere).
    pub fn unit(&mut self) -> Vec3 {
        loop {
            let v = self.vec(-1.0, 1.0);
            let l2 = v.x * v.x + v.y * v.y + v.z * v.z;
            if l2 > 0.01 && l2 <= 1.0 {
                let inv = 1.0 / l2.sqrt();
                return Vec3::new(v.x * inv, v.y * inv, v.z * inv);
            }
        }
    }
    /// Random unit vector, sometimes an exact axis direction (±x, ±y, ±z).
    pub fn unit_or_axis(&mut self, p_axis: f32) -> Vec3 {
        if self.chance(p_axis) {
            let s = if self.chance(0.5) { 1.0 } else { -1.0 };
            match self.below(3) {
                0 => Vec3::new(s, 0.0, 0.0),
                1 => Vec3::new(0.0, s, 0.0),
                _ => Vec3::new(0.0, 0.0, s),
            }
        } else {
            self.unit()
        }
    }
    /// Random rotation matrix (from a random unit quaternion).
    pub fn rotation(&mut self) -> Mat33 {
        loop {
            let q = [
                self.f(-1.0, 1.0),
                self.f(-1.0, 1.0),
                self.f(-1.0, 1.0),
                self.f(-1.0, 1.0),
            ];
            let n2 = q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3];
            if n2 > 0.01 && n2 <= 1.0 {
                let inv = 1.0 / n2.sqrt();
                return Mat33::from_quat(Quat::new(q[0] * inv, q[1] * inv, q[2] * inv, q[3] * inv));
            }
        }
    }
}

/// FNV-1a of a name (seed derivation).
#[must_use]
pub fn seed_of(name: &str) -> u64 {
    let mut h: u64 = 0xcbf2_9ce4_8422_2325;
    for b in name.bytes() {
        h ^= u64::from(b);
        h = h.wrapping_mul(0x0100_0000_01b3);
    }
    h
}

// ------------------------------------------------------------------------------------- words

/// A primitive as the words of its binary layout.
#[must_use]
pub fn prim_words(p: &Primitive) -> Vec<u32> {
    let v3 = |w: &mut Vec<u32>, v: Vec3| w.extend_from_slice(&v.to_bits());
    let mut w = Vec::new();
    match p {
        Primitive::Box(b) => {
            w.extend_from_slice(&b.basis.to_bits());
            w.push(b.oriented as u32);
            v3(&mut w, b.center);
            v3(&mut w, b.size);
        }
        Primitive::Triangle(t) => {
            for q in t.pt {
                v3(&mut w, q);
            }
            v3(&mut w, t.n);
        }
        Primitive::Ray(r) => {
            v3(&mut w, r.origin);
            v3(&mut w, r.dir);
        }
        Primitive::Sphere(s) => {
            v3(&mut w, s.center);
            w.push(s.r.to_bits());
        }
        Primitive::Cylinder(c) | Primitive::Capsule(c) => {
            v3(&mut w, c.center);
            v3(&mut w, c.axis);
            w.push(c.r.to_bits());
            w.push(c.hh.to_bits());
        }
        Primitive::Heightfield(_) => panic!("heightfields are not unprojected"),
    }
    w
}

/// Number of words of a primitive type (`None` for types without unprojection).
#[must_use]
pub fn prim_word_count(t: i32) -> Option<usize> {
    match t {
        0 => Some(16),
        1 => Some(12),
        3 => Some(6),
        4 => Some(4),
        5 | 6 => Some(8),
        _ => None,
    }
}

fn v3w(w: &[u32], i: usize) -> Vec3 {
    Vec3::from_bits([w[i], w[i + 1], w[i + 2]])
}

/// A primitive of type `t` from its words.
#[must_use]
pub fn prim_from_words(t: i32, w: &[u32]) -> Primitive {
    let f = |i: usize| f32::from_bits(w[i]);
    match t {
        0 => Primitive::Box(BoxPrim {
            basis: Mat33::from_bits(w[0..9].try_into().unwrap()),
            oriented: w[9] as i32,
            center: v3w(w, 10),
            size: v3w(w, 13),
        }),
        1 => Primitive::Triangle(Triangle {
            pt: [v3w(w, 0), v3w(w, 3), v3w(w, 6)],
            n: v3w(w, 9),
        }),
        3 => Primitive::Ray(RayPrim {
            origin: v3w(w, 0),
            dir: v3w(w, 3),
        }),
        4 => Primitive::Sphere(SpherePrim {
            center: v3w(w, 0),
            r: f(3),
        }),
        5 | 6 => {
            let c = CylinderPrim {
                center: v3w(w, 0),
                axis: v3w(w, 3),
                r: f(6),
                hh: f(7),
            };
            if t == 5 {
                Primitive::Cylinder(c)
            } else {
                Primitive::Capsule(c)
            }
        }
        _ => panic!("no unprojection primitive of type {t}"),
    }
}

/// `unproj_params` as its 13 words.
#[must_use]
pub fn params_words(p: &UnprojParams) -> [u32; 13] {
    let mut w = [0u32; 13];
    w[0] = p.mode as u32;
    w[1..4].copy_from_slice(&p.dir.to_bits());
    w[4..7].copy_from_slice(&p.center.to_bits());
    w[7] = p.vel.to_bits();
    w[8] = p.tmax.to_bits();
    w[9] = p.tmin.to_bits();
    w[10] = p.min_pt_dist.to_bits();
    w[11] = p.max_cos.to_bits();
    w[12] = p.flag as u32;
    w
}

/// `unproj_params` from its 13 words.
#[must_use]
pub fn params_from_words(w: &[u32]) -> UnprojParams {
    UnprojParams {
        mode: w[0] as i32,
        dir: v3w(w, 1),
        center: v3w(w, 4),
        vel: f32::from_bits(w[7]),
        tmax: f32::from_bits(w[8]),
        tmin: f32::from_bits(w[9]),
        min_pt_dist: f32::from_bits(w[10]),
        max_cos: f32::from_bits(w[11]),
        flag: w[12] as i32,
    }
}

/// `unproj_result` as its 12 words.
#[must_use]
pub fn result_words(r: &UnprojResult) -> [u32; 12] {
    let mut w = [0u32; 12];
    let t = r.t.to_bits();
    let c = r.cos_t.to_bits();
    w[0] = t as u32;
    w[1] = (t >> 32) as u32;
    w[2] = c as u32;
    w[3] = (c >> 32) as u32;
    w[4..7].copy_from_slice(&r.pt.to_bits());
    w[7..10].copy_from_slice(&r.n.to_bits());
    w[10] = r.i_feature[0] as u32;
    w[11] = r.i_feature[1] as u32;
    w
}

/// `unproj_result` from its 12 words.
#[must_use]
pub fn result_from_words(w: &[u32]) -> UnprojResult {
    UnprojResult {
        t: f64::from_bits(u64::from(w[0]) | (u64::from(w[1]) << 32)),
        cos_t: f64::from_bits(u64::from(w[2]) | (u64::from(w[3]) << 32)),
        pt: v3w(w, 4),
        n: v3w(w, 7),
        i_feature: [w[10] as i32, w[11] as i32],
    }
}

/// The initial result every generated case starts from (finite, recognisable values, so partial
/// writes show up and accidental reads stay deterministic).
#[must_use]
pub fn sentinel_result() -> UnprojResult {
    UnprojResult {
        t: 777.0,
        cos_t: -777.0,
        pt: Vec3::new(-555.0, -556.0, -557.0),
        n: Vec3::new(333.0, 334.0, 335.0),
        i_feature: [0x5555, 0x6666],
    }
}

/// An area buffer pre-filled with the probe's sentinels.
#[must_use]
pub fn sentinel_area(hdr: &[u32; 7]) -> ContactArea {
    let n = hdr[2] as usize;
    let mut a = new_area(n);
    a.i_type = hdr[0] as i32;
    a.npt = hdr[1] as i32;
    a.min_edge = f32::from_bits(hdr[3]);
    a.n = v3w(hdr, 4);
    for i in 0..n {
        let k = i as u32;
        a.pt[i] = Vec3::from_bits([0xDEAD_0000 + k, 0xDEAD_4000 + k, 0xDEAD_8000 + k]);
        a.prim[0][i] = (0xBEEF_0000u32 + k) as i32;
        a.prim[1][i] = (0xBEEF_4000u32 + k) as i32;
        a.feature[0][i] = (0xBEEF_8000u32 + k) as i32;
        a.feature[1][i] = (0xBEEF_C000u32 + k) as i32;
    }
    a
}

// ------------------------------------------------------------------------------------- cases

/// One unprojection call.
#[derive(Clone, Debug, PartialEq)]
pub struct Case {
    /// Table plane (0 linear, 1 rotational).
    pub mode: i32,
    pub ta: i32,
    pub tb: i32,
    pub fa: i32,
    pub fb: i32,
    pub params: [u32; 13],
    pub a: Vec<u32>,
    pub b: Vec<u32>,
    pub res: [u32; 12],
    /// Area header `+0, npt, nmaxpt, minedge, n xyz` (`None`: NULL area pointer).
    pub area: Option<[u32; 7]>,
}

impl Case {
    /// A call of `UNPROJECT[mode][a][b]` with `featA = featB = −1`, the sentinel result and no
    /// area. `params.mode` is set to `mode`.
    #[must_use]
    pub fn new(mode: i32, a: &Primitive, b: &Primitive, params: &UnprojParams) -> Self {
        let mut p = *params;
        p.mode = mode;
        Case {
            mode,
            ta: a.geom_type().id(),
            tb: b.geom_type().id(),
            fa: -1,
            fb: -1,
            params: params_words(&p),
            a: prim_words(a),
            b: prim_words(b),
            res: result_words(&sentinel_result()),
            area: None,
        }
    }

    /// With a contact-area buffer of `nmaxpt` points and the given `minedge`.
    #[must_use]
    pub fn with_area(mut self, nmaxpt: u32, min_edge: f32) -> Self {
        self.area = Some([0, 0, nmaxpt, min_edge.to_bits(), 0, 0, 0]);
        self
    }

    /// With explicit input feature ids.
    #[must_use]
    pub fn with_features(mut self, fa: i32, fb: i32) -> Self {
        self.fa = fa;
        self.fb = fb;
        self
    }

    /// The probe line.
    #[must_use]
    pub fn to_line(&self) -> String {
        let mut s = format!(
            "U {:x} {:x} {:x} {:x} {:x} P",
            self.mode, self.ta, self.tb, self.fa as u32, self.fb as u32
        );
        let words = |s: &mut String, w: &[u32]| {
            for x in w {
                let _ = write!(s, " {x:08x}");
            }
        };
        words(&mut s, &self.params);
        s.push_str(" A");
        words(&mut s, &self.a);
        s.push_str(" B");
        words(&mut s, &self.b);
        s.push_str(" R");
        words(&mut s, &self.res);
        if let Some(h) = &self.area {
            s.push_str(" AREA");
            words(&mut s, h);
        }
        s
    }

    /// Parse a probe line.
    pub fn parse(line: &str) -> Result<Case, String> {
        let mut it = line.split_whitespace();
        let word = |it: &mut std::str::SplitWhitespace| -> Result<u32, String> {
            let t = it.next().ok_or("truncated case")?;
            u32::from_str_radix(t, 16).map_err(|e| format!("bad word {t}: {e}"))
        };
        let expect = |it: &mut std::str::SplitWhitespace, k: &str| -> Result<(), String> {
            (it.next() == Some(k))
                .then_some(())
                .ok_or(format!("expected {k}"))
        };
        expect(&mut it, "U")?;
        let mode = word(&mut it)? as i32;
        let ta = word(&mut it)? as i32;
        let tb = word(&mut it)? as i32;
        let fa = word(&mut it)? as i32;
        let fb = word(&mut it)? as i32;
        let na = prim_word_count(ta).ok_or("bad tA")?;
        let nb = prim_word_count(tb).ok_or("bad tB")?;
        expect(&mut it, "P")?;
        let mut params = [0u32; 13];
        for w in &mut params {
            *w = word(&mut it)?;
        }
        expect(&mut it, "A")?;
        let a = (0..na)
            .map(|_| word(&mut it))
            .collect::<Result<Vec<_>, _>>()?;
        expect(&mut it, "B")?;
        let b = (0..nb)
            .map(|_| word(&mut it))
            .collect::<Result<Vec<_>, _>>()?;
        expect(&mut it, "R")?;
        let mut res = [0u32; 12];
        for w in &mut res {
            *w = word(&mut it)?;
        }
        let area = match it.next() {
            Some("AREA") => {
                let mut h = [0u32; 7];
                for w in &mut h {
                    *w = word(&mut it)?;
                }
                Some(h)
            }
            None => None,
            Some(t) => return Err(format!("unexpected {t}")),
        };
        Ok(Case {
            mode,
            ta,
            tb,
            fa,
            fb,
            params,
            a,
            b,
            res,
            area,
        })
    }

    /// Run the case through the port's table and format the answer like the probe.
    #[must_use]
    pub fn run_port(&self, tables: &UnprojTables) -> String {
        let mut p = params_from_words(&self.params);
        let a = prim_from_words(self.ta, &self.a);
        let b = prim_from_words(self.tb, &self.b);
        let mut out = result_from_words(&self.res);
        let mut area = self.area.as_ref().map(sentinel_area);
        let f = tables.t[self.mode as usize][self.ta as usize][self.tb as usize];
        let ret = f(&mut p, &a, self.fa, &b, self.fb, &mut out, area.as_mut());
        format_answer(ret, &p, &out, area.as_ref())
    }
}

/// Format an answer line like the probe.
#[must_use]
pub fn format_answer(
    ret: i32,
    p: &UnprojParams,
    out: &UnprojResult,
    area: Option<&ContactArea>,
) -> String {
    let mut s = format!("ret {:08x} P", ret as u32);
    for x in params_words(p) {
        let _ = write!(s, " {x:08x}");
    }
    s.push_str(" R");
    for x in result_words(out) {
        let _ = write!(s, " {x:08x}");
    }
    if let Some(a) = area {
        let n = a.n.to_bits();
        let _ = write!(
            s,
            " AREA {:08x} {:08x} {:08x} {:08x} {:08x} {:08x} {:08x}",
            a.i_type as u32,
            a.npt as u32,
            a.nmaxpt as u32,
            a.min_edge.to_bits(),
            n[0],
            n[1],
            n[2]
        );
        let cnt = a.npt.min(a.nmaxpt).max(0) as usize;
        for i in 0..cnt {
            let q = a.pt[i].to_bits();
            let _ = write!(
                s,
                " {:08x} {:08x} {:08x} {:08x} {:08x} {:08x} {:08x}",
                q[0],
                q[1],
                q[2],
                a.prim[0][i] as u32,
                a.prim[1][i] as u32,
                a.feature[0][i] as u32,
                a.feature[1][i] as u32
            );
        }
    }
    s
}

// ------------------------------------------------------------------------------------- oracle

/// Send raw command lines to `physoracle --p2b` and return one answer line per command.
pub fn oracle_raw(exe: &Path, lines: &[String]) -> Result<Vec<String>, String> {
    let mut cmd = Command::new(exe);
    if let Some(rt) = std::env::var_os("AAEMU_PHYS_RT") {
        cmd.arg("--rt").arg(rt);
    }
    let mut child = cmd
        .arg("--p2b")
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .map_err(|e| format!("cannot run {}: {e}", exe.display()))?;
    let mut stdin = child.stdin.take().ok_or("no stdin")?;
    let input: String = lines.iter().map(|l| format!("{l}\n")).collect();
    let writer = std::thread::spawn(move || {
        let _ = stdin.write_all(input.as_bytes());
    });
    let out = child
        .wait_with_output()
        .map_err(|e| format!("oracle failed: {e}"))?;
    let _ = writer.join();
    let text = String::from_utf8_lossy(&out.stdout).replace("\r\n", "\n");
    let mut it = text.lines();
    if it.next() != Some("physoracle-p2b 1") {
        return Err(format!(
            "unexpected oracle output: {}",
            text.lines().next().unwrap_or("")
        ));
    }
    let answers: Vec<String> = it.map(str::to_string).collect();
    if answers.len() != lines.len() || !out.status.success() {
        return Err(format!(
            "oracle answered {} of {} lines ({}): {}",
            answers.len(),
            lines.len(),
            out.status,
            answers.last().map_or("", String::as_str)
        ));
    }
    Ok(answers)
}

/// Run cases through the oracle.
pub fn oracle_cases(exe: &Path, cases: &[Case]) -> Result<Vec<String>, String> {
    let lines: Vec<String> = cases.iter().map(Case::to_line).collect();
    oracle_raw(exe, &lines)
}

// ------------------------------------------------------------------------------------- golden

fn golden_text(name: &str, cases: &[Case], answers: &[String]) -> String {
    let mut s = format!("p2b-golden 1 {name}\n");
    for (c, a) in cases.iter().zip(answers) {
        s.push_str(&c.to_line());
        s.push_str("\n= ");
        s.push_str(a);
        s.push('\n');
    }
    s
}

fn parse_golden(text: &str) -> Result<Vec<(Case, String)>, String> {
    let mut lines = text.lines();
    match lines.next() {
        Some(h) if h.starts_with("p2b-golden 1") => {}
        _ => return Err("not a p2b golden".into()),
    }
    let mut out = Vec::new();
    while let Some(c) = lines.next() {
        if c.is_empty() {
            continue;
        }
        let a = lines.next().ok_or("odd golden")?;
        let a = a.strip_prefix("= ").ok_or("bad golden answer line")?;
        out.push((Case::parse(c)?, a.to_string()));
    }
    Ok(out)
}

/// Compare the port with expected answers; returns a report of the mismatches (empty = all
/// equal).
#[must_use]
pub fn compare(name: &str, tables: &UnprojTables, recs: &[(Case, String)]) -> String {
    compare_masked(name, tables, recs, &|a: &str| a.to_string())
}

/// [`compare`] after passing both answers through `mask` (used to blank words the original
/// fills from uninitialised memory).
#[must_use]
pub fn compare_masked(
    name: &str,
    tables: &UnprojTables,
    recs: &[(Case, String)],
    mask: &dyn Fn(&str) -> String,
) -> String {
    let mut bad = 0usize;
    let mut report = String::new();
    let mut dump = String::new();
    for (i, (c, want)) in recs.iter().enumerate() {
        let got = mask(&c.run_port(tables));
        let want = &mask(want);
        if &got != want {
            bad += 1;
            let entry = format!(
                "#{i}\n  case:   {}\n  oracle: {want}\n  port:   {got}\n  diff:   {}\n",
                c.to_line(),
                word_diff(want, &got)
            );
            if bad <= 3 {
                report.push_str(&entry);
            }
            dump.push_str(&entry);
        }
    }
    if bad == 0 {
        return String::new();
    }
    if let Some(p) = std::env::var_os(DUMP_ENV) {
        let _ = std::fs::write(p, &dump);
    }
    format!("{name}: {bad} of {} cases differ\n{report}", recs.len())
}

/// Positions (word index in the answer) that differ.
#[must_use]
pub fn word_diff(a: &str, b: &str) -> String {
    let wa: Vec<&str> = a.split_whitespace().collect();
    let wb: Vec<&str> = b.split_whitespace().collect();
    let mut s = String::new();
    for i in 0..wa.len().max(wb.len()) {
        let (x, y) = (wa.get(i).copied(), wb.get(i).copied());
        if x != y {
            let _ = write!(s, "[{i}] {} vs {}; ", x.unwrap_or("-"), y.unwrap_or("-"));
        }
    }
    s
}

/// Golden + oracle check of a generator (see the module docs). `n` golden cases come from
/// `gen` with the seed derived from `name`; the live run (if enabled) uses another seed.
///
/// # Panics
/// On any mismatch or oracle / golden error.
pub fn check(name: &str, n: usize, gen: &dyn Fn(&mut Rng) -> Case) {
    check_masked(name, n, gen, &|a: &str| a.to_string());
}

/// Blank the result's `cos` words (answer words 19 and 20): linear cells never compute it, and
/// some compositions copy it from an uninitialised stack temporary.
#[must_use]
pub fn mask_cos(answer: &str) -> String {
    answer
        .split(' ')
        .enumerate()
        .map(|(i, w)| if i == 19 || i == 20 { "--------" } else { w })
        .collect::<Vec<_>>()
        .join(" ")
}

/// [`check`] comparing answers after `mask` (the golden stores the masked oracle answers).
///
/// # Panics
/// On any mismatch or oracle / golden error.
pub fn check_masked(
    name: &str,
    n: usize,
    gen: &dyn Fn(&mut Rng) -> Case,
    mask: &dyn Fn(&str) -> String,
) {
    let tables = UnprojTables::build();
    let golden = golden_dir().join(format!("{name}.trace"));
    let recs = if let Some(exe) = oracle_path() {
        let mut rng = Rng::new(seed_of(name));
        let cases: Vec<Case> = (0..n).map(|_| gen(&mut rng)).collect();
        let answers: Vec<String> = oracle_cases(&exe, &cases)
            .unwrap_or_else(|e| panic!("{name}: {e}"))
            .iter()
            .map(|a| mask(a))
            .collect();
        let fresh = golden_text(name, &cases, &answers);
        let stored = std::fs::read_to_string(&golden)
            .ok()
            .map(|s| s.replace("\r\n", "\n"));
        let bless = std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0");
        if stored.as_deref() != Some(fresh.as_str()) {
            if bless || stored.is_none() {
                std::fs::write(&golden, &fresh)
                    .unwrap_or_else(|e| panic!("cannot write {}: {e}", golden.display()));
            } else {
                panic!(
                    "{} differs from the oracle; set {BLESS_ENV}=1 to rewrite",
                    golden.display()
                );
            }
        }
        let mut recs: Vec<(Case, String)> = cases.into_iter().zip(answers).collect();
        let live: usize = std::env::var(LIVE_ENV)
            .ok()
            .and_then(|v| v.parse().ok())
            .unwrap_or(0);
        if live > 0 {
            let mut rng = Rng::new(seed_of(name) ^ 0x5DEE_CE66_D1CE_4E5B);
            let cases: Vec<Case> = (0..live).map(|_| gen(&mut rng)).collect();
            let answers = oracle_cases(&exe, &cases).unwrap_or_else(|e| panic!("{name}: {e}"));
            recs.extend(cases.into_iter().zip(answers));
        }
        recs
    } else {
        let text = std::fs::read_to_string(&golden)
            .unwrap_or_else(|e| panic!("golden {} missing: {e}", golden.display()))
            .replace("\r\n", "\n");
        parse_golden(&text).unwrap_or_else(|e| panic!("{}: {e}", golden.display()))
    };
    let report = compare_masked(name, &tables, &recs, mask);
    assert!(report.is_empty(), "{report}");
}

/// Like [`check`] for raw probe commands (extension hooks): `gen` makes a command line, `port`
/// computes the expected answer from the command line and the oracle's answer (some answers
/// carry engine data the port takes as input, e.g. a bounding box another phase computes) and
/// must reproduce the oracle's answer exactly. Golden records are `<command>` / `= <answer>`.
///
/// # Panics
/// On any mismatch or oracle / golden error.
pub fn check_raw(
    name: &str,
    n: usize,
    gen: &dyn Fn(&mut Rng) -> String,
    port: &dyn Fn(&str, &str) -> String,
) {
    let golden = golden_dir().join(format!("{name}.trace"));
    let recs: Vec<(String, String)> = if let Some(exe) = oracle_path() {
        let mut rng = Rng::new(seed_of(name));
        let lines: Vec<String> = (0..n).map(|_| gen(&mut rng)).collect();
        let answers = oracle_raw(&exe, &lines).unwrap_or_else(|e| panic!("{name}: {e}"));
        let mut fresh = format!("p2b-golden 1 {name}\n");
        for (c, a) in lines.iter().zip(&answers) {
            fresh.push_str(c);
            fresh.push_str("\n= ");
            fresh.push_str(a);
            fresh.push('\n');
        }
        let stored = std::fs::read_to_string(&golden)
            .ok()
            .map(|s| s.replace("\r\n", "\n"));
        let bless = std::env::var_os(BLESS_ENV).is_some_and(|v| v != "0");
        if stored.as_deref() != Some(fresh.as_str()) {
            if bless || stored.is_none() {
                std::fs::write(&golden, &fresh)
                    .unwrap_or_else(|e| panic!("cannot write {}: {e}", golden.display()));
            } else {
                panic!(
                    "{} differs from the oracle; set {BLESS_ENV}=1 to rewrite",
                    golden.display()
                );
            }
        }
        let mut recs: Vec<(String, String)> = lines.into_iter().zip(answers).collect();
        let live: usize = std::env::var(LIVE_ENV)
            .ok()
            .and_then(|v| v.parse().ok())
            .unwrap_or(0);
        if live > 0 {
            let mut rng = Rng::new(seed_of(name) ^ 0x5DEE_CE66_D1CE_4E5B);
            let lines: Vec<String> = (0..live).map(|_| gen(&mut rng)).collect();
            let answers = oracle_raw(&exe, &lines).unwrap_or_else(|e| panic!("{name}: {e}"));
            recs.extend(lines.into_iter().zip(answers));
        }
        recs
    } else {
        let text = std::fs::read_to_string(&golden)
            .unwrap_or_else(|e| panic!("golden {} missing: {e}", golden.display()))
            .replace("\r\n", "\n");
        let mut it = text.lines();
        assert!(
            it.next().is_some_and(|h| h.starts_with("p2b-golden 1")),
            "{}: not a p2b golden",
            golden.display()
        );
        let mut recs = Vec::new();
        while let Some(c) = it.next() {
            if c.is_empty() {
                continue;
            }
            let a = it
                .next()
                .and_then(|a| a.strip_prefix("= "))
                .unwrap_or_else(|| panic!("{}: bad golden", golden.display()));
            recs.push((c.to_string(), a.to_string()));
        }
        recs
    };
    let mut bad = 0usize;
    let mut report = String::new();
    let mut dump = String::new();
    for (i, (c, want)) in recs.iter().enumerate() {
        let got = port(c, want);
        if &got != want {
            bad += 1;
            let entry = format!(
                "#{i}\n  case:   {c}\n  oracle: {want}\n  port:   {got}\n  diff:   {}\n",
                word_diff(want, &got)
            );
            if bad <= 3 {
                report.push_str(&entry);
            }
            dump.push_str(&entry);
        }
    }
    if bad > 0 {
        if let Some(p) = std::env::var_os(DUMP_ENV) {
            let _ = std::fs::write(p, &dump);
        }
    }
    assert!(
        bad == 0,
        "{name}: {bad} of {} cases differ\n{report}",
        recs.len()
    );
}

/// Parse hex words.
#[must_use]
pub fn hex_words<'a>(it: &mut impl Iterator<Item = &'a str>, n: usize) -> Vec<u32> {
    (0..n)
        .map(|_| u32::from_str_radix(it.next().unwrap_or("x"), 16).expect("hex word"))
        .collect()
}

/// Format hex words (` %08x` each).
#[must_use]
pub fn fmt_words(w: &[u32]) -> String {
    let mut s = String::new();
    for x in w {
        let _ = write!(s, " {x:08x}");
    }
    s
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn case_line_roundtrip() {
        let a = Primitive::Box(BoxPrim::aligned(Vec3::new(1.0, 2.0, 3.0), Vec3::splat(0.5)));
        let b = Primitive::Capsule(CylinderPrim {
            center: Vec3::ZERO,
            axis: Vec3::Z,
            r: 0.3,
            hh: 1.0,
        });
        let c = Case::new(0, &a, &b, &UnprojParams::default())
            .with_area(16, 0.01)
            .with_features(3, -1);
        let l = c.to_line();
        assert_eq!(Case::parse(&l).unwrap(), c);
        assert_eq!(prim_from_words(0, &c.a).geom_type(), a.geom_type());
        assert_eq!(prim_from_words(6, &c.b).geom_type(), b.geom_type());
    }
}
