//! The per-thread solver context ("RB thread data", `qword_352186E0[tid]`, 0x5FC030 bytes) and
//! the functions the entities call while an island registers its contacts:
//!
//! * [`SolverThread::init`] — `InitSolver` 0x3509A900,
//! * [`SolverThread::register_contact`] — `RegisterContact` 0x350994B0,
//! * [`SolverThread::disable_cg`] — 0x35099570,
//! * [`SolverThread::alloc_tmp_contacts`] — `AllocSolverTmpBuf` 0x3509A9C0.
//!
//! The block is modelled as flat memory ([`Mem`], 32-bit words addressed by the binary's byte
//! offsets) so that every region keeps the original layout, sizes and stale contents between
//! islands (nothing in the binary clears it either). Pointer slots hold
//! * a [`ContactId`] (contact list `T+8`),
//! * a [`BodyId`] (body list `T+79880`),
//! * a byte offset into the block itself (pointers between the solver's own records; 0 = null).
//!
//! Region map (byte offsets):
//!
//! | offset | stride | content |
//! |---|---|---|
//! | 0 | | `nContacts` |
//! | 4 | | `nBodies` (the solver's return value) |
//! | 8 | 8 | contact list (9984) |
//! | 79880 | 8 | body list (9984; more bodies overflow into the next region, as in the binary) |
//! | 159752 | 112 | per-contact solver record ([`crec`]) |
//! | 1277960 | 128 | per-contact CG / LCP record ([`lcp`]) |
//! | 2555912 | 72 | per-contact `Pproj` (36) + `Kinv` (36) |
//! | 3274760 | 80 | per-body solver record ([`brec`]) |
//! | 4073480 | 88 | per-body graph record ([`bg`]) |
//! | 4952072 | 32 | contact-graph edges (stage 3) |
//! | 5271560 | 32 | heavy/light neighbour nodes (difficulty test, stage 3) |
//! | 5591048 | 16 | adjacency nodes (stage 3) |
//! | 5750792 | | `nAdj`, two counters |
//! | 5750804 | | temp buffer (0x80000 bytes) used by `AllocSolverTmpBuf` |
//! | 6275092 | | temp bytes used, overflow buffer pointer / used / capacity |
//! | 6275112 | 1 | "CG allowed" byte |

use super::body::BodyId;
use super::contact::{flags, Contact, ContactId, ContactOwner, ContactPool};

/// Size of the block in bytes.
pub const SIZE: usize = 0x5F_C030;
/// Maximum number of contacts per island (`9984`; later ones overwrite the last slot).
pub const MAX_CONTACTS: usize = 9984;

/// Byte offsets of the regions (see the module docs).
pub mod off {
    pub const N_CONTACTS: usize = 0;
    pub const N_BODIES: usize = 4;
    pub const CONTACTS: usize = 8;
    pub const BODIES: usize = 79_880;
    pub const CREC: usize = 159_752;
    pub const LCP: usize = 1_277_960;
    pub const PROJ: usize = 2_555_912;
    pub const BREC: usize = 3_274_760;
    pub const BG: usize = 4_073_480;
    pub const EDGE: usize = 4_952_072;
    pub const HL: usize = 5_271_560;
    pub const ADJ: usize = 5_591_048;
    pub const N_ADJ: usize = 5_750_792;
    pub const CNT1: usize = 5_750_796;
    pub const CNT2: usize = 5_750_800;
    pub const TMP: usize = 5_750_804;
    pub const TMP_USED: usize = 6_275_092;
    pub const OVF_PTR: usize = 6_275_096;
    pub const OVF_USED: usize = 6_275_104;
    pub const OVF_CAP: usize = 6_275_108;
    pub const CG_ALLOWED: usize = 6_275_112;
}

/// Per-contact solver record (112 bytes at `T+159752+112·i`): field offsets.
pub mod crec {
    pub const STRIDE: usize = 112;
    /// `+0` r0 = pt0 − pos0.
    pub const R0: usize = 0;
    /// `+12` r1 = pt1 − pos1.
    pub const R1: usize = 12;
    /// `+24` K (3×3, row-major).
    pub const K: usize = 24;
    /// `+60` n.
    pub const N: usize = 60;
    /// `+72` vreq.
    pub const VREQ: usize = 72;
    /// `+84` Pn (limit).
    pub const PN: usize = 84;
    /// `+88` friction.
    pub const MU: usize = 88;
    /// `+92` flags.
    pub const FLAGS: usize = 92;
    /// `+96`, `+100` body solver indices.
    pub const IB0: usize = 96;
    pub const IB1: usize = 100;
    /// `+104` (short) group count / hit count, `+106` (short) owner's solver index.
    pub const NHIT: usize = 104;
    pub const OWNER: usize = 106;
    /// `+108` accumulated normal impulse.
    pub const PN_ACC: usize = 108;
}

/// Per-contact CG / LCP record (128 bytes at `T+1277960+128·k`): field offsets.
pub mod lcp {
    pub const STRIDE: usize = 128;
    /// `+0` contact index | bit 31.
    pub const IDX: usize = 0;
    /// `+4` "project" flag.
    pub const PROJ: usize = 4;
    /// `+8`, `+16` pointers to the two body records.
    pub const B0: usize = 8;
    pub const B1: usize = 16;
    /// `+24`, `+36` arms.
    pub const R0: usize = 24;
    pub const R1: usize = 36;
    /// `+48` b (right-hand side).
    pub const B: usize = 48;
    /// `+60` residual r.
    pub const R: usize = 60;
    /// `+72` search direction p.
    pub const P: usize = 72;
    /// `+84` solution x.
    pub const X: usize = 84;
    /// `+96` scalar.
    pub const S: usize = 96;
    /// `+100` A·p.
    pub const AP: usize = 100;
    /// `+112` n.
    pub const N: usize = 112;
}

/// Per-body solver record (80 bytes at `T+3274760+80·j`): field offsets.
pub mod brec {
    pub const STRIDE: usize = 80;
    pub const V: usize = 0;
    pub const W: usize = 12;
    pub const MINV: usize = 24;
    pub const M: usize = 28;
    pub const IINV: usize = 32;
    pub const L: usize = 68;
}

/// Per-body graph record (88 bytes at `T+4073480+88·j`): field offsets.
pub mod bg {
    pub const STRIDE: usize = 88;
    /// `+0` heavy/light node list head (block offset).
    pub const HL: usize = 0;
    /// `+8` edge list head.
    pub const EDGES: usize = 8;
    /// `+16` adjacency list head.
    pub const ADJ: usize = 16;
    /// `+24` Minv.
    pub const MINV: usize = 24;
    /// `+28` level.
    pub const LEVEL: usize = 28;
    /// `+32` visit mark.
    pub const MARK: usize = 32;
    /// `+36` sort position.
    pub const POS: usize = 36;
    /// `+40` unprojection velocity, `+52` unprojection angular velocity.
    pub const VU: usize = 40;
    pub const WU: usize = 52;
    /// `+64`, `+76` saved collision force / torque accumulators.
    pub const FSAVE: usize = 64;
    pub const TSAVE: usize = 76;
}

/// Flat model of the block: little-endian 32-bit words addressed by byte offset.
#[derive(Clone)]
pub struct Mem {
    w: Vec<u32>,
}

impl std::fmt::Debug for Mem {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "Mem({} bytes)", self.w.len() * 4)
    }
}

impl Mem {
    /// A zeroed block of [`SIZE`] bytes.
    #[must_use]
    pub fn new() -> Self {
        Self {
            w: vec![0; SIZE / 4],
        }
    }
    #[inline]
    #[must_use]
    pub fn u(&self, o: usize) -> u32 {
        self.w[o >> 2]
    }
    #[inline]
    pub fn su(&mut self, o: usize, v: u32) {
        self.w[o >> 2] = v;
    }
    #[inline]
    #[must_use]
    pub fn i(&self, o: usize) -> i32 {
        self.w[o >> 2] as i32
    }
    #[inline]
    pub fn si(&mut self, o: usize, v: i32) {
        self.w[o >> 2] = v as u32;
    }
    #[inline]
    #[must_use]
    pub fn f(&self, o: usize) -> f32 {
        f32::from_bits(self.w[o >> 2])
    }
    #[inline]
    pub fn sf(&mut self, o: usize, v: f32) {
        self.w[o >> 2] = v.to_bits();
    }
    /// 64-bit slot (pointer).
    #[inline]
    #[must_use]
    pub fn q(&self, o: usize) -> u64 {
        u64::from(self.w[o >> 2]) | (u64::from(self.w[(o >> 2) + 1]) << 32)
    }
    #[inline]
    pub fn sq(&mut self, o: usize, v: u64) {
        self.w[o >> 2] = v as u32;
        self.w[(o >> 2) + 1] = (v >> 32) as u32;
    }
    /// 16-bit slot.
    #[inline]
    #[must_use]
    pub fn h(&self, o: usize) -> i16 {
        let w = self.w[o >> 2];
        (if o & 2 != 0 { w >> 16 } else { w & 0xFFFF }) as u16 as i16
    }
    #[inline]
    pub fn sh(&mut self, o: usize, v: i16) {
        let w = &mut self.w[o >> 2];
        let v = u32::from(v as u16);
        *w = if o & 2 != 0 {
            (*w & 0xFFFF) | (v << 16)
        } else {
            (*w & 0xFFFF_0000) | v
        };
    }
    #[inline]
    #[must_use]
    pub fn v3(&self, o: usize) -> [f32; 3] {
        [self.f(o), self.f(o + 4), self.f(o + 8)]
    }
    #[inline]
    pub fn sv3(&mut self, o: usize, v: [f32; 3]) {
        self.sf(o, v[0]);
        self.sf(o + 4, v[1]);
        self.sf(o + 8, v[2]);
    }
    /// 3×3 matrix (9 floats).
    #[inline]
    #[must_use]
    pub fn m9(&self, o: usize) -> [f32; 9] {
        let mut m = [0.0; 9];
        for (k, x) in m.iter_mut().enumerate() {
            *x = self.f(o + 4 * k);
        }
        m
    }
    #[inline]
    pub fn sm9(&mut self, o: usize, m: &[f32; 9]) {
        for (k, x) in m.iter().enumerate() {
            self.sf(o + 4 * k, *x);
        }
    }
    /// Zero `n` bytes (`memset`; `o` and `n` multiples of 4).
    pub fn zero(&mut self, o: usize, n: usize) {
        self.w[o >> 2..(o + n) >> 2].fill(0);
    }

    // ---- typed slots

    /// Contact `i` of the contact list.
    #[inline]
    #[must_use]
    pub fn contact(&self, i: usize) -> ContactId {
        ContactId(self.u(off::CONTACTS + 8 * i))
    }
    #[inline]
    pub fn set_contact(&mut self, i: usize, c: ContactId) {
        self.sq(off::CONTACTS + 8 * i, u64::from(c.0));
    }
    /// Body `j` of the body list.
    #[inline]
    #[must_use]
    pub fn body(&self, j: usize) -> BodyId {
        BodyId(self.u(off::BODIES + 8 * j))
    }
    #[inline]
    pub fn set_body(&mut self, j: usize, b: BodyId) {
        self.sq(off::BODIES + 8 * j, u64::from(b.0));
    }
    #[inline]
    #[must_use]
    pub fn n_contacts(&self) -> i32 {
        self.i(off::N_CONTACTS)
    }
    #[inline]
    #[must_use]
    pub fn n_bodies(&self) -> i32 {
        self.i(off::N_BODIES)
    }
}

impl Default for Mem {
    fn default() -> Self {
        Self::new()
    }
}

/// The per-thread solver context.
#[derive(Clone, Debug)]
pub struct SolverThread {
    /// Physics thread index (`tid`): selects the body's solver-index short (`B+200+2·tid`).
    /// The world steps on thread 0.
    pub tid: usize,
    /// The block (see the module docs).
    pub mem: Mem,
    /// Contact records handed out by [`SolverThread::alloc_tmp_contacts`] (allocated once in
    /// the contact pool and reused for every island, like the binary's temp buffer).
    tmp_recs: Vec<ContactId>,
    /// Records of `tmp_recs` used since the last [`SolverThread::init`].
    tmp_used: usize,
    /// Multi-body constraint entries (flag `0x400000`): [`ContactOwner::Temp`]`(k)` designates
    /// the run starting at `multi_body[k]` (`group_n` entries). In the binary these are 56-byte
    /// records owned by the rope entity (`E+960`); see [`MultiBodyEntry`].
    pub multi_body: Vec<MultiBodyEntry>,
}

/// One body of a multi-body (`0x400000`) constraint (56 bytes in the binary).
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct MultiBodyEntry {
    /// `+0` arm.
    pub r: [f32; 3],
    /// `+12` direction used by stage 1 (`Δv` sensitivity).
    pub n0: [f32; 3],
    /// `+24` direction used by stages 2/3 (impulse).
    pub n1: [f32; 3],
    /// `+36` (unused by the solver).
    pub unk36: f32,
    /// `+40` body.
    pub body: BodyId,
    /// `+48` solver index of the body (written by the solver).
    pub idx: i32,
}

impl SolverThread {
    /// A context for physics thread `tid` (the zero-filled block of `operator new` after the
    /// first `InitSolver`).
    #[must_use]
    pub fn new(tid: usize) -> Self {
        Self {
            tid,
            mem: Mem::new(),
            tmp_recs: Vec::new(),
            tmp_used: 0,
            multi_body: Vec::new(),
        }
    }

    /// `InitSolver` 0x3509A900: no contacts, no bodies, temp memory empty, CG allowed.
    pub fn init(&mut self) {
        self.mem.si(off::N_BODIES, 0);
        self.mem.si(off::N_CONTACTS, 0);
        self.mem.si(off::TMP_USED, 0);
        self.mem.su(off::CG_ALLOWED, (self.mem.u(off::CG_ALLOWED) & !0xFF) | 1);
        self.mem.si(off::OVF_USED, 0);
        self.tmp_used = 0;
    }

    /// `RegisterContact` 0x350994B0: append a contact to the island's list.
    ///
    /// * Ordinary contacts (neither `0x4000` shared nor `0x400000` multi-body) get their owner
    ///   pointer set to their own `+164` count.
    /// * A static dummy body in `pbody[1]` is replaced by the dummy of this thread (the port has
    ///   one dummy, [`BodyId::STATIC`]; `pbody[0]` is not checked, as in the binary).
    /// * At most 9984 contacts: the count is clamped to 9983 after each push, so further
    ///   contacts overwrite the last slot.
    pub fn register_contact(&mut self, pool: &mut ContactPool, id: ContactId) {
        let c = pool.get_mut(id);
        if c.flags & (flags::SHARED_PN | flags::MULTI_BODY) == 0 {
            c.owner = ContactOwner::Count(id);
        }
        // (pbody[1] pointing at any static dummy would be redirected to this thread's dummy;
        // the port has the single dummy BodyId::STATIC, so there is nothing to do.)
        let n = self.mem.i(off::N_CONTACTS);
        self.mem.set_contact(n as usize, id);
        let n = n + 1;
        let d = n - 9983;
        self.mem.si(off::N_CONTACTS, (d & (d >> 31)) + 9983);
    }

    /// 0x35099570: forbid the CG fast path for this island (an entity with flag `0x8000000` or
    /// in water).
    pub fn disable_cg(&mut self) {
        self.mem.su(off::CG_ALLOWED, self.mem.u(off::CG_ALLOWED) & !0xFF);
    }

    /// "CG allowed" byte (`T+6275112`).
    #[must_use]
    pub fn cg_allowed(&self) -> bool {
        self.mem.u(off::CG_ALLOWED) & 0xFF != 0
    }

    /// `AllocSolverTmpBuf` 0x3509A9C0 for contact records: `n` zeroed contact records
    /// (`n·176` bytes) valid until the next [`SolverThread::init`].
    ///
    /// The binary carves them out of the block's 0x80000-byte temp buffer and, past that, out
    /// of a growing heap buffer whose growth relocates the contact pointers already pushed
    /// (and logs "AllocSolverTmpBuf(%d)"). The port keeps the records in the contact pool
    /// (stable ids), so no relocation is needed; the records are reset to the zeroed state
    /// ([`Contact::zeroed`]) each time they are handed out, like the `memset`.
    pub fn alloc_tmp_contacts(&mut self, pool: &mut ContactPool, n: usize) -> Vec<ContactId> {
        while self.tmp_recs.len() < self.tmp_used + n {
            let id = ContactId(pool.recs.len() as u32);
            pool.recs.push(Contact::zeroed());
            self.tmp_recs.push(id);
        }
        let ids = self.tmp_recs[self.tmp_used..self.tmp_used + n].to_vec();
        for &id in &ids {
            *pool.get_mut(id) = Contact::zeroed();
        }
        self.tmp_used += n;
        // byte accounting of the binary (inline buffer first, then the overflow buffer)
        let bytes = (n * 176) as i32;
        let used = self.mem.i(off::TMP_USED);
        if ((used + bytes) as u32) < 0x8_0000 {
            self.mem.si(off::TMP_USED, used + bytes);
        } else {
            let o = self.mem.i(off::OVF_USED);
            if o + bytes > self.mem.i(off::OVF_CAP) {
                let cap = self.mem.i(off::OVF_CAP) + 0x8_0000;
                self.mem.si(off::OVF_CAP, cap);
            }
            self.mem.si(off::OVF_USED, o + bytes);
        }
        ids
    }

    /// Contacts registered for the island (`T+8`, the list `InvokeContactSolver` hands to the
    /// post-solve 0x3507F210; sorted by the solver when stage 3 ran).
    #[must_use]
    pub fn contacts(&self) -> Vec<ContactId> {
        let n = self.mem.n_contacts().max(0) as usize;
        (0..n).map(|i| self.mem.contact(i)).collect()
    }

    /// Bodies of the last solve (`T+79880`, `T+4` entries).
    #[must_use]
    pub fn bodies(&self) -> Vec<BodyId> {
        let n = self.mem.n_bodies().max(0) as usize;
        (0..n).map(|j| self.mem.body(j)).collect()
    }
}
