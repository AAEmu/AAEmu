//! The entity contact record (`entity_contact`, 176 bytes; `dynamics.md` §3.2) and the world's
//! contact pool.
//!
//! In the binary contacts are heap records linked into
//! * a rigid entity's circular contact list (sentinel at `E+536`/`E+544`, count `E+552`), with
//!   the contacts of one collider kept consecutive (`E+520[i]` points at the first contact of
//!   collider `i`; flag `0x80000` marks the last one of the run), or
//! * the world's free list (sentinel `W+21576`, free count `W+21592`, total `W+21596`, lock
//!   `W+22540`), refilled 64 records at a time by `sub_350CCE00`.
//!
//! The port keeps both lists with the same link semantics in one arena ([`ContactPool`]):
//! records are addressed by [`ContactId`]; sentinels are ordinary records (the world's free-list
//! sentinel is [`ContactId::FREE_HEAD`], each rigid entity allocates its own sentinel with
//! [`ContactPool::new_sentinel`]). Recycled records are **not** cleared (the binary reuses them
//! as they are), so stale fields behave the same.

use super::body::BodyId;
use crate::math::Vec3;
use crate::world::EntIdx;

/// Index of a contact record (or list sentinel) in the [`ContactPool`].
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct ContactId(pub u32);

impl ContactId {
    /// The world free-list sentinel (`W+21576`).
    pub const FREE_HEAD: ContactId = ContactId(0);
    /// "null" pointer.
    pub const NONE: ContactId = ContactId(u32::MAX);

    #[inline]
    #[must_use]
    pub fn is_none(self) -> bool {
        self == Self::NONE
    }

    /// Bit marking a solver temp-buffer record ([`ContactPool::temp`]).
    pub const TEMP_BIT: u32 = 0x8000_0000;

    /// Temp-buffer record `i` (`AllocSolverTmpBuf` 0x3509A9C0).
    #[inline]
    #[must_use]
    pub const fn temp(i: u32) -> ContactId {
        ContactId(Self::TEMP_BIT | i)
    }

    /// True for a temp-buffer record.
    #[inline]
    #[must_use]
    pub fn is_temp(self) -> bool {
        self.0 & Self::TEMP_BIT != 0 && !self.is_none()
    }
}

/// Contact flag bits (`+128`), `dynamics.md` §3.2.
pub mod flags {
    /// Stage-1 minimum hit count mask (0x3F = disabled).
    pub const HIT_MASK: u32 = 0x3F;
    pub const NEW: u32 = 0x40;
    pub const IGNORE: u32 = 0x80;
    pub const ANGULAR: u32 = 0x100;
    pub const BILATERAL_3D: u32 = 0x200;
    pub const BILATERAL_1D: u32 = 0x400;
    pub const BILATERAL_2D: u32 = 0x800;
    pub const BILATERAL: u32 = 0xE00;
    pub const LCP_ACTIVE: u32 = 0x1000;
    pub const SHARED_PN: u32 = 0x4000;
    pub const SOFT: u32 = 0x8000;
    pub const PROJ_AXIS: u32 = 0x10000;
    pub const PROJ_PLANE: u32 = 0x20000;
    pub const LAST_OF_COLLIDER: u32 = 0x80000;
    pub const EVENT_LOGGED: u32 = 0x200000;
    pub const MULTI_BODY: u32 = 0x400000;
    pub const KEEP_LIMIT: u32 = 0x800000;
    pub const AUXILIARY: u32 = 0x1000000;
}

/// What the `+168` pointer of a contact designates (`dynamics.md` §3.2): normally the
/// contact's own `+164` field, for shared-Pn contacts (`0x4000`) another contact's `+164`, for
/// multi-body contacts (`0x400000`) an array of bodies in solver temp memory.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum ContactOwner {
    #[default]
    Null,
    /// `&contact+164` of the given contact (itself when not shared).
    Count(ContactId),
    /// Solver temp-buffer offset (multi-body contacts; owned by the solver).
    Temp(u32),
}

/// `entity_contact` (176 bytes). Offsets in the field docs.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Contact {
    /// `+0` next in the list.
    pub next: ContactId,
    /// `+8` previous in the list.
    pub prev: ContactId,
    /// `+16` (unused by the rigid path; kept for completeness).
    pub unk16: u64,
    /// `+24`, `+36` contact point on each side (both set to the hit point).
    pub pt: [Vec3; 2],
    /// `+48` contact normal (pointing from the collider to the entity).
    pub n: Vec3,
    /// `+64` / `+72` the two entities.
    pub pent: [EntIdx; 2],
    /// `+80` / `+84` part indices.
    pub ipart: [i32; 2],
    /// `+88` (unused by the rigid path).
    pub unk88: u64,
    /// `+96` / `+104` the two bodies.
    pub pbody: [BodyId; 2],
    /// `+112` normal / constraint axis in the frame of body `iNormal` (`+156` bits 0–1).
    pub nloc: Vec3,
    /// `+124` friction coefficient.
    pub friction: f32,
    /// `+128` flags ([`flags`]).
    pub flags: u32,
    /// `+132` normal relative velocity (re-used post-solve).
    pub vn: f32,
    /// `+136` required separation velocity (vector along `n`).
    pub vreq: Vec3,
    /// `+148` accumulated normal impulse out / impulse limit in.
    pub pn: f32,
    /// `+152` penetration depth.
    pub penetration: f32,
    /// `+156` bits 0–1 `iNormal`, bits 2–16 material id 0, bits 17–31 material id 1.
    pub id_bits: u32,
    /// `+160` low 16 bits solver index, bits 16–29 part id (`& 0x3FFF`), bit 31 "first record
    /// of a pool block" (set by the pool allocator), bit 30 constraint link.
    pub idx_bits: u32,
    /// `+164` group count.
    pub group_n: i32,
    /// `+168` owner pointer.
    pub owner: ContactOwner,
}

impl Contact {
    /// A record as the pool allocator leaves it (the block is `memset` to 0 after the
    /// constructor loop of `sub_350CCE00`).
    #[must_use]
    pub fn zeroed() -> Self {
        Self {
            next: ContactId::NONE,
            prev: ContactId::NONE,
            unk16: 0,
            pt: [Vec3::ZERO; 2],
            n: Vec3::ZERO,
            pent: [EntIdx::NONE; 2],
            ipart: [0; 2],
            unk88: 0,
            pbody: [BodyId::STATIC; 2],
            nloc: Vec3::ZERO,
            friction: 0.0,
            flags: 0,
            vn: 0.0,
            vreq: Vec3::ZERO,
            pn: 0.0,
            penetration: 0.0,
            id_bits: 0,
            idx_bits: 0,
            group_n: 0,
            owner: ContactOwner::Null,
        }
    }

    /// `iNormal` (`+156` bits 0–1): which body's frame `nloc` is expressed in.
    #[inline]
    #[must_use]
    pub fn i_normal(&self) -> usize {
        (self.id_bits & 3) as usize
    }
}

/// All contact records of a world plus the free list.
#[derive(Clone, Debug)]
pub struct ContactPool {
    pub recs: Vec<Contact>,
    /// Solver temp-buffer records (ids [`ContactId::temp`]); handed out by
    /// [`ContactPool::alloc_temp`] / `SolverThread::alloc_tmp_contacts` and dropped when the
    /// next island starts (`InitSolver` resets the temp offsets).
    pub temp: Vec<Contact>,
    /// `W+21592` number of free records.
    pub n_free: i32,
    /// `W+21596` number of pooled records.
    pub n_total: i32,
}

impl Default for ContactPool {
    fn default() -> Self {
        let mut s = Self {
            recs: Vec::new(),
            temp: Vec::new(),
            n_free: 0,
            n_total: 0,
        };
        let h = s.new_sentinel();
        debug_assert_eq!(h, ContactId::FREE_HEAD);
        s
    }
}

impl ContactPool {
    #[inline]
    #[must_use]
    pub fn get(&self, id: ContactId) -> &Contact {
        if id.is_temp() {
            &self.temp[(id.0 & !ContactId::TEMP_BIT) as usize]
        } else {
            &self.recs[id.0 as usize]
        }
    }

    #[inline]
    pub fn get_mut(&mut self, id: ContactId) -> &mut Contact {
        if id.is_temp() {
            &mut self.temp[(id.0 & !ContactId::TEMP_BIT) as usize]
        } else {
            &mut self.recs[id.0 as usize]
        }
    }

    /// `n` contiguous zeroed temp records; returns the first (`ContactId::temp(k)`, the
    /// following ones are `temp(k + 1)`, …). Prefer `SolverThread::alloc_tmp_contacts`, which
    /// also resets the arena at the first allocation after `InitSolver`.
    pub fn alloc_temp(&mut self, n: usize) -> ContactId {
        let k = self.temp.len() as u32;
        self.temp.extend(std::iter::repeat_n(Contact::zeroed(), n));
        ContactId::temp(k)
    }

    /// A self-linked list sentinel (an entity's `E+536` head, or the world free list).
    pub fn new_sentinel(&mut self) -> ContactId {
        let id = ContactId(self.recs.len() as u32);
        let mut c = Contact::zeroed();
        c.next = id;
        c.prev = id;
        self.recs.push(c);
        id
    }

    /// `sub_350CCE00`: take the first record of the free list (allocating a block of 64 when
    /// the list is empty), unlink it and leave it self-linked.
    pub fn alloc(&mut self) -> ContactId {
        let head = ContactId::FREE_HEAD;
        let first = self.get(head).next;
        if self.get(first).next == first {
            self.grow(head);
        }
        let r = self.get(head).next;
        let (rn, rp) = (self.get(r).next, self.get(r).prev);
        self.get_mut(rn).prev = rp;
        self.get_mut(rp).next = rn;
        let c = self.get_mut(r);
        c.next = r;
        c.prev = r;
        self.n_free -= 1;
        r
    }

    /// The block allocation of `sub_350CCE00`: 64 zeroed records linked `0 → 1 → … → 63 →
    /// sentinel`, `rec0.prev = sentinel`, `sentinel.next = rec0`, bit 31 of `+160` set on
    /// record 0. The sentinel's `prev` is **not** updated (as in the binary).
    fn grow(&mut self, head: ContactId) {
        let base = self.recs.len() as u32;
        for i in 0..64u32 {
            let mut c = Contact::zeroed();
            c.next = if i == 63 { head } else { ContactId(base + i + 1) };
            c.prev = if i == 0 { head } else { ContactId(base + i - 1) };
            if i == 0 {
                c.idx_bits |= 0x8000_0000;
            }
            self.recs.push(c);
        }
        self.get_mut(head).next = ContactId(base);
        self.n_free += 64;
        self.n_total += 64;
    }

    /// Unlink `id` from whatever list it is in (`prev.next = next; next.prev = prev`).
    pub fn unlink(&mut self, id: ContactId) {
        let (n, p) = (self.get(id).next, self.get(id).prev);
        self.get_mut(p).next = n;
        self.get_mut(n).prev = p;
    }

    /// Insert `id` before `at` (`id.prev = at.prev; id.next = at; at.prev.next = id;
    /// at.prev = id`).
    pub fn insert_before(&mut self, id: ContactId, at: ContactId) {
        let p = self.get(at).prev;
        {
            let c = self.get_mut(id);
            c.prev = p;
            c.next = at;
        }
        self.get_mut(p).next = id;
        self.get_mut(at).prev = id;
    }
}
