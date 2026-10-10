//! Stages 2 ("LCPCG") and 3 ("LCPCG-unproj") of `InvokeContactSolver` 0x3509E850 (decompile
//! lines 3439–7130).
//!
//! Stage 2 runs when stage 1 did not converge, the island is "hard" and `nC < V+100`:
//! * per contact a right-hand side `b` (`crec+0`), a preconditioner (`Kinv`) and `vn` are set
//!   up; soft contacts become 3D bilateral (or disabled when their budget is below
//!   `0.01·m`);
//! * up to `V+56` passes: the active set (bilaterals, approaching contacts with `vn < thr`) is
//!   rebuilt, records are grouped (2D `0x800`, 1D `0x400`, angular, multi-body, normal-only,
//!   3D with limit, 3D bilateral) and a preconditioned CG is run on the impulses (`α ≤ 50`,
//!   iteration limit `min(2·nU, V+44)` or `min(nU, V+40)`, stall exit after `V+64` weak
//!   iterations); impulses are applied to the collision accumulators and `vn` of the inactive
//!   contacts recomputed;
//! * the result is rejected when `|Δw|` (via `Ibody`, as in the binary) or `|Δv|` exceeds
//!   `V+80`/`V+76` or is NaN; otherwise the impulses go into `P`/`L`.
//!
//! The scalar CG coefficients are double precision as in the binary.

use super::contact::flags as cf;
use super::helpers::{fmax, inv33, mat_vec};
use super::invoke::{bg_at, brec_at, crec_at, iinv9, kinv_at, lcp_at, proj_at, v3, Ctx, SolvePath};
use super::thread::{bg, brec, crec, lcp};
use crate::math::Vec3;

impl Ctx<'_> {
    /// Stage-2 per-contact setup (lines 3439–3820). Returns `v513`, the number of contacts
    /// whose initial residual exceeds the tolerance.
    fn s2_prep(&mut self, tol: f32) -> u32 {
        let mut n_bad = 0u32;
        for i in 0..self.nc() as usize {
            let cid = self.cid(i);
            {
                let c = self.pool.get_mut(cid);
                c.idx_bits = (c.idx_bits & 0xFFFF_0000) | (i as u32 & 0xFFFF);
            }
            let rc = crec_at(i);
            let (mut x, mut y, mut z);
            let (mut q0, mut q1, mut q2);
            let c = *self.pool.get(cid);
            if c.flags & cf::MULTI_BODY != 0 {
                let pp = proj_at(i);
                self.t.sv3(pp, v3(c.n));
                self.t.sv3(pp + 12, v3(c.vreq));
                self.pool.get_mut(cid).n = Vec3::new(1.0, 0.0, 0.0);
                let p = self.t.m9(pp);
                let b0 = *self.b(c.pbody[0]);
                let r0 = [c.pt[0].x - b0.pos.x, c.pt[0].y - b0.pos.y, c.pt[0].z - b0.pos.z];
                let (w0, v0) = (b0.w, b0.v);
                let ty = (r0[0] * w0.z - w0.x * r0[2]) + v0.y;
                let tx = (w0.y * r0[2] - w0.z * r0[1]) + v0.x;
                let tz = (w0.x * r0[1] - r0[0] * w0.y) + v0.z;
                let mut s = (ty * p[1] + tx * p[0]) + tz * p[2];
                let b1 = *self.b(c.pbody[1]);
                let r1 = [c.pt[1].x - b1.pos.x, c.pt[1].y - b1.pos.y, c.pt[1].z - b1.pos.z];
                let (w1, v1) = (b1.w, b1.v);
                let a = ((w1.y * r1[2] - w1.z * r1[1]) + v1.x) * p[3];
                let by = (r1[0] * w1.z - w1.x * r1[2]) + v1.y;
                let bz = (w1.x * r1[1] - r1[0] * w1.y) + v1.z;
                s += (by * p[4] + a) + bz * p[5];
                let base = self.multi_base(cid);
                for m in 0..c.group_n.max(0) as usize {
                    let e = self.multi[base + m];
                    let bb = self.b(e.body);
                    let (w, v, r, d) = (bb.w, bb.v, e.r, e.n0);
                    s += (((r[0] * w.z - r[2] * w.x) + v.y) * d[1]
                        + ((r[2] * w.y - r[1] * w.z) + v.x) * d[0])
                        + ((r[1] * w.x - r[0] * w.y) + v.z) * d[2];
                }
                self.t.sf(rc + crec::R0, -s);
                self.t.su(rc + crec::R0 + 4, 0x8000_0000);
                self.t.su(rc + crec::R0 + 8, 0x8000_0000);
                q0 = c.friction;
                q1 = 0.0;
                q2 = 0.0;
                let ki = kinv_at(i);
                self.t.sm9(ki, &[0.0; 9]);
                self.t.sf(ki, c.nloc.y);
                if self.t.f(rc + crec::PN_ACC) * self.t.f(rc + crec::K + 12)
                    > self.t.f(rc + crec::K + 24)
                {
                    self.pool.get_mut(cid).flags &= !cf::LCP_ACTIVE;
                }
                x = s;
                y = 0.0;
                z = 0.0;
            } else {
                let dv = self.rel_vel(cid);
                x = dv[0];
                y = dv[1];
                z = dv[2];
                q0 = c.vreq.x;
                q1 = c.vreq.y;
                q2 = c.vreq.z;
                if c.flags & 0x3_0000 != 0 {
                    let d = self.proj_apply(i, [x, y, z]);
                    x = d[0];
                    y = d[1];
                    z = d[2];
                }
                if c.flags & cf::SOFT != 0 {
                    let m = self.b(c.pbody[0]).m;
                    let cc = self.pool.get_mut(cid);
                    if m * 0.01 >= cc.pn {
                        cc.flags = 63;
                    } else {
                        cc.flags = (cc.flags & 0x3_0000) | cf::BILATERAL_3D;
                        let mut k = self.t.m9(rc + crec::K);
                        inv33(&mut k);
                        self.t.sm9(kinv_at(i), &k);
                    }
                }
                {
                    let cc = self.pool.get_mut(cid);
                    let m = (cc.flags & 0x3F) as i32;
                    cc.flags &= ((m - 1) >> 31) as u32 | 0x3F;
                }
                let c = *self.pool.get(cid);
                let n = v3(c.n);
                if c.flags & cf::BILATERAL_2D != 0 {
                    let d = (x * n[0] + y * n[1]) + z * n[2];
                    x -= n[0] * d;
                    y -= n[1] * d;
                    z -= n[2] * d;
                    self.t.sv3(rc + crec::R0, [x, y, z]);
                } else if c.flags & cf::BILATERAL_1D != 0
                    || (c.flags & cf::BILATERAL_3D == 0 && c.pn <= 0.0)
                {
                    let s = (y * n[1] + x * n[0]) + z * n[2];
                    self.t.sf(rc + crec::R0, s);
                    self.t.sf(rc + crec::R0 + 4, 0.0);
                    self.t.sf(rc + crec::R0 + 8, 0.0);
                    let a = q0 * n[0];
                    let b = q1 * n[1];
                    let cz = q2 * n[2];
                    x = n[0] * s;
                    q0 = (b + a) + cz;
                    q1 = 0.0;
                    q2 = 0.0;
                    y = n[1] * s;
                    z = n[2] * s;
                    let ki = kinv_at(i);
                    self.t.sm9(ki, &[0.0; 9]);
                    let k = self.t.m9(rc + crec::K);
                    let den = (((n[0] * k[1] + n[1] * k[4]) + n[2] * k[7]) * n[1]
                        + n[0] * ((n[1] * k[3] + n[0] * k[0]) + n[2] * k[6]))
                        + ((n[0] * k[2] + n[1] * k[5]) + n[2] * k[8]) * n[2];
                    self.t.sf(ki, 1.0 / den);
                } else {
                    self.t.sv3(rc + crec::R0, [x, y, z]);
                    if c.flags & cf::BILATERAL == 0 {
                        let mut k = self.t.m9(rc + crec::K);
                        inv33(&mut k);
                        self.t.sm9(kinv_at(i), &k);
                    }
                }
                let r = self.t.v3(rc + crec::R0);
                self.t.sv3(rc + crec::R0, [-r[0], -r[1], -r[2]]);
            }
            let c = *self.pool.get(cid);
            let n = v3(c.n);
            let (d0, d1, d2) = (x - q0, y - q1, z - q2);
            // comiss/jae: a NaN product counts
            if c.flags & cf::BILATERAL != 0 || !((n[0] * d0 + n[1] * d1) + n[2] * d2 >= 0.0) {
                let e = tol * tol - ((d0 * d0 + d1 * d1) + d2 * d2);
                n_bad += e.to_bits() >> 31;
            }
            let cc = self.pool.get_mut(cid);
            cc.vn = (n[0] * d0 + n[1] * d1) + n[2] * d2;
            cc.flags &= !cf::LCP_ACTIVE;
        }
        n_bad
    }

    /// Stages 2 and 3. Returns the path taken.
    pub(crate) fn stage23(&mut self, nb: i32, dt: f32) -> SolvePath {
        let inv_dt = 1.0 / dt;
        let tol = self.v.solver_lcp_tol; // v511
        let n_bad = self.s2_prep(tol);
        if n_bad == 0 {
            self.finish_forces(nb, inv_dt);
            return SolvePath::Stage1;
        }
        let mut passes = self.v.lcp_passes; // v1805
        for j in 0..nb.max(0) as usize {
            let b = self.bid(j);
            let (f, t) = (self.b(b).fcoll, self.b(b).tcoll);
            self.t.sv3(bg_at(j) + bg::FSAVE, v3(f));
            self.t.sv3(bg_at(j) + bg::TSAVE, v3(t));
        }
        let thr: f32 = if super::helpers::imin(self.v.lcp_iters_max, self.v.unk_156) > 0 {
            0.0
        } else {
            1e10
        };
        let tol2 = tol * tol;
        let mut res_max = 0.0f32; // v630
        let mut ends = [0i32; 7];
        loop {
            // classify
            let mut cnt = [0i32; 7];
            let mut changes = 0i32;
            for i in 0..self.nc() as usize {
                let cid = self.cid(i);
                let f0 = self.pool.get(cid).flags;
                if f0 & 0x3F != 0 {
                    continue;
                }
                let c = self.pool.get_mut(cid);
                c.flags = f0 | cf::LCP_ACTIVE;
                let fl = c.flags;
                if fl & cf::BILATERAL_2D != 0 {
                    cnt[0] += 1;
                    continue;
                }
                if fl & cf::BILATERAL_1D != 0 {
                    cnt[1] += 1;
                    continue;
                }
                if fl & cf::BILATERAL_3D != 0 {
                    cnt[6] += 1;
                    continue;
                }
                if !(thr > c.vn) {
                    c.flags = fl & !cf::LCP_ACTIVE;
                } else if fl & cf::ANGULAR != 0 {
                    cnt[2] += 1;
                } else if fl & cf::MULTI_BODY != 0 {
                    cnt[3] += 1;
                } else if 0.0 >= c.pn {
                    cnt[4] += 1;
                } else {
                    cnt[5] += 1;
                }
                if (f0 ^ c.flags) & cf::LCP_ACTIVE != 0 {
                    changes += 1;
                }
            }
            if !(changes != 0 || res_max >= tol2) {
                break;
            }
            // LABEL_234
            let n_u = cnt[1]
                + cnt[2]
                + cnt[3]
                + cnt[4]
                + cnt[5]
                + cnt[6]
                + 2 * (cnt[5] + cnt[6])
                + 2 * cnt[0];
            for k in 1..7 {
                cnt[k] += cnt[k - 1];
            }
            ends = cnt;
            let mut rz: f64 = 0.0; // v659
            for i in 0..self.nc() as usize {
                let cid = self.cid(i);
                let fl = self.pool.get(cid).flags;
                let ty: usize = if fl & cf::BILATERAL_2D != 0 {
                    0
                } else if fl & cf::BILATERAL_1D != 0 {
                    1
                } else if fl & cf::BILATERAL_3D != 0 {
                    6
                } else if fl & cf::LCP_ACTIVE != 0 {
                    if fl & cf::ANGULAR != 0 {
                        2
                    } else if fl & cf::MULTI_BODY != 0 {
                        3
                    } else if 0.0 >= self.pool.get(cid).pn {
                        4
                    } else {
                        5
                    }
                } else {
                    continue;
                };
                cnt[ty] -= 1;
                let k = cnt[ty] as usize;
                rz += self.s2_assign(i, k, ty);
            }
            // LABEL_273
            let (e0, e1, e2, e3, e4, e5, e6) =
                (ends[0], ends[1], ends[2], ends[3], ends[4], ends[5], ends[6]);
            let mut it = if passes == 1 || changes == 0 {
                super::helpers::imin(2 * n_u, self.v.lcp_iters_max)
            } else {
                super::helpers::imin(n_u, self.v.lcp_iters_min)
            };
            let mut stall: i32 = 0;
            if rz > 1e-10 && it > 0 {
                let span = e4 - e0; // v1804
                let start = e4 & ((e4 - e6) >> 31); // v1843
                loop {
                    self.s2_apply_p(nb, e2, e3, e6);
                    let mut pap = self.s2_ap(e0, e2, e3, e4, e6);
                    let alpha_d = {
                        let d = ((1e-10 - pap).abs() + (pap + 1e-10)) * 0.5;
                        let a = rz / d;
                        ((a + 50.0) - (50.0 - a).abs()) * 0.5
                    };
                    let alpha = alpha_d as f32;
                    let mut rz_new: f64 = 0.0;
                    let mut k = start;
                    let mut steps = 0i32;
                    if span < e6 {
                        loop {
                            let r = lcp_at(k as usize);
                            let ap = self.t.v3(r + lcp::AP);
                            let mut rr = self.t.v3(r + lcp::R);
                            rr[0] -= alpha * ap[0];
                            rr[1] -= alpha * ap[1];
                            rr[2] -= alpha * ap[2];
                            self.t.sv3(r + lcp::R, rr);
                            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
                            let mut zz = mat_vec(&self.t.m9(kinv_at(ci)), rr);
                            self.t.sv3(r + lcp::AP, zz);
                            if self.t.i(r + lcp::PROJ) != 0 {
                                zz = mat_vec(&self.t.m9(proj_at(ci)), zz);
                                self.t.sv3(r + lcp::AP, zz);
                            }
                            rz_new += f64::from((rr[1] * zz[1] + rr[0] * zz[0]) + rr[2] * zz[2]);
                            let p = self.t.v3(r + lcp::P);
                            let mut x = self.t.v3(r + lcp::X);
                            x[0] += alpha * p[0];
                            x[1] += alpha * p[1];
                            x[2] += alpha * p[2];
                            self.t.sv3(r + lcp::X, x);
                            k = (k + 1) & ((k - e6 + 1) >> 31);
                            steps += 1;
                            if k == e0 {
                                break;
                            }
                            if steps >= e6 {
                                // the binary never leaves this loop (see `endless`)
                                self.endless = true;
                                k = e0;
                                break;
                            }
                        }
                    }
                    while k < e4 {
                        let r = lcp_at(k as usize);
                        let rx =
                            (f64::from(self.t.f(r + lcp::R)) - f64::from(self.t.f(r + lcp::AP)) * alpha_d)
                                as f32;
                        self.t.sf(r + lcp::R, rx);
                        let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
                        let zx = rx * self.t.f(kinv_at(ci));
                        self.t.sf(r + lcp::AP, zx);
                        rz_new += f64::from(zx * rx);
                        let xx = (f64::from(self.t.f(r + lcp::S)) * alpha_d
                            + f64::from(self.t.f(r + lcp::X))) as f32;
                        self.t.sf(r + lcp::X, xx);
                        k += 1;
                    }
                    pap = 0.0;
                    let _ = pap;
                    res_max = 0.0;
                    let rz_n = ((1e-10 - rz_new).abs() + (rz_new + 1e-10)) * 0.5;
                    let dec = ((rz - rz_n) - f64::from(self.v.lcp_min_improvement) * rz) as f32;
                    let e = ((dec.to_bits() as i32) >> 30) & !1;
                    let u = 1 - (stall - e);
                    stall = -(u & (u >> 31));
                    let s1 = rz_n * 0.001 + rz;
                    let s2 = rz_n * 0.001 - rz;
                    rz = rz_n;
                    let beta_d = rz_n / ((s2.abs() + s1) * 0.5);
                    let mut k = start;
                    let mut steps = 0i32;
                    if span < e6 {
                        let beta = beta_d as f32;
                        loop {
                            let r = lcp_at(k as usize);
                            let mut p = self.t.v3(r + lcp::P);
                            p = [beta * p[0], beta * p[1], beta * p[2]];
                            let zz = self.t.v3(r + lcp::AP);
                            p = [zz[0] + p[0], zz[1] + p[1], zz[2] + p[2]];
                            self.t.sv3(r + lcp::P, p);
                            k = (k + 1) & ((k - e6 + 1) >> 31);
                            let rr = self.t.v3(r + lcp::R);
                            let r2 = (rr[0] * rr[0] + rr[1] * rr[1]) + rr[2] * rr[2];
                            res_max = fmax(res_max, r2);
                            steps += 1;
                            if k == e0 {
                                break;
                            }
                            if steps >= e6 {
                                self.endless = true;
                                k = e0;
                                break;
                            }
                        }
                    }
                    while k < e4 {
                        let r = lcp_at(k as usize);
                        let s = (f64::from(self.t.f(r + lcp::S)) * beta_d) as f32;
                        let s = self.t.f(r + lcp::AP) + s;
                        self.t.sf(r + lcp::S, s);
                        let n = self.t.v3(r + lcp::N);
                        self.t.sv3(r + lcp::P, [n[0] * s, n[1] * s, n[2] * s]);
                        let rx = self.t.f(r + lcp::R);
                        res_max = fmax(res_max, rx * rx);
                        k += 1;
                    }
                    it -= 1;
                    if !(it != 0 && res_max > tol2 && stall < self.v.lcp_stall_iters) {
                        break;
                    }
                }
            }
            for k in 0..e6.max(0) as usize {
                let r = lcp_at(k);
                let n = self.t.v3(r + lcp::N);
                let ap = self.t.v3(r + lcp::AP);
                let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
                let cid = self.cid(ci);
                self.pool.get_mut(cid).vn = (n[1] * ap[1] + n[0] * ap[0]) + n[2] * ap[2];
            }
            for k in e0.max(0)..e4 {
                let r = lcp_at(k as usize);
                let n = self.t.v3(r + lcp::N);
                let x = self.t.f(r + lcp::X);
                self.t.sv3(r + lcp::X, [n[0] * x, n[1] * x, n[2] * x]);
            }
            if changes == 0 {
                break;
            }
            self.zero_acc(nb);
            self.s2_apply_x_acc(e2, e3, e6);
            self.s2_recompute_vn();
            if passes <= 2 {
                for k in e1.max(0)..e5 {
                    let ci = (self.t.u(lcp_at(k as usize) + lcp::IDX) & 0x7FFF_FFFF) as usize;
                    let cid = self.cid(ci);
                    self.pool.get_mut(cid).vn = -1.0;
                }
            } else {
                for k in e1.max(0)..e5 {
                    let r = lcp_at(k as usize);
                    let n = self.t.v3(r + lcp::N);
                    let x = self.t.v3(r + lcp::X);
                    let m1 = self.t.f(self.t.q(r + lcp::B1) as usize + brec::MINV);
                    let m0 = self.t.f(self.t.q(r + lcp::B0) as usize + brec::MINV);
                    let mm = ((m0 - m1).abs() + (m0 + m1)) * 0.5;
                    let t3 = tol * 3.0;
                    let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
                    let cid = self.cid(ci);
                    let cn = self.pool.get(cid).n;
                    let vn = ((-x[1] * mm - n[1] * t3) * cn.y + (-x[0] * mm - n[0] * t3) * cn.x)
                        + (-x[2] * mm - n[2] * t3) * cn.z;
                    self.pool.get_mut(cid).vn = vn;
                }
            }
            passes -= 1;
            if passes <= 0 {
                break;
            }
        }
        // LABEL_393
        let mut ok = true;
        let (e2, e3, e6) = (ends[2], ends[3], ends[6]);
        if passes < self.v.lcp_passes {
            let mut mw = 0.0f32;
            for k in 0..e2.max(0) as usize {
                let r = lcp_at(k);
                let x = self.t.v3(r + lcp::X);
                let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
                let c = *self.pool.get(self.cid(ci));
                let i0 = self.b(c.pbody[0]).ibody;
                let i1 = self.b(c.pbody[1]).ibody;
                let (a, b, cz) = (x[0] * i0.x, x[1] * i0.y, x[2] * i0.z);
                let s0 = (b * b + a * a) + cz * cz;
                mw = fmax(mw, s0);
                let (a, b, cz) = (x[0] * i1.x, x[1] * i1.y, x[2] * i1.z);
                let s1 = (b * b + a * a) + cz * cz;
                mw = fmax(mw, s1);
            }
            if mw.is_nan() || mw > self.v.lcp_max_dw * self.v.lcp_max_dw {
                ok = false;
            }
            let mut mv = 0.0f32;
            for k in e2.max(0)..e6 {
                let r = lcp_at(k as usize);
                let x = self.t.v3(r + lcp::X);
                let s = (x[0] * x[0] + x[1] * x[1]) + x[2] * x[2];
                let m0 = self.t.f(self.t.q(r + lcp::B0) as usize + brec::MINV);
                mv = fmax(mv, (m0 * m0) * s);
                let m1 = self.t.f(self.t.q(r + lcp::B1) as usize + brec::MINV);
                mv = fmax(mv, (m1 * m1) * s);
            }
            if mv.is_nan() || mv > self.v.lcp_max_dv * self.v.lcp_max_dv {
                ok = false;
            } else if ok {
                self.s2_apply_x_pl(e2, e3, e6);
                for j in 0..nb.max(0) as usize {
                    let b = self.bid(j);
                    let bb = self.bm(b);
                    if !(0.0 >= bb.m) {
                        let m = bb.minv;
                        bb.v = Vec3::new(bb.p.x * m, bb.p.y * m, bb.p.z * m);
                        bb.w = Vec3::from_array(mat_vec(&bb.iinv.to_array(), v3(bb.l)));
                    }
                }
            }
            if !ok {
                for j in 0..nb.max(0) as usize {
                    let b = self.bid(j);
                    let f = self.t.v3(bg_at(j) + bg::FSAVE);
                    let t = self.t.v3(bg_at(j) + bg::TSAVE);
                    let bb = self.bm(b);
                    bb.fcoll = Vec3::from_array(f);
                    bb.tcoll = Vec3::from_array(t);
                }
            }
        }
        // LABEL_433
        for i in 0..self.nc() as usize {
            let cid = self.cid(i);
            if self.pool.get(cid).flags & cf::MULTI_BODY != 0 {
                let p = self.t.m9(proj_at(i));
                let c = self.pool.get_mut(cid);
                c.n = Vec3::new(p[0], p[1], p[2]);
                c.vreq = Vec3::new(p[3], p[4], p[5]);
            }
        }
        if !ok {
            self.finish_forces(nb, inv_dt);
            return SolvePath::Stage2Rejected;
        }
        self.stage3(nb, dt);
        self.finish_forces(nb, inv_dt);
        SolvePath::Stage3
    }

    /// Record assignment for contact `i` into LCP record `k` of group `ty` (LABEL_265).
    /// Returns `b·z` (added to `v659`).
    fn s2_assign(&mut self, i: usize, k: usize, ty: usize) -> f64 {
        let r = lcp_at(k);
        let idx = (self.t.u(r + lcp::IDX) & 0x8000_0000) | (i as u32 & 0x7FFF_FFFF);
        self.t.su(r + lcp::IDX, idx);
        let cid = self.cid(i);
        let c = *self.pool.get(cid);
        let rc = crec_at(i);
        let ib0 = self.t.i(rc + crec::IB0) as usize;
        let ib1 = self.t.i(rc + crec::IB1) as usize;
        self.t.sq(r + lcp::B0, brec_at(ib0) as u64);
        let p0 = self.b(c.pbody[0]).pos;
        self.t
            .sv3(r + lcp::R0, [c.pt[0].x - p0.x, c.pt[0].y - p0.y, c.pt[0].z - p0.z]);
        self.t.sq(r + lcp::B1, brec_at(ib1) as u64);
        let p1 = self.b(c.pbody[1]).pos;
        self.t
            .sv3(r + lcp::R1, [c.pt[1].x - p1.x, c.pt[1].y - p1.y, c.pt[1].z - p1.z]);
        self.t.sv3(r + lcp::N, v3(c.n));
        self.t.si(r + lcp::PROJ, i32::from(c.flags & 0x3_0000 != 0));
        let b = self.t.v3(rc + crec::R0);
        self.t.sv3(r + lcp::B, b);
        self.t.sv3(r + lcp::R, b);
        let mut zz = mat_vec(&self.t.m9(kinv_at(i)), b);
        if c.flags & 0x3_0000 != 0 {
            zz = mat_vec(&self.t.m9(proj_at(i)), zz);
        }
        let bz = f64::from((b[0] * zz[0] + b[1] * zz[1]) + b[2] * zz[2]);
        let p = if (1..=4).contains(&ty) {
            self.t.sf(r + lcp::S, zz[0]);
            let n = c.n;
            [n.x * zz[0], n.y * zz[0], n.z * zz[0]]
        } else {
            zz
        };
        self.t.sv3(r + lcp::P, p);
        self.t.sv3(r + lcp::X, [0.0; 3]);
        bz
    }

    /// CG step 1: zero the body records' `V`/`W` and apply the search directions to them.
    fn s2_apply_p(&mut self, nb: i32, e2: i32, e3: i32, e6: i32) {
        for j in 0..nb.max(0) as usize {
            let br = brec_at(j);
            self.t.sv3(br + brec::V, [0.0; 3]);
            self.t.sv3(br + brec::W, [0.0; 3]);
        }
        let t = &mut *self.t;
        for k in 0..e2.max(0) as usize {
            let r = lcp_at(k);
            let p = t.v3(r + lcp::P);
            let b0 = t.q(r + lcp::B0) as usize + brec::W;
            let w = t.v3(b0);
            t.sv3(b0, [p[0] + w[0], p[1] + w[1], p[2] + w[2]]);
            let b1 = t.q(r + lcp::B1) as usize + brec::W;
            let w = t.v3(b1);
            t.sv3(b1, [w[0] - p[0], w[1] - p[1], w[2] - p[2]]);
        }
        for k in e2.max(0)..e3 {
            let r = lcp_at(k as usize);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let mut s = self.t.f(r + lcp::S);
            for bi in 0..2 {
                let br = self.t.q(r + lcp::B0 + 8 * bi) as usize;
                let d = self.t.v3(proj_at(ci) + 12 * bi);
                let (dx, dy, dz) = (d[0] * s, d[1] * s, d[2] * s);
                let v = self.t.v3(br);
                self.t.sv3(br, [d[0] * s + v[0], dy + v[1], dz + v[2]]);
                let a = self.t.v3(r + lcp::R0 + 12 * bi);
                let w = self.t.v3(br + brec::W);
                self.t.sv3(
                    br + brec::W,
                    [
                        (dz * a[1] - dy * a[2]) + w[0],
                        (dx * a[2] - dz * a[0]) + w[1],
                        (dy * a[0] - dx * a[1]) + w[2],
                    ],
                );
                let cid = self.cid(ci);
                s *= self.pool.get(cid).nloc.x;
            }
            let cid = self.cid(ci);
            let gn = self.pool.get(cid).group_n;
            let base = self.multi_base(cid);
            for m in 0..gn.max(0) as usize {
                let e = self.multi[base + m];
                let s = self.t.f(r + lcp::S);
                let br = brec_at(e.idx as usize);
                let d = e.n1;
                let v = self.t.v3(br);
                self.t.sv3(br, [d[0] * s + v[0], d[1] * s + v[1], d[2] * s + v[2]]);
                let s = self.t.f(r + lcp::S);
                let (dx, dy, dz) = (d[0] * s, d[1] * s, d[2] * s);
                let w = self.t.v3(br + brec::W);
                self.t.sv3(
                    br + brec::W,
                    [
                        (dz * e.r[1] - dy * e.r[2]) + w[0],
                        (dx * e.r[2] - dz * e.r[0]) + w[1],
                        (dy * e.r[0] - dx * e.r[1]) + w[2],
                    ],
                );
            }
        }
        let t = &mut *self.t;
        for k in e3.max(0)..e6 {
            let r = lcp_at(k as usize);
            let p = t.v3(r + lcp::P);
            let b0 = t.q(r + lcp::B0) as usize;
            let b1 = t.q(r + lcp::B1) as usize;
            let a = t.v3(r + lcp::R0);
            let v = t.v3(b0);
            t.sv3(b0, [p[0] + v[0], p[1] + v[1], p[2] + v[2]]);
            let w = t.v3(b0 + brec::W);
            t.sv3(
                b0 + brec::W,
                [
                    (p[2] * a[1] - p[1] * a[2]) + w[0],
                    (p[0] * a[2] - a[0] * p[2]) + w[1],
                    (a[0] * p[1] - p[0] * a[1]) + w[2],
                ],
            );
            let v = t.v3(b1);
            t.sv3(b1, [v[0] - p[0], v[1] - p[1], v[2] - p[2]]);
            let a = t.v3(r + lcp::R1);
            let w = t.v3(b1 + brec::W);
            t.sv3(
                b1 + brec::W,
                [
                    w[0] - (a[1] * p[2] - a[2] * p[1]),
                    w[1] - (a[2] * p[0] - a[0] * p[2]),
                    w[2] - (a[0] * p[1] - a[1] * p[0]),
                ],
            );
        }
    }

    /// CG step 2: `A·p` per record and `pᵀA p` (double).
    fn s2_ap(&mut self, e0: i32, e2: i32, e3: i32, e4: i32, e6: i32) -> f64 {
        let t = &mut *self.t;
        for k in 0..e2.max(0) as usize {
            let r = lcp_at(k);
            let b1 = t.q(r + lcp::B1) as usize;
            let b0 = t.q(r + lcp::B0) as usize;
            let i0 = t.m9(b0 + brec::IINV);
            let i1 = t.m9(b1 + brec::IINV);
            let w0 = t.v3(b0 + brec::W);
            let w1 = t.v3(b1 + brec::W);
            let x = ((w0[1] * i0[1] + w0[0] * i0[0]) + w0[2] * i0[2])
                - ((w1[1] * i1[1] + w1[0] * i1[0]) + w1[2] * i1[2]);
            let y = ((w0[1] * i0[4] + w0[0] * i0[3]) + w0[2] * i0[5])
                - ((w1[1] * i1[4] + w1[0] * i1[3]) + w1[2] * i1[5]);
            let z = ((w0[1] * i0[7] + w0[0] * i0[6]) + w0[2] * i0[8])
                - ((w1[1] * i1[7] + w1[0] * i1[6]) + w1[2] * i1[8]);
            t.sv3(r + lcp::AP, [x, y, z]);
        }
        for k in e2.max(0)..e3 {
            let r = lcp_at(k as usize);
            self.t.sv3(r + lcp::AP, [0.0; 3]);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            for bi in 0..2 {
                let t = &*self.t;
                let br = t.q(r + lcp::B0 + 8 * bi) as usize;
                let a = t.v3(r + lcp::R0 + 12 * bi);
                let i = t.m9(br + brec::IINV);
                let w = t.v3(br + brec::W);
                let v = t.v3(br);
                let dwx = (w[1] * i[1] + w[0] * i[0]) + w[2] * i[2];
                let dwy = (w[1] * i[4] + w[0] * i[3]) + w[2] * i[5];
                let dwz = (w[1] * i[7] + w[0] * i[6]) + w[2] * i[8];
                let m = t.f(br + brec::MINV);
                let d = t.v3(proj_at(ci) + 12 * bi);
                let s = (((v[1] * m + (a[0] * dwz - a[2] * dwx)) * d[1]
                    + (v[0] * m + (a[2] * dwy - a[1] * dwz)) * d[0])
                    + (v[2] * m + (a[1] * dwx - a[0] * dwy)) * d[2])
                    + t.f(r + lcp::AP);
                self.t.sf(r + lcp::AP, s);
            }
            let cid = self.cid(ci);
            let gn = self.pool.get(cid).group_n;
            let base = self.multi_base(cid);
            for m in 0..gn.max(0) as usize {
                let e = self.multi[base + m];
                let t = &*self.t;
                let br = brec_at(e.idx as usize);
                let w = t.v3(br + brec::W);
                let v = t.v3(br);
                let i = t.m9(br + brec::IINV);
                let dwx = (w[1] * i[1] + w[0] * i[0]) + w[2] * i[2];
                let dwy = (w[1] * i[4] + w[0] * i[3]) + w[2] * i[5];
                let dwz = (w[1] * i[7] + w[0] * i[6]) + w[2] * i[8];
                let mm = t.f(br + brec::MINV);
                let (rr, d) = (e.r, e.n0);
                let s = (((v[0] * mm + (rr[2] * dwy - rr[1] * dwz)) * d[0]
                    + (v[1] * mm + (rr[0] * dwz - rr[2] * dwx)) * d[1])
                    + (v[2] * mm + (rr[1] * dwx - rr[0] * dwy)) * d[2])
                    + t.f(r + lcp::AP);
                self.t.sf(r + lcp::AP, s);
            }
        }
        let t = &mut *self.t;
        for k in e3.max(0)..e6 {
            let r = lcp_at(k as usize);
            let b0 = t.q(r + lcp::B0) as usize;
            let b1 = t.q(r + lcp::B1) as usize;
            let dw = mat_vec(&t.m9(b0 + brec::IINV), t.v3(b0 + brec::W));
            let a = t.v3(r + lcp::R0);
            let m0 = t.f(b0 + brec::MINV);
            let v0 = t.v3(b0);
            let x0 = v0[0] * m0 + (dw[1] * a[2] - dw[2] * a[1]);
            let y0 = v0[1] * m0 + (dw[2] * a[0] - dw[0] * a[2]);
            let z0 = v0[2] * m0 + (dw[0] * a[1] - a[0] * dw[1]);
            let dw = mat_vec(&t.m9(b1 + brec::IINV), t.v3(b1 + brec::W));
            let a = t.v3(r + lcp::R1);
            let m1 = t.f(b1 + brec::MINV);
            let v1 = t.v3(b1);
            let x1 = v1[0] * m1 + (a[2] * dw[1] - a[1] * dw[2]);
            let y1 = v1[1] * m1 + (a[0] * dw[2] - dw[0] * a[2]);
            let z1 = v1[2] * m1 + (dw[0] * a[1] - a[0] * dw[1]);
            t.sv3(r + lcp::AP, [x0 - x1, y0 - y1, z0 - z1]);
        }
        let mut pap: f64 = 0.0;
        for k in 0..e0.max(0) as usize {
            let r = lcp_at(k);
            let n = t.v3(r + lcp::N);
            let mut ap = t.v3(r + lcp::AP);
            let d = (n[1] * ap[1] + n[0] * ap[0]) + n[2] * ap[2];
            ap[0] -= n[0] * d;
            ap[1] -= n[1] * d;
            ap[2] -= n[2] * d;
            t.sv3(r + lcp::AP, ap);
            let p = t.v3(r + lcp::P);
            pap += f64::from((p[1] * ap[1] + p[0] * ap[0]) + p[2] * ap[2]);
        }
        for k in e0.max(0)..e4 {
            let r = lcp_at(k as usize);
            let n = t.v3(r + lcp::N);
            let ap = t.v3(r + lcp::AP);
            let s = (ap[1] * n[1] + ap[0] * n[0]) + ap[2] * n[2];
            t.sf(r + lcp::AP, s);
            pap += f64::from(t.f(r + lcp::S) * s);
        }
        for k in e4.max(0)..e6 {
            let r = lcp_at(k as usize);
            let p = t.v3(r + lcp::P);
            let ap = t.v3(r + lcp::AP);
            pap += f64::from((p[1] * ap[1] + p[0] * ap[0]) + p[2] * ap[2]);
        }
        pap
    }

    /// Apply the LCP solution `X` to the bodies' collision accumulators (per pass).
    fn s2_apply_x_acc(&mut self, e2: i32, e3: i32, e6: i32) {
        for k in 0..e2.max(0) as usize {
            let r = lcp_at(k);
            let x = self.t.v3(r + lcp::X);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let c = *self.pool.get(self.cid(ci));
            let t = &mut self.bm(c.pbody[0]).tcoll;
            *t = Vec3::new(x[0] + t.x, x[1] + t.y, x[2] + t.z);
            let t = &mut self.bm(c.pbody[1]).tcoll;
            *t = Vec3::new(t.x - x[0], t.y - x[1], t.z - x[2]);
        }
        for k in e2.max(0)..e3 {
            let r = lcp_at(k as usize);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let cid = self.cid(ci);
            let c = *self.pool.get(cid);
            let mut s = self.t.f(r + lcp::X);
            for bi in 0..2 {
                let d = self.t.v3(proj_at(ci) + 12 * bi);
                let a = self.t.v3(r + lcp::R0 + 12 * bi);
                let (dx, dy, dz) = (d[0] * s, d[1] * s, d[2] * s);
                let bb = self.bm(c.pbody[bi]);
                bb.fcoll = Vec3::new(dx + bb.fcoll.x, dy + bb.fcoll.y, dz + bb.fcoll.z);
                let t = bb.tcoll;
                bb.tcoll = Vec3::new(
                    (dz * a[1] - dy * a[2]) + t.x,
                    (dx * a[2] - dz * a[0]) + t.y,
                    (dy * a[0] - dx * a[1]) + t.z,
                );
                s *= self.pool.get(cid).nloc.x;
            }
            let base = self.multi_base(cid);
            for m in 0..c.group_n.max(0) as usize {
                let e = self.multi[base + m];
                let s = self.t.f(r + lcp::X);
                let d = e.n1;
                let bb = self.bm(e.body);
                bb.fcoll = Vec3::new(
                    d[0] * s + bb.fcoll.x,
                    d[1] * s + bb.fcoll.y,
                    d[2] * s + bb.fcoll.z,
                );
                let (dx, dy, dz) = (d[0] * s, d[1] * s, d[2] * s);
                let t = bb.tcoll;
                bb.tcoll = Vec3::new(
                    (e.r[1] * dz - dy * e.r[2]) + t.x,
                    (dx * e.r[2] - e.r[0] * dz) + t.y,
                    (e.r[0] * dy - dx * e.r[1]) + t.z,
                );
            }
        }
        for k in e3.max(0)..e6 {
            let r = lcp_at(k as usize);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let c = *self.pool.get(self.cid(ci));
            let x = self.t.v3(r + lcp::X);
            let a = self.t.v3(r + lcp::R0);
            let bb = self.bm(c.pbody[0]);
            bb.fcoll = Vec3::new(x[0] + bb.fcoll.x, x[1] + bb.fcoll.y, x[2] + bb.fcoll.z);
            let t = bb.tcoll;
            bb.tcoll = Vec3::new(
                (x[2] * a[1] - x[1] * a[2]) + t.x,
                (x[0] * a[2] - a[0] * x[2]) + t.y,
                (a[0] * x[1] - x[0] * a[1]) + t.z,
            );
            let a = self.t.v3(r + lcp::R1);
            let bb = self.bm(c.pbody[1]);
            bb.fcoll = Vec3::new(bb.fcoll.x - x[0], bb.fcoll.y - x[1], bb.fcoll.z - x[2]);
            let t = bb.tcoll;
            bb.tcoll = Vec3::new(
                t.x - (a[1] * x[2] - a[2] * x[1]),
                t.y - (a[2] * x[0] - a[0] * x[2]),
                t.z - (a[0] * x[1] - a[1] * x[0]),
            );
        }
    }

    /// Apply the accepted LCP solution to `P`/`L` (LABEL_393 accept branch).
    fn s2_apply_x_pl(&mut self, e2: i32, e3: i32, e6: i32) {
        for k in 0..e2.max(0) as usize {
            let r = lcp_at(k);
            let x = self.t.v3(r + lcp::X);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let c = *self.pool.get(self.cid(ci));
            let l = &mut self.bm(c.pbody[0]).l;
            *l = Vec3::new(x[0] + l.x, x[1] + l.y, x[2] + l.z);
            let l = &mut self.bm(c.pbody[1]).l;
            *l = Vec3::new(l.x - x[0], l.y - x[1], l.z - x[2]);
        }
        for k in e2.max(0)..e3 {
            let r = lcp_at(k as usize);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let cid = self.cid(ci);
            let c = *self.pool.get(cid);
            let mut s = self.t.f(r + lcp::X);
            for bi in 0..2 {
                let d = self.t.v3(proj_at(ci) + 12 * bi);
                let a = self.t.v3(r + lcp::R0 + 12 * bi);
                let (dx, dy, dz) = (d[0] * s, d[1] * s, s * d[2]);
                let bb = self.bm(c.pbody[bi]);
                bb.p = Vec3::new(dx + bb.p.x, dy + bb.p.y, dz + bb.p.z);
                let l = bb.l;
                bb.l = Vec3::new(
                    (a[1] * dz - a[2] * dy) + l.x,
                    (a[2] * dx - a[0] * dz) + l.y,
                    (a[0] * dy - a[1] * dx) + l.z,
                );
                s *= self.pool.get(cid).nloc.x;
            }
            let base = self.multi_base(cid);
            for m in 0..c.group_n.max(0) as usize {
                let e = self.multi[base + m];
                let s = self.t.f(r + lcp::X);
                let d = e.n1;
                let bb = self.bm(e.body);
                bb.p = Vec3::new(d[0] * s + bb.p.x, d[1] * s + bb.p.y, d[2] * s + bb.p.z);
                let (dx, dy, dz) = (d[0] * s, d[1] * s, d[2] * s);
                let l = bb.l;
                bb.l = Vec3::new(
                    (e.r[1] * dz - e.r[2] * dy) + l.x,
                    (e.r[2] * dx - e.r[0] * dz) + l.y,
                    (e.r[0] * dy - e.r[1] * dx) + l.z,
                );
            }
        }
        for k in e3.max(0)..e6 {
            let r = lcp_at(k as usize);
            let ci = (self.t.u(r + lcp::IDX) & 0x7FFF_FFFF) as usize;
            let c = *self.pool.get(self.cid(ci));
            let x = self.t.v3(r + lcp::X);
            let a = self.t.v3(r + lcp::R0);
            let bb = self.bm(c.pbody[0]);
            bb.p = Vec3::new(x[0] + bb.p.x, x[1] + bb.p.y, x[2] + bb.p.z);
            let l = bb.l;
            bb.l = Vec3::new(
                (x[2] * a[1] - x[1] * a[2]) + l.x,
                (x[0] * a[2] - a[0] * x[2]) + l.y,
                (a[0] * x[1] - x[0] * a[1]) + l.z,
            );
            let a = self.t.v3(r + lcp::R1);
            let bb = self.bm(c.pbody[1]);
            bb.p = Vec3::new(bb.p.x - x[0], bb.p.y - x[1], bb.p.z - x[2]);
            let l = bb.l;
            bb.l = Vec3::new(
                l.x - (a[1] * x[2] - a[2] * x[1]),
                l.y - (a[2] * x[0] - a[0] * x[2]),
                l.z - (a[0] * x[1] - a[1] * x[0]),
            );
        }
    }

    /// `vn` of the inactive contacts from the bodies' velocities plus the accumulated impulses.
    fn s2_recompute_vn(&mut self) {
        for i in 0..self.nc() as usize {
            let cid = self.cid(i);
            let c = *self.pool.get(cid);
            if c.flags & 0x103F != 0 {
                continue;
            }
            let (x, y, z);
            if c.flags & cf::MULTI_BODY != 0 {
                let mut s = 0.0f32;
                for bi in 0..2 {
                    let bb = *self.b(c.pbody[bi]);
                    let r = [
                        c.pt[bi].x - bb.pos.x,
                        c.pt[bi].y - bb.pos.y,
                        c.pt[bi].z - bb.pos.z,
                    ];
                    let dw = mat_vec(&iinv9(&bb), v3(bb.tcoll));
                    let wy = dw[1] + bb.w.y;
                    let wx = dw[0] + bb.w.x;
                    let wz = dw[2] + bb.w.z;
                    let f = bb.fcoll;
                    let d = self.t.v3(proj_at(i) + 12 * bi);
                    s += (((f.y * bb.minv + bb.v.y) + (wz * r[0] - r[2] * wx)) * d[1]
                        + ((f.x * bb.minv + bb.v.x) + (wy * r[2] - wz * r[1])) * d[0])
                        + ((f.z * bb.minv + bb.v.z) + (r[1] * wx - wy * r[0])) * d[2];
                }
                let base = self.multi_base(cid);
                for m in 0..c.group_n.max(0) as usize {
                    let e = self.multi[base + m];
                    let bb = *self.b(e.body);
                    let dw = mat_vec(&iinv9(&bb), v3(bb.tcoll));
                    let wz = bb.w.z + dw[2];
                    let wy = bb.w.y + dw[1];
                    let wx = dw[0] + bb.w.x;
                    let f = bb.fcoll;
                    let (r, d) = (e.r, e.n0);
                    s += (((f.y * bb.minv + bb.v.y) + (r[0] * wz - r[2] * wx)) * d[1]
                        + ((f.x * bb.minv + bb.v.x) + (r[2] * wy - r[1] * wz)) * d[0])
                        + ((f.z * bb.minv + bb.v.z) + (r[1] * wx - r[0] * wy)) * d[2];
                }
                x = s;
                y = 0.0f32;
                z = 0.0f32;
            } else {
                let b0 = *self.b(c.pbody[0]);
                let b1 = *self.b(c.pbody[1]);
                if c.flags & cf::ANGULAR != 0 {
                    let d0 = mat_vec(&iinv9(&b0), v3(b0.tcoll));
                    let ax = (d0[0] + b0.w.x) - b1.w.x;
                    let ay = (d0[1] + b0.w.y) - b1.w.y;
                    let az = (d0[2] + b0.w.z) - b1.w.z;
                    let d1 = mat_vec(&iinv9(&b1), v3(b1.tcoll));
                    x = ax - d1[0];
                    z = az - d1[2];
                    y = ay - d1[1];
                } else {
                    let r0 = [c.pt[0].x - b0.pos.x, c.pt[0].y - b0.pos.y, c.pt[0].z - b0.pos.z];
                    let r1 = [c.pt[1].x - b1.pos.x, c.pt[1].y - b1.pos.y, c.pt[1].z - b1.pos.z];
                    let d0 = mat_vec(&iinv9(&b0), v3(b0.tcoll));
                    let w0 = [d0[0] + b0.w.x, d0[1] + b0.w.y, d0[2] + b0.w.z];
                    let (f0, m0) = (b0.fcoll, b0.minv);
                    let y0 = (f0.y * m0 + b0.v.y) + (r0[0] * w0[2] - w0[0] * r0[2]);
                    let z0 = (f0.z * m0 + b0.v.z) + (w0[0] * r0[1] - r0[0] * w0[1]);
                    let x0 = (f0.x * m0 + b0.v.x) + (w0[1] * r0[2] - w0[2] * r0[1]);
                    let d1 = mat_vec(&iinv9(&b1), v3(b1.tcoll));
                    let w1y = d1[1] + b1.w.y;
                    let w1x = d1[0] + b1.w.x;
                    let w1z = d1[2] + b1.w.z;
                    let (f1, m1) = (b1.fcoll, b1.minv);
                    x = x0 - ((f1.x * m1 + b1.v.x) + (w1y * r1[2] - w1z * r1[1]));
                    y = y0 - ((f1.y * m1 + b1.v.y) + (r1[0] * w1z - w1x * r1[2]));
                    z = z0 - ((f1.z * m1 + b1.v.z) + (w1x * r1[1] - r1[0] * w1y));
                }
            }
            let (mut x, mut y, mut z) = (x, y, z);
            if c.flags & cf::MULTI_BODY == 0 && c.flags & 0x3_0000 != 0 {
                let d = self.proj_apply(i, [x, y, z]);
                x = d[0];
                y = d[1];
                z = d[2];
            }
            let n = c.n;
            self.pool.get_mut(cid).vn = (y * n.y + x * n.x) + z * n.z;
        }
    }
}
