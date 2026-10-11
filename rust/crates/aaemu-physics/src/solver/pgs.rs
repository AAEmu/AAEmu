//! Stage 1: projected Gauss–Seidel on velocity-level impulses, `sub_3509AC90`
//! (`InvokeContactSolver` passes `T+159752` (contact records), `T+2555912` (`Pproj`/`Kinv`),
//! `T+3274760` (body records), nC, nB, `E0`, `maxIt = max(4·nC, 6000)`, `tolN` (`V+20`) and
//! `tolT` (`V+72`)).
//!
//! Per sweep, in list order, every contact whose hit count allows it (`+104 ≥ flags & 0x3F`):
//! * multi-body (`0x400000`): 1D constraint over the contact's own two bodies plus its entries,
//! * ordinary / angular: `dv = vrel − vreq` (`Pproj` applied for `0x30000`), then
//!   * bilateral (`0xE00`): `ΔP = −Kinv·dv`, clamped to `1.01·limit` cumulatively,
//!   * soft (`0x8000`): steepest-descent full impulse with the friction excess taken from the
//!     limit budget (`+84`) and the separating part removed,
//!   * ordinary: acts while approaching (`dv·n < 0`) and `|dvn| > tolN` or (budget > 1e-4 and
//!     `|dvt| > tolT`): normal-only below μ = 0.01, else the full steepest-descent impulse with
//!     the cumulative cone budget.
//!
//! Termination: `iters += applied + (nC − applied) >> 4` per sweep; stop when nothing was
//! applied, `iters ≥ maxIt` or `Σ(m|v|² + w·L) ≥ 3·E0`. Returns the number applied in the last
//! sweep.

// `!(a > b)` etc. are deliberate: they reproduce the binary's unordered (NaN) branch
// semantics of `comiss` + `jbe`/`jb`/`jae`/`ja`.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use super::invoke::{brec_at, crec_at, kinv_at, proj_at, Ctx};
use super::thread::{brec, crec};

/// Apply `ΔP` (linear, at arms `r0`/`r1`) to two body records.
fn apply_lin(ctx: &mut Ctx<'_>, b0: usize, b1: usize, r0: [f32; 3], r1: [f32; 3], d: [f32; 3]) {
    let t = &mut *ctx.t;
    let (x, y, z) = (d[0], d[1], d[2]);
    // body 0
    let m0 = t.f(b0 + brec::MINV);
    t.sf(b0, x * m0 + t.f(b0));
    t.sf(b0 + 4, y * m0 + t.f(b0 + 4));
    t.sf(b0 + 8, z * m0 + t.f(b0 + 8));
    let a0 = z * r0[1] - y * r0[2];
    let a1 = x * r0[2] - r0[0] * z;
    let a2 = r0[0] * y - x * r0[1];
    let i = t.m9(b0 + brec::IINV);
    let wx = ((a1 * i[1] + a0 * i[0]) + a2 * i[2]) + t.f(b0 + 12);
    t.sf(b0 + 12, wx);
    let wy = ((a1 * i[4] + a0 * i[3]) + a2 * i[5]) + t.f(b0 + 16);
    t.sf(b0 + 16, wy);
    let wz = ((a1 * i[7] + a0 * i[6]) + a2 * i[8]) + t.f(b0 + 20);
    t.sf(b0 + 20, wz);
    t.sf(b0 + brec::L, a0 + t.f(b0 + brec::L));
    t.sf(b0 + brec::L + 4, a1 + t.f(b0 + brec::L + 4));
    t.sf(b0 + brec::L + 8, a2 + t.f(b0 + brec::L + 8));
    // body 1
    let m1 = t.f(b1 + brec::MINV);
    t.sf(b1, t.f(b1) - x * m1);
    t.sf(b1 + 4, t.f(b1 + 4) - y * m1);
    t.sf(b1 + 8, t.f(b1 + 8) - z * m1);
    let c0 = z * r1[1] - y * r1[2];
    let c1 = x * r1[2] - r1[0] * z;
    let c2 = r1[0] * y - x * r1[1];
    let i = t.m9(b1 + brec::IINV);
    let wx = t.f(b1 + 12) - ((c1 * i[1] + c0 * i[0]) + c2 * i[2]);
    let wy = t.f(b1 + 16) - ((c0 * i[3] + c1 * i[4]) + c2 * i[5]);
    let wz = t.f(b1 + 20) - ((c0 * i[6] + c1 * i[7]) + c2 * i[8]);
    t.sf(b1 + 12, wx);
    t.sf(b1 + 16, wy);
    t.sf(b1 + 20, wz);
    t.sf(b1 + brec::L, t.f(b1 + brec::L) - c0);
    t.sf(b1 + brec::L + 4, t.f(b1 + brec::L + 4) - c1);
    t.sf(b1 + brec::L + 8, t.f(b1 + brec::L + 8) - c2);
}

/// Apply an angular impulse `d` to two body records.
fn apply_ang(ctx: &mut Ctx<'_>, b0: usize, b1: usize, d: [f32; 3]) {
    let t = &mut *ctx.t;
    let (x, y, z) = (d[0], d[1], d[2]);
    let i = t.m9(b0 + brec::IINV);
    let w3 = ((y * i[1] + x * i[0]) + z * i[2]) + t.f(b0 + 12);
    let w4 = ((y * i[4] + x * i[3]) + z * i[5]) + t.f(b0 + 16);
    let w5 = ((y * i[7] + x * i[6]) + z * i[8]) + t.f(b0 + 20);
    t.sf(b0 + 12, w3);
    t.sf(b0 + 16, w4);
    t.sf(b0 + 20, w5);
    t.sf(b0 + brec::L, x + t.f(b0 + brec::L));
    t.sf(b0 + brec::L + 4, y + t.f(b0 + brec::L + 4));
    t.sf(b0 + brec::L + 8, z + t.f(b0 + brec::L + 8));
    let i = t.m9(b1 + brec::IINV);
    let a = (y * i[4] + x * i[3]) + z * i[5];
    let b = (y * i[7] + x * i[6]) + z * i[8];
    t.sf(b1 + 12, t.f(b1 + 12) - ((y * i[1] + x * i[0]) + z * i[2]));
    t.sf(b1 + 16, t.f(b1 + 16) - a);
    t.sf(b1 + 20, t.f(b1 + 20) - b);
    t.sf(b1 + brec::L + 4, t.f(b1 + brec::L + 4) - y);
    t.sf(b1 + brec::L, t.f(b1 + brec::L) - x);
    t.sf(b1 + brec::L + 8, t.f(b1 + brec::L + 8) - z);
}

/// Velocity of a body record at arm `r`: `w × r + v` written per component.
#[inline]
fn arm_vel(t: &super::thread::Mem, b: usize, r: [f32; 3]) -> [f32; 3] {
    let (vx, vy, vz) = (t.f(b), t.f(b + 4), t.f(b + 8));
    let (wx, wy, wz) = (t.f(b + 12), t.f(b + 16), t.f(b + 20));
    [
        (wy * r[2] - wz * r[1]) + vx,
        (wz * r[0] - wx * r[2]) + vy,
        (wx * r[1] - wy * r[0]) + vz,
    ]
}

/// Apply a multi-body impulse `s·d` to a body record at arm `r` (`sub_3509AC90` entry loop
/// form).
fn apply_mb(t: &mut super::thread::Mem, b: usize, r: [f32; 3], d: [f32; 3], s: f32) {
    let m = t.f(b + brec::MINV);
    let x = d[0] * s;
    let y = d[1] * s;
    let z = d[2] * s;
    let vy = m * y + t.f(b + 4);
    t.sf(b, x * m + t.f(b));
    let vz = m * z + t.f(b + 8);
    t.sf(b + 4, vy);
    t.sf(b + 8, vz);
    let a0 = z * r[1] - y * r[2];
    let a1 = x * r[2] - z * r[0];
    let a2 = y * r[0] - x * r[1];
    let i = t.m9(b + brec::IINV);
    let wz = ((a1 * i[7] + a0 * i[6]) + a2 * i[8]) + t.f(b + 20);
    let wy = ((a1 * i[4] + a0 * i[3]) + a2 * i[5]) + t.f(b + 16);
    let wx = ((a1 * i[1] + a0 * i[0]) + a2 * i[2]) + t.f(b + 12);
    t.sf(b + 12, wx);
    t.sf(b + 16, wy);
    t.sf(b + 20, wz);
    t.sf(b + brec::L, a0 + t.f(b + brec::L));
    t.sf(b + brec::L + 4, a1 + t.f(b + brec::L + 4));
    t.sf(b + brec::L + 8, a2 + t.f(b + brec::L + 8));
}

impl Ctx<'_> {
    /// `sub_3509AC90`.
    pub(crate) fn pgs(&mut self, nb: i32, e0: f32, max_it: i32, tol_n: f32, tol_t: f32) -> i32 {
        let nc = self.nc();
        let mut iters: i32 = 0;
        loop {
            let mut applied: i32 = 0;
            for ci in 0..nc.max(0) as usize {
                let rc = crec_at(ci);
                let fl = self.t.u(rc + crec::FLAGS);
                if i32::from(self.t.h(rc + crec::NHIT)) < (fl & 0x3F) as i32 {
                    self.t.sh(rc + crec::NHIT, 0);
                    continue;
                }
                let r0 = self.t.v3(rc + crec::R0);
                let r1 = self.t.v3(rc + crec::R1);
                let b0 = brec_at(self.t.i(rc + crec::IB0) as usize);
                let b1 = brec_at(self.t.i(rc + crec::IB1) as usize);
                let n = self.t.v3(rc + crec::N);
                if fl & 0x40_0000 != 0 {
                    if self.pgs_multi(ci, rc, b0, b1, r0, r1, n, tol_n) {
                        applied += 1;
                        iters += 1;
                    }
                    continue;
                }
                let k = self.t.m9(rc + crec::K);
                let vreq = self.t.v3(rc + crec::VREQ);
                let (mut x, mut y, mut z);
                if fl & 0x100 != 0 {
                    let t = &*self.t;
                    x = t.f(b0 + 12) - t.f(b1 + 12);
                    y = t.f(b0 + 16) - t.f(b1 + 16);
                    z = t.f(b0 + 20) - t.f(b1 + 20);
                } else {
                    let t = &*self.t;
                    let (w0x, w0y, w0z) = (t.f(b0 + 12), t.f(b0 + 16), t.f(b0 + 20));
                    let (w1x, w1y, w1z) = (t.f(b1 + 12), t.f(b1 + 16), t.f(b1 + 20));
                    let v163 = r1[2] * w1y - w1z * r1[1];
                    let v165 = w1x * r1[1] - r1[0] * w1y;
                    x = (((w0y * r0[2] - w0z * r0[1]) + t.f(b0)) - t.f(b1)) - v163;
                    y = (((r0[0] * w0z - w0x * r0[2]) + t.f(b0 + 4)) - t.f(b1 + 4))
                        - (r1[0] * w1z - w1x * r1[2]);
                    z = (((w0x * r0[1] - r0[0] * w0y) + t.f(b0 + 8)) - t.f(b1 + 8)) - v165;
                }
                y -= vreq[1];
                z -= vreq[2];
                x -= vreq[0];
                let pp = self.t.m9(proj_at(ci));
                if fl & 0x3_0000 != 0 {
                    let nx = (x * pp[0] + y * pp[1]) + z * pp[2];
                    let ny = (x * pp[3] + y * pp[4]) + z * pp[5];
                    let nz = (x * pp[6] + y * pp[7]) + z * pp[8];
                    x = nx;
                    y = ny;
                    z = nz;
                }
                let d: [f32; 3];
                if fl & 0xE00 != 0 {
                    let px = (x * pp[0] + y * pp[1]) + z * pp[2];
                    let py = (x * pp[3] + y * pp[4]) + z * pp[5];
                    let pz = (x * pp[6] + y * pp[7]) + z * pp[8];
                    let v184 = ((vreq[0] * vreq[0] + vreq[1] * vreq[1]) + vreq[2] * vreq[2])
                        * f32::from_bits(0x3B23_D70B);
                    let a2 = tol_n * tol_n;
                    // comiss/jbe: NaN skips too
                    if !((px * px + py * py) + pz * pz > ((a2 - v184).abs() + (a2 + v184)) * 0.5) {
                        self.t.sh(rc + crec::NHIT, 0);
                        continue;
                    }
                    let lim = self.t.f(rc + crec::PN);
                    let acc = self.t.f(rc + crec::PN_ACC);
                    let ki = self.t.m9(kinv_at(ci));
                    let (mx, my, mz) = (-x, -y, -z);
                    let mut dx = (my * ki[1] + mx * ki[0]) + mz * ki[2];
                    let mut dy = (my * ki[4] + mx * ki[3]) + mz * ki[5];
                    let mut dz = (my * ki[7] + mx * ki[6]) + mz * ki[8];
                    let mut dn = (n[0] * dx + n[1] * dy) + n[2] * dz;
                    let v198 = (acc + dn).abs() - lim * 1.01;
                    let ok;
                    if !(((v198 + lim) - (lim - v198).abs()) * 0.5 > f32::from_bits(0x3727_C5AC)) {
                        ok = true;
                    } else {
                        let s = (lim * 1.01 - acc.abs()) / dn.abs();
                        dx *= s;
                        dy *= s;
                        dz *= s;
                        ok = (0.001 - s).to_bits() >> 31 != 0;
                        dn *= s;
                    }
                    self.t.sf(rc + crec::PN_ACC, acc + dn);
                    if !ok {
                        self.t.sh(rc + crec::NHIT, 0);
                        continue;
                    }
                    d = [dx, dy, dz];
                } else if fl & 0x8000 != 0 {
                    let v270 = (y * y + x * x) + z * z;
                    let lim = self.t.f(rc + crec::PN);
                    if !(v270 > tol_t * tol_t && lim > self.t.f(b0 + brec::M) * 0.001) {
                        self.t.sh(rc + crec::NHIT, 0);
                        continue;
                    }
                    let den = (((x * k[1] + y * k[4]) + z * k[7]) * y
                        + x * ((x * k[0] + y * k[3]) + z * k[6]))
                        + ((x * k[2] + y * k[5]) + z * k[8]) * z;
                    let s = (-1.0 / den) * v270;
                    let (mut dx, mut dy, mut dz) = (x * s, y * s, z * s);
                    let dn = ((y * s) * n[1] + n[0] * (x * s)) + (z * s) * n[2];
                    let fr = dn * self.t.f(rc + crec::MU);
                    self.t
                        .sf(rc + crec::PN_ACC, dn + self.t.f(rc + crec::PN_ACC));
                    let v275 = ((dx * dx + dy * dy) + dz * dz) - dn * dn;
                    let pt = (((-v275).abs() + v275) * 0.5).sqrt();
                    if pt > fr * 1.01 {
                        let ex = pt - fr;
                        let half = lim * 0.5;
                        if half >= ex {
                            self.t.sf(rc + crec::PN, lim - ex);
                        } else {
                            self.t.sf(rc + crec::PN, half);
                            dx *= half / ex;
                            dy *= half / ex;
                            dz *= half / ex;
                        }
                        let v279 = (dx * n[0] + dy * n[1]) + dz * n[2];
                        let v280 = (v279 - (-v279).abs()) * 0.5;
                        dx -= n[0] * v280;
                        dy -= n[1] * v280;
                        dz -= n[2] * v280;
                    }
                    d = [dx, dy, dz];
                } else {
                    let vn = (x * n[0] + y * n[1]) + z * n[2];
                    // comiss/jae: a NaN vn is treated as approaching
                    if vn >= 0.0 {
                        self.t.sh(rc + crec::NHIT, 0);
                        continue;
                    }
                    let lim = self.t.f(rc + crec::PN);
                    let tz = n[2] * vn;
                    let a = tol_n - vn.abs();
                    let v202 = f32::from_bits(0x38D1_B717) - lim;
                    let ty = n[1] * vn;
                    let tx = n[0] * vn;
                    let v331 = tol_t * tol_t
                        - (((y - n[1] * vn) * (y - n[1] * vn) + (x - n[0] * vn) * (x - n[0] * vn))
                            + (z - n[2] * vn) * (z - n[2] * vn));
                    if ((a.to_bits() | (v202.to_bits() & v331.to_bits())) as i32) >= 0 {
                        self.t.sh(rc + crec::NHIT, 0);
                        continue;
                    }
                    let mu = self.t.f(rc + crec::MU);
                    if !(mu > 0.01) {
                        let den = (((n[0] * k[1] + n[1] * k[4]) + n[2] * k[7]) * n[1]
                            + n[0] * ((n[0] * k[0] + n[1] * k[3]) + n[2] * k[6]))
                            + ((n[0] * k[2] + n[1] * k[5]) + n[2] * k[8]) * n[2];
                        let s = (-1.0 / den) * vn;
                        d = [n[0] * s, n[1] * s, n[2] * s];
                        self.t
                            .sf(rc + crec::PN_ACC, s + self.t.f(rc + crec::PN_ACC));
                    } else {
                        let den = (x * ((x * k[0] + y * k[3]) + z * k[6])
                            + ((x * k[1] + y * k[4]) + z * k[7]) * y)
                            + ((x * k[2] + y * k[5]) + z * k[8]) * z;
                        let s = (-1.0 / den) * ((y * y + x * x) + z * z);
                        let (mut dy, mut dz, mut dx) = (y * s, z * s, x * s);
                        let dn = (n[0] * (x * s) + (y * s) * n[1]) + (z * s) * n[2];
                        let v206 = mu * dn + lim;
                        self.t
                            .sf(rc + crec::PN_ACC, dn + self.t.f(rc + crec::PN_ACC));
                        let v207 = ((dy * dy + dx * dx) + dz * dz) - dn * dn;
                        let v208 = (((-v207).abs() + v207) * 0.5).sqrt();
                        let v209 = v206 - v208;
                        self.t.sf(rc + crec::PN, v209);
                        if !(v209 >= 0.0) {
                            let f = v209 / v208;
                            let ux = x + (x - tx) * f;
                            let uy = y + (y - ty) * f;
                            let uz = z + (z - tz) * f;
                            let ka = (k[1] * uy + k[0] * ux) + k[2] * uz;
                            let kb = (k[4] * uy + k[3] * ux) + k[5] * uz;
                            let kc = (k[7] * uy + k[6] * ux) + k[8] * uz;
                            let nku = (n[0] * ka + kb * n[1]) + kc * n[2];
                            let m = -vn;
                            let (ax, ay, az, inv);
                            if nku * nku >= ((kb * kb + ka * ka) + kc * kc) * 0.001 {
                                ax = ux * m;
                                ay = m * uy;
                                az = m * uz;
                                inv = 1.0 / nku;
                            } else {
                                let p = (k[3] * n[1] + k[0] * n[0]) + k[6] * n[2];
                                ay = m * n[1];
                                ax = n[0] * m;
                                az = m * n[2];
                                inv = 1.0
                                    / ((n[0] * p
                                        + ((k[1] * n[0] + k[4] * n[1]) + k[7] * n[2]) * n[1])
                                        + ((k[2] * n[0] + k[5] * n[1]) + k[8] * n[2]) * n[2]);
                            }
                            dz = az * inv;
                            dy = ay * inv;
                            dx = ax * inv;
                            self.t.sf(rc + crec::PN, 0.0);
                        }
                        d = [dx, dy, dz];
                    }
                }
                // LABEL_41: apply
                let mut d = d;
                if fl & 0x3_0000 != 0 {
                    let (x, y, z) = (d[0], d[1], d[2]);
                    d = [
                        (x * pp[0] + y * pp[1]) + z * pp[2],
                        (x * pp[3] + y * pp[4]) + z * pp[5],
                        (x * pp[6] + y * pp[7]) + z * pp[8],
                    ];
                }
                if fl & 0x100 != 0 {
                    apply_ang(self, b0, b1, d);
                } else {
                    apply_lin(self, b0, b1, r0, r1, d);
                }
                applied += 1;
                let owner = self.t.h(rc + crec::OWNER) as isize;
                let oc = (crec_at(0) as isize + 112 * owner) as usize + crec::NHIT;
                let h = self.t.h(oc);
                self.t.sh(oc, h.wrapping_add(1));
                iters += 1;
                self.t.sh(rc + crec::NHIT, 0);
            }
            // energy
            let mut en = 0.0f32;
            for j in 0..nb.max(0) as usize {
                let b = brec_at(j);
                let t = &*self.t;
                let (vx, vy, vz) = (t.f(b), t.f(b + 4), t.f(b + 8));
                let (wx, wy, wz) = (t.f(b + 12), t.f(b + 16), t.f(b + 20));
                let (lx, ly, lz) = (t.f(b + 68), t.f(b + 72), t.f(b + 76));
                en += ((vx * vx + vy * vy) + vz * vz) * t.f(b + brec::M)
                    + ((wy * ly + wx * lx) + wz * lz);
            }
            iters = iters.wrapping_add(nc.wrapping_sub(applied) >> 4);
            if applied == 0 || iters >= max_it {
                return applied;
            }
            // comiss/jb: a NaN energy keeps iterating
            if en >= e0 * 3.0 {
                return applied;
            }
        }
    }

    /// The multi-body (`0x400000`) constraint of stage 1. Returns true when applied.
    #[allow(clippy::too_many_arguments)]
    fn pgs_multi(
        &mut self,
        ci: usize,
        rc: usize,
        b0: usize,
        b1: usize,
        r0: [f32; 3],
        r1: [f32; 3],
        n: [f32; 3],
        tol_n: f32,
    ) -> bool {
        let cid = self.cid(ci);
        let (gn, base) = (self.pool.get(cid).group_n, self.multi_base(cid));
        let t = &*self.t;
        let d1 = t.v3(rc + crec::VREQ);
        let v0 = arm_vel(t, b0, r0);
        let v1 = arm_vel(t, b1, r1);
        let mut s = ((v1[1] * d1[1] + v1[0] * d1[0]) + v1[2] * d1[2])
            + ((n[1] * v0[1] + n[0] * v0[0]) + v0[2] * n[2]);
        for m in 0..gn.max(0) as usize {
            let e = self.multi[base + m];
            let bb = brec_at(e.idx as usize);
            let w = arm_vel(t, bb, e.r);
            s += (w[0] * e.n0[0] + w[1] * e.n0[1]) + w[2] * e.n0[2];
        }
        let acc = t.f(rc + crec::PN_ACC);
        let unil = t.f(rc + crec::K + 12);
        let v87 = s - t.f(rc + crec::MU);
        let lim = t.f(rc + crec::K + 8);
        let a = v87 + tol_n;
        let b = (acc * unil - lim) - 1e-8;
        if ((a - b).abs() + (b + a)) * 0.5 >= 0.0 {
            return false;
        }
        let v90 = -(v87 * t.f(rc + crec::K + 4));
        let v91 = ((lim * 1.001 - acc) * unil) + (1.0 - unil) * v90;
        let p = ((v91 + v90) - (v90 - v91).abs()) * 0.5;
        let k0 = t.f(rc + crec::K);
        self.t.sf(rc + crec::PN_ACC, acc + p);
        apply_mb(self.t, b0, r0, n, p);
        apply_mb(self.t, b1, r1, d1, p * k0);
        for m in 0..gn.max(0) as usize {
            let e = self.multi[base + m];
            apply_mb(self.t, brec_at(e.idx as usize), e.r, e.n1, p);
        }
        true
    }
}
