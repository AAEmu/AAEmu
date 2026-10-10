//! The per-thread contact scratch of the primitive `Intersect` as raw memory.
//!
//! The original keeps every contact it produces in a per-caller arena (base 0x35256F80, stride
//! 0x39FB8 = 237 496 bytes, one slot per physics thread, `geometry.md` §1.2 / §3.4). Contacts
//! hold *pointers* into the same arena (border points, contact areas, area arrays), the pair and
//! unprojection functions write without capacity checks, and the counters live right behind
//! the pools they count — so an overrun of one pool lands in the next pool or in a counter.
//! To reproduce what is observable (which points survive, which counters get clobbered),
//! [`ThreadScratch`] models the arena range the primitive `Intersect` touches as 32-bit words
//! with the original layout, and pointers as the addresses the original would store.
//!
//! Layout (arena offsets; x64 VAs are `0x35256F80 + off` for caller 0):
//!
//! | off | size | content |
//! |---|---|---|
//! | `0xC7B8` | 64 × 136 | `geom_contact` records ([`CONTACTS`]) |
//! | `0xE9B8` | 4 | contact count ([`N_CONTACTS`]) |
//! | `0xE9C0` | 32 × 72 | `geom_contact_area` records ([`AREAS`]) |
//! | `0xF2C0` | 4 | area count ([`N_AREAS`]) |
//! | `0xF2C4` | 256 × 12 | area points ([`AREA_PTS`]) |
//! | `0xFEC4` | 256 × 4 ×4 | area `piPrim[0]`, `piFeature[0]`, `piPrim[1]`, `piFeature[1]` |
//! | `0x10EC4` | 4 | area point count ([`N_AREA_PTS`]) |
//! | `0x10EC8` | 2048 × 12 | border points ([`BORDER`]) |
//! | `0x16EC8` | 4 | border point count ([`N_BORDER`]) |
//! | `0x16ECC` | 4 | an int used by the mesh contact code (`CTriMesh` slots 54/58/59) |
//! | `0x16ED0` | … | mesh scratch (modelled up to [`HI`] so that overruns stay visible) |
//!
//! Reads outside `[LO, HI)` return 0 and writes there are dropped (the original would touch
//! unrelated arena memory or fault); with the counters the gates allow this only happens after
//! a counter has been clobbered by an overrun.
//!
//! Record layouts: `geom_contact` (136 B) `+0` double t, `+8` pt, `+20` n, `+32` dir, `+44`
//! iUnprojMode, `+48` vel, `+52` id\[2\], `+60` iPrim\[2\], `+68` iFeature\[2\], `+76` iNode\[2\],
//! `+88` ptborder, `+96` idxborder, `+104` nborderpt, `+112` center, `+124` u8
//! bBorderConsecutive, `+128` area. `geom_contact_area` (72 B) `+0` type, `+4` npt, `+8`
//! nmaxpt, `+12` minedge, `+16` piPrim\[0\], `+24` piPrim\[1\], `+32` piFeature\[0\], `+40`
//! piFeature\[1\], `+48` pt, `+56` n.

use crate::collision::contact::{ContactArea, GeomContact};
use crate::math::Vec3;

/// First modelled arena offset (the contact records).
pub const LO: u32 = 0xC7B8;
/// End of the modelled range.
pub const HI: u32 = 0x186D0;
/// `geom_contact[64]`.
pub const CONTACTS: u32 = 0xC7B8;
/// Contact count (x64 VA `0x35265938`).
pub const N_CONTACTS: u32 = 0xE9B8;
/// `geom_contact_area[32]`.
pub const AREAS: u32 = 0xE9C0;
/// Area count (`0x35266240`).
pub const N_AREAS: u32 = 0xF2C0;
/// Area points `Vec3[256]` (`0x35266244`).
pub const AREA_PTS: u32 = 0xF2C4;
/// `piPrim[0]` pool (`0x35266E44`).
pub const AREA_PRIM0: u32 = 0xFEC4;
/// `piFeature[0]` pool (`0x35267244`).
pub const AREA_FEAT0: u32 = 0x102C4;
/// `piPrim[1]` pool (`0x35267644`).
pub const AREA_PRIM1: u32 = 0x106C4;
/// `piFeature[1]` pool (`0x35267A44`).
pub const AREA_FEAT1: u32 = 0x10AC4;
/// Area point count (`0x35267E44`).
pub const N_AREA_PTS: u32 = 0x10EC4;
/// Border points `Vec3[2048]` (`0x35267E48`).
pub const BORDER: u32 = 0x10EC8;
/// Border point count (`0x3526DE48`).
pub const N_BORDER: u32 = 0x16EC8;
/// Size of a contact record.
pub const CONTACT_SIZE: u32 = 136;
/// Size of an area record.
pub const AREA_SIZE: u32 = 72;
/// Arena base of caller 0 (x64 VA) and the per-caller stride.
pub const ARENA_BASE_VA: u64 = 0x3525_6F80;
/// Per-caller stride of the arena.
pub const ARENA_STRIDE: u64 = 0x39FB8;

/// Extra entries loaded past `nmaxpt` for an unprojection call (see [`ThreadScratch::load_area`]).
pub const AREA_SLACK: usize = 128;

/// Contact record field offsets.
pub mod c {
    pub const T: u32 = 0;
    pub const PT: u32 = 8;
    pub const N: u32 = 20;
    pub const DIR: u32 = 32;
    pub const MODE: u32 = 44;
    pub const VEL: u32 = 48;
    pub const ID: u32 = 52;
    pub const IPRIM: u32 = 60;
    pub const IFEATURE: u32 = 68;
    pub const INODE: u32 = 76;
    pub const PTBORDER: u32 = 88;
    pub const IDXBORDER: u32 = 96;
    pub const NBORDER: u32 = 104;
    pub const CENTER: u32 = 112;
    pub const CONSECUTIVE: u32 = 124;
    pub const AREA: u32 = 128;
}

/// Area record field offsets.
pub mod a {
    pub const TYPE: u32 = 0;
    pub const NPT: u32 = 4;
    pub const NMAX: u32 = 8;
    pub const MINEDGE: u32 = 12;
    pub const PRIM0: u32 = 16;
    pub const PRIM1: u32 = 24;
    pub const FEAT0: u32 = 32;
    pub const FEAT1: u32 = 40;
    pub const PT: u32 = 48;
    pub const N: u32 = 56;
}

/// One physics thread's (`iCaller`'s) contact scratch: the state that persists between
/// `Intersect` calls (contacts, areas, border points and their counters). The caller owns one
/// per thread and passes it to every intersection call made on that thread; the rigid-body
/// code (CheckForNewContacts 0x350B3440) also edits the counters directly
/// ([`ThreadScratch::set_n_contacts`]).
#[derive(Clone)]
pub struct ThreadScratch {
    /// The thread's arena slot (`iCaller`): only used to form the stored addresses.
    pub i_caller: i32,
    /// The word the original finds in its uninitialised stack slots (see
    /// [`super`]'s "stale reads"); 0 by default, the oracle tests set the word they fill the
    /// stack with.
    pub stale_word: u32,
    words: Vec<u32>,
}

impl std::fmt::Debug for ThreadScratch {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("ThreadScratch")
            .field("i_caller", &self.i_caller)
            .field("n_contacts", &self.n_contacts())
            .field("n_areas", &self.n_areas())
            .field("n_area_pts", &self.n_area_pts())
            .field("n_border", &self.n_border())
            .finish_non_exhaustive()
    }
}

impl Default for ThreadScratch {
    fn default() -> Self {
        Self::new(0)
    }
}

impl ThreadScratch {
    /// A zeroed scratch (the arena is a zero-initialised static).
    #[must_use]
    pub fn new(i_caller: i32) -> Self {
        Self {
            i_caller,
            stale_word: 0,
            words: vec![0; ((HI - LO) / 4) as usize],
        }
    }

    /// Fill the modelled range with `pattern(i)` (word index from [`LO`]); the oracle probe
    /// fills the DLL's arena the same way so both start from identical memory.
    pub fn fill_with(&mut self, pattern: impl Fn(u32) -> u32) {
        for (i, w) in self.words.iter_mut().enumerate() {
            *w = pattern(i as u32);
        }
    }

    /// Arena base address of this caller.
    #[must_use]
    pub fn base(&self) -> u64 {
        ARENA_BASE_VA.wrapping_add((self.i_caller as i64 as u64).wrapping_mul(ARENA_STRIDE))
    }

    /// Address of an arena offset.
    #[must_use]
    pub fn addr(&self, off: i64) -> u64 {
        self.base().wrapping_add(off as u64)
    }

    /// Arena offset of an address (`None` for null or addresses outside this arena).
    #[must_use]
    pub fn off_of(&self, addr: u64) -> Option<i64> {
        let d = addr.wrapping_sub(self.base());
        (addr != 0 && d < ARENA_STRIDE).then_some(d as i64)
    }

    fn idx(off: i64) -> Option<usize> {
        if off >= i64::from(LO) && off + 4 <= i64::from(HI) && off & 3 == 0 {
            Some(((off - i64::from(LO)) / 4) as usize)
        } else {
            None
        }
    }

    /// Word at an arena offset (0 outside the model).
    #[must_use]
    pub fn rd(&self, off: i64) -> u32 {
        Self::idx(off).map_or(0, |i| self.words[i])
    }

    /// Write a word (dropped outside the model).
    pub fn wr(&mut self, off: i64, v: u32) {
        if let Some(i) = Self::idx(off) {
            self.words[i] = v;
        }
    }

    #[must_use]
    pub fn rd_i(&self, off: i64) -> i32 {
        self.rd(off) as i32
    }
    #[must_use]
    pub fn rd_f(&self, off: i64) -> f32 {
        f32::from_bits(self.rd(off))
    }
    pub fn wr_f(&mut self, off: i64, v: f32) {
        self.wr(off, v.to_bits());
    }
    #[must_use]
    pub fn rd_v(&self, off: i64) -> Vec3 {
        Vec3::new(self.rd_f(off), self.rd_f(off + 4), self.rd_f(off + 8))
    }
    pub fn wr_v(&mut self, off: i64, v: Vec3) {
        self.wr_f(off, v.x);
        self.wr_f(off + 4, v.y);
        self.wr_f(off + 8, v.z);
    }
    #[must_use]
    pub fn rd_q(&self, off: i64) -> u64 {
        u64::from(self.rd(off)) | (u64::from(self.rd(off + 4)) << 32)
    }
    pub fn wr_q(&mut self, off: i64, v: u64) {
        self.wr(off, v as u32);
        self.wr(off + 4, (v >> 32) as u32);
    }
    #[must_use]
    pub fn rd_d(&self, off: i64) -> f64 {
        f64::from_bits(self.rd_q(off))
    }
    pub fn wr_d(&mut self, off: i64, v: f64) {
        self.wr_q(off, v.to_bits());
    }

    /// Read through a stored pointer (`None` = outside this arena / null).
    #[must_use]
    pub fn rd_v_at(&self, addr: u64) -> Vec3 {
        self.off_of(addr).map_or(Vec3::ZERO, |o| self.rd_v(o))
    }
    pub fn wr_v_at(&mut self, addr: u64, v: Vec3) {
        if let Some(o) = self.off_of(addr) {
            self.wr_v(o, v);
        }
    }
    #[must_use]
    pub fn rd_at(&self, addr: u64) -> u32 {
        self.off_of(addr).map_or(0, |o| self.rd(o))
    }
    pub fn wr_at(&mut self, addr: u64, v: u32) {
        if let Some(o) = self.off_of(addr) {
            self.wr(o, v);
        }
    }

    // ---- counters ----

    #[must_use]
    pub fn n_contacts(&self) -> i32 {
        self.rd_i(N_CONTACTS.into())
    }
    /// The rigid-body code rolls the count back (`-= n`) before re-running an intersection.
    pub fn set_n_contacts(&mut self, n: i32) {
        self.wr(N_CONTACTS.into(), n as u32);
    }
    #[must_use]
    pub fn n_areas(&self) -> i32 {
        self.rd_i(N_AREAS.into())
    }
    pub fn set_n_areas(&mut self, n: i32) {
        self.wr(N_AREAS.into(), n as u32);
    }
    #[must_use]
    pub fn n_area_pts(&self) -> i32 {
        self.rd_i(N_AREA_PTS.into())
    }
    pub fn set_n_area_pts(&mut self, n: i32) {
        self.wr(N_AREA_PTS.into(), n as u32);
    }
    #[must_use]
    pub fn n_border(&self) -> i32 {
        self.rd_i(N_BORDER.into())
    }
    pub fn set_n_border(&mut self, n: i32) {
        self.wr(N_BORDER.into(), n as u32);
    }

    /// Arena offset of contact record `i` (may be outside the pool for corrupted counts).
    #[must_use]
    pub fn contact_off(i: i32) -> i64 {
        i64::from(CONTACTS) + i64::from(CONTACT_SIZE) * i64::from(i)
    }

    /// Arena offset of area record `i`.
    #[must_use]
    pub fn area_off(i: i32) -> i64 {
        i64::from(AREAS) + i64::from(AREA_SIZE) * i64::from(i)
    }

    // ---- areas as the unprojection functions see them ----

    /// The area record at `off` as a [`ContactArea`] whose buffers are the memory its pointers
    /// designate: `nmaxpt` entries plus [`AREA_SLACK`] more, so that writers that ignore
    /// `nmaxpt` (box–box, box–cylinder, cylinder–cylinder) write into (and read from) the
    /// memory that follows the buffer, as in the original, instead of growing zero-filled
    /// vectors.
    #[must_use]
    pub fn load_area(&self, off: i64) -> ContactArea {
        let nmax = self.rd_i(off + i64::from(a::NMAX));
        self.load_area_n(off, nmax.clamp(0, 4096) as usize + AREA_SLACK)
    }

    /// [`Self::load_area`] with `n` entries per buffer.
    #[must_use]
    pub fn load_area_n(&self, off: i64, n: usize) -> ContactArea {
        let nmax = self.rd_i(off + i64::from(a::NMAX));
        let arr = |field: u32| -> Vec<i32> {
            let p = self.rd_q(off + i64::from(field));
            (0..n)
                .map(|i| self.rd_at(p.wrapping_add(4 * i as u64)) as i32)
                .collect()
        };
        let pp = self.rd_q(off + i64::from(a::PT));
        ContactArea {
            min_edge: self.rd_f(off + i64::from(a::MINEDGE)),
            prim: [arr(a::PRIM0), arr(a::PRIM1)],
            feature: [arr(a::FEAT0), arr(a::FEAT1)],
            pt: (0..n)
                .map(|i| self.rd_v_at(pp.wrapping_add(12 * i as u64)))
                .collect(),
            i_type: self.rd_i(off + i64::from(a::TYPE)),
            npt: self.rd_i(off + i64::from(a::NPT)),
            nmaxpt: nmax,
            n: self.rd_v(off + i64::from(a::N)),
        }
    }

    /// Write an area back after an unprojection call: the header fields, then every buffer
    /// entry that differs from `before` (the value [`Self::load_area`] returned), including
    /// entries a writer put past `nmaxpt`, which land in whatever follows the buffer (the next
    /// pool, a counter), as in the original. Only changed words are written, so buffers whose
    /// loaded ranges overlap (after an overrun) do not write stale copies over each other.
    pub fn store_area(&mut self, off: i64, before: &ContactArea, ar: &ContactArea) {
        self.wr(off + i64::from(a::TYPE), ar.i_type as u32);
        self.wr(off + i64::from(a::NPT), ar.npt as u32);
        self.wr(off + i64::from(a::NMAX), ar.nmaxpt as u32);
        self.wr_f(off + i64::from(a::MINEDGE), ar.min_edge);
        self.wr_v(off + i64::from(a::N), ar.n);
        let pp = self.rd_q(off + i64::from(a::PT));
        for (i, p) in ar.pt.iter().enumerate() {
            let old = before.pt.get(i).map(|q| q.to_bits());
            if old != Some(p.to_bits()) {
                self.wr_v_at(pp.wrapping_add(12 * i as u64), *p);
            }
        }
        for (field, v, b) in [
            (a::PRIM0, &ar.prim[0], &before.prim[0]),
            (a::PRIM1, &ar.prim[1], &before.prim[1]),
            (a::FEAT0, &ar.feature[0], &before.feature[0]),
            (a::FEAT1, &ar.feature[1], &before.feature[1]),
        ] {
            let p = self.rd_q(off + i64::from(field));
            for (i, x) in v.iter().enumerate() {
                if b.get(i) != Some(x) {
                    self.wr_at(p.wrapping_add(4 * i as u64), *x as u32);
                }
            }
        }
    }

    // ---- decoding for consumers ----

    /// Contact record `i` as a [`GeomContact`] (border points and area read through the
    /// record's pointers, the area's arrays with `max(npt, nmaxpt)` entries).
    #[must_use]
    pub fn contact(&self, i: i32) -> GeomContact {
        let o = Self::contact_off(i);
        let f = |k: u32| self.rd_f(o + i64::from(k));
        let int = |k: u32| self.rd_i(o + i64::from(k));
        let v = |k: u32| self.rd_v(o + i64::from(k));
        let nb = int(c::NBORDER).clamp(0, 1 << 16) as usize;
        let pb = self.rd_q(o + i64::from(c::PTBORDER));
        let pi = self.rd_q(o + i64::from(c::IDXBORDER));
        let border = (0..nb)
            .map(|k| self.rd_v_at(pb.wrapping_add(12 * k as u64)))
            .collect();
        let idx_border = if pi == 0 {
            Vec::new()
        } else {
            (0..nb)
                .map(|k| {
                    let q = pi.wrapping_add(8 * k as u64);
                    [self.rd_at(q) as i32, self.rd_at(q + 4) as i32]
                })
                .collect()
        };
        let pa = self.rd_q(o + i64::from(c::AREA));
        let area = self.off_of(pa).map(|ao| {
            let npt = self.rd_i(ao + i64::from(a::NPT));
            let nmax = self.rd_i(ao + i64::from(a::NMAX));
            self.load_area_n(ao, npt.max(nmax).clamp(0, 4096) as usize)
        });
        GeomContact {
            t: self.rd_d(o),
            pt: v(c::PT),
            n: v(c::N),
            dir: v(c::DIR),
            i_unproj_mode: int(c::MODE),
            vel: f(c::VEL),
            id: [int(c::ID), int(c::ID + 4)],
            i_prim: [int(c::IPRIM), int(c::IPRIM + 4)],
            i_feature: [int(c::IFEATURE), int(c::IFEATURE + 4)],
            i_node: [int(c::INODE), int(c::INODE + 4)],
            border,
            idx_border,
            center: v(c::CENTER),
            border_consecutive: self.rd(o + i64::from(c::CONSECUTIVE)) & 0xFF != 0,
            area,
        }
    }

    /// Contacts `first .. first + n` (what the engine's `*ppcontacts` designates).
    #[must_use]
    pub fn contacts(&self, first: i32, n: i32) -> Vec<GeomContact> {
        (0..n.max(0)).map(|k| self.contact(first + k)).collect()
    }

    // ---- test support: the probe's view of the memory ----

    /// A stored pointer as the probe prints it: 0 for null, the arena offset for addresses
    /// inside this arena, `0xFFFFFFFF` otherwise.
    #[must_use]
    pub fn ptr_word(&self, p: u64) -> u32 {
        if p == 0 {
            0
        } else {
            self.off_of(p).map_or(u32::MAX, |o| o as u32)
        }
    }

    /// FNV-1a over the words of `[from, to)`.
    #[must_use]
    pub fn hash(&self, from: u32, to: u32) -> u32 {
        let mut h: u32 = 0x811C_9DC5;
        let mut o = i64::from(from);
        while o < i64::from(to) {
            for b in self.rd(o).to_le_bytes() {
                h = (h ^ u32::from(b)).wrapping_mul(0x0100_0193);
            }
            o += 4;
        }
        h
    }

    /// The probe's dump of contact record `i`: the 34 words with each pointer pair (`+88`,
    /// `+96`, `+128`) replaced by [`Self::ptr_word`], then `B <n> <points>` (≤ 64, through
    /// the border pointer) and `A <area words> <entries>` or `NOAREA`.
    #[must_use]
    pub fn dump_contact(&self, i: i32) -> String {
        use std::fmt::Write;
        let o = Self::contact_off(i);
        let mut s = String::from(" C");
        let mut k = 0u32;
        while k < CONTACT_SIZE {
            if k == c::PTBORDER || k == c::IDXBORDER || k == c::AREA {
                let _ = write!(s, " {:08x}", self.ptr_word(self.rd_q(o + i64::from(k))));
                k += 8;
            } else {
                let _ = write!(s, " {:08x}", self.rd(o + i64::from(k)));
                k += 4;
            }
        }
        let nb = self.rd_i(o + i64::from(c::NBORDER));
        let pb = self.rd_q(o + i64::from(c::PTBORDER));
        let _ = write!(s, " B");
        if (0..=64).contains(&nb) && self.off_of(pb).is_some() {
            for q in 0..nb {
                let at = pb.wrapping_add(12 * q as u64);
                for j in 0..3 {
                    let _ = write!(s, " {:08x}", self.rd_at(at + 4 * j));
                }
            }
        } else {
            s.push_str(" X");
        }
        let pa = self.rd_q(o + i64::from(c::AREA));
        match self.off_of(pa) {
            Some(ao) => {
                s.push_str(" A");
                let mut k = 0u32;
                while k < AREA_SIZE {
                    if (a::PRIM0..=a::PT).contains(&k) {
                        let _ = write!(s, " {:08x}", self.ptr_word(self.rd_q(ao + i64::from(k))));
                        k += 8;
                    } else {
                        let _ = write!(s, " {:08x}", self.rd(ao + i64::from(k)));
                        k += 4;
                    }
                }
                let npt = self.rd_i(ao + i64::from(a::NPT)).clamp(0, 64);
                let ptr = |f: u32| self.rd_q(ao + i64::from(f));
                for q in 0..npt as u64 {
                    let pp = ptr(a::PT).wrapping_add(12 * q);
                    for j in 0..3 {
                        let _ = write!(s, " {:08x}", self.rd_at(pp + 4 * j));
                    }
                    for f in [a::PRIM0, a::PRIM1, a::FEAT0, a::FEAT1] {
                        let _ = write!(s, " {:08x}", self.rd_at(ptr(f).wrapping_add(4 * q)));
                    }
                }
            }
            None => s.push_str(" NOAREA"),
        }
        s
    }
}
