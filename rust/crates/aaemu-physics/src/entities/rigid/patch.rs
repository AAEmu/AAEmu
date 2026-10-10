//! Rigid-entity contact-patch fit, called by `CRigidEntity::RegisterContacts` (0x350BDBA0):
//!
//! * [`fit_contact_patch`]: `sub_350AF550`, which fits a plane through the contacts of one collider,
//!   builds their 2D convex hull, optionally reduces it, links the hull contacts through `+16`,
//!   and tests whether the centre of mass, projected along gravity, lies inside the hull;
//! * [`convex_hull_2d`]: `sub_3512AD60`, the 2D quickhull;
//! * [`sort_hull_nodes`]: `sub_350AA280`, the quicksort of the hull nodes by corner cross product;
//! * [`reduce_hull`]: the inlined vertex-reduction loop of `sub_350AF550`
//!   (0x350AFC97 … 0x350B0213).
//!
//! The original works on two stack arrays: 32-byte point records (`x`, `y`, list `prev` `+8`,
//! list `next` `+16`, contact index `+24`) and 64-byte hull nodes (point `+0`, outside-point
//! list `+8`, ring `next` `+16`, ring `prev` `+24`, cross `+32`, length product `+36`, sorted
//! `next` `+40`, sorted `prev` `+48`, sort index `+56`). The port keeps them as [`HullPt`] and
//! [`HullNode`] with pointers replaced by array indices ([`NIL`] for null), and repeats every link
//! write in the original order, so degenerate inputs end up with the same links.
//!
//! The mod-3 tables read by `sub_350AF550` are `inc = {1,2,0}` at 0x352097A0 and
//! `dec = {2,0,1}` at 0x352097B0. The decompile prints them as 0x35209BA0/0x35209BB0, but those
//! hold text. They are addressed RIP-relative from the image base, `lea rsi, [rip-0xAF8BF]`.
//! The port uses [`INC_MOD3`] and [`DEC_MOD3`].
//!
//! **The `+16` field.** Each hull contact gets the next hull contact (ring order) written to its
//! `+16` field. [`Contact::unk16`](crate::solver::Contact) is a `u64`, so the port stores
//! [`ContactId::0`](ContactId) there. Use [`patch_next`] and [`set_patch_next`] to read and write it.

// `!(a > b)` spells out the NaN behaviour of the original's `comiss` branches.
#![allow(clippy::neg_cmp_op_on_partial_ord)]

use crate::collision::swept::common::normalize_in_place;
use crate::geometry::shapes::{DEC_MOD3, INC_MOD3};
use crate::math::{isneg, max_idiom, Vec3};
use crate::solver::contact::flags;
use crate::solver::{Contact, ContactId, ContactPool};

/// Null link in [`HullPt`] / [`HullNode`].
pub(crate) const NIL: u32 = u32::MAX;

/// At most this many contacts take part in a fit (the `v184[256]` buffer).
pub const MAX_PATCH_CONTACTS: usize = 256;

/// `0.0225` (0x3CB851EC at 0x351F17DC): `sin² 0.15` threshold of the hull reduction.
const REDUCE_SIN2: f32 = f32::from_bits(0x3CB8_51EC);
/// `0.81` as stored (0x3F4F5C28 at 0x351EDBB0).
const AXIS_COS2: f32 = f32::from_bits(0x3F4F_5C28);
/// `0.001` (0x3A83126F at 0x351EDB8C).
const EPS_REL: f32 = f32::from_bits(0x3A83_126F);

/// A 32-byte point record of `sub_3512AD60` (in-plane coordinates of one contact).
#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) struct HullPt {
    /// `+0`, `+4`.
    pub x: f32,
    pub y: f32,
    /// `+8` previous / `+16` next in the outside-point list of a hull edge.
    pub prev: u32,
    pub next: u32,
    /// `+24` index of the contact (into the collected contact array).
    pub idx: i32,
}

impl HullPt {
    #[must_use]
    pub fn new(x: f32, y: f32, idx: i32) -> Self {
        Self {
            x,
            y,
            prev: NIL,
            next: NIL,
            idx,
        }
    }
}

/// A 64-byte hull node (hull vertex; the edge from it to `next`).
#[derive(Clone, Copy, Debug, PartialEq)]
pub(crate) struct HullNode {
    /// `+0` the vertex (index into the point array).
    pub pt: u32,
    /// `+8` head of the list of points outside the edge `pt → next.pt` ([`NIL`] when empty).
    pub points: u32,
    /// `+16` / `+24` hull ring.
    pub next: u32,
    pub prev: u32,
    /// `+32` corner cross product, `+36` product of the squared edge lengths (reduction only).
    pub cross: f32,
    pub len: f32,
    /// `+40` / `+48` list sorted by `cross` (reduction only).
    pub snext: u32,
    pub sprev: u32,
    /// `+56` sort permutation slot (reduction only).
    pub idx: i32,
}

impl HullNode {
    fn new(pt: u32) -> Self {
        Self {
            pt,
            points: NIL,
            next: NIL,
            prev: NIL,
            cross: 0.0,
            len: 0.0,
            snext: NIL,
            sprev: NIL,
            idx: 0,
        }
    }
}

/// Insert point `p` at the tail of the circular list whose head is stored in `head`. This is the
/// insertion idiom used throughout `sub_3512AD60`: `head.prev.next = p; p.prev = head.prev;
/// head.prev = p; p.next = head`, or a self-linked `p` that becomes the head.
#[inline]
fn list_push(pts: &mut [HullPt], head: &mut u32, p: u32) {
    if *head != NIL {
        let h = *head as usize;
        let t = pts[h].prev as usize;
        pts[t].next = p;
        pts[p as usize].prev = pts[h].prev;
        pts[h].prev = p;
        pts[p as usize].next = *head;
    } else {
        pts[p as usize].next = p;
        pts[p as usize].prev = p;
        *head = p;
    }
}

/// `(a.y − p.y)·(b.x − p.x) − (b.y − p.y)·(a.x − p.x)`: the edge-visibility test of the hull walks.
#[inline]
fn side(p: &HullPt, a: &HullPt, b: &HullPt) -> f32 {
    (a.y - p.y) * (b.x - p.x) - (b.y - p.y) * (a.x - p.x)
}

/// `sub_3512AD60` (0x3512AD60, 3326 bytes): 2D convex hull (quickhull) of `pts`. `pts` holds
/// `x`, `y` and `idx`, and its list links are used as scratch. The hull nodes are written to
/// `nodes`, which is cleared first. Returns the number of nodes created.
///
/// Algorithm, with the original's tie rules:
/// 1. `i0` is the point with the largest `y` (first on ties). `i1` is the point farthest from it,
///    `|d|² = dy² + dx²` (first on ties, starting from point 0). Nodes 0 (`i0`) and 1 (`i1`) form a
///    two-node ring.
/// 2. Every other point goes to the outside list of node 0 when
///    `e.y·(p.x − p0.x) − (p.y − p0.y)·e.x` has a clear sign bit, else to node 1's list. A `-0.0`
///    result goes to node 1.
/// 3. Repeat while some node (scanned from 0) has outside points. Take the point of that list
///    (walked through `prev`) farthest from the node's edge. Walk back over ring edges that see
///    the point (`side > 0`) and forward over the same, then remove the point from its list.
///    Collect the outside points of the nodes from the back-walk start up to, but excluding, the
///    forward-walk end. Insert a new node for the point. Hand the collected points to whichever
///    new edge (`a → new` or `new → b`) has the larger
///    `side · |other edge|²`. A point goes there if that value is not `<= 0`, so NaN is kept;
///    otherwise it is dropped.
///
/// The return value counts **created** nodes. In exact arithmetic the walks never move, so every
/// node stays on the ring. With float noise a walk can skip nodes. Those nodes leave the ring but
/// are still counted and still sit in `nodes`. Callers (as in the original) then iterate over
/// nodes that are no longer on the ring. This is mirrored.
///
/// The original reads point 0 even for an empty input. The port requires `pts.len() >= 1`, and
/// `sub_350AF550` always passes at least 3.
///
/// # Panics
/// If `pts` is empty.
pub(crate) fn convex_hull_2d(pts: &mut [HullPt], nodes: &mut Vec<HullNode>) -> i32 {
    assert!(!pts.is_empty(), "sub_3512AD60 needs at least one point");
    let n = pts.len() as i32;

    // 1a. largest y (branch-free select on the sign of cur.y − p.y)
    let mut i0: i32 = 0;
    for k in 0..n {
        let d = pts[i0 as usize].y - pts[k as usize].y;
        if isneg(d) != 0 {
            i0 = k;
        }
    }
    // 1b. farthest point from p0
    let p0 = pts[i0 as usize];
    let d2 = |p: &HullPt| (p.y - p0.y) * (p.y - p0.y) + (p.x - p0.x) * (p.x - p0.x);
    let mut i1: i32 = 0;
    for k in 1..n {
        let d = d2(&pts[i1 as usize]) - d2(&pts[k as usize]);
        if isneg(d) != 0 {
            i1 = k;
        }
    }

    nodes.clear();
    let mut a = HullNode::new(i0 as u32);
    a.next = 1;
    a.prev = 1;
    let mut b = HullNode::new(i1 as u32);
    b.next = 0;
    b.prev = 0;
    nodes.push(a);
    nodes.push(b);

    // 2. initial partition
    let p1 = pts[i1 as usize];
    let ex = p1.x - p0.x;
    let ey = p1.y - p0.y;
    for k in 0..n {
        if (k - i0).wrapping_mul(k - i1) == 0 {
            continue;
        }
        let p = pts[k as usize];
        let c = ey * (p.x - p0.x) - (p.y - p0.y) * ex;
        let s = isneg(c) as usize;
        let mut head = nodes[s].points;
        list_push(pts, &mut head, k as u32);
        nodes[s].points = head;
    }

    // 3. expansion
    let mut cnt: i32 = 2;
    loop {
        let mut j: i32 = 0;
        while j < cnt && nodes[j as usize].points == NIL {
            j += 1;
        }
        if j == cnt {
            break;
        }
        let orig = j as u32;
        let mut a = orig;

        // farthest outside point of edge orig → orig.next
        let head = nodes[orig as usize].points;
        let q0 = pts[nodes[orig as usize].pt as usize];
        let qn = pts[nodes[nodes[orig as usize].next as usize].pt as usize];
        let (x0, y0) = (q0.x, q0.y);
        let ex = qn.x - x0;
        let ey = qn.y - y0;
        let mut best = head;
        let mut q = head;
        loop {
            let cur = q;
            q = pts[q as usize].prev;
            let pb = pts[best as usize];
            let pc = pts[cur as usize];
            let cb = ey * (pb.x - x0) - (pb.y - y0) * ex;
            let cc = ey * (pc.x - x0) - (pc.y - y0) * ex;
            if isneg(cb - cc) != 0 {
                best = cur;
            }
            if q == head {
                break;
            }
        }
        let pb = pts[best as usize];

        // walk back over edges that see the point
        let mut pv = nodes[a as usize].prev;
        while pv != orig {
            let c = pts[nodes[a as usize].pt as usize];
            let p = pts[nodes[nodes[a as usize].prev as usize].pt as usize];
            if !(side(&p, &c, &pb) > 0.0) {
                break;
            }
            a = pv;
            pv = nodes[pv as usize].prev;
        }
        // walk forward
        let mut bn = nodes[orig as usize].next;
        if bn != orig {
            loop {
                let nx = nodes[bn as usize].next;
                let pa = pts[nodes[bn as usize].pt as usize];
                let pd = pts[nodes[nx as usize].pt as usize];
                if !(side(&pa, &pd, &pb) > 0.0) {
                    break;
                }
                bn = nx;
                if nx == orig {
                    break;
                }
            }
        }

        // unlink the point from its list
        {
            let bp = pts[best as usize].prev;
            let bx = pts[best as usize].next;
            pts[bp as usize].next = bx;
            let bx = pts[best as usize].next;
            let bp = pts[best as usize].prev;
            pts[bx as usize].prev = bp;
        }
        if nodes[orig as usize].points == best {
            let bp = pts[best as usize].prev;
            nodes[orig as usize].points = if bp != best { bp } else { NIL };
        }

        // collect the outside points of the nodes a .. bn (exclusive)
        let mut gathered = NIL;
        if a != bn {
            let mut d = a;
            let mut found = true;
            while nodes[d as usize].points == NIL {
                d = nodes[d as usize].next;
                if d == bn {
                    found = false;
                    break;
                }
            }
            if found && d != bn {
                gathered = nodes[d as usize].points;
                nodes[d as usize].points = NIL;
                let mut k = nodes[d as usize].next;
                while k != bn {
                    let kp = nodes[k as usize].points;
                    if kp != NIL {
                        let g = gathered as usize;
                        let tail = pts[g].prev as usize;
                        pts[tail].next = pts[kp as usize].next;
                        let kn = pts[kp as usize].next as usize;
                        pts[kn].prev = pts[g].prev;
                        pts[g].prev = kp;
                        pts[kp as usize].next = gathered;
                        nodes[k as usize].points = NIL;
                    }
                    k = nodes[k as usize].next;
                }
            }
        }

        // the new node between a and bn
        let nw = cnt as u32;
        debug_assert_eq!(nodes.len(), cnt as usize);
        nodes.push(HullNode::new(NIL));
        nodes[a as usize].next = nw;
        nodes[nw as usize].prev = a;
        nodes[bn as usize].prev = nw;
        nodes[nw as usize].next = bn;
        nodes[nw as usize].pt = best;
        nodes[nw as usize].points = NIL;
        nodes[a as usize].points = NIL;

        // re-partition the collected points between a → nw and nw → nw.next
        if gathered != NIL {
            let pa = pts[nodes[a as usize].pt as usize];
            let pm = pts[nodes[nodes[a as usize].next as usize].pt as usize];
            let e0x = pm.x - pa.x;
            let e0y = pm.y - pa.y;
            let pw = pts[nodes[nw as usize].pt as usize];
            let pz = pts[nodes[nodes[nw as usize].next as usize].pt as usize];
            let e1x = pz.x - pw.x;
            let e1y = pz.y - pw.y;
            let l0 = e0x * e0x + e0y * e0y;
            let l1 = e1x * e1x + e1y * e1y;
            let targets = [a, nw];
            let mut q = gathered;
            loop {
                let saved = pts[q as usize].prev;
                let p = pts[q as usize];
                let pa = pts[nodes[a as usize].pt as usize];
                let pw = pts[nodes[nw as usize].pt as usize];
                let c0 = (e0y * (p.x - pa.x) - (p.y - pa.y) * e0x) * l1;
                let c1 = (e1y * (p.x - pw.x) - (p.y - pw.y) * e1x) * l0;
                let s = isneg(c0 - c1) as usize;
                let v = [c0, c1][s];
                // `comiss 0, v; jae skip`: NaN is inserted
                if !(0.0 >= v) {
                    let t = targets[s] as usize;
                    let mut head = nodes[t].points;
                    list_push(pts, &mut head, q);
                    nodes[t].points = head;
                }
                q = saved;
                if q == gathered {
                    break;
                }
            }
        }
        cnt += 1;
    }
    cnt
}

/// `sub_350AA280` (0x350AA280, 580 bytes): recursive quicksort of the `idx` permutation in
/// `nodes[left..=right]`, keyed on `nodes[idx].cross` ascending. This is the K&R scheme. The
/// middle element is swapped to the front as the pivot (`(left + right) >> 1`). Elements whose
/// key is strictly smaller than the pivot's (ordered compare, so NaN never moves) go to the
/// front. The left part is recursed into and the right part is handled by the loop.
pub(crate) fn sort_hull_nodes(nodes: &mut [HullNode], mut left: i32, right: i32) {
    let key = |nodes: &[HullNode], i: i32| nodes[nodes[i as usize].idx as usize].cross;
    let swap = |nodes: &mut [HullNode], i: i32, j: i32| {
        let t = nodes[i as usize].idx;
        nodes[i as usize].idx = nodes[j as usize].idx;
        nodes[j as usize].idx = t;
    };
    while left < right {
        swap(nodes, left, (left + right) >> 1);
        let mut last = left;
        for i in left + 1..=right {
            if key(nodes, left) > key(nodes, i) {
                last += 1;
                swap(nodes, last, i);
            }
        }
        swap(nodes, left, last);
        sort_hull_nodes(nodes, left, last - 1);
        left = last + 1;
    }
}

/// Corner terms of a hull node from its ring neighbours: `cross = dpy·dnx − dny·dpx` and
/// `len = (dpx² + dpy²)·(dny² + dnx²)`, with `dp = prev − p` and `dn = next − p`.
#[inline]
fn corner(pts: &[HullPt], nodes: &mut [HullNode], i: u32) {
    let nd = nodes[i as usize];
    let p = pts[nd.pt as usize];
    let pp = pts[nodes[nd.prev as usize].pt as usize];
    let pn = pts[nodes[nd.next as usize].pt as usize];
    let dpx = pp.x - p.x;
    let dpy = pp.y - p.y;
    let dny = pn.y - p.y;
    let dnx = pn.x - p.x;
    nodes[i as usize].cross = dpy * dnx - dny * dpx;
    nodes[i as usize].len = (dpx * dpx + dpy * dpy) * (dny * dny + dnx * dnx);
}

/// The hull reduction inlined in `sub_350AF550` (0x350AFC97 … 0x350B0213), which runs when the
/// caller's `iCaller` argument (`max_verts`) is non-zero.
///
/// 1. For every created node `i < cnt`, set `idx = i` and compute `cross` and `len`. This
///    includes nodes that are no longer on the ring (see [`convex_hull_2d`]).
/// 2. Sort ([`sort_hull_nodes`]) and link the nodes into a circular list by ascending `cross`.
///    The head is the smallest.
/// 3. While more than 2 vertices remain: stop if `cnt <= max_verts` and the head's
///    `cross² >= len · 0.0225` (corner sine ≥ 0.15). Otherwise remove the head from the ring
///    and the sorted list. The new head is the old head's sorted successor. Recompute the
///    corner of the old ring `prev` and then of `next`, and move each to its place in the
///    sorted list.
///
/// The sorted-list walks stop once the step counter reaches `cnt + 1` (`steps > cnt`) and use the original's compares. Forward
/// moves while `cross > other` (ordered). Backward stops at `cross >= other` (ordered), so NaN
/// keeps walking. Backward also ends when the walk reaches the list tail. The list is sorted
/// by the **raw** cross product, while the stop test is normalised by `len`. So when
/// `cnt > max_verts`, the vertex dropped is the one with the smallest unnormalised cross
/// product, not the flattest corner. This is mirrored.
///
/// Returns `(head, cnt)`, the ring start and the vertex count after the reduction.
pub(crate) fn reduce_hull(
    pts: &[HullPt],
    nodes: &mut [HullNode],
    cnt: i32,
    max_verts: i32,
) -> (u32, i32) {
    let mut cnt = cnt;
    for i in 0..cnt {
        nodes[i as usize].idx = i;
        corner(pts, nodes, i as u32);
    }
    sort_hull_nodes(nodes, 0, cnt - 1);

    let mut head = nodes[0].idx as u32;
    nodes[head as usize].sprev = head;
    nodes[head as usize].snext = head;
    let mut last = head;
    for k in 1..cnt {
        let j = nodes[k as usize].idx as u32;
        nodes[j as usize].sprev = last;
        nodes[j as usize].snext = nodes[last as usize].snext;
        let ln = nodes[last as usize].snext;
        nodes[ln as usize].sprev = j;
        nodes[last as usize].snext = j;
        last = j;
    }

    while cnt > 2 {
        let h = nodes[head as usize];
        if cnt <= max_verts && h.cross * h.cross >= h.len * REDUCE_SIN2 {
            break;
        }
        cnt -= 1;
        // unlink from the ring
        nodes[h.next as usize].prev = h.prev;
        nodes[h.prev as usize].next = h.next;
        // unlink from the sorted list
        nodes[h.snext as usize].sprev = h.sprev;
        nodes[h.sprev as usize].snext = h.snext;
        let nb = [h.prev, h.next];
        head = h.snext;

        for &r in &nb {
            corner(pts, nodes, r);
            let rc = nodes[r as usize].cross;
            let r_next = nodes[r as usize].snext;
            let mut k = r_next;
            let mut steps = 0;
            while k != head {
                if !(rc > nodes[k as usize].cross) {
                    break;
                }
                if steps > cnt {
                    break;
                }
                k = nodes[k as usize].snext;
                steps += 1;
            }
            if r_next != k {
                // move forward: insert before k
                if r == head {
                    head = r_next;
                }
                if r != k {
                    let rp = nodes[r as usize].sprev;
                    nodes[r_next as usize].sprev = rp;
                    let rn = nodes[r as usize].snext;
                    let rp = nodes[r as usize].sprev;
                    nodes[rp as usize].snext = rn;
                    nodes[r as usize].sprev = nodes[k as usize].sprev;
                    nodes[r as usize].snext = k;
                    let kp = nodes[k as usize].sprev;
                    nodes[kp as usize].snext = r;
                    nodes[k as usize].sprev = r;
                }
            } else {
                let r_prev = nodes[r as usize].sprev;
                let mut m = r_prev;
                let mut steps = 0;
                while nodes[m as usize].snext != head {
                    if rc >= nodes[m as usize].cross {
                        break;
                    }
                    if steps > cnt {
                        break;
                    }
                    m = nodes[m as usize].sprev;
                    steps += 1;
                }
                if r_prev != m {
                    // move backward: insert after m
                    if nodes[m as usize].snext == head {
                        head = r;
                    }
                    if r != m {
                        let rn = nodes[r as usize].snext;
                        nodes[rn as usize].sprev = r_prev;
                        let rn = nodes[r as usize].snext;
                        let rp = nodes[r as usize].sprev;
                        nodes[rp as usize].snext = rn;
                        nodes[r as usize].sprev = m;
                        nodes[r as usize].snext = nodes[m as usize].snext;
                        let mn = nodes[m as usize].snext;
                        nodes[mn as usize].sprev = r;
                        nodes[m as usize].snext = r;
                    }
                }
            }
        }
    }
    (head, cnt)
}

/// The next contact of the patch ring that [`fit_contact_patch`] wrote to `c+16`.
#[must_use]
pub fn patch_next(c: &Contact) -> ContactId {
    ContactId(c.unk16 as u32)
}

/// Store `next` in `c+16` (see [`patch_next`]).
pub fn set_patch_next(c: &mut Contact, next: ContactId) {
    c.unk16 = u64::from(next.0);
}

/// Outputs of [`fit_contact_patch`]. A `None` means the original leaves that out-parameter
/// unwritten on the path taken.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct PatchFit {
    /// Return value.
    pub ok: bool,
    /// `*a6`: the number of collected contacts. After the hull is built it is the number of
    /// hull contacts linked through `+16`.
    pub count: i32,
    /// `*a7`: the first contact of the hull ring (written once the hull is built).
    pub first: Option<ContactId>,
    /// `a8[0..3]`: the patch normal (written once the plane fit passes its conditioning test).
    pub normal: Option<Vec3>,
    /// `*a9`: the largest distance of a contact from the fitted plane (written together with
    /// the normal).
    pub dist: Option<f32>,
}

/// `sub_350AF550` (0x350AF550, 4008 bytes): fits the contact patch of one collider. Called by
/// `RegisterContacts` as `fit(this, firstContactOfCollider, 0x80000, sz·0.15, iCaller, &n,
/// &first, &normal, &dist, &body.com, &gravity)`.
///
/// * `sentinel`: the entity's contact-list head (`E+536`). `start`: the first contact to look at.
///   `stop_flags`: the walk stops after the first contact with one of these flags
///   (`0x80000` [`flags::LAST_OF_COLLIDER`] at the call site).
/// * `tol`: the allowed distance from the plane. `caller`: `iCaller`, which is both the
///   "few contacts" threshold and the reduction's vertex budget (0 = no reduction).
/// * `com`: centre of mass. `gravity`: the entity's gravity (`E+616`).
///
/// Steps:
/// 1. Walk the list from `start` until the sentinel, collecting contacts whose `IGNORE` (0x80)
///    flag is clear. Stop after 256 or after a contact (ignored or not) carrying `stop_flags`.
///    Their `pt[0]` are summed in list order.
/// 2. Return `false` if `n < 3`, or if `n <= caller` and `|g|² == 0`. `ucomiss`/`je`, so a NaN
///    `|g|²` also counts as zero.
/// 3. Build the covariance of the points about their mean. Pick the axis `i` with the largest
///    `|cofactor|`. Return `false` if `|M[dec][dec]·M[inc][inc]|·0.001 >= cofactor_i`.
///    Otherwise `n[i] = 1`, solve the other two components by Cramer's rule, and normalise
///    (`sub_35021020`).
/// 4. In-plane axes: `u[s] = 0`, `u[inc s] = n[dec s]`, `u[dec s] = −n[inc s]`, with
///    `s = (n.x² > 0.81·|n|²)`. `u` is **not** normalised. `v = n × u`. Each contact is projected
///    to `(d·u, d·v)` with `d = p − mean`. `dist` is the running [`max_idiom`] of `|d·n|`.
///    Return `false` unless `tol >= dist`.
/// 5. Build the hull ([`convex_hull_2d`]). If `caller != 0`, reduce it ([`reduce_hull`]);
///    otherwise the ring starts at node 0 and `cnt` is the created-node count.
/// 6. Walk the ring `cnt` times from its start, writing each contact's ring successor to `+16`
///    ([`set_patch_next`]) and counting. `first` is the ring start's contact.
/// 7. If `|g|² <= 0` (or NaN), return `true`. If `cnt < 3`, or `(n·g)² < |g|²·0.001`, return
///    `false`. Otherwise project the CoM along `g` onto the plane through the mean. Return
///    `false` if it lies strictly outside any ring edge
///    (`(b.y−a.y)(x−a.x) − (y−a.y)(b.x−a.x) > 0`), else `true`.
///
/// Mutations: only the `+16` field ([`Contact::unk16`]) of the hull contacts. The list order and
/// all other contact fields are untouched.
#[allow(clippy::too_many_arguments)]
pub fn fit_contact_patch(
    pool: &mut ContactPool,
    sentinel: ContactId,
    start: ContactId,
    stop_flags: u32,
    tol: f32,
    caller: i32,
    com: Vec3,
    gravity: Vec3,
) -> PatchFit {
    // 1. collect
    let mut list: Vec<ContactId> = Vec::with_capacity(MAX_PATCH_CONTACTS);
    let (mut sx, mut sy, mut sz) = (0.0f32, 0.0f32, 0.0f32);
    let mut c = start;
    while c != sentinel {
        let r = pool.get(c);
        if r.flags & flags::IGNORE == 0 {
            let p = r.pt[0];
            sx += p.x;
            sy += p.y;
            sz += p.z;
            list.push(c);
        }
        if list.len() >= MAX_PATCH_CONTACTS {
            break;
        }
        if r.flags & stop_flags != 0 {
            break;
        }
        c = r.next;
    }
    let n = list.len() as i32;
    let mut out = PatchFit {
        ok: false,
        count: n,
        first: None,
        normal: None,
        dist: None,
    };

    // 2. early outs
    let g2 = (gravity.x * gravity.x + gravity.y * gravity.y) + gravity.z * gravity.z;
    if (n <= caller && (g2 == 0.0 || g2.is_nan())) || n < 3 {
        return out;
    }

    // 3. plane fit
    let inv = 1.0 / n as f32;
    let my = sy * inv;
    let mz = sz * inv;
    let mx = inv * sx;
    let mut m = [0.0f32; 9];
    for &id in &list {
        let p = pool.get(id).pt[0];
        let dx = p.x - mx;
        let dy = p.y - my;
        let dz = p.z - mz;
        m[0] += dx * dx;
        m[1] += dy * dx;
        m[2] += dz * dx;
        m[3] += dy * dx;
        m[4] += dy * dy;
        m[5] += dy * dz;
        m[6] += dz * dx;
        m[7] += dz * dy;
        m[8] += dz * dz;
    }
    let det = [
        (m[8] * m[4] - m[7] * m[5]).abs(),
        (m[8] * m[0] - m[6] * m[2]).abs(),
        (m[4] * m[0] - m[3] * m[1]).abs(),
    ];
    let s1 = isneg(det[0] - det[1]) as i32;
    let s2 = isneg(det[s1 as usize] - det[2]) as i32;
    let t = (2 * s2) | s1;
    let i = (t & (((t ^ 2) | 4) >> 1)) as usize;
    let (ic, id) = (INC_MOD3[i], DEC_MOD3[i]);
    let diag = m[4 * id] * m[4 * ic];
    if diag.abs() * EPS_REL >= det[i] {
        return out;
    }
    let off = m[3 * id + ic] * m[3 * ic + id];
    let mut nv = [0.0f32; 3];
    nv[i] = 1.0;
    let inv_det = 1.0 / (diag - off);
    {
        let (c, d) = (ic, id);
        nv[c] = -((m[3 * c + i] * m[4 * d] - m[3 * d + i] * m[d + 3 * c]) * inv_det);
        nv[d] = -((m[3 * d + i] * m[4 * c] - m[3 * c + i] * m[3 * d + c]) * inv_det);
    }
    let mut nrm = Vec3::new(nv[0], nv[1], nv[2]);
    normalize_in_place(&mut nrm);
    out.normal = Some(nrm);

    // 4. in-plane axes and projection
    let l2 = (nrm.y * nrm.y + nrm.x * nrm.x) + nrm.z * nrm.z;
    let s = isneg(l2 * AXIS_COS2 - nrm.x * nrm.x) as usize;
    let na = [nrm.x, nrm.y, nrm.z];
    let mut ua = [0.0f32; 3];
    ua[s] = 0.0;
    ua[INC_MOD3[s]] = na[DEC_MOD3[s]];
    ua[DEC_MOD3[s]] = -na[INC_MOD3[s]];
    let u = Vec3::new(ua[0], ua[1], ua[2]);
    let v = Vec3::new(
        u.z * nrm.y - u.y * nrm.z,
        u.x * nrm.z - u.z * nrm.x,
        u.y * nrm.x - u.x * nrm.y,
    );
    let mut dist = 0.0f32;
    let mut pts: Vec<HullPt> = Vec::with_capacity(list.len());
    for (k, &id) in list.iter().enumerate() {
        let p = pool.get(id).pt[0];
        let dx = p.x - mx;
        let dy = p.y - my;
        let dz = p.z - mz;
        let h = ((nrm.x * dx + dy * nrm.y) + dz * nrm.z).abs();
        dist = max_idiom(dist, h);
        let x = (dx * u.x + dy * u.y) + dz * u.z;
        let y = (dx * v.x + dy * v.y) + dz * v.z;
        pts.push(HullPt::new(x, y, k as i32));
    }
    out.dist = Some(dist);
    if !(tol >= dist) {
        return out;
    }

    // 5. hull (+ reduction)
    let mut nodes: Vec<HullNode> = Vec::with_capacity(list.len());
    let mut cnt = convex_hull_2d(&mut pts, &mut nodes);
    let head = if caller != 0 {
        let (h, c2) = reduce_hull(&pts, &mut nodes, cnt, caller);
        cnt = c2;
        h
    } else {
        0
    };

    // 6. link the hull contacts through +16
    let contact_of = |nd: u32| list[pts[nodes[nd as usize].pt as usize].idx as usize];
    out.count = 0;
    let mut nd = head;
    for _ in 0..cnt {
        let nx = nodes[nd as usize].next;
        let (ci, cn) = (contact_of(nd), contact_of(nx));
        set_patch_next(pool.get_mut(ci), cn);
        out.count += 1;
        nd = nx;
    }
    out.first = Some(contact_of(head));

    // 7. support test
    let g2 = (gravity.y * gravity.y + gravity.x * gravity.x) + gravity.z * gravity.z;
    if !(g2 > 0.0) {
        out.ok = true;
        return out;
    }
    if cnt < 3 {
        return out;
    }
    let proj = (nrm.x * gravity.x + gravity.y * nrm.y) + gravity.z * nrm.z;
    if !(proj * proj >= g2 * EPS_REL) {
        return out;
    }
    let t = (((my - com.y) * nrm.y + nrm.x * (mx - com.x)) + (mz - com.z) * nrm.z) / proj;
    let px = (com.x - mx) + gravity.x * t;
    let py = (com.y - my) + gravity.y * t;
    let pz = (com.z - mz) + gravity.z * t;
    let x2 = (px * u.x + py * u.y) + pz * u.z;
    let y2 = (px * v.x + py * v.y) + pz * v.z;
    let mut nd = head;
    for _ in 0..cnt {
        let a = pts[nodes[nd as usize].pt as usize];
        nd = nodes[nd as usize].next;
        let b = pts[nodes[nd as usize].pt as usize];
        if (b.y - a.y) * (x2 - a.x) - (y2 - a.y) * (b.x - a.x) > 0.0 {
            return out;
        }
    }
    out.ok = true;
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn pts_of(xy: &[(f32, f32)]) -> Vec<HullPt> {
        xy.iter()
            .enumerate()
            .map(|(i, &(x, y))| HullPt::new(x, y, i as i32))
            .collect()
    }

    /// The point indices of the ring starting at `head`, `cnt` steps.
    fn ring(nodes: &[HullNode], head: u32, cnt: i32) -> Vec<u32> {
        let mut v = Vec::new();
        let mut nd = head;
        for _ in 0..cnt {
            v.push(nodes[nd as usize].pt);
            nd = nodes[nd as usize].next;
        }
        assert_eq!(nd, head, "ring of {cnt} nodes must close");
        v
    }

    fn hull(xy: &[(f32, f32)]) -> (Vec<HullPt>, Vec<HullNode>, i32) {
        let mut p = pts_of(xy);
        let mut n = Vec::new();
        let c = convex_hull_2d(&mut p, &mut n);
        (p, n, c)
    }

    #[test]
    fn constants_match_binary() {
        assert_eq!(REDUCE_SIN2, 0.0225f32);
        assert_eq!(AXIS_COS2.to_bits(), 0x3F4F_5C28);
        assert_eq!(EPS_REL, 0.001f32);
        // sub_35021020's threshold (0x1E3CE508) is the `1e-20` literal of normalize_in_place
        assert_eq!(1e-20f32.to_bits(), 0x1E3C_E508);
    }

    #[test]
    fn hull_square_with_inner_and_edge_points() {
        // corners 0..3, interior 4, on-edge 5, near-corner interior 6
        let xy = [
            (0.0, 0.0),
            (1.0, 0.0),
            (1.0, 1.0),
            (0.0, 1.0),
            (0.5, 0.5),
            (0.5, 0.0),
            (0.9, 0.9),
        ];
        let (_, n, c) = hull(&xy);
        assert_eq!(c, 4);
        // i0 = first max y (2), i1 = farthest from it (0); CCW ring
        assert_eq!(ring(&n, 0, c), vec![2, 3, 0, 1]);
        assert_eq!(n.len(), 4);
        assert!(n.iter().all(|nd| nd.points == NIL));
    }

    #[test]
    fn hull_triangle_with_interior_points() {
        let xy = [
            (0.2, 0.3),
            (0.0, 0.0),
            (0.4, 0.2),
            (2.0, 0.0),
            (1.0, 2.0),
            (1.0, 0.5),
        ];
        let (_, n, c) = hull(&xy);
        assert_eq!(c, 3);
        assert_eq!(ring(&n, 0, c), vec![4, 1, 3]);
    }

    #[test]
    fn hull_collinear() {
        // hand-traced: (2,0) is dropped (both partition products are +0), (1,0) becomes a
        // vertex because it is the first outside point of node 0.
        let xy = [(0.0, 0.0), (1.0, 0.0), (2.0, 0.0), (3.0, 0.0)];
        let (_, n, c) = hull(&xy);
        assert_eq!(c, 3);
        assert_eq!(ring(&n, 0, c), vec![0, 1, 3]);
    }

    #[test]
    fn hull_all_duplicates() {
        // i0 == i1 == 0, a third node for the first remaining point, the rest dropped
        let xy = [(1.0, 2.0); 5];
        let (_, n, c) = hull(&xy);
        assert_eq!(c, 3);
        assert_eq!(n[0].pt, 0);
        assert_eq!(n[1].pt, 0);
        assert_eq!(ring(&n, 0, c), vec![0, 1, 0]);
    }

    #[test]
    fn hull_duplicate_vertex() {
        let xy = [
            (0.0, 0.0),
            (1.0, 0.0),
            (1.0, 1.0),
            (0.0, 1.0),
            (1.0, 0.0),
            (0.0, 0.0),
        ];
        let (_, n, c) = hull(&xy);
        assert_eq!(c, 4);
        // the first-found copy of each corner is kept
        assert_eq!(ring(&n, 0, c), vec![2, 3, 0, 1]);
    }

    #[test]
    fn hull_single_and_two_points() {
        let (_, n, c) = hull(&[(3.0, 4.0)]);
        assert_eq!(c, 2);
        assert_eq!((n[0].pt, n[1].pt), (0, 0));
        let (_, n, c) = hull(&[(0.0, 0.0), (1.0, 1.0)]);
        assert_eq!(c, 2);
        assert_eq!(ring(&n, 0, c), vec![1, 0]);
    }

    #[test]
    fn hull_regular_polygon_and_many_points() {
        // 40-gon plus 300 interior points: every polygon vertex, nothing else, CCW
        let mut xy = Vec::new();
        for k in 0..40 {
            let a = k as f32 * (std::f32::consts::TAU / 40.0);
            xy.push((10.0 * a.cos(), 10.0 * a.sin()));
        }
        let mut seed = 12345u32;
        let mut rnd = || {
            seed = seed.wrapping_mul(1_103_515_245).wrapping_add(12345);
            ((seed >> 8) as f32 / 16_777_216.0) * 10.0 - 5.0
        };
        for _ in 0..300 {
            let x = rnd();
            let y = rnd();
            xy.push((x, y));
        }
        let (p, n, c) = hull(&xy);
        assert_eq!(c, 40);
        let r = ring(&n, 0, c);
        assert!(r.iter().all(|&i| i < 40));
        for k in 0..40 {
            let a = p[r[k] as usize];
            let b = p[r[(k + 1) % 40] as usize];
            let d = p[r[(k + 2) % 40] as usize];
            assert!((b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x) > 0.0);
        }
    }

    fn sort_keys(keys: &[f32]) -> Vec<i32> {
        let mut n: Vec<HullNode> = keys
            .iter()
            .enumerate()
            .map(|(i, &k)| {
                let mut nd = HullNode::new(0);
                nd.cross = k;
                nd.idx = i as i32;
                nd
            })
            .collect();
        sort_hull_nodes(&mut n, 0, keys.len() as i32 - 1);
        n.iter().map(|nd| nd.idx).collect()
    }

    #[test]
    fn sort_hand_traced_ties() {
        // traced by hand through the K&R scheme of 0x350AA280
        assert_eq!(sort_keys(&[2.0, 1.0, 2.0, 1.0]), vec![1, 3, 2, 0]);
        assert_eq!(sort_keys(&[4.0; 4]), vec![1, 2, 0, 3]);
        assert_eq!(sort_keys(&[5.0]), vec![0]);
    }

    #[test]
    fn sort_is_a_sorted_permutation() {
        let mut seed = 7u32;
        let keys: Vec<f32> = (0..256)
            .map(|_| {
                seed = seed.wrapping_mul(1_103_515_245).wrapping_add(12345);
                ((seed >> 20) % 50) as f32 - 25.0
            })
            .collect();
        let idx = sort_keys(&keys);
        for w in idx.windows(2) {
            assert!(keys[w[0] as usize] <= keys[w[1] as usize]);
        }
        let mut seen = vec![false; keys.len()];
        for &i in &idx {
            assert!(!seen[i as usize]);
            seen[i as usize] = true;
        }
    }

    #[test]
    fn reduce_keeps_square_within_budget() {
        let (p, mut n, c) = hull(&[(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]);
        let (h, c2) = reduce_hull(&p, &mut n, c, 4);
        assert_eq!(c2, 4);
        // all corners have cross 1; the sort leaves idx [1, 2, 0, 3] → head node 1
        assert_eq!(h, 1);
        assert_eq!(ring(&n, h, c2), vec![0, 1, 2, 3]);
        assert!(n.iter().all(|nd| nd.cross == 1.0 && nd.len == 1.0));
    }

    #[test]
    fn reduce_drops_to_budget() {
        let (p, mut n, c) = hull(&[(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]);
        let (h, c2) = reduce_hull(&p, &mut n, c, 3);
        assert_eq!(c2, 3);
        // node 1 (point 0) removed; the ring closes over the other three
        let r = ring(&n, h, c2);
        assert!(!r.contains(&0));
    }

    #[test]
    fn reduce_drops_flat_vertex_even_within_budget() {
        let xy = [
            (0.0, 0.0),
            (1.0, 0.0),
            (1.0, 1.0),
            (0.0, 1.0),
            (0.5, -0.001),
        ];
        let (p, mut n, c) = hull(&xy);
        assert_eq!(c, 5);
        let (h, c2) = reduce_hull(&p, &mut n, c, 8);
        assert_eq!(c2, 4);
        let r = ring(&n, h, c2);
        assert!(!r.contains(&4));
        // the sorted list stays a closed, ascending cycle over the survivors
        let mut s = h;
        for _ in 0..c2 {
            let nx = n[s as usize].snext;
            assert_eq!(n[nx as usize].sprev, s);
            assert!(nx == h || n[s as usize].cross <= n[nx as usize].cross);
            s = nx;
        }
        assert_eq!(s, h);
    }

    #[test]
    fn reduce_never_below_two() {
        let (p, mut n, c) = hull(&[(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)]);
        let (h, c2) = reduce_hull(&p, &mut n, c, 1);
        assert_eq!(c2, 2);
        assert_eq!(ring(&n, h, c2).len(), 2);
    }

    // ------------------------------------------------------------------ full fit

    fn pool_with(pts: &[(Vec3, u32)]) -> (ContactPool, ContactId, Vec<ContactId>) {
        let mut pool = ContactPool::default();
        let head = pool.new_sentinel();
        let mut ids = Vec::new();
        for &(p, f) in pts {
            let id = pool.alloc();
            {
                let c = pool.get_mut(id);
                c.pt = [p, p];
                c.flags = f;
            }
            pool.insert_before(id, head);
            ids.push(id);
        }
        (pool, head, ids)
    }

    fn box_contacts() -> Vec<(Vec3, u32)> {
        vec![
            (Vec3::new(1.0, 1.0, 0.0), 0),
            (Vec3::new(-1.0, 1.0, 0.0), 0),
            (Vec3::new(-1.0, -1.0, 0.0), 0),
            (Vec3::new(1.0, -1.0, 0.0), flags::LAST_OF_COLLIDER),
        ]
    }

    const G: Vec3 = Vec3::new(0.0, 0.0, -9.8);
    const STOP: u32 = flags::LAST_OF_COLLIDER;

    #[test]
    fn box_resting_on_plane() {
        let (mut pool, head, c) = pool_with(&box_contacts());
        let before = pool.recs.clone();
        let com = Vec3::new(0.0, 0.0, 0.5);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.15, 4, com, G);
        assert!(r.ok);
        assert_eq!(r.count, 4);
        // Cramer's rule gives -0.0 for the in-plane components
        let n = r.normal.unwrap();
        assert_eq!(n.to_bits(), [0x8000_0000, 0x8000_0000, 0x3F80_0000]);
        assert_eq!(r.dist, Some(0.0));
        // hand-traced: 2D points (y, -x); hull ring c1 → c2 → c3 → c0; sort head = node 1 (c3)
        assert_eq!(r.first, Some(c[3]));
        assert_eq!(patch_next(pool.get(c[3])), c[0]);
        assert_eq!(patch_next(pool.get(c[0])), c[1]);
        assert_eq!(patch_next(pool.get(c[1])), c[2]);
        assert_eq!(patch_next(pool.get(c[2])), c[3]);
        // nothing but +16 changed
        for (a, b) in before.iter().zip(&pool.recs) {
            let mut a = *a;
            a.unk16 = b.unk16;
            assert_eq!(&a, b);
        }
    }

    #[test]
    fn box_com_outside_support() {
        let (mut pool, head, c) = pool_with(&box_contacts());
        let com = Vec3::new(3.0, 0.0, 0.5);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.15, 4, com, G);
        assert!(!r.ok);
        assert_eq!(r.count, 4);
        assert_eq!(r.first, Some(c[3]));
        // linked all the same
        assert_eq!(patch_next(pool.get(c[2])), c[3]);
    }

    #[test]
    fn no_reduction_and_zero_gravity() {
        let (mut pool, head, c) = pool_with(&box_contacts());
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.15, 0, Vec3::ZERO, Vec3::ZERO);
        // n = 4 > caller = 0 passes the early out; |g|² = 0 → true after linking
        assert!(r.ok);
        assert_eq!(r.count, 4);
        // ring from node 0 (c1): c1 → c2 → c3 → c0
        assert_eq!(r.first, Some(c[1]));
        assert_eq!(patch_next(pool.get(c[1])), c[2]);
        assert_eq!(patch_next(pool.get(c[0])), c[1]);
    }

    #[test]
    fn few_contacts_without_gravity() {
        let (mut pool, head, c) = pool_with(&box_contacts());
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.15, 4, Vec3::ZERO, Vec3::ZERO);
        let expect = PatchFit {
            ok: false,
            count: 4,
            first: None,
            normal: None,
            dist: None,
        };
        assert_eq!(r, expect);
        // NaN |g|² counts as zero (ucomiss/je)
        let nan = Vec3::new(f32::NAN, 0.0, 0.0);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.15, 4, Vec3::ZERO, nan);
        assert_eq!(r, expect);
        // fewer than 3 contacts
        let r = fit_contact_patch(&mut pool, head, c[2], STOP, 0.15, 0, Vec3::ZERO, G);
        assert_eq!(r.count, 2);
        assert!(!r.ok);
    }

    #[test]
    fn tilted_patch() {
        let xy = [
            (1.0, 1.0),
            (-1.0, 1.0),
            (-1.0, -1.0),
            (1.0, -1.0),
            (0.2, 0.1),
        ];
        let pts: Vec<(Vec3, u32)> = xy
            .iter()
            .map(|&(x, y)| (Vec3::new(x, y, 0.5 * x), 0))
            .collect();
        let (mut pool, head, c) = pool_with(&pts);
        let com = Vec3::new(0.0, 0.0, 1.0);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.01, 4, com, G);
        assert!(r.ok);
        assert_eq!(r.count, 4);
        let n = r.normal.unwrap();
        let e = Vec3::new(-0.5, 0.0, 1.0).normalized();
        assert!((n.x - e.x).abs() < 1e-6 && n.y.abs() < 1e-6 && (n.z - e.z).abs() < 1e-6);
        assert!(r.dist.unwrap() < 1e-6);
        // the interior contact is not on the ring
        let mut seen = vec![r.first.unwrap()];
        let mut k = patch_next(pool.get(seen[0]));
        while k != seen[0] {
            seen.push(k);
            k = patch_next(pool.get(k));
        }
        assert_eq!(seen.len(), 4);
        assert!(!seen.contains(&c[4]));
        // CoM beyond the low edge: outside
        let com = Vec3::new(-2.0, 0.0, 0.0);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.01, 4, com, G);
        assert!(!r.ok);
    }

    #[test]
    fn not_planar_beyond_tolerance() {
        let mut pts = box_contacts();
        pts[1].0.z = 0.1;
        let (mut pool, head, c) = pool_with(&pts);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.01, 4, Vec3::ZERO, G);
        assert!(!r.ok);
        assert!(r.normal.is_some());
        assert!(r.dist.unwrap() > 0.01);
        assert_eq!(r.first, None);
        assert_eq!(r.count, 4);
    }

    #[test]
    fn collinear_contacts_rejected() {
        let pts: Vec<(Vec3, u32)> = (0..4).map(|k| (Vec3::new(k as f32, 0.0, 0.0), 0)).collect();
        let (mut pool, head, c) = pool_with(&pts);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 1.0, 0, Vec3::ZERO, G);
        assert!(!r.ok);
        assert_eq!(r.normal, None);
        assert_eq!(r.count, 4);
    }

    #[test]
    fn ignore_and_stop_flags() {
        let pts = vec![
            (Vec3::new(0.0, 0.0, 0.0), 0),
            (Vec3::new(9.0, 9.0, 9.0), flags::IGNORE),
            (Vec3::new(1.0, 0.0, 0.0), 0),
            (Vec3::new(1.0, 1.0, 0.0), 0),
            // ignored, but its stop flag still ends the walk
            (Vec3::new(9.0, 9.0, 9.0), flags::IGNORE | STOP),
            (Vec3::new(0.0, 1.0, 0.0), 0),
        ];
        let (mut pool, head, c) = pool_with(&pts);
        let z = Vec3::ZERO;
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.1, 10, z, z);
        assert_eq!(r.count, 3);
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 0.1, 0, z, z);
        assert!(r.ok);
        assert_eq!(r.count, 3);
        // starting past the stop contact runs to the sentinel
        let r = fit_contact_patch(&mut pool, head, c[5], STOP, 0.1, 10, z, z);
        assert_eq!(r.count, 1);
    }

    #[test]
    fn at_most_256_contacts() {
        let pts: Vec<(Vec3, u32)> = (0..300)
            .map(|k| {
                let a = k as f32 * 0.37;
                (Vec3::new(a.cos(), a.sin(), (k % 7) as f32 * 0.01), 0)
            })
            .collect();
        let (mut pool, head, c) = pool_with(&pts);
        let z = Vec3::ZERO;
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 1.0, 1000, z, z);
        assert_eq!(r.count, 256);
        // with the fit running, the hull only sees the first 256 contacts
        let r = fit_contact_patch(&mut pool, head, c[0], STOP, 1.0, 0, z, G);
        assert!(r.normal.is_some());
        let mut k = r.first.unwrap();
        for _ in 0..r.count {
            assert!(c[..256].contains(&k));
            k = patch_next(pool.get(k));
        }
        assert_eq!(Some(k), r.first);
    }
}
