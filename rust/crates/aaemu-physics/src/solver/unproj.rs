//! Stage 3 of `InvokeContactSolver` ("LCPCG-unproj", decompile lines 5697–7130): positional
//! unprojection after an accepted stage 2, plus its helpers (graph routines `sub_35098D80`,
//! `sub_35099370`, `sub_35098CB0`, the per-body CG product `sub_35099990` and the three
//! quicksorts `sub_350995C0`, `sub_350996D0`, `sub_35099840`).
//!
//! 1. Contacts with the same `pbody[0]` (consecutive runs) are sorted by `pbody[1]` *address*
//!    (port: [`BodyArena::sort_key`](super::body::BodyArena::sort_key), compared as
//!    `2·key` so that, as with real addresses, any two distinct keys count as different in the
//!    sort's "all equal" test). Pairs of nodes (`±Σ vreq` of the active contacts with
//!    `vreq ≠ 0`) are linked to both bodies.
//! 2. Edges (body, two neighbours with opposing pushes) form a graph; a root (lowest `Minv`)
//!    and the bodies touching static ones get level 0, levels propagate (`sub_35099370`,
//!    `sub_35098D80`, `sub_35098CB0`).
//! 3. Bodies are sorted by level (`sub_350996D0`), contacts by the position of their upper body
//!    (`sub_35099840` with comparator `sub_350991B0`).
//! 4. Bottom-up per upper body a small CG solves pseudo velocities `vu`/`wu` for its contacts'
//!    `vreq` (clamped by `V+84`), which then move the pose (`pos += vu·dt`, `q = rot(wu·dt)·q`)
//!    or, with `V+88 ≠ 0`, are added to the velocities. `B+208` gets the pseudo kinetic energy.

use super::body::BodyId;
use super::contact::flags as cf;
use super::helpers::{fmax, fmin, mat_vec, normalize_or_z, quat_axis_angle, rotate, rotate_inv};
use super::invoke::{bg_at, iinv9, kinv_at, lcp_at, v3, Ctx};
use super::thread::{bg, lcp, off};
use crate::math::{Quat, Vec3};

const MAX_EDGES: i32 = 9984;

/// `ucomiss` + `je`/`jne` without a parity check: unordered compares equal.
#[inline]
fn ueq(a: f32, b: f32) -> bool {
    a == b || a.is_nan() || b.is_nan()
}

#[inline]
fn level_at(j: i32) -> usize {
    bg_at(j as usize) + bg::LEVEL
}

fn edge_at(k: i32) -> usize {
    off::EDGE + 32 * k as usize
}

impl Ctx<'_> {
    fn level(&self, j: i32) -> i32 {
        self.t.i(level_at(j))
    }
    fn set_level(&mut self, j: i32, l: i32) {
        self.t.si(level_at(j), l);
    }

    /// `sub_35098CB0`: propagate `level + 1` along the adjacency lists, stamping visited
    /// bodies with `stamp`.
    fn propagate(&mut self, a: i32, stamp: i32) {
        let g = bg_at(a as usize);
        if self.t.i(g + bg::MARK) == stamp {
            let c = self.t.i(off::CNT2);
            self.t.si(off::CNT2, c.wrapping_add(1));
            return;
        }
        self.t.si(g + bg::MARK, stamp);
        let mut i = self.t.q(g + bg::ADJ) as usize;
        while i != 0 {
            let t = self.t.i(i);
            let la = self.level(a);
            if self.level(t) <= la {
                self.set_level(t, la.wrapping_add(1));
                self.propagate(t, stamp);
            }
            i = self.t.q(i + 8) as usize;
        }
    }

    /// `sub_35099370`: for the edges of body `a` that contain `b`, mark them done and lift the
    /// third body above `a`.
    fn edge_visit(&mut self, a: i32, b: i32) {
        let mut i = self.t.q(bg_at(a as usize) + bg::EDGES) as usize;
        while i != 0 {
            let eq = |x: i32, y: i32| -> i32 {
                let d = x.wrapping_sub(y);
                -((d ^ d.wrapping_sub(1)) >> 31)
            };
            let v9 = eq(self.t.i(i + 4), b);
            let r = eq(self.t.i(i), a);
            if ((v9 | eq(self.t.i(i + 8), b)) & r) != 0 {
                self.t.si(i + 24, 1);
                let v12 = self.t.i(i + 4 + 4 * v9 as usize);
                let v13 = (self.level(a) as u32).wrapping_add(1);
                let l = self.level(v12);
                if (l as u32) < v13 {
                    self.set_level(v12, v13 as i32);
                    let c = self.t.i(off::CNT1).wrapping_add(1);
                    self.t.si(off::CNT1, c);
                    self.propagate(v12, c);
                } else {
                    let d = l.wrapping_sub(v13 as i32);
                    self.set_level(v12, l.wrapping_sub(d & (d >> 31)));
                }
                self.adj_add(a, v12);
            }
            i = self.t.q(i + 16) as usize;
        }
    }

    /// `sub_35098D80`: add `b` to the adjacency list of `a` (at most 9984 nodes in total) and
    /// continue with [`Ctx::edge_visit`]`(b, a)`.
    fn adj_add(&mut self, a: i32, b: i32) {
        let g = bg_at(a as usize) + bg::ADJ;
        let mut i = self.t.q(g) as usize;
        while i != 0 {
            if self.t.i(i) == b {
                return;
            }
            i = self.t.q(i + 8) as usize;
        }
        let n = self.t.i(off::N_ADJ);
        if n < 9984 {
            let node = off::ADJ + 16 * n as usize;
            self.t.si(node, b);
            let head = self.t.q(g);
            self.t.sq(node + 8, head);
            self.t.sq(g, node as u64);
            self.t.si(off::N_ADJ, n + 1);
            self.edge_visit(b, a);
        }
    }

    /// Address-order key of a body (see the module docs).
    fn addr_key(&self, b: BodyId) -> u64 {
        self.bodies.sort_key[b.0 as usize].wrapping_mul(2)
    }

    /// `sub_350995C0`: quicksort of contacts `lo..=hi` by `pbody[1]` address.
    fn sort_by_body1(&mut self, lo: i32, hi: i32) {
        let (mut v4, mut v8) = (lo, lo);
        if lo >= hi {
            return;
        }
        loop {
            let mid = (v4 + hi) >> 1;
            let (a, b) = (self.cid(v8 as usize), self.cid(mid as usize));
            self.t.set_contact(v8 as usize, b);
            self.t.set_contact(mid as usize, a);
            let mut flag: i64 = 0;
            let mut store = v4;
            let pv = v8;
            for j in (v4 + 1)..=hi {
                let kp = self.addr_key(self.pool.get(self.cid(pv as usize)).pbody[1]);
                let kj = self.addr_key(self.pool.get(self.cid(j as usize)).pbody[1]);
                flag |= (kj.wrapping_sub(kp) as i64) >> 1;
                if kj.wrapping_add(200) < kp.wrapping_add(200) {
                    store += 1;
                    v8 += 1;
                    let (x, y) = (self.cid(v8 as usize), self.cid(j as usize));
                    self.t.set_contact(v8 as usize, y);
                    self.t.set_contact(j as usize, x);
                }
            }
            let (x, y) = (self.cid(v8 as usize), self.cid(pv as usize));
            self.t.set_contact(pv as usize, x);
            self.t.set_contact(v8 as usize, y);
            if flag == 0 {
                return;
            }
            self.sort_by_body1(v4, store - 1);
            v4 = store + 1;
            v8 += 1;
            if store + 1 >= hi {
                return;
            }
        }
    }

    /// `sub_350996D0`: quicksort of the body list `lo..=hi` by level.
    fn sort_bodies(&mut self, lo: i32, hi: i32) {
        let (mut v4, mut v7) = (lo, lo);
        if lo >= hi {
            return;
        }
        loop {
            let mid = (v4 + hi) >> 1;
            let (a, b) = (self.bid(v7 as usize), self.bid(mid as usize));
            self.t.set_body(v7 as usize, b);
            self.t.set_body(mid as usize, a);
            let mut flag: i32 = 0;
            let mut store = v4;
            let pv = v7;
            for j in (v4 + 1)..=hi {
                let l16 = self.level(i32::from(self.sidx(self.bid(j as usize))));
                let l17 = self.level(i32::from(self.sidx(self.bid(pv as usize))));
                flag |= l16.wrapping_sub(l17);
                if l16 < l17 {
                    store += 1;
                    v7 += 1;
                    let (x, y) = (self.bid(v7 as usize), self.bid(j as usize));
                    self.t.set_body(v7 as usize, y);
                    self.t.set_body(j as usize, x);
                }
            }
            let (x, y) = (self.bid(v7 as usize), self.bid(pv as usize));
            self.t.set_body(pv as usize, x);
            self.t.set_body(v7 as usize, y);
            if flag == 0 {
                return;
            }
            self.sort_bodies(v4, store - 1);
            v4 = store + 1;
            v7 += 1;
            if store + 1 >= hi {
                return;
            }
        }
    }

    /// Key of the comparator `sub_350991B0`: the position of the contact's upper body.
    fn upper_pos(&self, i: i32) -> i32 {
        let c = self.pool.get(self.cid(i as usize));
        let s0 = i32::from(self.sidx(c.pbody[0]));
        let s1 = i32::from(self.sidx(c.pbody[1]));
        let k = ((self.level(s0).wrapping_sub(self.level(s1)) as u32) >> 31) as usize;
        let s = i32::from(self.sidx(c.pbody[k]));
        self.t.i(bg_at(s as usize) + bg::POS)
    }

    /// `sub_35099840`: quicksort of contacts `lo..=hi` by the upper body's position.
    fn sort_contacts(&mut self, lo: i32, hi: i32) {
        let (mut v4, mut v7) = (lo, lo);
        if lo >= hi {
            return;
        }
        loop {
            let mid = (v4 + hi) >> 1;
            let (a, b) = (self.cid(v7 as usize), self.cid(mid as usize));
            self.t.set_contact(v7 as usize, b);
            self.t.set_contact(mid as usize, a);
            let mut flag: i32 = 0;
            let mut store = v4;
            for j in (v4 + 1)..=hi {
                let kj = self.upper_pos(j);
                let kp = self.upper_pos(v4);
                flag |= kj.wrapping_sub(kp);
                if kj < kp {
                    store += 1;
                    v7 += 1;
                    let (x, y) = (self.cid(v7 as usize), self.cid(j as usize));
                    self.t.set_contact(v7 as usize, y);
                    self.t.set_contact(j as usize, x);
                }
            }
            let (x, y) = (self.cid(v4 as usize), self.cid(v7 as usize));
            self.t.set_contact(v4 as usize, y);
            self.t.set_contact(v7 as usize, x);
            if flag == 0 {
                return;
            }
            self.sort_contacts(v4, store - 1);
            v4 = store + 1;
            v7 += 1;
            if store + 1 >= hi {
                return;
            }
        }
    }

    /// `sub_35099990`: apply the residual impulses of `list` (`na` angular first, then linear)
    /// to the body's accumulators, compute `A·r` per contact and return `Σ (Ar·P)·r·Kinv`
    /// (double).
    fn body_cg_product(&mut self, b: BodyId, list: &[usize], na: usize) -> f64 {
        {
            let bb = self.bm(b);
            bb.fcoll = Vec3::ZERO;
            bb.tcoll = Vec3::ZERO;
        }
        for &i in &list[..na] {
            let r = lcp_at(i);
            let rx = self.t.f(r + lcp::R);
            let p = self.t.v3(r + lcp::P);
            let k = self.t.f(kinv_at(i));
            let (a, bq, c) = (p[0] * rx, p[1] * rx, p[2] * rx);
            let t = &mut self.bm(b).tcoll;
            *t = Vec3::new(t.x + a * k, t.y + bq * k, t.z + c * k);
        }
        let pos = self.b(b).pos;
        for &i in &list[na..] {
            let r = lcp_at(i);
            let c = self.pool.get(self.cid(i));
            let side = ((c.flags >> 13) & 1) as usize;
            let pt = c.pt[side];
            let (rx, ry, rz) = (pt.x - pos.x, pt.y - pos.y, pt.z - pos.z);
            let s = self.t.f(r + lcp::R);
            let p = self.t.v3(r + lcp::P);
            let (px, py, pz) = (p[0] * s, p[1] * s, p[2] * s);
            let k = self.t.f(kinv_at(i));
            let (dy, dx, dz) = (py * k, px * k, pz * k);
            let bb = self.bm(b);
            bb.fcoll = Vec3::new(dx + bb.fcoll.x, dy + bb.fcoll.y, dz + bb.fcoll.z);
            let t = bb.tcoll;
            bb.tcoll = Vec3::new(
                (dz * ry - dy * rz) + t.x,
                (rz * dx - dz * rx) + t.y,
                (dy * rx - ry * dx) + t.z,
            );
        }
        let bb = *self.b(b);
        let (t, i) = (bb.tcoll, iinv9(&bb));
        for &k in &list[..na] {
            let ap = [
                (t.x * i[0] + t.y * i[1]) + t.z * i[2],
                (t.y * i[4] + t.x * i[3]) + t.z * i[5],
                (t.x * i[6] + t.y * i[7]) + t.z * i[8],
            ];
            self.t.sv3(lcp_at(k) + lcp::AP, ap);
        }
        for &k in &list[na..] {
            let c = self.pool.get(self.cid(k));
            let side = ((c.flags >> 13) & 1) as usize;
            let pt = c.pt[side];
            let (rx, ry, rz) = (pt.x - bb.pos.x, pt.y - bb.pos.y, pt.z - bb.pos.z);
            let dwx = (t.x * i[0] + t.y * i[1]) + t.z * i[2];
            let dwz = (t.y * i[7] + t.x * i[6]) + t.z * i[8];
            let dwy = (t.x * i[3] + t.y * i[4]) + t.z * i[5];
            let m = bb.minv;
            let f = bb.fcoll;
            self.t.sv3(
                lcp_at(k) + lcp::AP,
                [
                    f.x * m + (dwy * rz - dwz * ry),
                    f.y * m + (dwz * rx - rz * dwx),
                    f.z * m + (ry * dwx - dwy * rx),
                ],
            );
        }
        let mut s: f64 = 0.0;
        for &k in list {
            let r = lcp_at(k);
            let ap = self.t.v3(r + lcp::AP);
            let p = self.t.v3(r + lcp::P);
            let d = (ap[1] * p[1] + ap[0] * p[0]) + ap[2] * p[2];
            s += f64::from((d * self.t.f(r + lcp::R)) * self.t.f(kinv_at(k)));
        }
        s
    }

    /// Stage 3 (LABEL_433 with an accepted stage 2 … LABEL_635).
    pub(crate) fn stage3(&mut self, nb: i32, dt: f32) {
        let nc = self.nc();
        for j in 0..nb.max(0) {
            let b = self.bid(j as usize);
            self.set_sidx(b, j as i16);
            let g = bg_at(j as usize);
            self.t.sq(g + bg::HL, 0);
            self.t.si(g + bg::LEVEL, -1);
            self.t.sq(g + bg::EDGES, 0);
            self.t.sq(g + bg::ADJ, 0);
            self.t.si(g + bg::MARK, 0);
        }
        for i in 0..nc as usize {
            let cid = self.cid(i);
            let c = self.pool.get_mut(cid);
            let m = (c.flags & cf::MULTI_BODY) as i32;
            c.flags &= !((-m >> 31) as u32) | 0xFFFF_EFFF;
        }
        // runs of pbody[0], sorted by pbody[1], with ± Σ vreq nodes on both bodies
        let mut node = 0usize;
        let mut start = 0i32;
        while start < self.nc() {
            let b0 = self.pool.get(self.cid(start as usize)).pbody[0];
            let mut end = start + 1;
            while end < self.nc() && self.pool.get(self.cid(end as usize)).pbody[0] == b0 {
                end += 1;
            }
            self.sort_by_body1(start, end - 1);
            let mut s = start;
            while s < end {
                let p1 = self.pool.get(self.cid(s as usize)).pbody[1];
                let mut e = s;
                let (mut sx, mut sy, mut sz) = (0.0f32, 0.0f32, 0.0f32);
                let mut fl = 0u32;
                while e < end {
                    let c = self.pool.get(self.cid(e as usize));
                    if c.pbody[1] != p1 {
                        break;
                    }
                    let vr = c.vreq;
                    if c.flags & cf::LCP_ACTIVE != 0 && (vr.x * vr.x + vr.y * vr.y) + vr.z * vr.z > 0.0
                    {
                        fl |= c.flags;
                        sx += vr.x;
                        sy += vr.y;
                        sz += vr.z;
                    }
                    e += 1;
                }
                if fl != 0 {
                    let c = *self.pool.get(self.cid(s as usize));
                    self.push_hl(&mut node, c.pbody[0], c.pbody[1], [sx, sy, sz], Some(fl));
                    self.push_hl(&mut node, c.pbody[1], c.pbody[0], [-sx, -sy, -sz], Some(fl));
                }
                s = e;
            }
            start = end;
        }
        // edges
        let mut ne: i32 = 0;
        'outer: for j in 0..nb.max(0) {
            let mut a = self.t.q(bg_at(j as usize) + bg::HL) as usize;
            while a != 0 {
                let mut b = self.t.q(a + 24) as usize;
                while b != 0 {
                    let na = self.t.v3(a + 4);
                    let nbv = self.t.v3(b + 4);
                    let d = (nbv[1] * na[1] + nbv[0] * na[0]) + nbv[2] * na[2];
                    if !(d >= 0.0) && (self.t.u(a + 16) ^ self.t.u(b + 16)) & 0x100 == 0 {
                        let e = edge_at(ne);
                        self.t.si(e, j);
                        self.t.si(e + 4, self.t.i(a));
                        self.t.si(e + 8, self.t.i(b));
                        let head = self.t.q(bg_at(j as usize) + bg::EDGES);
                        self.t.sq(e + 16, head);
                        self.t.si(e + 24, 0);
                        self.t.sq(bg_at(j as usize) + bg::EDGES, e as u64);
                        ne += 1;
                        if ne == MAX_EDGES {
                            break 'outer;
                        }
                    }
                    b = self.t.q(b + 24) as usize;
                }
                a = self.t.q(a + 24) as usize;
            }
        }
        for j in 0..nb.max(0) {
            let m = self.b(self.bid(j as usize)).minv;
            self.t.sf(bg_at(j as usize) + bg::MINV, m);
        }
        let minv = |t: &super::thread::Mem, j: i32| t.f(bg_at(j as usize) + bg::MINV);
        // root: lowest Minv among the edge ends
        let mut root: i32 = 0;
        let mut best = 1e10f32;
        for k in 0..ne {
            let e = edge_at(k);
            for o in [4, 8] {
                let x = self.t.i(e + o);
                if best > minv(self.t, x) {
                    root = x;
                    best = minv(self.t, x);
                }
            }
        }
        if ne <= 0 || best > 0.0 {
            for i in 0..self.nc() as usize {
                let c = *self.pool.get(self.cid(i));
                let (m0, m1) = (self.b(c.pbody[0]).minv, self.b(c.pbody[1]).minv);
                if ueq(m0 * m1, 0.0) {
                    let k = usize::from(ueq(m0, 0.0));
                    let s = i32::from(self.sidx(c.pbody[k]));
                    self.t.sf(bg_at(s as usize) + bg::MINV, 0.0);
                }
            }
            let mut best = 1e10f32;
            for k in 0..ne {
                let e = edge_at(k);
                for o in [4, 8] {
                    let x = self.t.i(e + o);
                    if best > minv(self.t, x) {
                        root = x;
                        best = minv(self.t, x);
                    }
                }
            }
        }
        self.t.si(off::CNT2, 0);
        self.t.si(off::N_ADJ, 0);
        for k in 0..ne {
            let e = edge_at(k);
            let a = self.t.i(e + 4);
            let side = if root == a || ueq(0.0, minv(self.t, a)) {
                Some(0)
            } else {
                let b = self.t.i(e + 8);
                if root == b || ueq(0.0, minv(self.t, b)) {
                    Some(1)
                } else {
                    None
                }
            };
            if let Some(sd) = side {
                let other = self.t.i(e + 4 + 4 * sd);
                self.set_level(other, 0);
                let e0 = self.t.i(e);
                let l = self.level(e0);
                let d = 1 - l;
                self.set_level(e0, 1 - (d & (d >> 31)));
                self.edge_visit(e0, other);
            }
        }
        // level assignment over the remaining edges
        let mut round = 0;
        loop {
            loop {
                let mut pick: i32 = -1;
                let mut lmin: i32 = 0x10000;
                for k in 0..ne {
                    let e = edge_at(k);
                    if self.t.i(e + 24) == 0 {
                        let u = |x: i32| (x as u32 & 0xFFFF) as i32;
                        let l8 = u(self.level(self.t.i(e + 8)));
                        let l4 = u(self.level(self.t.i(e + 4)));
                        let d = l4 - l8;
                        let m1 = l8 + (d & (d >> 31));
                        let l0 = u(self.level(self.t.i(e)));
                        let d = l0 - m1;
                        let m2 = m1 + (d & (d >> 31));
                        let s = (m2 - lmin) >> 31;
                        pick = (k & s) | (!s & pick);
                        let d = lmin - m2;
                        lmin = m2 + (d & (d >> 31));
                    }
                }
                if pick < 0 {
                    break;
                }
                let e = edge_at(pick);
                self.t.si(e + 24, 1);
                let (x0, x4, x8) = (self.t.i(e), self.t.i(e + 4), self.t.i(e + 8));
                let (l0, l4, l8) = (self.level(x0), self.level(x4), self.level(x8));
                let mask = (((l0 >> 31) & 4) | ((l4 >> 31) & 1) | ((l8 >> 31) & 2)) ^ 7;
                let lower = if ((l8.wrapping_sub(l4) as u32) >> 31) != 0 { x8 } else { x4 };
                match mask {
                    7 => {
                        if l0 >= self.level(lower) {
                            self.adj_add(lower, x0);
                        }
                    }
                    3 => {
                        self.set_level(x0, 1);
                        if lmin <= 1 {
                            self.adj_add(lower, x0);
                        } else {
                            self.adj_add(x0, x4);
                            self.adj_add(x0, x8);
                        }
                    }
                    4 => {
                        let v = l0 + 1;
                        self.set_level(x8, v);
                        self.set_level(x4, v);
                        self.adj_add(x0, x4);
                        self.adj_add(x0, x8);
                    }
                    0 => {}
                    _ => {
                        let a = (l0 as u32 & 0xFFFF) as i32;
                        let d = a - (lmin + 1);
                        self.set_level(x0, lmin + 1 + (d & (d >> 31)));
                        if mask & 2 == 0 {
                            self.adj_add(x4, x0);
                        } else {
                            self.adj_add(x0, x4);
                        }
                    }
                }
            }
            if round > 0 {
                break;
            }
            for k in 0..ne {
                let e = edge_at(k);
                let (x0, x4, x8) = (self.t.i(e), self.t.i(e + 4), self.t.i(e + 8));
                if self.level(x0) & self.level(x4) & self.level(x8) == -1 {
                    self.t.si(e + 24, 1);
                    self.set_level(x0, 0);
                    self.set_level(x8, 1);
                    self.set_level(x4, 1);
                    self.adj_add(x0, x4);
                    self.adj_add(x0, x8);
                }
            }
            round += 1;
        }
        let mut lmax: i32 = 0;
        for j in 0..nb.max(0) {
            let d = lmax - self.level(j);
            lmax -= d & (d >> 31);
        }
        let top = lmax + 1;
        for j in 0..nb.max(0) {
            let l = self.level(j);
            self.set_level(j, (top & (l >> 31)) | (l - (l & (l >> 31))));
            if ueq(0.0, self.b(self.bid(j as usize)).minv) {
                self.set_level(j, 0);
            }
        }
        self.sort_bodies(0, nb - 1);
        for j in 0..nb.max(0) {
            let s = i32::from(self.sidx(self.bid(j as usize)));
            let g = bg_at(s as usize);
            self.t.si(g + bg::POS, j);
            self.t.sv3(g + bg::VU, [0.0; 3]);
            self.t.sv3(g + bg::WU, [0.0; 3]);
        }
        self.sort_contacts(0, self.nc() - 1);
        self.unproject_runs();
        self.unproject_bodies(nb, dt);
    }

    /// The per-upper-body CG over the sorted contacts (decompile lines 6236–6950).
    fn unproject_runs(&mut self) {
        let tol_t = self.v.solver_tol_t;
        let tt = tol_t * tol_t;
        let mut start: i32 = 0;
        while start < self.nc() {
            let c0 = *self.pool.get(self.cid(start as usize));
            let up = |me: &Self, c: &super::contact::Contact| -> usize {
                let s0 = i32::from(me.sidx(c.pbody[0]));
                let s1 = i32::from(me.sidx(c.pbody[1]));
                ((me.level(s0).wrapping_sub(me.level(s1)) as u32) >> 31) as usize
            };
            let bs = c0.pbody[up(self, &c0)];
            let mut n_lin = 0usize;
            let mut n_ang = 0usize;
            let mut i = start;
            while i < self.nc() {
                let cid = self.cid(i as usize);
                let c = *self.pool.get(cid);
                let side = up(self, &c);
                if c.pbody[side] != bs {
                    break;
                }
                let vr = c.vreq;
                if c.flags & cf::LCP_ACTIVE != 0
                    && (vr.x * vr.x + vr.y * vr.y) + vr.z * vr.z > f32::from_bits(0x38D1_B717)
                {
                    let r = lcp_at(i as usize);
                    {
                        let cc = self.pool.get_mut(cid);
                        cc.flags = ((side as u32) << 13) | (c.flags & 0xFFFF_DFFF);
                        cc.idx_bits = (cc.idx_bits & 0xFFFF_0000) | (i as u32 & 0xFFFF);
                    }
                    let sg = (2 * side as i32 - 1) as f32;
                    self.t.sv3(r + lcp::AP, [vr.x * sg, vr.y * sg, vr.z * sg]);
                    let n = c.n;
                    let l2 = (vr.x * vr.x + vr.y * vr.y) + vr.z * vr.z;
                    let d = (n.y * vr.y + n.x * vr.x) + n.z * vr.z;
                    let p = if (d * d - l2).abs() >= l2 * 0.01 {
                        normalize_or_z(v3(vr))
                    } else {
                        v3(n)
                    };
                    self.t.sv3(r + lcp::P, p);
                    let other = c.pbody[side ^ 1];
                    let bb = *self.b(bs);
                    let ob = *self.b(other);
                    let ki = kinv_at(i as usize);
                    let c = *self.pool.get(cid);
                    if c.flags & cf::ANGULAR == 0 {
                        let pts = c.pt[side];
                        let (rx, ry, rz) = (pts.x - bb.pos.x, pts.y - bb.pos.y, pts.z - bb.pos.z);
                        let pto = c.pt[side ^ 1];
                        let (ox, oy, oz) = (pto.x - ob.pos.x, pto.y - ob.pos.y, pto.z - ob.pos.z);
                        let a0 = ry * p[2] - rz * p[1];
                        let a1 = rz * p[0] - rx * p[2];
                        let a2 = rx * p[1] - ry * p[0];
                        let im = iinv9(&bb);
                        let u0 = (a1 * im[1] + a0 * im[0]) + a2 * im[2];
                        let u1 = (a0 * im[3] + a1 * im[4]) + a2 * im[5];
                        let u2 = (a0 * im[6] + a1 * im[7]) + a2 * im[8];
                        let k = (((u1 * rz - u2 * ry) * p[0] + (rx * u2 - u0 * rz) * p[1])
                            + (u0 * ry - rx * u1) * p[2])
                            + bb.minv;
                        self.t.sf(ki, k);
                        if bb.minv * 0.1 > k {
                            self.pool.get_mut(cid).flags &= !cf::LCP_ACTIVE;
                        } else {
                            let g = bg_at(bb.solver_idx[self.tid] as usize);
                            let wu = self.t.v3(g + bg::WU);
                            let vu = self.t.v3(g + bg::VU);
                            let wz = wu[2] + bb.w.z;
                            let wx = wu[0] + bb.w.x;
                            let wy = wu[1] + bb.w.y;
                            let ax = (wy * rz) - (wz * ry);
                            let ay = (vu[1] + bb.v.y) + (rx * wz - wx * rz);
                            let az = (vu[2] + bb.v.z) + (wx * ry - rx * wy);
                            let mut ap = self.t.v3(r + lcp::AP);
                            ap[0] += (bb.v.x + vu[0]) + ax;
                            ap[1] += ay;
                            ap[2] += az;
                            let g = bg_at(ob.solver_idx[self.tid] as usize);
                            let wu = self.t.v3(g + bg::WU);
                            let vu = self.t.v3(g + bg::VU);
                            let wx = ob.w.x + wu[0];
                            let wy = wu[1] + ob.w.y;
                            let wz = wu[2] + ob.w.z;
                            let by = (vu[1] + ob.v.y) + (ox * wz - wx * oz);
                            ap[0] -= (vu[0] + ob.v.x) + (wy * oz - wz * oy);
                            ap[1] -= by;
                            ap[2] -= (vu[2] + ob.v.z) + (wx * oy - ox * wy);
                            self.t.sv3(r + lcp::AP, ap);
                            n_lin += 1;
                        }
                    } else {
                        let u = mat_vec(&iinv9(&bb), p);
                        self.t.sf(ki, (u[1] * p[1] + u[0] * p[0]) + u[2] * p[2]);
                        n_ang += 1;
                        let g = bg_at(bb.solver_idx[self.tid] as usize);
                        let wu = self.t.v3(g + bg::WU);
                        let g2 = bg_at(ob.solver_idx[self.tid] as usize);
                        let wu2 = self.t.v3(g2 + bg::WU);
                        let mut ap = self.t.v3(r + lcp::AP);
                        ap[0] += ((wu[0] + bb.w.x) - ob.w.x) - wu2[0];
                        ap[1] += ((wu[1] + bb.w.y) - ob.w.y) - wu2[1];
                        ap[2] += ((wu[2] + bb.w.z) - ob.w.z) - wu2[2];
                        self.t.sv3(r + lcp::AP, ap);
                    }
                } else {
                    self.pool.get_mut(cid).flags &= !cf::LCP_ACTIVE;
                }
                i += 1;
            }
            let end = i;
            let n = n_lin + n_ang;
            if !ueq(0.0, self.b(bs).minv) {
                let mut list = vec![0usize; n];
                let (mut ka, mut kl) = (0usize, n_ang);
                let mut k = start;
                while k < self.nc() {
                    let c = *self.pool.get(self.cid(k as usize));
                    if c.pbody[up(self, &c)] != bs {
                        break;
                    }
                    if c.flags & cf::LCP_ACTIVE != 0 {
                        let r = lcp_at(k as usize);
                        let ki = kinv_at(k as usize);
                        let kv = 1.0 / self.t.f(ki);
                        self.t.sf(ki, kv);
                        let p = self.t.v3(r + lcp::P);
                        let ap = self.t.v3(r + lcp::AP);
                        let rr = -((p[1] * ap[1] + p[0] * ap[0]) + p[2] * ap[2]);
                        self.t.sf(r + lcp::R, rr);
                        self.t.sf(r + lcp::B, rr);
                        self.t.sf(r + lcp::S, kv * rr);
                        self.t.si(r + lcp::X, 0);
                        if c.flags & cf::ANGULAR != 0 {
                            list[ka] = k as usize;
                            ka += 1;
                        } else {
                            list[kl] = k as usize;
                            kl += 1;
                        }
                    }
                    k += 1;
                }
                let end2 = k;
                self.unproject_body(bs, &list, n_ang, tt);
                start = end2;
            } else {
                start = end;
            }
        }
    }

    /// CG and clamp of one upper body (`b*`) over its contacts.
    fn unproject_body(&mut self, bs: BodyId, list: &[usize], na: usize, tt: f32) {
        let n = list.len();
        let mut rr = self.body_cg_product(bs, list, na);
        let mut iters = n as i32;
        if n != 0 && rr > f64::from(tt) {
            loop {
                let pos = self.b(bs).pos;
                {
                    let bb = self.bm(bs);
                    bb.fcoll = Vec3::ZERO;
                    bb.tcoll = Vec3::ZERO;
                }
                for &k in &list[..na] {
                    let r = lcp_at(k);
                    let p = self.t.v3(r + lcp::P);
                    let s = self.t.f(r + lcp::S);
                    let t = &mut self.bm(bs).tcoll;
                    *t = Vec3::new(p[0] * s + t.x, p[1] * s + t.y, p[2] * s + t.z);
                }
                for &k in &list[na..] {
                    let r = lcp_at(k);
                    let c = self.pool.get(self.cid(k));
                    let pt = c.pt[((c.flags >> 13) & 1) as usize];
                    let (rx, ry, rz) = (pt.x - pos.x, pt.y - pos.y, pt.z - pos.z);
                    let p = self.t.v3(r + lcp::P);
                    let s = self.t.f(r + lcp::S);
                    let (px, py, pz) = (p[0] * s, p[1] * s, p[2] * s);
                    let bb = self.bm(bs);
                    bb.fcoll = Vec3::new(px + bb.fcoll.x, py + bb.fcoll.y, pz + bb.fcoll.z);
                    let t = bb.tcoll;
                    bb.tcoll = Vec3::new(
                        (pz * ry - py * rz) + t.x,
                        (px * rz - rx * pz) + t.y,
                        (rx * py - px * ry) + t.z,
                    );
                }
                let bb = *self.b(bs);
                let dw = mat_vec(&iinv9(&bb), v3(bb.tcoll));
                for &k in &list[..na] {
                    self.t.sv3(lcp_at(k) + lcp::AP, dw);
                }
                for &k in &list[na..] {
                    let c = self.pool.get(self.cid(k));
                    let pt = c.pt[((c.flags >> 13) & 1) as usize];
                    let (rx, ry, rz) = (pt.x - bb.pos.x, pt.y - bb.pos.y, pt.z - bb.pos.z);
                    let f = bb.fcoll;
                    let m = bb.minv;
                    self.t.sv3(
                        lcp_at(k) + lcp::AP,
                        [
                            f.x * m + (rz * dw[1] - ry * dw[2]),
                            f.y * m + (rx * dw[2] - rz * dw[0]),
                            f.z * m + (ry * dw[0] - rx * dw[1]),
                        ],
                    );
                }
                let mut pap: f64 = 0.0;
                for &k in list {
                    let r = lcp_at(k);
                    let ap = self.t.v3(r + lcp::AP);
                    let p = self.t.v3(r + lcp::P);
                    let d = (ap[1] * p[1] + ap[0] * p[0]) + ap[2] * p[2];
                    self.t.sf(r + lcp::AP, d);
                    pap += f64::from((d * d) * self.t.f(kinv_at(k)));
                }
                let a = rr / (((1e-10 - pap).abs() + (pap + 1e-10)) * 0.5);
                let a = ((a + 20.0) - (20.0 - a).abs()) * 0.5;
                for &k in list {
                    let r = lcp_at(k);
                    let rx = (f64::from(self.t.f(r + lcp::R)) - f64::from(self.t.f(r + lcp::AP)) * a)
                        as f32;
                    self.t.sf(r + lcp::R, rx);
                    let x = (f64::from(self.t.f(r + lcp::S)) * a + f64::from(self.t.f(r + lcp::X)))
                        as f32;
                    self.t.sf(r + lcp::X, x);
                }
                let rn = self.body_cg_product(bs, list, na);
                let beta = rn / rr;
                rr = rn;
                for &k in list {
                    let r = lcp_at(k);
                    let s = (f64::from(self.t.f(r + lcp::S)) * beta) as f32;
                    let s = self.t.f(kinv_at(k)) * self.t.f(r + lcp::R) + s;
                    self.t.sf(r + lcp::S, s);
                }
                let last = iters == 1;
                iters -= 1;
                if last || !(rn > f64::from(tt)) {
                    break;
                }
            }
        }
        // final impulse from X
        let pos = self.b(bs).pos;
        {
            let bb = self.bm(bs);
            bb.fcoll = Vec3::ZERO;
            bb.tcoll = Vec3::ZERO;
        }
        for &k in &list[..na] {
            let r = lcp_at(k);
            let p = self.t.v3(r + lcp::P);
            let x = self.t.f(r + lcp::X);
            let t = &mut self.bm(bs).tcoll;
            *t = Vec3::new(p[0] * x + t.x, p[1] * x + t.y, p[2] * x + t.z);
        }
        let mut rmax = 0.0f32;
        for &k in &list[na..] {
            let r = lcp_at(k);
            let c = self.pool.get(self.cid(k));
            let pt = c.pt[((c.flags >> 13) & 1) as usize];
            let (rx, ry, rz) = (pt.x - pos.x, pt.y - pos.y, pt.z - pos.z);
            let p = self.t.v3(r + lcp::P);
            let x = self.t.f(r + lcp::X);
            let (px, py, pz) = (p[0] * x, p[1] * x, p[2] * x);
            let bb = self.bm(bs);
            bb.fcoll = Vec3::new(px + bb.fcoll.x, py + bb.fcoll.y, pz + bb.fcoll.z);
            let t = bb.tcoll;
            bb.tcoll = Vec3::new(
                (pz * ry - py * rz) + t.x,
                (px * rz - rx * pz) + t.y,
                (rx * py - px * ry) + t.z,
            );
        }
        let bb = *self.b(bs);
        let g = bg_at(bb.solver_idx[self.tid] as usize);
        let wu = mat_vec(&iinv9(&bb), v3(bb.tcoll));
        self.t.sv3(g + bg::WU, wu);
        let m = bb.minv;
        let vu = [bb.fcoll.x * m, bb.fcoll.y * m, bb.fcoll.z * m];
        self.t.sv3(g + bg::VU, vu);
        let n = list.len();
        let (f2, ang_scale) = if na == n {
            (2.0f32, 1.0f32)
        } else {
            for &k in &list[na..] {
                let c = self.pool.get(self.cid(k));
                let pt = c.pt[((c.flags >> 13) & 1) as usize];
                let dz = pt.z - bb.pos.z;
                let l = ((pt.y - bb.pos.y) * (pt.y - bb.pos.y) + (pt.x - bb.pos.x) * (pt.x - bb.pos.x))
                    + dz * dz;
                rmax = fmax(rmax, l);
            }
            (1.0f32, rmax)
        };
        let mut bmax = 0.0f32;
        for &k in &list[..na] {
            let b = self.t.f(lcp_at(k) + lcp::B);
            bmax = fmax(bmax, (b * b) * ang_scale);
        }
        for &k in &list[na..] {
            let b = self.t.f(lcp_at(k) + lcp::B);
            bmax = fmax(bmax, b * b);
        }
        let lim = self.v.unk_188 * self.v.unk_188;
        let cap = fmin(bmax, lim);
        let ew = ((wu[1] * wu[1] + wu[0] * wu[0]) + wu[2] * wu[2]) * ang_scale;
        let ev = (vu[1] * vu[1] + vu[0] * vu[0]) + vu[2] * vu[2];
        let e = fmax(ev, ew);
        if e > cap * f2 {
            let s = (cap / e).sqrt();
            self.t.sv3(g + bg::VU, [s * vu[0], s * vu[1], s * vu[2]]);
            self.t.sv3(g + bg::WU, [s * wu[0], s * wu[1], s * wu[2]]);
        }
    }

    /// Final per-body loop (LABEL_621): pseudo energy into `B+208`, then the pose (or velocity)
    /// update and the saved accumulators back.
    fn unproject_bodies(&mut self, nb: i32, dt: f32) {
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let s = self.sidx(b) as usize;
            self.set_sidx(b, 0);
            let g = bg_at(s);
            let wu = self.t.v3(g + bg::WU);
            let vu = self.t.v3(g + bg::VU);
            let bb = *self.b(b);
            let a = rotate_inv(bb.q, wu);
            let a = [a[0] * bb.ibody.x, bb.ibody.y * a[1], bb.ibody.z * a[2]];
            let lw = rotate(bb.q, a);
            let e = (((lw[1] * wu[1] + lw[0] * wu[0]) + lw[2] * wu[2]) * bb.minv
                + ((vu[0] * vu[0] + vu[1] * vu[1]) + vu[2] * vu[2]))
                * 0.5;
            self.bm(b).e_aux = e;
            if !(0.0 >= e) {
                if self.v.unproject_by_velocity != 0 {
                    let bm = self.bm(b);
                    bm.v = Vec3::new(vu[0] + bm.v.x, vu[1] + bm.v.y, vu[2] + bm.v.z);
                    self.t.sv3(g + bg::VU, [0.0; 3]);
                    let bm = self.bm(b);
                    bm.w = Vec3::new(wu[0] + bm.w.x, wu[1] + bm.w.y, wu[2] + bm.w.z);
                    self.t.sv3(g + bg::WU, [0.0; 3]);
                    let bm = self.bm(b);
                    let m = bm.m;
                    bm.p = Vec3::new(bm.v.x * m, bm.v.y * m, bm.v.z * m);
                    let q = bm.q;
                    let qc = Quat::new(-q.x, -q.y, -q.z, q.w);
                    let a = rotate(qc, v3(bm.w));
                    let a = [bm.ibody.x * a[0], a[1] * bm.ibody.y, a[2] * bm.ibody.z];
                    bm.l = Vec3::from_array(rotate(q, a));
                } else {
                    let bm = self.bm(b);
                    bm.pos = Vec3::new(
                        vu[0] * dt + bm.pos.x,
                        vu[1] * dt + bm.pos.y,
                        vu[2] * dt + bm.pos.z,
                    );
                    let wu = self.t.v3(g + bg::WU);
                    let w2 = (wu[0] * wu[0] + wu[1] * wu[1]) + wu[2] * wu[2];
                    let bm = self.bm(b);
                    if w2 * (dt * dt) >= 9e-6f32 {
                        let len = w2.sqrt();
                        let inv = 1.0 / len;
                        let dq = quat_axis_angle(len * dt, [inv * wu[0], inv * wu[1], inv * wu[2]]);
                        bm.q = dq.product(bm.q);
                    } else {
                        let h = Quat::new(wu[0] * 0.5, wu[1] * 0.5, wu[2] * 0.5, 0.0);
                        let d = h.product(bm.q);
                        let q = bm.q;
                        bm.q = Quat::new(
                            d.x * dt + q.x,
                            d.y * dt + q.y,
                            d.z * dt + q.z,
                            d.w * dt + q.w,
                        );
                    }
                    bm.q = bm.q.normalized_xwyz();
                }
            }
            let f = self.t.v3(g + bg::FSAVE);
            let t = self.t.v3(g + bg::TSAVE);
            let bm = self.bm(b);
            bm.fcoll = Vec3::from_array(f);
            bm.tcoll = Vec3::from_array(t);
        }
    }
}
