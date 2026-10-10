//! The engine's incremental 3D convex hull (sub_35128440, also `IGeomManager` slot via
//! `CPhysicalWorld` 0x35096AA0): used by the OBB builder for large nodes and by the `CTriMesh`
//! constructor for the principal-axes fit.
//!
//! The original works on pointer-linked circular lists over static buffers (1024 point nodes of
//! 16 bytes, 1024 faces of 96 bytes, grown by 256) under a global spin lock; the port uses
//! index-linked lists with the same insertion / splice / unlink order, so the output triangle
//! order — which feeds the double-precision fit — is the original's.
//!
//! Face record (96 bytes): `+0 next, +8 prev, +16 point list, +24 normal (unnormalised),
//! +36 origin (the first vertex), +48 vertex ids[3], +64 neighbours[3] (neighbour k shares the
//! edge ids[k] → ids[k+1]), +88 deleted`.

use crate::math::Vec3;

const NONE: i32 = -1;
/// `-9.9999999e36f` (the "no point / no face yet" distance).
const NEG_HUGE: f32 = f32::from_bits(0xFCF0_BDC2);

#[derive(Clone, Copy, Debug)]
struct HFace {
    next: i32,
    prev: i32,
    pts: i32,
    n: Vec3,
    o: Vec3,
    idx: [i32; 3],
    nb: [i32; 3],
    deleted: i32,
}

impl Default for HFace {
    fn default() -> Self {
        Self {
            next: NONE,
            prev: NONE,
            pts: NONE,
            n: Vec3::ZERO,
            o: Vec3::ZERO,
            idx: [0; 3],
            nb: [NONE; 3],
            deleted: 0,
        }
    }
}

/// sub_3512BA60: `(d·n)·|d·n| > (tol·|tol|·|d|²)·|n|²` — "`d` is above the plane by more than
/// the angle `tol`" (single precision; `tol` is 0, 0.002, −0.001 or −0.002 at the call sites).
#[inline]
fn above(d: Vec3, n: Vec3, tol: f32) -> bool {
    let dot = (n.y * d.y + d.x * n.x) + n.z * d.z;
    let d2 = (d.y * d.y + d.x * d.x) + d.z * d.z;
    let n2 = (n.x * n.x + n.y * n.y) + n.z * n.z;
    dot.abs() * dot > ((tol.abs() * tol) * d2) * n2
}

#[inline]
fn isneg(x: f32) -> i32 {
    (x.to_bits() >> 31) as i32
}

struct Hull<'a> {
    p: &'a [Vec3],
    pn: Vec<i32>,
    pp: Vec<i32>,
    f: Vec<HFace>,
}

impl Hull<'_> {
    fn unlink_pt(&mut self, i: i32) {
        let (prev, next) = (self.pp[i as usize], self.pn[i as usize]);
        if prev != NONE {
            self.pn[prev as usize] = next;
        }
        if self.pn[i as usize] != NONE {
            self.pp[self.pn[i as usize] as usize] = self.pp[i as usize];
        }
        self.pn[i as usize] = NONE;
        self.pp[i as usize] = NONE;
    }

    /// Splices the circular point list `list` into the collector after its head.
    fn splice_pts(&mut self, coll: &mut i32, list: i32) {
        if list == NONE {
            return;
        }
        if *coll != NONE {
            let c = *coll as usize;
            let l = list as usize;
            let a = self.pn[c];
            self.pp[a as usize] = self.pp[l];
            let b = self.pp[l];
            self.pn[b as usize] = self.pn[c];
            self.pn[c] = list;
            self.pp[l] = *coll;
        } else {
            *coll = list;
        }
    }

    /// Unlinks a face from the face list (neighbours' links patched; its own left as is).
    fn unlink_face_keep(&mut self, i: i32) {
        let (prev, next) = (self.f[i as usize].prev, self.f[i as usize].next);
        if prev != NONE {
            self.f[prev as usize].next = next;
        }
        if self.f[i as usize].next != NONE {
            let nx = self.f[i as usize].next;
            self.f[nx as usize].prev = self.f[i as usize].prev;
        }
    }

    fn unlink_face(&mut self, i: i32) {
        self.unlink_face_keep(i);
        self.f[i as usize].next = NONE;
        self.f[i as usize].prev = NONE;
    }

    /// sub_351282C0: moves every point of the circular list starting at `start` to the first
    /// face of the list `flist` it is above (tolerance 0.002); unassigned points are dropped.
    fn assign(&mut self, start: i32, flist: i32) {
        if start == NONE {
            return;
        }
        let last = self.pp[start as usize];
        let mut i = start;
        loop {
            let nxt = self.pn[i as usize];
            self.unlink_pt(i);
            let pt = self.p[i as usize];
            let mut f = flist;
            loop {
                let fc = self.f[f as usize];
                if above(pt - fc.o, fc.n, 0.002) {
                    let h = fc.pts;
                    if h != NONE {
                        self.pn[i as usize] = self.pn[h as usize];
                        self.pp[i as usize] = h;
                        let hn = self.pn[h as usize];
                        self.pp[hn as usize] = i;
                        self.pn[h as usize] = i;
                    } else {
                        self.pp[i as usize] = i;
                        self.pn[i as usize] = i;
                        self.f[f as usize].pts = i;
                    }
                    break;
                }
                f = fc.next;
                if f == flist {
                    break;
                }
            }
            if i == last || nxt == NONE {
                break;
            }
            i = nxt;
        }
    }
}

/// sub_35128440: the convex hull of `points` as triangles of point indices (outward normals),
/// in the original's face-list order; `None` when the hull is degenerate (a face of the initial
/// tetrahedron with `|n|² < 1e-6`, or the list collapsing).
///
/// Algorithm (quickhull): the initial tetrahedron is the first point of minimum x, the farthest
/// point from it, the farthest from that line and the farthest (|distance|) from that plane;
/// points are assigned to faces; then repeatedly, for the next face (in list order, at most
/// `2·n²` steps) with points: take its farthest point, find the face it is farthest above, flood
/// the visible region through neighbours, remove further faces it sees (tolerance −0.001, plus
/// a 64-face fan walk at −0.002 around faces with one deleted neighbour), cone the horizon,
/// link the new faces by a sorted search on vertex ids, and redistribute the orphaned points.
#[must_use]
pub fn convex_hull(points: &[Vec3]) -> Option<Vec<[i32; 3]>> {
    let n = points.len() as i32;
    if n < 1 {
        return None;
    }
    let p = points;
    let pt = |i: i32| p[i as usize];

    // First point of minimum x (strict, by the sign of the difference).
    let mut v41 = 0i32;
    for i in 1..n {
        v41 += (i - v41) & -isneg(pt(i).x - pt(v41).x);
    }
    let c = pt(v41);
    let d2 = |q: Vec3| {
        let (dx, dy, dz) = (q.x - c.x, q.y - c.y, q.z - c.z);
        (dy * dy + dx * dx) + dz * dz
    };
    let mut v59 = 0i32;
    for i in 1..n {
        v59 += (i - v59) & -isneg(d2(pt(v59)) - d2(pt(i)));
    }
    let e = pt(v59) - c;
    let line2 = |q: Vec3| {
        let (dx, dy, dz) = (q.x - c.x, q.y - c.y, q.z - c.z);
        let cx = dz * e.y - dy * e.z;
        let cy = dx * e.z - e.x * dz;
        let cz = e.x * dy - dx * e.y;
        (cy * cy + cx * cx) + cz * cz
    };
    let mut v94 = 0i32;
    for i in 1..n {
        v94 += (i - v94) & -isneg(line2(pt(v94)) - line2(pt(i)));
    }
    let ee = pt(v59) - c;
    let ff = pt(v94) - c;
    let nx = ee.y * ff.z - ee.z * ff.y;
    let ny = ff.x * ee.z - ee.x * ff.z;
    let nz = ee.x * ff.y - ff.x * ee.y;
    let pdist = |q: Vec3| ((q.y - c.y) * ny + (q.x - c.x) * nx) + (q.z - c.z) * nz;
    let mut v171 = 0i32;
    for i in 1..n {
        v171 += (i - v171) & -isneg(pdist(pt(v171)).abs() - pdist(pt(i)).abs());
    }
    let mut tet = [v41, v59, v94, v171];
    if pdist(pt(v171)) > 0.0 {
        tet[1] = v94;
        tet[2] = v59;
    }

    let mut h = Hull {
        p,
        pn: (0..n).map(|i| if i + 1 < n { i + 1 } else { 0 }).collect(),
        pp: (0..n).map(|i| if i > 0 { i - 1 } else { n - 1 }).collect(),
        f: Vec::with_capacity(1024),
    };
    for &t in &tet {
        h.unlink_pt(t);
    }

    // Initial faces: face k omits tetrahedron vertex k.
    let mut head = NONE;
    for k in 0..4usize {
        let mut fc = HFace::default();
        let mut m = 0;
        for (j, &t) in tet.iter().enumerate() {
            if j != k {
                fc.idx[m] = t;
                m += 1;
            }
        }
        let (p0, p1, p2) = (pt(fc.idx[0]), pt(fc.idx[1]), pt(fc.idx[2]));
        let a = p1 - p0;
        let b = p2 - p0;
        fc.n = Vec3::new(
            a.y * b.z - a.z * b.y,
            b.x * a.z - a.x * b.z,
            a.x * b.y - b.x * a.y,
        );
        fc.o = p0;
        if above(pt(tet[k]) - fc.o, fc.n, 0.0) {
            fc.idx.swap(0, 2);
            fc.n = Vec3::new(-fc.n.x, -fc.n.y, -fc.n.z);
        }
        let id = h.f.len() as i32;
        h.f.push(fc);
        if head != NONE {
            let hn = h.f[head as usize].next;
            h.f[id as usize].prev = head;
            h.f[id as usize].next = hn;
            h.f[hn as usize].prev = id;
            h.f[head as usize].next = id;
        } else {
            head = id;
            h.f[id as usize].prev = id;
            h.f[id as usize].next = id;
        }
    }
    for i in 0..4usize {
        for j in 0..4usize {
            if j == i {
                continue;
            }
            for k in 0..3usize {
                for l in 0..3usize {
                    let fi = h.f[i].idx;
                    let fj = h.f[j].idx;
                    if fi[k] == fj[l] {
                        let k1 = if k == 2 { 0 } else { k + 1 };
                        let l1 = if l == 0 { 2 } else { l - 1 };
                        if fi[k1] == fj[l1] {
                            h.f[i].nb[k] = j as i32;
                            break;
                        }
                    }
                }
            }
        }
    }
    for fc in &h.f {
        if (fc.n.x * fc.n.x + fc.n.y * fc.n.y) + fc.n.z * fc.n.z < 0.000_001 {
            return None;
        }
    }
    // Remaining points onto the initial faces.
    if let Some(first) = (0..n).find(|&i| h.pn[i as usize] != NONE) {
        h.assign(first, head);
    }

    let mut v236 = head;
    let cap = 2 * n * n;
    let mut steps = 0i32;
    let mut cur = v236;
    let mut v419 = NONE;
    'outer: loop {
        let fpts = h.f[cur as usize].pts;
        let mut v264 = h.f[cur as usize].next;
        if fpts != NONE {
            // (a) the eye point: max n·p in the face's list.
            let fc = h.f[cur as usize];
            let mut best = NEG_HUGE;
            let mut eye = NONE;
            let mut q = fpts;
            loop {
                let pq = pt(q);
                let d = (fc.n.y * pq.y + fc.n.x * pq.x) + fc.n.z * pq.z;
                if d > best {
                    best = d;
                    eye = q;
                }
                q = h.pn[q as usize];
                if q == fpts {
                    break;
                }
            }
            if eye == NONE {
                eye = fpts;
            }
            // (b) remove it from the list.
            if fpts == eye {
                h.f[cur as usize].pts = h.pn[eye as usize];
            }
            h.unlink_pt(eye);
            let mut rest = h.f[cur as usize].pts;
            if rest == eye {
                rest = NONE;
            }
            h.f[cur as usize].pts = rest;
            let ep = pt(eye);

            // (d) the face the eye is farthest above.
            let mut t = v236;
            let last = h.f[v236 as usize].prev;
            let mut far = NEG_HUGE;
            let mut v395;
            loop {
                let ft = h.f[t as usize];
                v395 = ft.next;
                let d = ((ep.y - ft.o.y) * ft.n.y + (ep.x - ft.o.x) * ft.n.x)
                    + (ep.z - ft.o.z) * ft.n.z;
                if d > far {
                    v419 = t;
                    far = d;
                }
                if t == last {
                    break;
                }
                t = ft.next;
            }
            let start = v419;
            // (e) start the visible list; the orphan collector starts with its points.
            let mut coll = h.f[start as usize].pts;
            if start == v236 {
                v236 = h.f[start as usize].next;
            }
            if start == v395 {
                v395 = h.f[start as usize].next;
            }
            h.unlink_face_keep(start);
            h.f[start as usize].deleted = 1;
            h.f[start as usize].prev = start;
            h.f[start as usize].next = start;
            // (f) flood the visible region through neighbours (tolerance 0).
            let mut v288 = start;
            loop {
                for k in 0..3 {
                    let nbf = h.f[v288 as usize].nb[k];
                    if nbf != NONE && h.f[nbf as usize].deleted == 0 {
                        let fb = h.f[nbf as usize];
                        if above(ep - fb.o, fb.n, 0.0) {
                            let pl = fb.pts;
                            h.splice_pts(&mut coll, pl);
                            if nbf == v236 {
                                v236 = h.f[nbf as usize].next;
                            }
                            if nbf == v395 {
                                v395 = h.f[nbf as usize].next;
                            }
                            h.unlink_face(nbf);
                            h.f[nbf as usize].deleted = 1;
                            let nx = h.f[v288 as usize].next;
                            h.f[nbf as usize].next = nx;
                            h.f[nbf as usize].prev = v288;
                            h.f[nx as usize].prev = nbf;
                            h.f[v288 as usize].next = nbf;
                        }
                    }
                }
                v288 = h.f[v288 as usize].next;
                if v288 == v419 {
                    break;
                }
            }
            let _ = v395;
            // (g) delete further faces the eye sees.
            let mut v398 = v236;
            loop {
                if v236 == NONE {
                    return None;
                }
                let mut v313 = h.f[v236 as usize].prev;
                let mut v314 = v236;
                let mut v396 = v313;
                let mut changed = 0;
                loop {
                    v264 = h.f[v314 as usize].next;
                    let fs = h.f[v314 as usize];
                    let mut deleted_here = false;
                    if above(ep - fs.o, fs.n, -0.001) {
                        let nd: i32 = fs.nb.iter().map(|&b| h.f[b as usize].deleted).sum();
                        if nd >= 2 {
                            let pl = fs.pts;
                            if v314 == v313 {
                                v313 = v264;
                            }
                            v396 = v313;
                            h.splice_pts(&mut coll, pl);
                            if v314 == v236 {
                                v236 = fs.next;
                                v398 = fs.next;
                            }
                            if v314 == v264 {
                                v264 = fs.next;
                            }
                            h.unlink_face(v314);
                            h.f[v314 as usize].deleted = 1;
                            changed = 1;
                            deleted_here = true;
                        } else if nd == 1 {
                            // Fan walk around the vertices of the edge shared with the deleted
                            // neighbour, both directions.
                            let m = fs
                                .nb
                                .iter()
                                .position(|&b| h.f[b as usize].deleted != 0)
                                .unwrap_or(3);
                            for dir in [-1i32, 1] {
                                let mut face = v314;
                                let mut edge = m as i32;
                                let mut stack: Vec<i32> = Vec::with_capacity(64);
                                let mut reached = false;
                                loop {
                                    let mut e2 = edge + dir;
                                    if e2 > 2 {
                                        e2 -= 3;
                                    }
                                    if e2 < 0 {
                                        e2 += 3;
                                    }
                                    let nxt = h.f[face as usize].nb[e2 as usize];
                                    let fnb = h.f[nxt as usize].nb;
                                    edge =
                                        fnb.iter().position(|&b| b == face).map_or(3, |x| x as i32);
                                    stack.push(face);
                                    face = nxt;
                                    let fn_ = h.f[nxt as usize];
                                    if !above(ep - fn_.o, fn_.n, -0.002) {
                                        break;
                                    }
                                    if fn_.deleted != 0 {
                                        reached = true;
                                        break;
                                    }
                                    if stack.len() == 64 {
                                        break;
                                    }
                                }
                                if !reached || m == 3 || face == fs.nb[m] {
                                    continue;
                                }
                                for &sf in stack.iter().rev() {
                                    if sf == v396 {
                                        v396 = h.f[sf as usize].next;
                                    }
                                    let pl = h.f[sf as usize].pts;
                                    h.splice_pts(&mut coll, pl);
                                    if sf == v398 {
                                        v398 = h.f[sf as usize].next;
                                    }
                                    if sf == v264 {
                                        v264 = h.f[sf as usize].next;
                                    }
                                    h.unlink_face(sf);
                                    h.f[sf as usize].deleted = 1;
                                }
                                changed = 1;
                            }
                            v236 = v398;
                            v313 = v396;
                        }
                    }
                    let _ = deleted_here;
                    if v314 == v313 {
                        break;
                    }
                    v314 = v264;
                    if v264 == NONE {
                        break;
                    }
                }
                if changed == 0 {
                    break;
                }
                if v236 == NONE {
                    return None;
                }
            }
            if v236 == NONE || h.f[v236 as usize].deleted != 0 {
                return None;
            }
            // (i) cone the horizon from the eye.
            let first_new = h.f.len() as i32;
            let mut v345 = NONE;
            let mut v346 = v236;
            loop {
                for k in 0..3usize {
                    let nb = h.f[v346 as usize].nb[k];
                    if h.f[nb as usize].deleted != 0 {
                        let k1 = if k == 2 { 0 } else { k + 1 };
                        let mut nf = HFace {
                            idx: [eye, h.f[v346 as usize].idx[k1], h.f[v346 as usize].idx[k]],
                            ..HFace::default()
                        };
                        let (p0, p1, p2) = (pt(nf.idx[0]), pt(nf.idx[1]), pt(nf.idx[2]));
                        let a = p1 - p0;
                        let b = p2 - p0;
                        nf.n = Vec3::new(
                            a.y * b.z - a.z * b.y,
                            a.z * b.x - b.z * a.x,
                            b.y * a.x - a.y * b.x,
                        );
                        nf.o = p0;
                        nf.nb = [NONE, v346, NONE];
                        let id = h.f.len() as i32;
                        h.f.push(nf);
                        h.f[v346 as usize].nb[k] = id;
                        if v345 != NONE {
                            let hn = h.f[v345 as usize].next;
                            h.f[id as usize].prev = v345;
                            h.f[id as usize].next = hn;
                            h.f[hn as usize].prev = id;
                            h.f[v345 as usize].next = id;
                        } else {
                            h.f[id as usize].prev = id;
                            h.f[id as usize].next = id;
                            v345 = id;
                        }
                    }
                }
                v346 = h.f[v346 as usize].next;
                if v346 == v236 {
                    break;
                }
            }
            // (j) link the new faces: neighbour 0 = the new face whose ids[2] == my ids[1].
            let count = h.f.len() as i32 - first_new;
            let mut keys: Vec<i32> = (first_new..h.f.len() as i32)
                .map(|i| h.f[i as usize].idx[2])
                .collect();
            let mut vals: Vec<i32> = (first_new..h.f.len() as i32).collect();
            qsort(&mut keys, &mut vals, 0, count - 1);
            for fid in first_new..h.f.len() as i32 {
                let key = h.f[fid as usize].idx[1];
                let (mut lo, mut hi) = (0i32, count);
                loop {
                    let mid = (hi + lo) >> 1;
                    let kv = keys[mid as usize];
                    if kv == key {
                        lo = mid;
                        break;
                    }
                    if kv >= key {
                        hi = mid;
                    } else {
                        lo = mid;
                    }
                    if lo >= hi - 1 {
                        break;
                    }
                }
                let other = vals[lo as usize];
                h.f[fid as usize].nb[0] = other;
                h.f[other as usize].nb[2] = fid;
            }
            // (k) stop if a new face is left unlinked.
            if count > 0
                && (first_new..h.f.len() as i32)
                    .any(|i| h.f[i as usize].nb[0] == NONE || h.f[i as usize].nb[2] == NONE)
            {
                break 'outer;
            }
            // (l) redistribute the orphans onto the new faces, then merge the lists.
            h.assign(coll, v345);
            if v345 != NONE {
                if v264 != NONE {
                    let hn = h.f[v264 as usize].next;
                    h.f[hn as usize].prev = h.f[v345 as usize].prev;
                    let lp = h.f[v345 as usize].prev;
                    h.f[lp as usize].next = h.f[v264 as usize].next;
                    h.f[v264 as usize].next = v345;
                    h.f[v345 as usize].prev = v264;
                } else {
                    v264 = v345;
                }
            }
        } else if v264 == v236 {
            break;
        }
        cur = v264;
        steps += 1;
        if steps >= cap || cur == NONE {
            break;
        }
    }
    if v236 == NONE {
        return None;
    }
    let mut out = Vec::new();
    let mut f = v236;
    loop {
        out.push(h.f[f as usize].idx);
        f = h.f[f as usize].next;
        if f == v236 || f == NONE {
            break;
        }
    }
    Some(out)
}

/// sub_35128150: the K&R quicksort (middle pivot moved to the front) on `keys` with parallel
/// `vals`.
fn qsort(keys: &mut [i32], vals: &mut [i32], lo: i32, hi: i32) {
    let mut lo = lo;
    while lo < hi {
        let mid = (lo + hi) >> 1;
        keys.swap(lo as usize, mid as usize);
        vals.swap(lo as usize, mid as usize);
        let mut last = lo;
        for i in lo + 1..=hi {
            if keys[i as usize] < keys[lo as usize] {
                last += 1;
                keys.swap(last as usize, i as usize);
                vals.swap(last as usize, i as usize);
            }
        }
        keys.swap(lo as usize, last as usize);
        vals.swap(lo as usize, last as usize);
        qsort(keys, vals, lo, last - 1);
        lo = last + 1;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn volume_sign_ok(pts: &[Vec3], tris: &[[i32; 3]]) -> bool {
        let c = pts.iter().fold(Vec3::ZERO, |a, &b| a + b) * (1.0 / pts.len() as f32);
        tris.iter().all(|t| {
            let (a, b, cc) = (pts[t[0] as usize], pts[t[1] as usize], pts[t[2] as usize]);
            let n = (b - a).cross(cc - a);
            n.dot(c - a) < 0.0
        })
    }

    #[test]
    fn cube_hull() {
        let mut pts = Vec::new();
        for i in 0..8 {
            pts.push(Vec3::new(
                (i & 1) as f32,
                ((i >> 1) & 1) as f32,
                ((i >> 2) & 1) as f32,
            ));
        }
        pts.push(Vec3::new(0.5, 0.5, 0.5)); // interior
        let tris = convex_hull(&pts).expect("hull");
        assert_eq!(tris.len(), 12);
        assert!(volume_sign_ok(&pts, &tris));
        assert!(tris.iter().all(|t| t.iter().all(|&i| i != 8)));
    }

    #[test]
    fn sphere_points_hull() {
        let mut pts = Vec::new();
        let mut s = 12345u32;
        let mut rnd = || {
            s = s.wrapping_mul(1_103_515_245).wrapping_add(12345);
            ((s >> 8) & 0xFFFF) as f32 / 65535.0 * 2.0 - 1.0
        };
        for _ in 0..300 {
            let v = Vec3::new(rnd(), rnd(), rnd());
            pts.push(v);
        }
        let tris = convex_hull(&pts).expect("hull");
        assert!(tris.len() >= 4);
        assert!(volume_sign_ok(&pts, &tris));
        // Every point is inside or on the hull.
        for (i, q) in pts.iter().enumerate() {
            for t in &tris {
                let (a, b, c) = (pts[t[0] as usize], pts[t[1] as usize], pts[t[2] as usize]);
                let n = (b - a).cross(c - a);
                assert!(n.dot(*q - a) <= 1e-3 * n.len(), "point {i} outside");
            }
        }
    }

    #[test]
    fn flat_points_are_degenerate() {
        let pts: Vec<Vec3> = (0..10)
            .map(|i| Vec3::new(i as f32, (i * i) as f32, 0.0))
            .collect();
        assert!(convex_hull(&pts).is_none());
    }

    #[test]
    fn quicksort_sorts() {
        let mut k = vec![5, 3, 9, 1, 7, 3];
        let mut v = vec![0, 1, 2, 3, 4, 5];
        let n = k.len() as i32;
        qsort(&mut k, &mut v, 0, n - 1);
        assert_eq!(k, vec![1, 3, 3, 5, 7, 9]);
    }
}
