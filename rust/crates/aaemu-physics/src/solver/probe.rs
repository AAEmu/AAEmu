//! Test support for the P4b solver: island cases shared with the oracle's `--p4b` probe
//! (`oracle/p4b_probe_solver.c`), running them through the port and through the real DLL, and
//! golden files.
//!
//! A case is one text line (hex words, floats as raw bits):
//!
//! ```text
//! S <dt> <e0> <prev> <cg> <nvars> {<off> <word>}* <nb> {53 words}* D {53 words} <nc> {44 words}* <nmb> {14 words}* [<nrep>]
//! ```
//!
//! and the answer
//!
//! ```text
//! ret <eax> a5 <word> nbl <n> cl {contact index}* bl {body index}* B {53}* D {53} C {44}* M {14}*
//! ```
//!
//! Bodies are referred to by index (`-1` = the static dummy), the contact owner pointer by
//! `(kind, index)` (0 null, 1 `&contact[index]+164`, 2 multi-body entry `index`). In the port,
//! body `i` is `BodyId(i + 1)` (sort key `i + 1`; the dummy `BodyId::STATIC` has key 0 — the
//! oracle places the bodies above the dummy's address) and contact `i` is `ContactId(i + 1)`.
//!
//! Goldens `tests/golden/<name>.trace` hold `S …` / `= …` record pairs. [`check`] works like the
//! P2b checker: without the oracle the port must reproduce the stored answers; with
//! `AAEMU_PHYS_ORACLE` the golden is regenerated (must match, or `AAEMU_PHYS_BLESS=1`);
//! `AAEMU_PHYS_P4B_LIVE=<n>` adds `n` fresh oracle-only cases; `AAEMU_PHYS_P4B_DUMP=<file>`
//! receives all mismatches.

use super::body::{BodyArena, BodyId, RigidBody};
use super::contact::{Contact, ContactId, ContactOwner, ContactPool};
use super::thread::{MultiBodyEntry, SolverThread};
use crate::math::{Mat33, Quat, Vec3};
use crate::oracle::{golden_dir, oracle_path, BLESS_ENV};
use crate::physvars::{PhysVars, SIZE as VARS_SIZE, WORLD_OFFSET};
use crate::world::EntIdx;
use std::fmt::Write as _;
use std::io::Write as _;
use std::path::Path;
use std::process::{Command, Stdio};

/// Environment variable: number of extra live (oracle-only) cases per [`check`].
pub const LIVE_ENV: &str = "AAEMU_PHYS_P4B_LIVE";
/// Environment variable: file that receives all mismatches.
pub const DUMP_ENV: &str = "AAEMU_PHYS_P4B_DUMP";

/// splitmix64 with float helpers.
#[derive(Clone, Debug)]
pub struct Rng(pub u64);

impl Rng {
    #[must_use]
    pub fn new(seed: u64) -> Self {
        Rng(seed)
    }
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
    /// Uniform integer in `lo..=hi`.
    pub fn range(&mut self, lo: i32, hi: i32) -> i32 {
        lo + self.below((hi - lo + 1) as u32) as i32
    }
    pub fn chance(&mut self, p: f32) -> bool {
        self.f(0.0, 1.0) < p
    }
    pub fn pick<T: Copy>(&mut self, items: &[T]) -> T {
        items[self.below(items.len() as u32) as usize]
    }
    pub fn vec(&mut self, lo: f32, hi: f32) -> Vec3 {
        Vec3::new(self.f(lo, hi), self.f(lo, hi), self.f(lo, hi))
    }
    /// Random unit vector (float-normalised).
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
    /// Random unit quaternion.
    pub fn quat(&mut self) -> Quat {
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
                return Quat::new(q[0] * inv, q[1] * inv, q[2] * inv, q[3] * inv);
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

/// The 53 words of a body image.
#[must_use]
pub fn body_words(b: &RigidBody) -> [u32; 53] {
    let mut w = [0u32; 53];
    let mut put = |i: usize, v: f32| w[i] = v.to_bits();
    for (k, v) in [b.pos, b.p, b.l, b.w, b.v].iter().enumerate() {
        let base = [0, 7, 10, 13, 16][k];
        put(base, v.x);
        put(base + 1, v.y);
        put(base + 2, v.z);
    }
    put(3, b.q.x);
    put(4, b.q.y);
    put(5, b.q.z);
    put(6, b.q.w);
    put(19, b.m);
    put(20, b.minv);
    put(21, b.volume);
    for (k, v) in [b.ibody, b.ibody_inv].iter().enumerate() {
        put(22 + 3 * k, v.x);
        put(23 + 3 * k, v.y);
        put(24 + 3 * k, v.z);
    }
    put(28, b.qfb.x);
    put(29, b.qfb.y);
    put(30, b.qfb.z);
    put(31, b.qfb.w);
    put(32, b.offsfb.x);
    put(33, b.offsfb.y);
    put(34, b.offsfb.z);
    for (k, v) in b.iinv.to_array().iter().enumerate() {
        put(35 + k, *v);
    }
    for (k, v) in [b.fcoll, b.tcoll].iter().enumerate() {
        put(44 + 3 * k, v.x);
        put(45 + 3 * k, v.y);
        put(46 + 3 * k, v.z);
    }
    put(52, b.e_aux);
    w[50] = u32::from(b.solver_idx[0] as u16) | (u32::from(b.solver_idx[1] as u16) << 16);
    w[51] = u32::from(b.solver_idx[2] as u16) | (u32::from(b.solver_idx[3] as u16) << 16);
    w
}

/// Body from its 53 words.
#[must_use]
pub fn body_from_words(w: &[u32]) -> RigidBody {
    let f = |i: usize| f32::from_bits(w[i]);
    let v = |i: usize| Vec3::new(f(i), f(i + 1), f(i + 2));
    let mut iinv = [0.0; 9];
    for (k, x) in iinv.iter_mut().enumerate() {
        *x = f(35 + k);
    }
    RigidBody {
        pos: v(0),
        q: Quat::new(f(3), f(4), f(5), f(6)),
        p: v(7),
        l: v(10),
        w: v(13),
        v: v(16),
        m: f(19),
        minv: f(20),
        volume: f(21),
        ibody: v(22),
        ibody_inv: v(25),
        qfb: Quat::new(f(28), f(29), f(30), f(31)),
        offsfb: v(32),
        iinv: Mat33::from_array(iinv),
        fcoll: v(44),
        tcoll: v(47),
        solver_idx: [
            w[50] as u16 as i16,
            (w[50] >> 16) as u16 as i16,
            w[51] as u16 as i16,
            (w[51] >> 16) as u16 as i16,
        ],
        e_aux: f(52),
    }
}

fn body_index(b: BodyId) -> u32 {
    if b == BodyId::STATIC {
        u32::MAX
    } else {
        b.0 - 1
    }
}
fn body_of(w: u32) -> BodyId {
    if w as i32 <= -1 {
        BodyId::STATIC
    } else {
        BodyId(w + 1)
    }
}

/// The 44 words of a contact image (pointer words encoded, see the module docs).
#[must_use]
pub fn contact_words(c: &Contact) -> [u32; 44] {
    let mut w = [0u32; 44];
    let mut put = |i: usize, v: f32| w[i] = v.to_bits();
    for (k, v) in [c.pt[0], c.pt[1], c.n].iter().enumerate() {
        put(6 + 3 * k, v.x);
        put(7 + 3 * k, v.y);
        put(8 + 3 * k, v.z);
    }
    put(28, c.nloc.x);
    put(29, c.nloc.y);
    put(30, c.nloc.z);
    put(31, c.friction);
    put(33, c.vn);
    put(34, c.vreq.x);
    put(35, c.vreq.y);
    put(36, c.vreq.z);
    put(37, c.pn);
    put(38, c.penetration);
    w[15] = 0;
    w[20] = c.ipart[0] as u32;
    w[21] = c.ipart[1] as u32;
    w[24] = body_index(c.pbody[0]);
    w[26] = body_index(c.pbody[1]);
    w[32] = c.flags;
    w[39] = c.id_bits;
    w[40] = c.idx_bits;
    w[41] = c.group_n as u32;
    let (k, i) = match c.owner {
        ContactOwner::Null => (0, 0),
        ContactOwner::Count(id) => (1, id.0 - 1),
        ContactOwner::Temp(t) => (2, t),
    };
    w[42] = k;
    w[43] = i;
    w
}

/// Contact from its 44 words.
#[must_use]
pub fn contact_from_words(w: &[u32]) -> Contact {
    let f = |i: usize| f32::from_bits(w[i]);
    let v = |i: usize| Vec3::new(f(i), f(i + 1), f(i + 2));
    let mut c = Contact::zeroed();
    c.next = ContactId::NONE;
    c.prev = ContactId::NONE;
    c.pt = [v(6), v(9)];
    c.n = v(12);
    c.pent = [EntIdx::NONE; 2];
    c.ipart = [w[20] as i32, w[21] as i32];
    c.pbody = [body_of(w[24]), body_of(w[26])];
    c.nloc = v(28);
    c.friction = f(31);
    c.flags = w[32];
    c.vn = f(33);
    c.vreq = v(34);
    c.pn = f(37);
    c.penetration = f(38);
    c.id_bits = w[39];
    c.idx_bits = w[40];
    c.group_n = w[41] as i32;
    c.owner = match w[42] {
        1 => ContactOwner::Count(ContactId(w[43] + 1)),
        2 => ContactOwner::Temp(w[43]),
        _ => ContactOwner::Null,
    };
    c
}

/// The 14 words of a multi-body entry.
#[must_use]
pub fn mb_words(e: &MultiBodyEntry) -> [u32; 14] {
    let mut w = [0u32; 14];
    for k in 0..3 {
        w[k] = e.r[k].to_bits();
        w[3 + k] = e.n0[k].to_bits();
        w[6 + k] = e.n1[k].to_bits();
    }
    w[9] = e.unk36.to_bits();
    w[10] = body_index(e.body);
    w[12] = e.idx as u32;
    w
}

/// Multi-body entry from its 14 words.
#[must_use]
pub fn mb_from_words(w: &[u32]) -> MultiBodyEntry {
    let f = |i: usize| f32::from_bits(w[i]);
    MultiBodyEntry {
        r: [f(0), f(1), f(2)],
        n0: [f(3), f(4), f(5)],
        n1: [f(6), f(7), f(8)],
        unk36: f(9),
        body: body_of(w[10]),
        idx: w[12] as i32,
    }
}

/// One island.
#[derive(Clone, Debug)]
pub struct Case {
    pub dt: f32,
    pub e0: f32,
    pub prev: i32,
    pub cg: bool,
    /// `(world offset, word)` written into the vars block.
    pub vars: Vec<(usize, u32)>,
    pub bodies: Vec<RigidBody>,
    pub dummy: RigidBody,
    /// Contacts; `pbody` uses the port ids (`BodyId(i + 1)`, `BodyId::STATIC`), the owner
    /// `ContactOwner::Count(ContactId(i + 1))` / `Temp(k)`.
    pub contacts: Vec<Contact>,
    pub multi: Vec<MultiBodyEntry>,
    pub nrep: u32,
}

impl Case {
    /// The command line.
    #[must_use]
    pub fn to_line(&self) -> String {
        let mut s = String::with_capacity(64 + 500 * (self.bodies.len() + self.contacts.len()));
        let _ = write!(
            s,
            "S {:08x} {:08x} {:08x} {:x} {:x}",
            self.dt.to_bits(),
            self.e0.to_bits(),
            self.prev as u32,
            u32::from(self.cg),
            self.vars.len()
        );
        for (o, v) in &self.vars {
            let _ = write!(s, " {o:x} {v:08x}");
        }
        let _ = write!(s, " {:x}", self.bodies.len());
        for b in &self.bodies {
            for w in body_words(b) {
                let _ = write!(s, " {w:08x}");
            }
        }
        s.push_str(" D");
        for w in body_words(&self.dummy) {
            let _ = write!(s, " {w:08x}");
        }
        let _ = write!(s, " {:x}", self.contacts.len());
        for c in &self.contacts {
            for w in contact_words(c) {
                let _ = write!(s, " {w:08x}");
            }
        }
        let _ = write!(s, " {:x}", self.multi.len());
        for e in &self.multi {
            for w in mb_words(e) {
                let _ = write!(s, " {w:08x}");
            }
        }
        if self.nrep != 1 {
            let _ = write!(s, " {:x}", self.nrep);
        }
        s
    }

    /// Parse a command line.
    ///
    /// # Errors
    /// On malformed input.
    pub fn parse(line: &str) -> Result<Case, String> {
        let mut it = line.split_ascii_whitespace();
        if it.next() != Some("S") {
            return Err("not an S command".into());
        }
        let mut word = || -> Result<u32, String> {
            let t = it.next().ok_or("missing word")?;
            u32::from_str_radix(t, 16).map_err(|e| format!("{t}: {e}"))
        };
        let dt = f32::from_bits(word()?);
        let e0 = f32::from_bits(word()?);
        let prev = word()? as i32;
        let cg = word()? != 0;
        let nv = word()? as usize;
        let mut vars = Vec::new();
        for _ in 0..nv {
            vars.push((word()? as usize, word()?));
        }
        let nb = word()? as usize;
        let mut bodies = Vec::new();
        for _ in 0..nb {
            let w: Result<Vec<u32>, String> = (0..53).map(|_| word()).collect();
            bodies.push(body_from_words(&w?));
        }
        drop(word);
        if it.next() != Some("D") {
            return Err("expected D".into());
        }
        let mut word = || -> Result<u32, String> {
            let t = it.next().ok_or("missing word")?;
            u32::from_str_radix(t, 16).map_err(|e| format!("{t}: {e}"))
        };
        let w: Result<Vec<u32>, String> = (0..53).map(|_| word()).collect();
        let dummy = body_from_words(&w?);
        let nc = word()? as usize;
        let mut contacts = Vec::new();
        for _ in 0..nc {
            let w: Result<Vec<u32>, String> = (0..44).map(|_| word()).collect();
            contacts.push(contact_from_words(&w?));
        }
        let nm = word()? as usize;
        let mut multi = Vec::new();
        for _ in 0..nm {
            let w: Result<Vec<u32>, String> = (0..14).map(|_| word()).collect();
            multi.push(mb_from_words(&w?));
        }
        let nrep = word().unwrap_or(1);
        Ok(Case {
            dt,
            e0,
            prev,
            cg,
            vars,
            bodies,
            dummy,
            contacts,
            multi,
            nrep,
        })
    }

    /// The world vars of the case.
    #[must_use]
    pub fn phys_vars(&self) -> PhysVars {
        let mut b = PhysVars::default().to_bytes();
        for &(o, v) in &self.vars {
            let k = o - WORLD_OFFSET;
            if k + 4 <= VARS_SIZE {
                b[k..k + 4].copy_from_slice(&v.to_le_bytes());
            }
        }
        PhysVars::from_bytes(&b)
    }

    /// Run the case through the port; returns the answer line.
    #[must_use]
    pub fn run_port(&self) -> String {
        self.run_port_full().0
    }

    /// Run the case through the port; returns the answer line and the solve result.
    #[must_use]
    pub fn run_port_full(&self) -> (String, super::invoke::SolveResult) {
        let mut arena = BodyArena::default();
        arena.bodies[0] = self.dummy;
        for b in &self.bodies {
            let id = arena.alloc(0);
            arena.sort_key[id.0 as usize] = u64::from(id.0);
            *arena.get_mut(id) = *b;
        }
        let mut pool = ContactPool::default();
        let mut ids = Vec::new();
        for c in &self.contacts {
            let id = ContactId(pool.recs.len() as u32);
            pool.recs.push(*c);
            ids.push(id);
        }
        let vars = self.phys_vars();
        let mut th = SolverThread::new(0);
        th.multi_body = self.multi.clone();
        let mut res = None;
        for _ in 0..self.nrep.max(1) {
            th.init();
            for &id in &ids {
                th.register_contact(&mut pool, id);
            }
            if !self.cg {
                th.disable_cg();
            }
            res = Some(th.invoke(self.dt, &vars, self.e0, self.prev, &mut arena, &mut pool));
        }
        let res = res.unwrap_or_default();
        let mut s = String::new();
        let nbl = th.mem.n_bodies();
        let _ = write!(
            s,
            "ret {:08x} a5 {:08x} nbl {:08x} cl",
            res.ret as u32, res.n_contacts as u32, nbl as u32
        );
        for c in th.contacts() {
            let _ = write!(s, " {:08x}", c.0.wrapping_sub(1));
        }
        s.push_str(" bl");
        for j in 0..nbl.max(0) as usize {
            let _ = write!(s, " {:08x}", body_index(th.mem.body(j)));
        }
        s.push_str(" B");
        for i in 0..self.bodies.len() {
            for w in body_words(arena.get(BodyId(i as u32 + 1))) {
                let _ = write!(s, " {w:08x}");
            }
        }
        s.push_str(" D");
        for w in body_words(arena.get(BodyId::STATIC)) {
            let _ = write!(s, " {w:08x}");
        }
        s.push_str(" C");
        for &id in &ids {
            for w in contact_words(pool.get(id)) {
                let _ = write!(s, " {w:08x}");
            }
        }
        s.push_str(" M");
        for e in &th.multi_body {
            for w in mb_words(e) {
                let _ = write!(s, " {w:08x}");
            }
        }
        (s, res)
    }
}

/// Histogram of the solve paths of `n` generated cases (diagnostics for generator coverage).
#[must_use]
pub fn path_stats(name: &str, n: usize, gen: &dyn Fn(&mut Rng) -> Case) -> String {
    let mut rng = Rng::new(seed_of(name));
    let mut h = std::collections::BTreeMap::new();
    for _ in 0..n {
        let (_, r) = gen(&mut rng).run_port_full();
        *h.entry(format!("{:?}", r.path)).or_insert(0usize) += 1;
    }
    format!("{name}: {h:?}")
}

/// Send command lines to `physoracle --p4b`; one answer per line.
///
/// # Errors
/// When the oracle cannot run or answers badly.
pub fn oracle_raw(exe: &Path, lines: &[String]) -> Result<Vec<String>, String> {
    let mut cmd = Command::new(exe);
    if let Some(rt) = std::env::var_os("AAEMU_PHYS_RT") {
        cmd.arg("--rt").arg(rt);
    }
    let mut child = cmd
        .arg("--p4b")
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
    if it.next() != Some("physoracle-p4b 1") {
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

/// Word-level diff of two answers (first differences, labelled by section).
#[must_use]
pub fn word_diff(a: &str, b: &str) -> String {
    let wa: Vec<&str> = a.split(' ').collect();
    let wb: Vec<&str> = b.split(' ').collect();
    let mut out = String::new();
    let mut sec = "";
    let mut base = 0usize;
    let mut n = 0;
    for i in 0..wa.len().max(wb.len()) {
        let (x, y) = (wa.get(i).copied().unwrap_or("-"), wb.get(i).copied().unwrap_or("-"));
        if x.len() != 8 || x.chars().any(|c| !c.is_ascii_hexdigit()) {
            sec = x;
            base = i + 1;
        }
        if x != y {
            let _ = write!(out, " [{sec}+{}] {x}/{y}", i - base);
            n += 1;
            if n >= 12 {
                out.push_str(" …");
                break;
            }
        }
    }
    out
}

/// Golden + oracle check of a generator (`n` golden cases from the seed of `name`).
///
/// # Panics
/// On any mismatch or oracle / golden error.
pub fn check(name: &str, n: usize, gen: &dyn Fn(&mut Rng) -> Case) {
    let golden = golden_dir().join(format!("{name}.trace"));
    let recs: Vec<(String, String)> = if let Some(exe) = oracle_path() {
        let mut rng = Rng::new(seed_of(name));
        let lines: Vec<String> = (0..n).map(|_| gen(&mut rng).to_line()).collect();
        let answers = oracle_raw(&exe, &lines).unwrap_or_else(|e| panic!("{name}: {e}"));
        let mut fresh = format!("p4b-golden 1 {name}\n");
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
            let lines: Vec<String> = (0..live).map(|_| gen(&mut rng).to_line()).collect();
            for chunk in lines.chunks(200) {
                let answers = oracle_raw(&exe, chunk).unwrap_or_else(|e| panic!("{name}: {e}"));
                recs.extend(chunk.iter().cloned().zip(answers));
            }
        }
        recs
    } else {
        let text = std::fs::read_to_string(&golden)
            .unwrap_or_else(|e| panic!("golden {} missing: {e}", golden.display()))
            .replace("\r\n", "\n");
        let mut it = text.lines();
        assert!(
            it.next().is_some_and(|h| h.starts_with("p4b-golden 1")),
            "{}: not a p4b golden",
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
        let case = Case::parse(c).unwrap_or_else(|e| panic!("{name} #{i}: {e}"));
        let got = case.run_port();
        if &got != want {
            bad += 1;
            let entry = format!("#{i}\n  case:   {c}\n  diff:  {}\n", word_diff(want, &got));
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
