//! sub_35110F50 (inferred name `IntersectPolygons2D`, + sub_35110970, sub_35110CF0): the 2D
//! intersection of a convex-or-not polygon with a polygon (`bClosed = 1`, face–face) or with a
//! polyline (`bClosed = 0`, face–edge), the contact-area builder of `CTriMesh` slot 54.
//!
//! The binary works in globals guarded by a spin lock: a uniform grid over the overlap of the
//! two bounding boxes (`0x35225C50` cell counts / starts, `0x3521DC50` edge lists), the sorted
//! intersection list (`0x35235C60`, 16-byte `{x, y, edge0, edge1}`, ≤ 255), the output points
//! (`0x3522DC60`) and ids (`0x35229C50`, `(i0 + 1) | (i1 + 1) << 16`). Only the output ids
//! outlive a call in a way callers can see: when the first polygon has fewer than 3 points (or
//! the second fewer than `bClosed + 2`) the function returns the other input polygon with the
//! **ids of an earlier call** (kept: the port keeps them in [`PolyScratch::ids`]). Two words
//! after the id array (`0x3522DC50` / `0x3522DC54`) are tested as "return an input" switches;
//! nothing writes them (0).
//!
//! Algorithm: both polygons' edges are binned into a grid of about `2(n0+n1)` cells
//! (`min(…, 4095)`); the edges of the polygon with the smaller "perimeter box per vertex" (or of
//! the polyline) are intersected with the other's edges in the cells they cover
//! (sub_35110970, two points for collinear overlaps), the hits are kept per edge sorted by their
//! parameter (dedup at relative `1e-7`), then the output is walked: at each hit the sign of the
//! edges' cross product decides whether to continue along this polygon's or the other's
//! vertices (`bClosed`) or whether the polyline enters / leaves the polygon. Without any hit,
//! a point-in-polygon test (sub_35110CF0, a +y ray over the grid) picks one polygon whole.

#![allow(clippy::neg_cmp_op_on_partial_ord)]

use crate::math::Vec3;

/// The globals of the function that persist across calls (see the module docs).
#[derive(Clone, Debug)]
pub struct PolyScratch {
    /// `0x35229C50`: output ids (4096).
    pub ids: Vec<i32>,
}

impl Default for PolyScratch {
    fn default() -> Self {
        Self { ids: vec![0; 4096] }
    }
}

/// Which buffer the output points are in (`*a7`).
#[derive(Clone, Debug, PartialEq)]
pub enum PolyOut {
    /// The first input polygon.
    Input0,
    /// The second input polygon.
    Input1,
    /// The function's own output buffer.
    Own(Vec<[f32; 2]>),
}

#[inline]
fn round_magic(x: f32) -> i32 {
    ((x + 12_582_912.0).to_bits() as i32).wrapping_sub(0x4B40_0000)
}

#[inline]
fn sgnnz_f(x: f32) -> f32 {
    ((((x.to_bits() as i32) >> 30) & !1) + 1) as f32
}

#[inline]
fn neg(x: f32) -> bool {
    x.to_bits() >> 31 != 0
}

/// sub_35110970: intersection of segments `a` and `b`; returns the number of points written
/// to `out` (0, 1, or 2 for collinear overlaps). Bugs kept: in the collinear case both ends
/// of `b`'s interval get the projection of `b[0]`, and the first output point is an end of `a`.
pub fn seg_isect(a: &[[f32; 2]], b: &[[f32; 2]], out: &mut [[f32; 2]; 2]) -> i32 {
    let ay = a[1][1] - a[0][1];
    let ax = a[1][0] - a[0][0];
    let bx = b[1][0] - b[0][0];
    let dx = b[0][0] - a[0][0];
    let by = b[1][1] - b[0][1];
    let dy = b[0][1] - a[0][1];
    let x0 = ay * bx;
    let x13 = ay * ay;
    let la = ax * ax + x13;
    let cross = by * ax - x0;
    let x2 = (by * by + bx * bx) * (la * 1e-6);
    let x1 = ay * dx;
    if cross * cross > x2 {
        let sg = sgnnz_f(cross);
        let x6 = (by * dx - dy * bx) * sg;
        let sn = (x1 - dy * ax) * sg;
        let den = cross * sg;
        let t1 = ((sn * 2.0) - den).abs() - den;
        let t0 = ((x6 * 2.0) - den).abs() - den;
        if neg(t1) && neg(t0) {
            let t = x6 / den;
            out[0] = [a[0][0] + t * ax, ay * t + a[0][1]];
            return 1;
        }
        return 0;
    }
    let sn = x1 - dy * ax;
    let lim = ((dy * dy + dx * dx) * 1e-6) * la;
    if sn * sn >= lim {
        return 0;
    }
    let p0 = (b[0][1] - a[0][1]) * ay + dx * ax;
    let diff = ((b[1][1] - a[0][1]) * ay + (b[1][0] - a[0][0]) * ax) - p0;
    let s = usize::from(neg(diff));
    // b's sorted ends: [s] = b0, [s^1] = b1; both projections are p0 (kept)
    let bpt = if s == 0 { [b[0], b[1]] } else { [b[1], b[0]] };
    let (blo, bhi) = (p0, p0);
    let lo = ((0.0 - blo).abs() + (blo + 0.0)) * 0.5;
    let hi = ((bhi + la) - (la - bhi).abs()) * 0.5;
    if lo > hi {
        return 0;
    }
    out[0] = if neg(0.0 - blo) { a[1] } else { a[0] };
    out[1] = bpt[usize::from(neg(bhi - la))];
    2
}

/// The grid the edges are binned in.
struct Grid {
    nx: i32,
    ny: i32,
    lox: f32,
    loy: f32,
    sx: f32,
    sy: f32,
    /// Cell starts (after the fill pass) — `cells + 1` entries.
    start: Vec<i32>,
    /// Edge entries `edge | poly << 31`.
    items: Vec<u32>,
}

impl Grid {
    fn cell(&self, p: [f32; 2]) -> (i32, i32) {
        (
            round_magic(((p[0] - self.lox) * self.sx) - 0.5),
            round_magic(((p[1] - self.loy) * self.sy) - 0.5),
        )
    }

    /// The covered cell rectangle of an edge from cell `c0` to `c1`, as the passes clamp it:
    /// `x ∈ [max(min x, 0), min(max x, nx − 1)]`, `y ∈ [min(max(min y, 0), ny), min(max y, ny)]`.
    fn rect(&self, c0: (i32, i32), c1: (i32, i32)) -> (i32, i32, i32, i32) {
        let mn = |a: i32, b: i32| {
            let d = a.wrapping_sub(b);
            b.wrapping_add(d & (d >> 31))
        };
        let (x0, y0) = c0;
        let (x1, y1) = c1;
        let m114 = {
            let d = x0.wrapping_sub(x1);
            d & (d >> 31)
        };
        let xlo = {
            let m = x1.wrapping_add(m114);
            let q = m.wrapping_neg();
            (q & (q >> 31)).wrapping_neg()
        };
        let m116 = {
            let d = y0.wrapping_sub(y1);
            d & (d >> 31)
        };
        let ylo = {
            let q = y1.wrapping_add(m116).wrapping_neg();
            let x = q & (q >> 31);
            let t = self.ny.wrapping_add(x);
            (t & (t >> 31)).wrapping_sub(x)
        };
        let xhi = mn(x0.wrapping_sub(m114), self.nx - 1);
        let yhi = mn(y0.wrapping_sub(m116), self.ny);
        (xlo, xhi, ylo, yhi)
    }
}

/// sub_35110CF0: whether `pt` (in cell `c`) is inside the polygon `poly` (side `side`'s
/// entries skipped): crossings of a +y ray, rows from `c.y` to `ny` until one row has a hit.
fn point_inside(g: &Grid, side: u32, c: (i32, i32), poly: &[[f32; 2]], pt: [f32; 2]) -> bool {
    let (x, mut y) = c;
    if (x as u32) >= (g.nx as u32) || y < 0 {
        return false;
    }
    let mut cnt = 0i32;
    let mut found = false;
    if y > g.ny {
        return false;
    }
    loop {
        if found {
            break;
        }
        let idx = (y * g.nx + x) as usize;
        let (s, e) = (g.start[idx], g.start[idx + 1]);
        for k in s..e {
            let it = g.items[k as usize];
            if it >> 31 == side {
                continue;
            }
            let i = (it & 0x7FFF_FFFF) as usize;
            let (p, q) = (poly[i], poly[i + 1]);
            let dx = q[0] - p[0];
            let dy = q[1] - p[1];
            let sg = sgnnz_f(dx);
            let x6 = (p[1] * dx - dy * p[0]) + pt[0] * dy;
            let t1 = ((q[0] + p[0]) - pt[0] * 2.0).abs() - (p[0] - q[0]).abs();
            let t2 = (dx * sg) * pt[1] - x6 * sg;
            if neg(t1) && neg(t2) {
                found = true;
                let b = dx.to_bits() as i32;
                cnt += -1 - (b.wrapping_sub(1) >> 31) - (b >> 31);
            }
        }
        y += 1;
        if y > g.ny {
            break;
        }
    }
    cnt > 0
}

/// One entry of the intersection list.
#[derive(Clone, Copy, Debug, Default)]
struct Hit {
    p: [f32; 2],
    e: [i32; 2],
}

/// sub_35110F50: `polys[k]` are `n[k]` points plus the closing copy (`n[k] + 1` entries).
/// Returns the count and the output (points, and ids in `scr.ids`).
#[allow(clippy::too_many_lines)]
pub fn intersect_polygons(
    scr: &mut PolyScratch,
    polys: [&[[f32; 2]]; 2],
    n: [i32; 2],
    closed: i32,
) -> (i32, PolyOut) {
    if n[0] < 3 {
        return (n[1] & -closed, PolyOut::Input1);
    }
    if n[1] < closed + 2 {
        return (n[0] & -closed, PolyOut::Input0);
    }
    let minf = |a: f32, b: f32| ((a + b) - (a - b).abs()) * 0.5;
    let maxf = |a: f32, b: f32| ((a - b).abs() + (a + b)) * 0.5;
    let mut mn = [[0f32; 2]; 2];
    let mut mx = [[0f32; 2]; 2];
    for k in 0..2 {
        let p = polys[k];
        mn[k] = p[0];
        mx[k] = p[0];
        for q in &p[1..n[k] as usize] {
            mn[k][0] = minf(mn[k][0], q[0]);
            mn[k][1] = minf(mn[k][1], q[1]);
            mx[k][0] = maxf(mx[k][0], q[0]);
            mx[k][1] = maxf(mx[k][1], q[1]);
        }
    }
    let lox = maxf(mn[0][0], mn[1][0]);
    let loy = maxf(mn[0][1], mn[1][1]);
    let hix = minf(mx[0][0], mx[1][0]);
    let hiy = minf(mx[0][1], mx[1][1]);
    let v79 = (hiy - loy).abs() * 1e-5 + (hix - lox);
    let v80 = v79.abs();
    let v81 = v80 * 1e-5 + (hiy - loy);
    let t5 = f32::from_bits(0x36A7_C5AC);
    if !(v79 > v81.abs() * t5) || !(v81 > v80 * t5) {
        return (0, PolyOut::Own(Vec::new()));
    }
    let v82 = v81 * 0.01;
    let v83 = v79 * 0.01;
    let loy2 = loy - v82;
    let lox2 = lox - v83;
    let v84 = (hix + v83) - lox2;
    let nn = {
        let a = (n[0] + n[1]) * 2 - 4095;
        (a & (a >> 31)) + 4095
    };
    let v85 = nn as f32;
    let v86 = (hiy + v82) - loy2;
    let ex = v86.abs() * 1e-5 + v84;
    let ey = ex.abs() * 1e-5 + v86;
    let iey = 1.0 / ey;
    let iex = 1.0 / ex;
    let r1 = iex * ey;
    let m1 = ((r1 + v85) - (r1 - v85).abs()) * 0.5;
    let r2 = iey * ex;
    let m2 = ((r2 + v85) - (r2 - v85).abs()) * 0.5;
    let q = (((m1 - 1.0).abs() + (m1 + 1.0)) * 0.5) * ((nn * 4) as f32) + 1.0;
    let mut ny = {
        let x = round_magic(q.sqrt() * 0.5 - 1.0);
        let d = 1 - x;
        1 - (d & (d >> 31))
    };
    let mut nx = {
        let v = ((((m2 - 1.0).abs() + (m2 + 1.0)) * 0.5) * (ny as f32) - 0.5) + 12_582_912.0;
        let x = (v.to_bits() as i32).wrapping_sub(0x4B40_0000);
        let d = 1 - x;
        1 - (d & (d >> 31))
    };
    if (ny + 1).wrapping_mul(nx) > 4095 {
        let d = ny - 4095;
        ny = (d & (d >> 31)) + 4095;
        nx = 4095 / (ny + 1);
    }
    let cells = nx.wrapping_mul(ny + 1);
    let sx = nx as f32 * iex;
    let sy = ny as f32 * iey;
    if (cells as u32) >= 0xFFF {
        return (0, PolyOut::Own(Vec::new()));
    }
    let ne = [n[0], n[1] - (closed ^ 1)];
    let mut g = Grid {
        nx,
        ny,
        lox: lox2,
        loy: loy2,
        sx,
        sy,
        start: vec![0; cells as usize + 1],
        items: Vec::new(),
    };
    // pass 1: counts
    for k in 0..2 {
        let p = polys[k];
        let mut c0 = g.cell(p[0]);
        for i in 0..ne[k].max(0) as usize {
            let c1 = g.cell(p[i + 1]);
            let (xlo, xhi, ylo, yhi) = g.rect(c0, c1);
            for x in xlo..=xhi {
                for y in ylo..=yhi {
                    g.start[(x + y * nx) as usize] += 1;
                }
            }
            c0 = c1;
        }
    }
    for i in 1..=cells as usize {
        g.start[i] += g.start[i - 1];
    }
    if (g.start[cells as usize - 1] as u32) > 0x2000 {
        return (0, PolyOut::Own(Vec::new()));
    }
    g.items = vec![0; g.start[cells as usize - 1].max(0) as usize];
    // pass 2: fill (from the end of each cell's range)
    for k in 0..2u32 {
        let p = polys[k as usize];
        let mut c0 = g.cell(p[0]);
        for i in 0..ne[k as usize].max(0) as usize {
            let c1 = g.cell(p[i + 1]);
            let (xlo, xhi, ylo, yhi) = g.rect(c0, c1);
            for x in xlo..=xhi {
                for y in ylo..=yhi {
                    let c = (x + y * nx) as usize;
                    g.start[c] -= 1;
                    g.items[g.start[c] as usize] = i as u32 | (k << 31);
                }
            }
            c0 = c1;
        }
    }
    // the polygon whose edges are walked
    let mut list: Vec<Hit> = Vec::new();
    let w: usize;
    if closed != 0 {
        let a = (((mx[1][0] - mn[1][0]) + mx[1][1]) - mn[1][1]) * n[0] as f32;
        let b = (((mx[0][0] - mn[0][0]) + mx[0][1]) - mn[0][1]) * ne[1] as f32;
        w = usize::from(neg(a - b));
    } else {
        w = 1;
        list.push(Hit {
            p: polys[1][0],
            e: [-1, 0],
        });
    }
    let o = w ^ 1;
    let pw = polys[w];
    let mut c0 = g.cell(pw[0]);
    for i in 0..ne[w].max(0) as usize {
        let c1 = g.cell(pw[i + 1]);
        let first = list.len();
        let (xlo, xhi, ylo, yhi) = g.rect(c0, c1);
        for x in xlo..=xhi {
            for y in ylo..=yhi {
                let c = (x + y * nx) as usize;
                for kk in g.start[c]..g.start[c + 1] {
                    let it = g.items[kk as usize];
                    if (it >> 31) as usize == w {
                        continue;
                    }
                    let e = (it & 0x7FFF_FFFF) as usize;
                    let mut out = [[0f32; 2]; 2];
                    let r = seg_isect(&pw[i..i + 2], &polys[o][e..e + 2], &mut out);
                    let (a0, a1) = (pw[i], pw[i + 1]);
                    let ddx = a1[0] - a0[0];
                    let ddy = a1[1] - a0[1];
                    for m in (0..r as usize).rev() {
                        let pt = out[m];
                        let t = (pt[1] - a0[1]) * ddy + (pt[0] - a0[0]) * ddx;
                        let tol = t * 1e-7;
                        let dup = list[first..].iter().any(|h| {
                            (((h.p[1] - a0[1]) * ddy + (h.p[0] - a0[0]) * ddx) - t).abs() <= tol
                        });
                        if dup {
                            continue;
                        }
                        if list.len() == 255 {
                            return (0, PolyOut::Own(Vec::new()));
                        }
                        let mut at = list.len();
                        while at > first {
                            let h = list[at - 1];
                            if ((h.p[1] - a0[1]) * ddy + (h.p[0] - a0[0]) * ddx) <= t {
                                break;
                            }
                            at -= 1;
                        }
                        let mut e2 = [0i32; 2];
                        e2[w] = i as i32;
                        e2[o] = e as i32;
                        list.insert(at, Hit { p: pt, e: e2 });
                    }
                }
            }
        }
        c0 = c1;
    }
    let nh;
    if closed == 0 {
        if list.len() == 255 {
            return (0, PolyOut::Own(Vec::new()));
        }
        let last = Hit {
            p: polys[1][ne[1] as usize],
            e: [-1, ne[1]],
        };
        list.push(last);
        nh = list.len();
        list.push(last);
    } else {
        nh = list.len();
        if nh > 0 {
            list.push(list[0]);
        } else {
            list.push(Hit::default());
        }
    }
    let ids = &mut scr.ids;
    if nh == 2 * (closed ^ 1) as usize {
        let c = g.cell(pw[0]);
        let inside = point_inside(&g, w as u32, c, polys[o], pw[0]);
        let mut sel = w;
        if closed != 0 && !inside {
            sel ^= 1;
        }
        // the counts are the inputs' again here (`v264` restored)
        let cnt = n[sel];
        let mut j = 0;
        while j < cnt {
            ids[j as usize] = (j + 1) << (16 * sel);
            j += 1;
        }
        let r = -(closed | i32::from(inside)) & j;
        return (
            r,
            if sel == 1 {
                PolyOut::Input1
            } else {
                PolyOut::Input0
            },
        );
    }
    let sel_dir = w as i32; // v288
    let mut out = Vec::new();
    let mut prev_in = false;
    let mut prev: i32 = -1;
    for k in 0..nh {
        let h = list[k];
        let pk = if prev < 0 { nh as i32 - 1 } else { prev } as usize;
        let ew = h.e[w];
        let nw = n[w];
        let ew1 = if ew + 1 - nw < 0 { ew + 1 } else { 0 };
        let pwp = polys[w];
        let d = [
            pwp[ew1 as usize][0] - pwp[ew as usize][0],
            pwp[ew1 as usize][1] - pwp[ew as usize][1],
        ];
        let eo = h.e[o];
        let po = polys[o];
        let sgn: bool = if eo < 0 {
            let c = g.cell(h.p);
            point_inside(&g, sel_dir as u32, c, po, h.p)
        } else {
            let no = n[o];
            let eo1 = if eo + 1 - no < 0 { eo + 1 } else { 0 };
            let v = (po[eo1 as usize][1] - po[eo as usize][1]) * d[0]
                - d[1] * (po[eo1 as usize][0] - po[eo as usize][0]);
            neg(v)
        };
        let tgt = (sel_dir as usize) ^ usize::from(sgn) ^ 1;
        if closed != 0 || sgn || prev_in {
            out.push(h.p);
            ids[out.len() - 1] = (h.e[0] + 1) | ((h.e[1] + 1) << 16);
            if out.len() >= 4096 {
                return (out.len() as i32, PolyOut::Own(out));
            }
        }
        if closed != 0 || sgn {
            let pv = polys[tgt];
            let nv = n[tgt];
            let mut e = h.e[tgt];
            let nxt = list[k + 1];
            let stop = nxt.e[tgt];
            let wrap = |e: i32| if nv - e - 2 >= 0 { e + 1 } else { 0 };
            let mut force = false;
            if e == stop {
                let y0 = pv[e as usize][1];
                let e1 = wrap(e);
                let ddy = pv[e1 as usize][1] - y0;
                let x0 = pv[e as usize][0];
                let ddx = pv[e1 as usize][0] - x0;
                force = ((h.p[1] - y0) * ddy + (h.p[0] - x0) * ddx)
                    > ((nxt.p[1] - y0) * ddy + (nxt.p[0] - x0) * ddx);
            }
            while force || (e != stop && (tgt == w || e != list[pk].e[tgt])) {
                force = false;
                out.push(pv[(e + 1) as usize]);
                ids[out.len() - 1] = (e + 2) << (16 * tgt);
                if out.len() >= 4096 {
                    return (out.len() as i32, PolyOut::Own(out));
                }
                e = wrap(e);
            }
        }
        prev = k as i32;
        prev_in = sgn;
    }
    (out.len() as i32, PolyOut::Own(out))
}

/// The 2D output lifted back into the frame: `p = (x·a0 + o) + y·a1` per component.
#[must_use]
pub fn lift(frame: &crate::geometry::shapes::protocol::ContactFrame, p: [f32; 2]) -> Vec3 {
    let (o, a0, a1) = (frame.origin, frame.axes[0], frame.axes[1]);
    Vec3::new(
        (p[0] * a0.x + o.x) + p[1] * a1.x,
        (p[0] * a0.y + o.y) + p[1] * a1.y,
        (p[0] * a0.z + o.z) + p[1] * a1.z,
    )
}
