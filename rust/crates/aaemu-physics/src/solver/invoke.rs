//! `InvokeContactSolver` 0x3509E850 (x86 0x350A62B0): setup, island difficulty test, the CG
//! fast path ("PreCG"), the stage-1 driver, the hand-over to stages 2/3 and the finish.
//!
//! The function is ported block by block from the decompile; offsets into the solver block are
//! the binary's (see [`super::thread`]). Comments quote the binary's local names where it helps
//! to compare (`v15` = island is "hard", `v19` = number of bodies, …).

use super::body::{BodyArena, BodyId, RigidBody};
use super::contact::{flags as cf, ContactId, ContactOwner, ContactPool};
use super::helpers::{fmax, imax, inv22, inv33, k_accum, outer, rotate};
use super::thread::{crec, lcp, off, brec, bg, Mem, MultiBodyEntry, SolverThread};
use crate::math::{Quat, Vec3};
use crate::physvars::PhysVars;

/// Outputs of [`SolverThread::invoke`] (the three values the island step 0x3507F9B0 passes on to
/// the post-solve 0x3507F210).
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct SolveResult {
    /// Return value (`eax`): `T+4`, the number of bodies in the solver's body list. 0 (and no
    /// other effect) when the island has no contacts. The post-solve compares it with
    /// `W+308` (30) in its sleep decision.
    pub ret: i32,
    /// `*a5` out: number of contacts in the list [`SolverThread::contacts`] (the post-solve walks
    /// them and calls slot 59 for parts with flag `0x400000`). Unchanged input when the island
    /// has no contacts.
    pub n_contacts: i32,
    /// Which path finished the solve (diagnostics).
    pub path: SolvePath,
}

/// How [`SolverThread::invoke`] finished.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum SolvePath {
    /// No contacts.
    #[default]
    Empty,
    /// The CG fast path was accepted.
    PreCg,
    /// Stage 1 only (converged, or stage 2 not applicable).
    Stage1,
    /// Stage 2 ran and was rejected (its impulses are undone).
    Stage2Rejected,
    /// Stages 2 and 3 ran.
    Stage3,
}

/// Solver state shared by the stages.
pub(crate) struct Ctx<'a> {
    pub t: &'a mut Mem,
    pub bodies: &'a mut BodyArena,
    pub pool: &'a mut ContactPool,
    pub multi: &'a mut Vec<MultiBodyEntry>,
    pub tid: usize,
    pub v: &'a PhysVars,
}

impl Ctx<'_> {
    #[inline]
    pub fn b(&self, id: BodyId) -> &RigidBody {
        self.bodies.get(id)
    }
    #[inline]
    pub fn bm(&mut self, id: BodyId) -> &mut RigidBody {
        self.bodies.get_mut(id)
    }
    /// Solver index short of a body (`B+200+2·tid`).
    #[inline]
    pub fn sidx(&self, id: BodyId) -> i16 {
        self.b(id).solver_idx[self.tid]
    }
    #[inline]
    pub fn set_sidx(&mut self, id: BodyId, v: i16) {
        let tid = self.tid;
        self.bm(id).solver_idx[tid] = v;
    }
    #[inline]
    pub fn nc(&self) -> i32 {
        self.t.n_contacts()
    }
    #[inline]
    pub fn cid(&self, i: usize) -> ContactId {
        self.t.contact(i)
    }
    #[inline]
    pub fn bid(&self, j: usize) -> BodyId {
        self.t.body(j)
    }
    /// Multi-body entries of a contact (`owner` run of `group_n` entries).
    pub fn multi_base(&self, c: ContactId) -> usize {
        match self.pool.get(c).owner {
            ContactOwner::Temp(k) => k as usize,
            _ => 0,
        }
    }
}

#[inline]
pub(crate) fn v3(v: Vec3) -> [f32; 3] {
    [v.x, v.y, v.z]
}

#[inline]
pub(crate) fn iinv9(b: &RigidBody) -> [f32; 9] {
    b.iinv.to_array()
}

pub(crate) const fn crec_at(i: usize) -> usize {
    off::CREC + crec::STRIDE * i
}
pub(crate) const fn lcp_at(k: usize) -> usize {
    off::LCP + lcp::STRIDE * k
}
pub(crate) const fn proj_at(i: usize) -> usize {
    off::PROJ + 72 * i
}
pub(crate) const fn kinv_at(i: usize) -> usize {
    off::PROJ + 72 * i + 36
}
pub(crate) const fn brec_at(j: usize) -> usize {
    off::BREC + brec::STRIDE * j
}
pub(crate) const fn bg_at(j: usize) -> usize {
    off::BG + bg::STRIDE * j
}

impl SolverThread {
    /// `InvokeContactSolver` 0x3509E850.
    ///
    /// * `dt` — the island time step (xmm0),
    /// * `vars` — the world's physics vars (`W+104`, rdx),
    /// * `e0` — energy reference (xmm2): the island step passes
    ///   `max_idiom(Σmass·2.5e-5, Σ CalcEnergy(dt))`,
    /// * `prev` — `*a5` on entry (the island step passes 0; a value above 0x10000 encodes
    ///   `nBodies << 16 | stage-1 result` of an earlier call and skips setup and stage 1),
    /// * `bodies`, `pool` — the world's body arena and contact pool.
    ///
    /// Before the call the caller runs [`SolverThread::init`] and registers the island's
    /// contacts ([`SolverThread::register_contact`]); every body's solver index for this
    /// thread must be 0 (it is reset by the solver itself). Afterwards the contact list
    /// ([`SolverThread::contacts`]) and the result go to the post-solve.
    pub fn invoke(
        &mut self,
        dt: f32,
        vars: &PhysVars,
        e0: f32,
        prev: i32,
        bodies: &mut BodyArena,
        pool: &mut ContactPool,
    ) -> SolveResult {
        let mut ctx = Ctx {
            t: &mut self.mem,
            bodies,
            pool,
            multi: &mut self.multi_body,
            tid: self.tid,
            v: vars,
        };
        ctx.run(dt, e0, prev)
    }
}

impl Ctx<'_> {
    fn run(&mut self, dt: f32, e0: f32, prev: i32) -> SolveResult {
        if self.nc() == 0 {
            return SolveResult {
                ret: 0,
                n_contacts: prev,
                path: SolvePath::Empty,
            };
        }
        let inv_dt = 1.0 / dt;
        let tol_n = self.v.solver_tol_n;
        let n_contacts = self.nc();
        let mut hard = 0u32; // v15
        let nb: i32; // v19 / v1908
        let ret1: i32; // v1735
        if prev <= 0x10000 {
            nb = self.setup();
            if self.v.lcp_passes > 0 {
                hard = self.difficulty(nb);
            }
            let max_it = {
                let a = self.nc().wrapping_mul(self.v.solver_iters_per_contact);
                imax(a, self.v.solver_max_iters_floor)
            };
            self.t.si(off::N_BODIES, nb);
            for j in 0..nb as usize {
                let b = self.bid(j);
                self.set_sidx(b, 0);
                self.bm(b).e_aux = 0.0;
            }
            if self.t.u(off::CG_ALLOWED) & 0xFF != 0 && self.nc() < 16 && self.precg(nb, e0, inv_dt)
            {
                return SolveResult {
                    ret: self.t.n_bodies(),
                    n_contacts,
                    path: SolvePath::PreCg,
                };
            }
            self.stage1_prep(nb);
            ret1 = self.pgs(nb, e0, max_it, tol_n, self.v.solver_tol_t);
        } else {
            nb = prev >> 16;
            ret1 = prev & 0xFFFF;
        }
        self.after_stage1(nb);
        let mut path = SolvePath::Stage1;
        if ret1 != 0 && self.v.lcp_passes > 0 && hard != 0 && self.nc() < self.v.lcp_max_contacts {
            path = self.stage23(nb, dt);
        } else {
            self.finish_forces(nb, inv_dt);
        }
        // LABEL_638
        for i in 0..self.nc() as usize {
            let c = self.cid(i);
            let pn = self.t.f(crec_at(i) + crec::PN_ACC);
            self.pool.get_mut(c).pn = pn;
        }
        SolveResult {
            ret: self.t.n_bodies(),
            n_contacts,
            path,
        }
    }

    /// LABEL_635: `B+176 = (P − P_saved)/dt`, `B+188 = (L − L_saved)/dt`.
    pub(crate) fn finish_forces(&mut self, nb: i32, inv_dt: f32) {
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let bb = self.bm(b);
            let (p, f) = (bb.p, bb.fcoll);
            bb.fcoll = Vec3::new(inv_dt * (p.x - f.x), (p.y - f.y) * inv_dt, (p.z - f.z) * inv_dt);
            let (l, t) = (bb.l, bb.tcoll);
            bb.tcoll = Vec3::new((l.x - t.x) * inv_dt, (l.y - t.y) * inv_dt, (l.z - t.z) * inv_dt);
        }
    }

    /// Add a body to the solver list if it is not there yet; returns its solver index + 1.
    fn enlist(&mut self, b: BodyId, nb: &mut i32) -> i32 {
        let s = i32::from(self.sidx(b));
        if s == 0 {
            self.t.set_body(*nb as usize, b);
            *nb += 1;
            self.set_sidx(b, *nb as i16);
            *nb
        } else {
            s
        }
    }

    /// Setup loop (decompile lines 1922–2306): body list, `Pproj`, `K`, `Kinv` per contact.
    /// Returns the number of bodies.
    fn setup(&mut self) -> i32 {
        let mut nb = 0i32;
        let mut i = 0usize;
        while (i as i32) < self.nc() {
            let cid = self.cid(i);
            let rc = crec_at(i);
            for k in 0..2 {
                let b = self.pool.get(cid).pbody[k];
                let s = self.enlist(b, &mut nb);
                self.t.si(rc + crec::IB0 + 4 * k, s - 1);
            }
            let (fl, gn) = {
                let c = self.pool.get(cid);
                (c.flags, c.group_n)
            };
            if fl & cf::MULTI_BODY != 0 {
                let base = self.multi_base(cid);
                for m in 0..gn.max(0) as usize {
                    let b = self.multi[base + m].body;
                    let s = self.enlist(b, &mut nb);
                    self.multi[base + m].idx = s - 1;
                }
            } else {
                self.pool.get_mut(cid).group_n = 0;
            }
            if fl & cf::KEEP_LIMIT == 0 {
                self.pool.get_mut(cid).pn = 0.0;
            }
            let pp = proj_at(i);
            self.t.sm9(pp, &[1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0]);
            let c = *self.pool.get(cid);
            if c.flags & cf::PROJ_AXIS != 0 {
                let a = v3(c.nloc);
                self.t.sm9(pp, &outer(a, a));
            } else if c.flags & cf::PROJ_PLANE != 0 {
                let (x, y, z) = (c.nloc.x, c.nloc.y, c.nloc.z);
                let mut p = self.t.m9(pp);
                p[0] -= x * x;
                p[1] -= y * x;
                p[2] -= z * x;
                p[3] -= y * x;
                p[4] -= y * y;
                p[5] -= z * y;
                p[6] -= z * x;
                p[7] -= y * z;
                p[8] -= z * z;
                self.t.sm9(pp, &p);
            }
            let lc = lcp_at(i);
            self.t.sv3(lc + lcp::X, [0.0; 3]);
            self.pool.get_mut(cid).idx_bits = (c.idx_bits & 0xFFFF_0000) | (i as u32 & 0xFFFF);
            let kk = rc + crec::K;
            if c.flags & cf::MULTI_BODY != 0 {
                self.t.sv3(kk, v3(c.nloc));
                let d = 1e-8f32 - c.nloc.z;
                self.t.sf(kk + 12, (d.to_bits() >> 31) as f32);
            } else if c.flags & cf::ANGULAR != 0 {
                let i0 = iinv9(self.b(c.pbody[0]));
                let i1 = iinv9(self.b(c.pbody[1]));
                let mut k = [0.0; 9];
                for e in 0..9 {
                    k[e] = i1[e] + i0[e];
                }
                self.t.sm9(kk, &k);
            } else {
                let mut k = [0.0f32; 9];
                for (n, b) in c.pbody.iter().enumerate() {
                    let bb = self.b(*b);
                    let r = [
                        c.pt[n].x - bb.pos.x,
                        c.pt[n].y - bb.pos.y,
                        c.pt[n].z - bb.pos.z,
                    ];
                    k_accum(&mut k, &iinv9(bb), bb.minv, r);
                }
                self.t.sm9(kk, &k);
            }
            let ki = kinv_at(i);
            if c.flags & cf::BILATERAL_3D != 0 {
                let mut k = self.t.m9(kk);
                inv33(&mut k);
                self.t.sm9(ki, &k);
            } else if c.flags & cf::BILATERAL_2D != 0 {
                self.tangent_basis(i, &c);
            } else if c.flags & cf::BILATERAL_1D != 0 {
                let n = v3(c.n);
                let k = self.t.m9(kk);
                let p = outer(n, n);
                self.t.sm9(pp, &p);
                let a0 = (n[1] * k[3] + n[0] * k[0]) + n[2] * k[6];
                let a1 = (n[0] * k[1] + n[1] * k[4]) + n[2] * k[7];
                let a2 = (n[0] * k[2] + n[1] * k[5]) + n[2] * k[8];
                let d = (a1 * n[1] + n[0] * a0) + a2 * n[2];
                let inv = 1.0 / d;
                let mut q = [0.0; 9];
                for e in 0..9 {
                    q[e] = inv * p[e];
                }
                self.t.sm9(ki, &q);
            } else if c.friction < 0.01 {
                let n = v3(c.n);
                self.t.sm9(pp, &outer(n, n));
            }
            i += 1;
        }
        nb
    }

    /// The `0x800` (2D tangent bilateral) branch of the setup: `Kinv = T·(Tᵀ K T)⁻¹·Tᵀ` over a
    /// tangent basis, `Pproj = I − n nᵀ`.
    fn tangent_basis(&mut self, i: usize, c: &super::contact::Contact) {
        let n = v3(c.n);
        let nx2 = n[0] * n[0];
        let t = ((n[1] * n[1] + nx2) + n[2] * n[2]) * f32::from_bits(0x3F4F_5C28) - nx2;
        let s = (t.to_bits() >> 31) as usize;
        let (a, b) = ([1usize, 2][s], [2usize, 0][s]);
        let mut u = [0.0f32; 3];
        u[s] = 0.0;
        u[a] = n[b];
        u[b] = -n[a];
        let l2 = (u[0] * u[0] + u[1] * u[1]) + u[2] * u[2];
        let t1 = if !(l2 > f32::from_bits(0x1E3C_E508)) {
            [0.0, 0.0, 1.0]
        } else {
            let inv = 1.0 / l2.sqrt();
            [u[0] * inv, inv * u[1], inv * u[2]]
        };
        let t2 = [
            n[1] * t1[2] - n[2] * t1[1],
            t1[0] * n[2] - n[0] * t1[2],
            n[0] * t1[1] - t1[0] * n[1],
        ];
        let k = self.t.m9(crec_at(i) + crec::K);
        let ts = [t1, t2];
        let mut m = [0.0f32; 4];
        for (a, ta) in ts.iter().enumerate() {
            let c0 = (ta[0] * k[0] + ta[1] * k[3]) + ta[2] * k[6];
            let c1 = (ta[0] * k[1] + ta[1] * k[4]) + ta[2] * k[7];
            let c2 = (ta[0] * k[2] + ta[1] * k[5]) + ta[2] * k[8];
            for (b, tb) in ts.iter().enumerate() {
                m[2 * a + b] = (c0 * tb[0] + c1 * tb[1]) + c2 * tb[2];
            }
        }
        inv22(&mut m);
        let ki = kinv_at(i);
        let mut q = [0.0f32; 9];
        for (a, ta) in ts.iter().enumerate() {
            for (b, tb) in ts.iter().enumerate() {
                let mm = m[2 * a + b];
                for r in 0..3 {
                    for cc in 0..3 {
                        q[3 * r + cc] += mm * (ta[r] * tb[cc]);
                    }
                }
            }
        }
        self.t.sm9(ki, &q);
        let mut p = outer(n, n);
        for x in &mut p {
            *x *= -1.0;
        }
        p[0] += 1.0;
        p[4] += 1.0;
        p[8] += 1.0;
        self.t.sm9(proj_at(i), &p);
    }

    /// Island difficulty test (decompile lines 2307–2487): returns `v15`.
    fn difficulty(&mut self, nb: i32) -> u32 {
        let mut hard = 0u32;
        let vmax = self.v.hard_speed;
        for j in 0..nb.max(0) as usize {
            let g = bg_at(j);
            self.t.sq(g + bg::HL, 0);
            self.t.si(g + bg::LEVEL, -1);
            let v = self.b(self.bid(j)).v;
            let d = vmax * vmax - ((v.x * v.x + v.y * v.y) + v.z * v.z);
            hard |= d.to_bits() >> 31;
        }
        let ratio = self.v.heavy_light_mass_ratio;
        // comiss 1.0, ratio / jb: a NaN ratio runs the graph test
        if 1.0 >= ratio {
            return 1;
        }
        if hard != 0 {
            return hard;
        }
        // heavy/light neighbour lists over runs of contacts with the same pbody[0]
        let nc = self.nc();
        let mut node = 0usize;
        let mut start = 0i32;
        while start < nc {
            let b0 = self.pool.get(self.cid(start as usize)).pbody[0];
            let mut end = start + 1;
            while end < nc && self.pool.get(self.cid(end as usize)).pbody[0] == b0 {
                end += 1;
            }
            let mut s = start;
            while s < end {
                let p1 = self.pool.get(self.cid(s as usize)).pbody[1];
                let mut e = s;
                let (mut sx, mut sy, mut sz) = (0.0f32, 0.0f32, 0.0f32);
                while e < end {
                    let c = self.pool.get(self.cid(e as usize));
                    if c.pbody[1] != p1 {
                        break;
                    }
                    e += 1;
                    sx += c.n.x;
                    sy += c.n.y;
                    sz += c.n.z;
                }
                let c = *self.pool.get(self.cid(s as usize));
                let (cb0, cb1) = (c.pbody[0], c.pbody[1]);
                if !(self.b(cb1).minv * ratio >= self.b(cb0).minv) {
                    self.push_hl(&mut node, cb0, cb1, [sx, sy, sz], None);
                }
                if !(self.b(cb0).minv * ratio >= self.b(cb1).minv) {
                    self.push_hl(&mut node, cb1, cb0, [-sx, -sy, -sz], None);
                }
                s = e;
            }
            start = end;
        }
        for j in 0..nb.max(0) as usize {
            if hard != 0 {
                break;
            }
            let mut a = self.t.q(bg_at(j) + bg::HL) as usize;
            while a != 0 {
                let next = self.t.q(a + 24) as usize;
                let na = self.t.v3(a + 4);
                let mut b = next;
                while b != 0 {
                    let nb_ = self.t.v3(b + 4);
                    b = self.t.q(b + 24) as usize;
                    let d = (na[1] * nb_[1] + na[0] * nb_[0]) + na[2] * nb_[2];
                    hard |= d.to_bits() >> 31;
                }
                a = next;
            }
        }
        hard
    }

    /// Push a heavy/light node on the list of `owner`: `{other's solver index, n, flags}`.
    pub(crate) fn push_hl(
        &mut self,
        node: &mut usize,
        owner: BodyId,
        other: BodyId,
        n: [f32; 3],
        fl: Option<u32>,
    ) {
        let a = off::HL + 32 * *node;
        let og = bg_at(self.sidx(owner) as usize);
        let head = self.t.q(og + bg::HL);
        self.t.sq(a + 24, head);
        self.t.si(a, i32::from(self.sidx(other)));
        self.t.sv3(a + 4, n);
        if let Some(f) = fl {
            self.t.su(a + 16, f);
        }
        self.t.sq(og + bg::HL, a as u64);
        *node += 1;
    }

    /// Relative velocity of a contact from the bodies (PreCG / stage-2 form).
    fn rel_vel(&self, cid: ContactId) -> [f32; 3] {
        let c = self.pool.get(cid);
        let b0 = self.b(c.pbody[0]);
        let b1 = self.b(c.pbody[1]);
        if c.flags & cf::ANGULAR != 0 {
            return [b0.w.x - b1.w.x, b0.w.y - b1.w.y, b0.w.z - b1.w.z];
        }
        let r0 = [c.pt[0].x - b0.pos.x, c.pt[0].y - b0.pos.y, c.pt[0].z - b0.pos.z];
        let r1 = [c.pt[1].x - b1.pos.x, c.pt[1].y - b1.pos.y, c.pt[1].z - b1.pos.z];
        let (w0, v0, w1, v1) = (b0.w, b0.v, b1.w, b1.v);
        let x = (((w0.y * r0[2] - w0.z * r0[1]) + v0.x) - v1.x) - (w1.y * r1[2] - w1.z * r1[1]);
        let z = (((w0.x * r0[1] - w0.y * r0[0]) + v0.z) - v1.z) - (w1.x * r1[1] - w1.y * r1[0]);
        let y = (((w0.z * r0[0] - w0.x * r0[2]) + v0.y) - v1.y) - (w1.z * r1[0] - w1.x * r1[2]);
        [x, y, z]
    }

    /// `Pproj·v` in the PreCG order: `x = (y·P1 + x·P0) + z·P2`, `y = (x·P3 + y·P4) + z·P5`,
    /// `z = (x·P6 + y·P7) + z·P8`.
    pub(crate) fn proj_apply(&self, i: usize, d: [f32; 3]) -> [f32; 3] {
        let p = self.t.m9(proj_at(i));
        let (x, y, z) = (d[0], d[1], d[2]);
        [
            (y * p[1] + x * p[0]) + z * p[2],
            (x * p[3] + y * p[4]) + z * p[5],
            (x * p[6] + y * p[7]) + z * p[8],
        ]
    }

    /// Zero the collision accumulators of all listed bodies.
    pub(crate) fn zero_acc(&mut self, nb: i32) {
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let bb = self.bm(b);
            bb.fcoll = Vec3::ZERO;
            bb.tcoll = Vec3::ZERO;
        }
    }

    /// Apply the impulse stored at `lcp[i] + field` to the bodies' accumulators (PreCG form).
    fn apply_acc(&mut self, i: usize, field: usize, second: bool) {
        let cid = self.cid(i);
        let c = *self.pool.get(cid);
        let p = self.t.v3(lcp_at(i) + field);
        let (b0, b1) = (c.pbody[0], c.pbody[1]);
        if c.flags & cf::ANGULAR != 0 {
            let t = &mut self.bm(b0).tcoll;
            *t = Vec3::new(p[0] + t.x, p[1] + t.y, p[2] + t.z);
            let t = &mut self.bm(b1).tcoll;
            *t = Vec3::new(t.x - p[0], t.y - p[1], t.z - p[2]);
            return;
        }
        let pos0 = self.b(b0).pos;
        let pos1 = self.b(b1).pos;
        let r0 = [c.pt[0].x - pos0.x, c.pt[0].y - pos0.y, c.pt[0].z - pos0.z];
        let r1 = [c.pt[1].x - pos1.x, c.pt[1].y - pos1.y, c.pt[1].z - pos1.z];
        let _ = second;
        {
            let bb = self.bm(b0);
            bb.fcoll = Vec3::new(p[0] + bb.fcoll.x, p[1] + bb.fcoll.y, p[2] + bb.fcoll.z);
            let t = bb.tcoll;
            bb.tcoll = Vec3::new(
                (p[2] * r0[1] - p[1] * r0[2]) + t.x,
                (p[0] * r0[2] - p[2] * r0[0]) + t.y,
                (p[1] * r0[0] - p[0] * r0[1]) + t.z,
            );
        }
        {
            let bb = self.bm(b1);
            bb.fcoll = Vec3::new(bb.fcoll.x - p[0], bb.fcoll.y - p[1], bb.fcoll.z - p[2]);
            let t = bb.tcoll;
            bb.tcoll = Vec3::new(
                t.x - (p[2] * r1[1] - p[1] * r1[2]),
                t.y - (p[0] * r1[2] - p[2] * r1[0]),
                t.z - (p[1] * r1[0] - p[0] * r1[1]),
            );
        }
    }

    /// `Δv` of a contact from the bodies' accumulators (`Minv·F`, `Iinv·T`).
    fn acc_dv(&self, cid: ContactId) -> [f32; 3] {
        let c = self.pool.get(cid);
        let b0 = self.b(c.pbody[0]);
        let b1 = self.b(c.pbody[1]);
        if c.flags & cf::ANGULAR != 0 {
            let (t0, i0) = (b0.tcoll, iinv9(b0));
            let (t1, i1) = (b1.tcoll, iinv9(b1));
            let x = ((t0.y * i0[1] + t0.x * i0[0]) + t0.z * i0[2])
                - ((t1.y * i1[1] + t1.x * i1[0]) + t1.z * i1[2]);
            let v305 = (t1.y * i1[4] + t1.x * i1[3]) + t1.z * i1[5];
            let v306 = (t1.y * i1[7] + t1.x * i1[6]) + t1.z * i1[8];
            let z = ((t0.y * i0[7] + t0.x * i0[6]) + t0.z * i0[8]) - v306;
            let y = ((t0.y * i0[4] + t0.x * i0[3]) + t0.z * i0[5]) - v305;
            return [x, y, z];
        }
        let dw = |b: &RigidBody| {
            let (t, i) = (b.tcoll, iinv9(b));
            [
                (t.y * i[1] + t.x * i[0]) + t.z * i[2],
                (t.y * i[4] + t.x * i[3]) + t.z * i[5],
                (t.y * i[7] + t.x * i[6]) + t.z * i[8],
            ]
        };
        let r0 = [c.pt[0].x - b0.pos.x, c.pt[0].y - b0.pos.y, c.pt[0].z - b0.pos.z];
        let r1 = [c.pt[1].x - b1.pos.x, c.pt[1].y - b1.pos.y, c.pt[1].z - b1.pos.z];
        let w0 = dw(b0);
        let (f0, m0) = (b0.fcoll, b0.minv);
        let x0 = f0.x * m0 + (w0[1] * r0[2] - w0[2] * r0[1]);
        let y0 = f0.y * m0 + (r0[0] * w0[2] - w0[0] * r0[2]);
        let z0 = f0.z * m0 + (w0[0] * r0[1] - r0[0] * w0[1]);
        let w1 = dw(b1);
        let (f1, m1) = (b1.fcoll, b1.minv);
        [
            x0 - (f1.x * m1 + (w1[1] * r1[2] - w1[2] * r1[1])),
            y0 - (f1.y * m1 + (r1[0] * w1[2] - w1[0] * r1[2])),
            z0 - (f1.z * m1 + (w1[0] * r1[1] - r1[0] * w1[1])),
        ]
    }

    /// The CG fast path (decompile lines 2499–3226). Returns true when accepted (the bodies
    /// and contacts are final and the solver returns).
    fn precg(&mut self, nb: i32, e0: f32, inv_dt: f32) -> bool {
        let mut rr: f64 = 0.0; // v172
        let mut err: f64 = 0.0; // v173
        let nc = self.nc() as usize;
        for i in 0..nc {
            let cid = self.cid(i);
            let dv = self.rel_vel(cid);
            let pd = self.proj_apply(i, dv);
            let vreq = self.pool.get(cid).vreq;
            let l = lcp_at(i);
            let r = [vreq.x - pd[0], vreq.y - pd[1], vreq.z - pd[2]];
            self.t.sv3(l + lcp::R, r);
            self.t.sv3(l + lcp::P, r);
            self.t.sv3(l + lcp::X, [0.0; 3]);
            let r2 = (r[1] * r[1] + r[0] * r[0]) + r[2] * r[2];
            let e = err as f32;
            rr += f64::from(r2);
            err = f64::from(fmax(e, r2));
        }
        let mut iters = 6 * self.nc();
        loop {
            self.zero_acc(nb);
            for i in 0..self.nc() as usize {
                self.apply_acc(i, lcp::P, false);
            }
            for i in 0..self.nc() as usize {
                let cid = self.cid(i);
                let dv = self.acc_dv(cid);
                let ap = self.proj_apply(i, dv);
                self.t.sv3(lcp_at(i) + lcp::AP, ap);
            }
            let mut pap: f64 = 0.0;
            for i in 0..self.nc() as usize {
                let l = lcp_at(i);
                let p = self.t.v3(l + lcp::P);
                let ap = self.t.v3(l + lcp::AP);
                pap += f64::from((p[1] * ap[1] + p[0] * ap[0]) + p[2] * ap[2]);
            }
            if !(pap * pap >= 1e-30) {
                break;
            }
            let mut rr_new: f64 = 0.0;
            let alpha = (rr / pap) as f32;
            for i in 0..self.nc() as usize {
                let l = lcp_at(i);
                let ap = self.t.v3(l + lcp::AP);
                let mut r = self.t.v3(l + lcp::R);
                r[0] -= ap[0] * alpha;
                r[1] -= ap[1] * alpha;
                r[2] -= ap[2] * alpha;
                self.t.sv3(l + lcp::R, r);
                rr_new += f64::from((r[1] * r[1] + r[0] * r[0]) + r[2] * r[2]);
                let p = self.t.v3(l + lcp::P);
                let mut x = self.t.v3(l + lcp::X);
                x[0] += p[0] * alpha;
                x[1] += p[1] * alpha;
                x[2] += p[2] * alpha;
                self.t.sv3(l + lcp::X, x);
            }
            if rr_new > rr * 500.0 {
                break;
            }
            let beta = (rr_new / rr) as f32;
            rr = rr_new;
            err = 0.0;
            for i in 0..self.nc() as usize {
                let l = lcp_at(i);
                let mut p = self.t.v3(l + lcp::P);
                let r = self.t.v3(l + lcp::R);
                p = [beta * p[0], beta * p[1], beta * p[2]];
                p = [r[0] + p[0], r[1] + p[1], r[2] + p[2]];
                self.t.sv3(l + lcp::P, p);
                let r2 = (r[1] * r[1] + r[0] * r[0]) + r[2] * r[2];
                err = f64::from(fmax(err as f32, r2));
            }
            iters -= 1;
            if iters == 0 {
                break;
            }
            if !(err > f64::from(self.v.solver_tol_n * self.v.solver_tol_n)) {
                break;
            }
        }
        self.zero_acc(nb);
        for i in 0..self.nc() as usize {
            self.apply_acc(i, lcp::X, true);
        }
        // energy with the impulses applied
        let mut en = 0.0f32;
        for j in 0..nb.max(0) as usize {
            let b = self.b(self.bid(j));
            let (f, t, p, l, v, w, m) = (b.fcoll, b.tcoll, b.p, b.l, b.v, b.w, b.minv);
            let i = iinv9(b);
            let a = ((p.x + f.x) * (f.x * m + v.x) + (p.y + f.y) * (f.y * m + v.y))
                + (p.z + f.z) * (f.z * m + v.z);
            let bterm = ((t.x + l.x) * (((t.y * i[1] + t.x * i[0]) + t.z * i[2]) + w.x)
                + (l.y + t.y) * (((t.x * i[3] + t.y * i[4]) + t.z * i[5]) + w.y))
                + (l.z + t.z) * (((t.x * i[6] + t.y * i[7]) + t.z * i[8]) + w.z);
            en += a + bterm;
        }
        // acceptance
        let mut i = 0usize;
        while i < self.nc() as usize {
            let cid = self.cid(i);
            let c = *self.pool.get(cid);
            let l = lcp_at(i);
            let x = self.t.v3(l + lcp::X);
            let n = v3(c.n);
            let pn = (x[1] * n[1] + x[0] * n[0]) + x[2] * n[2];
            let tx = x[0] - pn * n[0];
            let ty = x[1] - pn * n[1];
            let tz = x[2] - pn * n[2];
            let pt2 = (tx * tx + ty * ty) + tz * tz;
            let b0 = self.b(c.pbody[0]);
            let b1 = self.b(c.pbody[1]);
            let (d, lim) = if c.flags & cf::ANGULAR != 0 {
                let (i0, i1) = (iinv9(b0), iinv9(b1));
                let a1 = (x[1] * i1[1] + x[0] * i1[0]) + x[2] * i1[2];
                let a2 = (x[1] * i1[4] + x[0] * i1[3]) + x[2] * i1[5];
                let d0 = ((x[1] * i0[1] + x[0] * i0[0]) + x[2] * i0[2]) + a1;
                let d1 = ((x[1] * i0[4] + x[0] * i0[3]) + x[2] * i0[5]) + a2;
                let d2 = ((x[1] * i0[7] + x[0] * i0[6]) + x[2] * i0[8])
                    + ((x[1] * i1[7] + x[0] * i1[6]) + x[2] * i1[8]);
                ([d0, d1, d2], -0.015f32)
            } else {
                let m = b1.minv + b0.minv;
                ([x[0] * m, x[1] * m, x[2] * m], -0.004f32)
            };
            if c.flags & cf::BILATERAL == 0 {
                if !((d[0] * n[0] + d[1] * n[1]) + d[2] * n[2] >= -0.05) {
                    break;
                }
                let lim2 = pn * c.friction + c.pn;
                let r = self.t.v3(l + lcp::R);
                if pt2 > lim2 * lim2 + 0.001
                    || !(((c.vreq.x + r[0]) * n[0] + (c.vreq.y + r[1]) * n[1])
                        + (c.vreq.z + r[2]) * n[2]
                        >= lim)
                {
                    break;
                }
            }
            if c.flags & cf::BILATERAL != 0 && c.flags & cf::KEEP_LIMIT != 0 && pn > c.pn * 1.01 {
                break;
            }
            i += 1;
        }
        if !(i == self.nc() as usize && !(en >= e0 * 1.5) && !(err >= 1e-4)) {
            return false;
        }
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let bb = self.bm(b);
            bb.p = Vec3::new(bb.fcoll.x + bb.p.x, bb.fcoll.y + bb.p.y, bb.fcoll.z + bb.p.z);
            bb.l = Vec3::new(bb.tcoll.x + bb.l.x, bb.tcoll.y + bb.l.y, bb.tcoll.z + bb.l.z);
            if !(0.0 >= bb.m) {
                let m = bb.minv;
                bb.v = Vec3::new(bb.p.x * m, bb.p.y * m, bb.p.z * m);
                let (l, i) = (bb.l, bb.iinv.to_array());
                bb.w = Vec3::new(
                    (l.y * i[1] + l.x * i[0]) + l.z * i[2],
                    (l.x * i[3] + l.y * i[4]) + l.z * i[5],
                    (l.x * i[6] + l.y * i[7]) + l.z * i[8],
                );
            }
            bb.fcoll = Vec3::new(inv_dt * bb.fcoll.x, inv_dt * bb.fcoll.y, inv_dt * bb.fcoll.z);
            bb.tcoll = Vec3::new(inv_dt * bb.tcoll.x, inv_dt * bb.tcoll.y, inv_dt * bb.tcoll.z);
        }
        for i in 0..self.nc() as usize {
            let cid = self.cid(i);
            let n = self.pool.get(cid).n;
            let l = lcp_at(i);
            let x = self.t.v3(l + lcp::X);
            let pn = (n.y * x[1] + n.x * x[0]) + n.z * x[2];
            self.t.sf(l + lcp::S, pn);
            self.pool.get_mut(cid).pn = pn;
        }
        true
    }

    /// Stage-1 inputs (decompile lines 3233–3358): contact records and body records.
    fn stage1_prep(&mut self, nb: i32) {
        for i in 0..self.nc() as usize {
            let cid = self.cid(i);
            let c = *self.pool.get(cid);
            let p0 = self.b(c.pbody[0]).pos;
            let p1 = self.b(c.pbody[1]).pos;
            let rc = crec_at(i);
            self.t
                .sv3(rc + crec::R0, [c.pt[0].x - p0.x, c.pt[0].y - p0.y, c.pt[0].z - p0.z]);
            self.t
                .sv3(rc + crec::R1, [c.pt[1].x - p1.x, c.pt[1].y - p1.y, c.pt[1].z - p1.z]);
            self.t.sv3(rc + crec::N, v3(c.n));
            self.t.sv3(rc + crec::VREQ, v3(c.vreq));
            self.t.sf(rc + crec::PN, c.pn);
            self.t.su(rc + crec::FLAGS, c.flags);
            self.t.sf(rc + crec::MU, c.friction);
            self.t.sh(rc + crec::NHIT, c.group_n as i16);
            let owner = match c.owner {
                ContactOwner::Count(o) => (self.pool.get(o).idx_bits & 0xFFFF) as i16,
                _ => 0,
            };
            self.t.sh(rc + crec::OWNER, owner);
            self.t.si(rc + crec::PN_ACC, 0);
        }
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let br = brec_at(j);
            let bb = self.bm(b);
            bb.fcoll = bb.p;
            let (l, v, w, minv, m, i) = (bb.l, bb.v, bb.w, bb.minv, bb.m, bb.iinv.to_array());
            bb.tcoll = l;
            self.t.sv3(br + brec::L, v3(l));
            self.t.sv3(br + brec::V, v3(v));
            self.t.sv3(br + brec::W, v3(w));
            self.t.sf(br + brec::MINV, minv);
            self.t.sm9(br + brec::IINV, &i);
            self.t.sf(br + brec::M, m);
        }
    }

    /// After stage 1 (decompile lines 3381–3436): velocities back into the bodies,
    /// `P = M·v`, `L = q·(Ibody ∘ (q⁻¹·w))`; contact `Pn` restored from the record.
    fn after_stage1(&mut self, nb: i32) {
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let br = brec_at(j);
            let v = self.t.v3(br + brec::V);
            let w = self.t.v3(br + brec::W);
            let bb = self.bm(b);
            bb.v = Vec3::new(v[0], v[1], v[2]);
            let m = bb.m;
            bb.p = Vec3::new(v[0] * m, v[1] * m, v[2] * m);
            bb.w = Vec3::new(w[0], w[1], w[2]);
            let q = bb.q;
            let qc = Quat::new(-q.x, -q.y, -q.z, q.w);
            let a = rotate(qc, w);
            let ib = bb.ibody;
            let a = [ib.x * a[0], ib.y * a[1], ib.z * a[2]];
            let l = rotate(q, a);
            bb.l = Vec3::new(l[0], l[1], l[2]);
        }
        for i in 0..self.nc() as usize {
            let cid = self.cid(i);
            let pn = self.t.f(crec_at(i) + crec::PN);
            self.pool.get_mut(cid).pn = pn;
        }
    }
}
