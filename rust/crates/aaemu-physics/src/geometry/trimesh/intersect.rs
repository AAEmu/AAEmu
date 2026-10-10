//! The generic `CGeometry::Intersect` (slot 10 of `CGeometry`, x64 0x3514FD70, x86 0x35158610)
//! with the tree-vs-tree traversal sub_3514DFD0 (x86 0x35156AF0), the sweep traversal
//! sub_3514E580 and the recentring helper sub_3514EFB0: the path of every intersection that
//! involves a mesh or a heightfield (`geometry.md` §3.4, §5.3).
//!
//! # `Intersect(A, B, gwdA, gwdB, ip, &contacts)` (from the disassembly)
//!
//! 1. Defaults when `ip` is null ([`default_local_ip`]). Per-caller arena resets: BV pool,
//!    primitive pools, OVERLAP scratch (−1), mesh buffer counters, used-node map / list
//!    counters; unless `ip.keep_prev_contacts`: the area pools, the contact count and the word
//!    +0x16EC8. 64 contacts already in use → return 0.
//! 2. The first contact slot gets a pool area unless `ip.no_area_contacts` or a pool is
//!    exhausted.
//! 3. A sweep with `|gwdA.v|² == 0` is turned off: `ip.bSweepTest = 0`, **`gwdA.v = (0,0,1)`
//!    and `ip.vrel_min = 0` (written through the caller's pointers; kept)**.
//! 4. Both records: transform, `1/scale`, motion, start node (`gwd+88`), rotation centre
//!    (`ip+4`), `±ip.unproj_dir` (`+224`), pivots, stop flags; then the relative transforms
//!    (`R_rel = R_otherᵀ·R` via sub_35020320, `offset_rel`, `scale_rel`, `rscale_rel`).
//!    `ip+112 = A.+24 & B.+24`.
//! 5. Non-sweep: slot 53 on A (failure → return 0 without cleanup) and on B (failure → A's
//!    slot 55, return 0); the first area's `minedge = min(A.minPtDist, B.minPtDist)`;
//!    recentre on B's offset (sub_3514EFB0); traverse from the start nodes when their **sum**
//!    is non-zero, and from the roots when that gave no contact.
//! 6. If B has the lower priority (B registered the contacts), every contact is mirrored to A's
//!    point of view. **Bug kept:** `id[0]` is read as a signed *byte* when moved to `id[1]`.
//! 7. The contacts are selection-sorted by `t`, **largest first** (sign of `(float)(t_i − t_j)`).
//! 8. Points, centres, pool borders and area points get the recentring offset added back
//!    (in double); areas are dropped under `ip.no_area_contacts`.
//! 9. Slot 55 on A and B; the arena's contact count grows by the number returned.
//!
//! Sweep (`ip.bSweepTest` with `|v| > 0`): [`intersect_sweep`] / [`traverse_sweep`]; one
//! contact at most (the earliest hit), no mirroring, no sorting.

use super::protocol::mesh::bv_record_prim;
use super::protocol::{BorderKind, CallerArena, GeomProtocol, IntersectCtx, Side, CONTACT_POOL};
use crate::collision::contact::{
    GeomContact, IntersectionParams, PrimInters, UnprojParams, UnprojResult,
};
use crate::collision::pairs::PairTables;
use crate::collision::swept::common::rotate_vec;
use crate::collision::swept::UnprojTables;
use crate::geometry::bvtree::{BvHandle, BvRecord};
use crate::geometry::GeomWorldData;
use crate::ids::GeomType;
use crate::math::crt::{cosf, sinf};
use crate::math::{Mat33, Vec3};

/// Inputs of `Intersect` that the shared structs do not carry.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub struct IntersectExtra {
    /// `gwd+88` of A and B: the BV nodes to start the traversal from (0 = roots).
    pub start_node: [i32; 2],
    /// `ip+98`: stop after the first contact.
    pub first_only: bool,
}

/// The tables the intersection code dispatches through.
#[derive(Clone, Copy)]
pub struct Tables<'a> {
    pub pairs: &'a PairTables,
    pub unproj: &'a UnprojTables,
}

/// What `Intersect` returns.
#[derive(Clone, Debug, Default)]
pub struct IntersectResult {
    /// The contacts (`contacts.len()` is the return value).
    pub contacts: Vec<GeomContact>,
    /// Where each contact's border pointer points.
    pub border_kinds: Vec<BorderKind>,
    /// `ip+112` as the call left it.
    pub ip_convex: i32,
}

/// The `intersection_params` the original builds on its stack when the caller passes none.
#[must_use]
pub fn default_local_ip() -> IntersectionParams {
    IntersectionParams {
        i_unprojection_mode: 0,
        center_of_rotation: Vec3::ZERO,
        axis_of_rotation: Vec3::ZERO,
        time_interval: 100.0,
        vrel_min: f32::from_bits(0x3586_37BD),
        max_surface_gap_angle: f32::from_bits(0x3C8E_FA35),
        min_axis_dist: 0.0,
        unproj_dir: Vec3::new(0.0, 0.0, 1.0),
        max_unproj: 1e10,
        pt_outside_pivot: [Vec3::splat(f32::from_bits(0x51BA_43B7)); 2],
        sweep_test: false,
        keep_prev_contacts: false,
        no_area_contacts: false,
        no_intersection: false,
    }
}

/// sub_35020320: `a·b` (row-major), `r[i][j] = (a[i][1]·b[1][j] + a[i][0]·b[0][j]) +
/// a[i][2]·b[2][j]`.
#[must_use]
pub fn mat_mul_20320(a: &Mat33, b: &Mat33) -> Mat33 {
    let (a, b) = (&a.m, &b.m);
    let mut r = [[0.0f32; 3]; 3];
    for i in 0..3 {
        for j in 0..3 {
            r[i][j] = (a[i][1] * b[1][j] + a[i][0] * b[0][j]) + a[i][2] * b[2][j];
        }
    }
    Mat33 { m: r }
}

/// Fills a record's transform and motion from its `geom_world_data` (null: identity, unit
/// scale, no motion).
fn fill_side(s: &mut Side, gwd: Option<&GeomWorldData>) {
    let gt = &mut s.gt;
    match gwd {
        Some(g) => {
            gt.offset = g.offset;
            gt.r = g.r;
            gt.scale = g.scale;
            gt.scale_r = 1.0 / g.scale;
            gt.v = g.v;
            gt.w = g.w;
            gt.center_of_mass = g.center_of_mass;
        }
        None => {
            gt.offset = Vec3::ZERO;
            gt.r = Mat33::IDENTITY;
            gt.scale = 1.0;
            gt.scale_r = 1.0;
            gt.v = Vec3::ZERO;
            gt.w = Vec3::ZERO;
            gt.center_of_mass = Vec3::ZERO;
        }
    }
}

/// `offset_rel` of side `t` in the frame of `o`:
/// `x = ((d.y·R10 + d.x·R00) + d.z·R20)·rs`, `y = rs·((d.x·R01 + d.y·R11) + d.z·R21)`, …
fn offset_rel(t: &Side, o: &Side) -> Vec3 {
    let d = Vec3::new(
        t.gt.offset.x - o.gt.offset.x,
        t.gt.offset.y - o.gt.offset.y,
        t.gt.offset.z - o.gt.offset.z,
    );
    let r = &o.gt.r.m;
    let rs = o.gt.scale_r;
    Vec3::new(
        ((d.y * r[1][0] + d.x * r[0][0]) + d.z * r[2][0]) * rs,
        rs * ((d.x * r[0][1] + d.y * r[1][1]) + d.z * r[2][1]),
        rs * ((d.x * r[0][2] + d.y * r[1][2]) + d.z * r[2][2]),
    )
}

/// The relative-transform loop after the record fill.
fn relative_transforms(s: &mut [Side; 2]) {
    for k in 0..2 {
        let o = k ^ 1;
        let off = offset_rel(&s[k], &s[o]);
        let rr = mat_mul_20320(&s[o].gt.r.transpose(), &s[k].gt.r);
        let scale_rel = s[k].gt.scale * s[o].gt.scale_r;
        let scale_rel_r = s[o].gt.scale * s[k].gt.scale_r;
        let g = &mut s[k].gt;
        g.offset_rel = off;
        g.r_rel = rr;
        g.scale_rel = scale_rel;
        g.scale_rel_r = scale_rel_r;
    }
}

/// sub_3514EFB0: recomputes both `offset_rel` (same operations, same results), moves both
/// records so that B's offset is the origin (double subtraction, one rounding) and returns the
/// offset to add back.
fn recenter(s: &mut [Side; 2]) -> [f64; 3] {
    let a_rel = offset_rel(&s[0], &s[1]);
    let b_rel = offset_rel(&s[1], &s[0]);
    s[0].gt.offset_rel = a_rel;
    s[1].gt.offset_rel = b_rel;
    let o = s[1].gt.offset;
    let off = [f64::from(o.x), f64::from(o.y), f64::from(o.z)];
    let sub = |v: Vec3| {
        Vec3::new(
            (f64::from(v.x) - off[0]) as f32,
            (f64::from(v.y) - off[1]) as f32,
            (f64::from(v.z) - off[2]) as f32,
        )
    };
    let a = &mut s[0].gt;
    a.offset = sub(a.offset);
    a.center_of_mass = sub(a.center_of_mass);
    a.center_of_rotation = sub(a.center_of_rotation);
    a.pt_outside_pivot = sub(a.pt_outside_pivot);
    let b = &mut s[1].gt;
    b.center_of_mass = sub(b.center_of_mass);
    b.center_of_rotation = sub(b.center_of_rotation);
    b.pt_outside_pivot = sub(b.pt_outside_pivot);
    b.offset = Vec3::ZERO;
    off
}

fn rec_type(r: &BvRecord) -> GeomType {
    GeomType::from_id(r.kind).unwrap_or(GeomType::Box)
}

/// sub_3514DFD0: the tree-vs-tree traversal. `ha` is a BV of A in A's frame, `hb` a BV of B
/// placed in A's frame. Returns the number of contacts registered below this pair.
pub fn traverse(
    ctx: &mut IntersectCtx<'_>,
    geoms: [&dyn GeomProtocol; 2],
    ha: BvHandle,
    hb: BvHandle,
) -> i32 {
    let ra = ctx.arena.bv.bv_record(ha);
    let rb = ctx.arena.bv.bv_record(hb);
    let used_a = ctx.s[0].node_used(&ctx.arena.bv, ra.i_node);
    let used_b = ctx.s[1].node_used(&ctx.arena.bv, rb.i_node);
    if used_a & used_b != 0 {
        return 0;
    }
    let ovl = ctx.tables.overlap_fn(rec_type(&ra), rec_type(&rb));
    if !ovl(&bv_record_prim(&ra), &bv_record_prim(&rb)) {
        return 0;
    }
    let (ta, tb) = (geoms[0].proto_bv_tree(), geoms[1].proto_bv_tree());
    let (ta, tb) = (ta.tree(), tb.tree());
    let va = ta.split_priority(&ctx.arena.bv, ha);
    let vb = tb.split_priority(&ctx.arena.bv, hb);
    let stopped = |c: &IntersectCtx<'_>| c.s[0].gt.stop + c.s[1].gt.stop > 0;
    if va > vb && va > 0.0 {
        let (c0, c1) = ta
            .get_node_children_bvs(&mut ctx.arena.bv, ha)
            .expect("BV slot 14 on a tree without children BVs");
        let mut n = traverse(ctx, geoms, c0, hb);
        if !stopped(ctx) {
            n += traverse(ctx, geoms, c1, hb);
        }
        ta.release_last_bvs(&mut ctx.arena.bv);
        return n;
    }
    if vb > 0.0 {
        let g = &ctx.s[1].gt;
        let (r, o, sc) = (g.r_rel, g.offset_rel, g.scale_rel);
        let (c0, c1) = tb
            .get_node_children_bvs_transformed(&mut ctx.arena.bv, &r, o, sc, hb)
            .expect("BV slot 15 on a tree without children BVs");
        let mut n = traverse(ctx, geoms, ha, c0);
        if !stopped(ctx) {
            n += traverse(ctx, geoms, ha, c1);
        }
        tb.release_last_bvs(&mut ctx.arena.bv);
        return n;
    }
    leaves(ctx, geoms, &ra, &rb, used_a, used_b)
}

/// The leaf branch of sub_3514DFD0: both leaves' primitives (A local with B as collider,
/// B in A's frame with A as collider), every pair through `INTERSECT[s][s^1]`, hits registered
/// by the lower-priority side `s` (A on ties).
fn leaves(
    ctx: &mut IntersectCtx<'_>,
    geoms: [&dyn GeomProtocol; 2],
    ra: &BvRecord,
    rb: &BvRecord,
    used_a: i32,
    used_b: i32,
) -> i32 {
    let (ma, mb) = (geoms[0].proto_min_vtx_dist(), geoms[1].proto_min_vtx_dist());
    let tol = ((ma + mb) - (ma - mb).abs()) * 0.5;
    // Fields of the stack record the traversal does not initialise are zero here.
    let mut inters = PrimInters {
        tolerance: tol * tol,
        border_max: 15,
        ..PrimInters::default()
    };
    let s = usize::from(geoms[1].proto_priority() - geoms[0].proto_priority() < 0);
    let na = geoms[0].proto_node_contents(ctx, 0, ra.i_node, rb, used_b, 1);
    let nb = geoms[1].proto_node_contents(ctx, 1, rb.i_node, ra, used_a, 0);
    let mut sum = 0;
    for i in 0..na.max(0) as usize {
        for j in 0..nb.max(0) as usize {
            let (is, io) = if s == 0 { (i, j) } else { (j, i) };
            let ps = ctx.s[s].gt.prims[is];
            let po = ctx.s[s ^ 1].gt.prims[io];
            let f = ctx.tables.intersect_fn(
                GeomType::from_id(ctx.s[s].gt.prim_type).unwrap_or(GeomType::Box),
                GeomType::from_id(ctx.s[s ^ 1].gt.prim_type).unwrap_or(GeomType::Box),
            );
            if !f(&ps, &po, &mut inters) {
                continue;
            }
            let id_of = |sd: &Side, k: usize| i16::from(sd.gt.ids.get(k).copied().unwrap_or(0));
            inters.id = [id_of(&ctx.s[s], is), id_of(&ctx.s[s ^ 1], io)];
            inters.i_node = [ra.i_node, rb.i_node];
            ctx.s[1].bvs.b_marked = 0;
            ctx.s[0].bvs.b_marked = 0;
            sum += geoms[s].proto_register(ctx, s, geoms[s ^ 1], is, io, &mut inters);
            if (ctx.s[1].bvs.b_marked & ctx.s[0].bvs.b_marked) + ctx.s[1].gt.stop + ctx.s[0].gt.stop
                > 0
            {
                return sum;
            }
            inters.unk_84 = 0;
            inters.border.clear();
            inters.border_max = 15;
        }
    }
    sum
}

/// BV slot 11 on A (from `na`) and slot 9 on B (placed in A's frame), then the traversal.
fn run_traversal(ctx: &mut IntersectCtx<'_>, geoms: [&dyn GeomProtocol; 2], na: i32, nb: i32) {
    let ha = geoms[0]
        .proto_bv_tree()
        .tree()
        .get_node_bv(&mut ctx.arena.bv, na);
    let g = &ctx.s[1].gt;
    let (r, o, sc) = (g.r_rel, g.offset_rel, g.scale_rel);
    let hb =
        geoms[1]
            .proto_bv_tree()
            .tree()
            .get_node_bv_transformed(&mut ctx.arena.bv, &r, o, sc, nb);
    traverse(ctx, geoms, ha, hb);
}

/// Step 6: mirror every contact to A's point of view (`n`, `dir` negated, `pt` moved back by the
/// unprojection, ids / prims / features swapped; area normal and id arrays swapped).
fn mirror_contacts(ctx: &mut IntersectCtx<'_>) {
    let center = ctx.s[0].gt.center_of_rotation;
    let rot_about = |p: Vec3, axis: Vec3, t: f64| {
        let ang = (-t) as f32;
        let d = Vec3::new(p.x - center.x, p.y - center.y, p.z - center.z);
        let r = rotate_vec(d, axis, cosf(ang), sinf(ang));
        Vec3::new(r.x + center.x, r.y + center.y, r.z + center.z)
    };
    for c in &mut ctx.contacts {
        let rot = c.i_unproj_mode != 0;
        if c.area.as_ref().is_some_and(|a| a.npt != 0) {
            let ar = c.area.as_mut().expect("checked");
            std::mem::swap(&mut c.n, &mut ar.n);
            ar.prim.swap(0, 1);
            ar.feature.swap(0, 1);
            if rot {
                let ang = (-c.t) as f32;
                c.n = rotate_vec(c.n, c.dir, cosf(ang), sinf(ang));
                ar.n = rotate_vec(ar.n, c.dir, cosf(ang), sinf(ang));
                for p in ar.pt.iter_mut().take(ar.npt.max(0) as usize) {
                    *p = rot_about(*p, c.dir, c.t);
                }
            } else {
                let t = c.t as f32;
                for p in ar.pt.iter_mut().take(ar.npt.max(0) as usize) {
                    *p = Vec3::new(p.x - c.dir.x * t, p.y - c.dir.y * t, p.z - c.dir.z * t);
                }
            }
        } else {
            if rot {
                let ang = (-c.t) as f32;
                c.n = rotate_vec(c.n, c.dir, cosf(ang), sinf(ang));
            }
            c.n = Vec3::new(-c.n.x, -c.n.y, -c.n.z);
        }
        if c.border.len() > 1 {
            for e in &mut c.idx_border {
                e.swap(0, 1);
            }
        }
        if rot {
            c.pt = rot_about(c.pt, c.dir, c.t);
        } else {
            let t = c.t as f32;
            c.pt = Vec3::new(
                c.pt.x - c.dir.x * t,
                c.pt.y - c.dir.y * t,
                c.pt.z - c.dir.z * t,
            );
        }
        c.dir = Vec3::new(-c.dir.x, -c.dir.y, -c.dir.z);
        // `movsx ecx, byte [c+52]`: id[0] reaches id[1] as a signed byte (kept)
        let id0 = i32::from(c.id[0] as i8);
        c.id[0] = c.id[1];
        c.id[1] = id0;
        c.i_prim.swap(0, 1);
        c.i_feature.swap(0, 1);
    }
}

/// Step 7: selection sort, largest `t` first.
fn sort_contacts(ctx: &mut IntersectCtx<'_>) {
    let n = ctx.contacts.len();
    for i in 0..n.saturating_sub(1) {
        let mut m = i;
        for j in i + 1..n {
            let d = (ctx.contacts[m].t - ctx.contacts[j].t) as f32;
            if d.to_bits() >> 31 != 0 {
                m = j;
            }
        }
        if m != i {
            ctx.contacts.swap(i, m);
            ctx.border_kind.swap(i, m);
        }
    }
}

/// Step 8: the recentring offset added back (border aliases follow their point).
fn add_offset(ctx: &mut IntersectCtx<'_>, off: [f64; 3]) {
    let add = |p: Vec3| {
        Vec3::new(
            (f64::from(p.x) + off[0]) as f32,
            (f64::from(p.y) + off[1]) as f32,
            (f64::from(p.z) + off[2]) as f32,
        )
    };
    let no_area = ctx.ip.no_area_contacts;
    for (c, kind) in ctx.contacts.iter_mut().zip(&ctx.border_kind) {
        c.pt = add(c.pt);
        c.center = add(c.center);
        match kind {
            BorderKind::Pt => c.border = vec![c.pt],
            BorderKind::Center => c.border = vec![c.center],
            BorderKind::Pool => {
                for p in &mut c.border {
                    *p = add(*p);
                }
            }
        }
        if no_area {
            c.area = None;
        } else if let Some(ar) = c.area.as_mut() {
            for p in ar.pt.iter_mut().take(ar.npt.max(0) as usize) {
                *p = add(*p);
            }
        }
    }
}

/// `CGeometry::Intersect` (0x3514FD70) of `a` against `b` (see the module docs). `None`
/// placements are the null pointers of the original; `ip` `None` uses [`default_local_ip`],
/// a passed one may be modified (step 3).
#[allow(clippy::too_many_arguments)]
pub fn intersect(
    tables: Tables<'_>,
    arena: &mut CallerArena,
    a: &dyn GeomProtocol,
    b: &dyn GeomProtocol,
    mut gwd_a: Option<&mut GeomWorldData>,
    gwd_b: Option<&GeomWorldData>,
    ip_in: Option<&mut IntersectionParams>,
    extra: IntersectExtra,
) -> IntersectResult {
    let mut local_ip = default_local_ip();
    let ip: &mut IntersectionParams = match ip_in {
        Some(p) => p,
        None => &mut local_ip,
    };
    arena.bv.bv_count = 0;
    arena.overlap_scratch = -1;
    if !ip.keep_prev_contacts {
        arena.n_areas = 0;
        arena.n_area_pts = 0;
        arena.n_contacts = 0;
        arena.w16ec8 = 0;
    }
    if arena.n_contacts >= CONTACT_POOL {
        return IntersectResult::default();
    }
    arena.bv.reset_used_pools();
    let first_area = arena.peek_area(ip.no_area_contacts);
    if ip.sweep_test {
        if let Some(g) = gwd_a.as_deref_mut() {
            let v = g.v;
            if (v.x * v.x + v.y * v.y) + v.z * v.z == 0.0 {
                ip.sweep_test = false;
                g.v = Vec3::new(0.0, 0.0, 1.0);
                ip.vrel_min = 0.0;
            }
        }
    }
    let n_max = CONTACT_POOL - arena.n_contacts;
    let mut s = [Side::new(0), Side::new(0)];
    let gwds = [gwd_a.as_deref().copied(), gwd_b.copied()];
    for (k, sd) in s.iter_mut().enumerate() {
        fill_side(sd, gwds[k].as_ref());
        let g = &mut sd.gt;
        g.center_of_rotation = ip.center_of_rotation;
        let f = (1 - 2 * k as i32) as f32;
        g.unproj_dir = Vec3::new(
            ip.unproj_dir.x * f,
            ip.unproj_dir.y * f,
            ip.unproj_dir.z * f,
        );
        g.n_max_contacts = n_max;
        g.stop = 0;
        g.field_260 = 0;
        g.pt_outside_pivot = ip.pt_outside_pivot[k];
        g.params_stop_at_first = extra.first_only;
    }
    relative_transforms(&mut s);
    let ip_convex = a.proto_flag24() & b.proto_flag24();
    let mut ctx = IntersectCtx {
        tables: tables.pairs,
        unproj: tables.unproj,
        ip: *ip,
        ip_convex,
        first_only: extra.first_only,
        arena,
        s,
        contacts: Vec::new(),
        border_kind: Vec::new(),
        next_area: first_area,
        bboxes: [a.proto_bbox(), b.proto_bbox()],
    };
    let geoms: [&dyn GeomProtocol; 2] = [a, b];
    let keep = ip.keep_prev_contacts;
    if ip.sweep_test {
        return intersect_sweep(ctx, geoms, keep);
    }
    if a.proto_prepare(&mut ctx, 0, keep) == 0 {
        return IntersectResult::default();
    }
    if b.proto_prepare(&mut ctx, 1, keep) == 0 {
        a.proto_cleanup(&mut ctx, 0);
        return IntersectResult::default();
    }
    if let Some(ar) = ctx.next_area.as_mut() {
        let (x, y) = (ctx.s[0].gt.min_pt_dist, ctx.s[1].gt.min_pt_dist);
        ar.min_edge = ((x + y) - (x - y).abs()) * 0.5;
    }
    let off = recenter(&mut ctx.s);
    let [sa, sb] = extra.start_node;
    if sa + sb != 0 {
        run_traversal(&mut ctx, geoms, sa, sb);
    }
    if ctx.contacts.is_empty() {
        run_traversal(&mut ctx, geoms, 0, 0);
    }
    if geoms[1].proto_priority() - geoms[0].proto_priority() < 0 {
        mirror_contacts(&mut ctx);
    }
    sort_contacts(&mut ctx);
    finish(ctx, geoms, off)
}

/// Steps 8–9.
fn finish(
    mut ctx: IntersectCtx<'_>,
    geoms: [&dyn GeomProtocol; 2],
    off: [f64; 3],
) -> IntersectResult {
    add_offset(&mut ctx, off);
    geoms[0].proto_cleanup(&mut ctx, 0);
    geoms[1].proto_cleanup(&mut ctx, 1);
    ctx.arena.n_contacts += ctx.contacts.len() as i32;
    IntersectResult {
        contacts: ctx.contacts,
        border_kinds: ctx.border_kind,
        ip_convex: ctx.ip_convex,
    }
}

/// The sweep branch of `Intersect` (from 0x35151621): A moves along `v̂ = gwdA.v/|v|` by
/// `L = |v|·time_interval`. Pivots become 1e11; slot 53 on both (a failure cleans up A only);
/// the single contact slot is pre-set to `t = L`, `dir = −v̂`, `vel = |v|`, one border point,
/// no area; BV slot 10 gives A's swept root (a tree whose slot 10 is a nop — the ray —
/// leaves the original with an uninitialised pointer; the port returns no contact), slot 9 B's
/// root in A's frame; [`traverse_sweep`] keeps the earliest hit. The pre-set border points at
/// the *first pool contact's* `pt` (the call's own contact unless previous contacts are kept).
fn intersect_sweep(
    mut ctx: IntersectCtx<'_>,
    geoms: [&dyn GeomProtocol; 2],
    keep: bool,
) -> IntersectResult {
    let v = ctx.s[0].gt.v;
    let l = ((v.y * v.y + v.x * v.x) + v.z * v.z).sqrt();
    let inv = 1.0 / l;
    let len = l * ctx.ip.time_interval;
    let vh = Vec3::new(inv * v.x, v.y * inv, v.z * inv);
    {
        let a = &mut ctx.s[0];
        a.sweep_dir = vh;
        a.sweep_len = len;
        let r = &a.gt.r.m;
        a.sweep_dir_loc = Vec3::new(
            (r[1][0] * vh.y + r[0][0] * vh.x) + r[2][0] * vh.z,
            (r[1][1] * vh.y + r[0][1] * vh.x) + r[2][1] * vh.z,
            (r[1][2] * vh.y + r[0][2] * vh.x) + r[2][2] * vh.z,
        );
        a.sweep_len_loc = a.gt.scale_r * len;
    }
    let big = Vec3::splat(f32::from_bits(0x51BA_43B7));
    ctx.s[1].gt.pt_outside_pivot = big;
    ctx.s[0].gt.pt_outside_pivot = big;
    if geoms[0].proto_prepare(&mut ctx, 0, keep) == 0
        || geoms[1].proto_prepare(&mut ctx, 1, keep) == 0
    {
        geoms[0].proto_cleanup(&mut ctx, 0);
        return IntersectResult::default();
    }
    let off = recenter(&mut ctx.s);
    let mut c = GeomContact {
        vel: l,
        t: f64::from(len),
        dir: Vec3::new(-vh.x, -vh.y, -vh.z),
        ..GeomContact::default()
    };
    let (dl, ll) = (ctx.s[0].sweep_dir_loc, ctx.s[0].sweep_len_loc);
    let ha = geoms[0]
        .proto_bv_tree()
        .tree()
        .get_node_bv_swept(&mut ctx.arena.bv, dl, ll, 0);
    let g = &ctx.s[1].gt;
    let (r, o, sc) = (g.r_rel, g.offset_rel, g.scale_rel);
    let hb =
        geoms[1]
            .proto_bv_tree()
            .tree()
            .get_node_bv_transformed(&mut ctx.arena.bv, &r, o, sc, 0);
    let mut found = false;
    if let Some(ha) = ha {
        traverse_sweep(&mut ctx, geoms, ha, hb, &mut c, &mut found);
    }
    if found {
        c.border = vec![c.pt];
        ctx.push_contact(c, BorderKind::Pt);
    }
    finish(ctx, geoms, off)
}

/// sub_3514E580: the sweep traversal. Descends like [`traverse`] (no stop checks; A's children
/// swept with BV slot 13 and released with slot 17); at leaf pairs A is moved to its end pose
/// (`offset += v̂·L`), A's leaf triangles are listed without tests (`bLocal = −1`), B's
/// against A's swept BV, and every pair goes through `UNPROJECT[0][tA][tB]` along `−v̂`
/// (`tmax = L`, `tmin = 0`, `minPtDist = min`, `maxcos = 0.1`). A result is taken when
/// `n·v̂ > 0`, `t ≤ L` and `L − t` is below the best so far (double comparisons; a NaN passes
/// the last two like the binary's `comisd`/`ja`/`jae`); the contact gets `t = L − t`,
/// `pt = centre =` the result point, the normalised `n`, triangle indices, features, ids and
/// nodes.
fn traverse_sweep(
    ctx: &mut IntersectCtx<'_>,
    geoms: [&dyn GeomProtocol; 2],
    ha: BvHandle,
    hb: BvHandle,
    c: &mut GeomContact,
    found: &mut bool,
) -> i32 {
    let ra = ctx.arena.bv.bv_record(ha);
    let rb = ctx.arena.bv.bv_record(hb);
    let ovl = ctx.tables.overlap_fn(rec_type(&ra), rec_type(&rb));
    if !ovl(&bv_record_prim(&ra), &bv_record_prim(&rb)) {
        return 0;
    }
    let (ta, tb) = (geoms[0].proto_bv_tree(), geoms[1].proto_bv_tree());
    let (ta, tb) = (ta.tree(), tb.tree());
    let va = ta.split_priority(&ctx.arena.bv, ha);
    let vb = tb.split_priority(&ctx.arena.bv, hb);
    if va > vb && va > 0.0 {
        let (dl, ll) = (ctx.s[0].sweep_dir_loc, ctx.s[0].sweep_len_loc);
        let (c0, c1) = ta
            .get_node_children_bvs_swept(&mut ctx.arena.bv, ha, dl, ll)
            .expect("BV slot 13 on a tree without swept children");
        let n = traverse_sweep(ctx, geoms, c0, hb, c, found);
        let n = n + traverse_sweep(ctx, geoms, c1, hb, c, found);
        ta.release_last_swept_bvs(&mut ctx.arena.bv);
        return n;
    }
    if vb > 0.0 {
        let g = &ctx.s[1].gt;
        let (r, o, sc) = (g.r_rel, g.offset_rel, g.scale_rel);
        let (c0, c1) = tb
            .get_node_children_bvs_transformed(&mut ctx.arena.bv, &r, o, sc, hb)
            .expect("BV slot 15 on a tree without children BVs");
        let n = traverse_sweep(ctx, geoms, ha, c0, c, found);
        let n = n + traverse_sweep(ctx, geoms, ha, c1, c, found);
        tb.release_last_bvs(&mut ctx.arena.bv);
        return n;
    }
    let saved = ctx.s[0].gt.offset;
    let (sd, len) = (ctx.s[0].sweep_dir, ctx.s[0].sweep_len);
    ctx.s[0].gt.offset = Vec3::new(
        sd.x * len + saved.x,
        sd.y * len + saved.y,
        sd.z * len + saved.z,
    );
    let (ma, mb) = (geoms[0].proto_min_vtx_dist(), geoms[1].proto_min_vtx_dist());
    // `center` and `vel` of the stack params are not written by the original (unused by the
    // linear cells).
    let mut p = UnprojParams {
        mode: 0,
        dir: Vec3::new(-sd.x, -sd.y, -sd.z),
        center: Vec3::ZERO,
        vel: 0.0,
        tmax: len,
        tmin: 0.0,
        min_pt_dist: ((mb + ma) - (ma - mb).abs()) * 0.5,
        max_cos: 0.1,
        flag: 0,
    };
    let na = geoms[0].proto_node_contents(ctx, 0, ra.i_node, &rb, 0, -1);
    let nb = geoms[1].proto_node_contents(ctx, 1, rb.i_node, &ra, 0, 0);
    let mut res = UnprojResult::default();
    let mut n_found = 0;
    for i in 0..na.max(0) as usize {
        for j in 0..nb.max(0) as usize {
            let pa = ctx.s[0].gt.prims[i];
            let pb = ctx.s[1].gt.prims[j];
            if ctx
                .unproj
                .unproject(0, &mut p, &pa, -1, &pb, -1, &mut res, None)
                == 0
            {
                continue;
            }
            let nd = (res.n.x * sd.x + res.n.y * sd.y) + res.n.z * sd.z;
            // `comiss nd, 0; jbe`: NaN rejects
            if nd.partial_cmp(&0.0) != Some(std::cmp::Ordering::Greater) {
                continue;
            }
            let lf = f64::from(len);
            if res.t > lf {
                continue;
            }
            let d = lf - res.t;
            if d >= c.t {
                continue;
            }
            c.t = d;
            c.center = res.pt;
            c.pt = res.pt;
            let n = res.n;
            let l2 = (n.y * n.y + n.x * n.x) + n.z * n.z;
            c.n = if l2 > 1e-20 {
                let k = 1.0 / l2.sqrt();
                Vec3::new(k * n.x, n.y * k, n.z * k)
            } else {
                Vec3::new(0.0, 0.0, 1.0)
            };
            let tri_idx = |sdx: &Side, k: usize| {
                if sdx.gt.prim_type == 1 {
                    sdx.prim_idx.get(k).copied().unwrap_or(0)
                } else {
                    0
                }
            };
            c.i_prim = [tri_idx(&ctx.s[0], i), tri_idx(&ctx.s[1], j)];
            c.i_feature = res.i_feature;
            c.id = [
                i32::from(ctx.s[0].gt.ids.get(i).copied().unwrap_or(0)),
                i32::from(ctx.s[1].gt.ids.get(j).copied().unwrap_or(0)),
            ];
            c.i_node = [ra.i_node, rb.i_node];
            *found = true;
            n_found += 1;
        }
    }
    ctx.s[0].gt.offset = saved;
    n_found
}
