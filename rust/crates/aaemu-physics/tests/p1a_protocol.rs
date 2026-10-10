//! P1a: the intersection-test protocol slots of the primitives (48, 49, 53, 54, 56, 57, 58,
//! 59), bit-exact against the oracle (`oracle/p1a_probes_protocol.c`, command `gproto`, and
//! `gprobe probe=prep` for slot 48).
//!
//! Goldens: `tests/scenarios/p1a_protocol_*.scn`. With `AAEMU_PHYS_ORACLE` set, the
//! `*_fuzz` tests additionally run thousands of oracle-only cases per probe.

mod p1a_support;

use aaemu_physics::collision::contact::{GeomContact, PrimInters, UnprojParams, UnprojResult};
use aaemu_physics::geometry::primitives::{BoxPrim, CylinderPrim, Primitive, SpherePrim};
use aaemu_physics::geometry::shapes::protocol::{
    overlap_box_sphere, ray_register_intersection, ContactFrame, FeaturePolygon, GeomUnderTest,
    IntersectionProtocol, SphereUnprojectCallees, UsedNodeMap,
};
use aaemu_physics::geometry::shapes::{BoxGeom, CapsuleGeom, CylinderGeom, RayGeom, SphereGeom};
use aaemu_physics::math::{Mat33, Vec3};
use p1a_support::*;

// ------------------------------------------------------------------------------ helpers

/// The class of a case as both trait objects the tests need.
enum G {
    Box(BoxGeom),
    Sphere(SphereGeom),
    Cylinder(CylinderGeom),
    Capsule(CapsuleGeom),
    Ray(RayGeom),
}

impl G {
    fn of(c: &Case) -> Self {
        match c.ty {
            0 => G::Box(BoxGeom::new(&box_prim(&c.prim))),
            3 => G::Ray(RayGeom::new(&ray_prim(&c.prim))),
            4 => G::Sphere(SphereGeom::new(&sphere_prim(&c.prim))),
            5 => G::Cylinder(CylinderGeom::new(&cyl_prim(&c.prim))),
            6 => G::Capsule(CapsuleGeom::new(&cyl_prim(&c.prim))),
            t => panic!("type {t}"),
        }
    }
    fn p(&self) -> &dyn IntersectionProtocol {
        match self {
            G::Box(g) => g,
            G::Sphere(g) => g,
            G::Cylinder(g) => g,
            G::Capsule(g) => g,
            G::Ray(g) => g,
        }
    }
}

/// A primitive of geometry type `ty` from its raw words.
fn prim_from(ty: i32, w: &[u32]) -> Primitive {
    match ty {
        0 => Primitive::Box(box_prim(w)),
        3 => Primitive::Ray(ray_prim(w)),
        4 => Primitive::Sphere(sphere_prim(w)),
        5 => Primitive::Cylinder(cyl_prim(w)),
        6 => Primitive::Capsule(cyl_prim(w)),
        t => panic!("type {t}"),
    }
}

/// The raw words of a primitive (the probe's layout).
fn push_prim(o: &mut Vec<String>, p: &Primitive) {
    match p {
        Primitive::Box(b) => push_box(o, b),
        Primitive::Sphere(s) => {
            push_v(o, s.center);
            o.push(hx(s.r));
        }
        Primitive::Cylinder(c) | Primitive::Capsule(c) => {
            push_v(o, c.center);
            push_v(o, c.axis);
            o.push(hx(c.r));
            o.push(hx(c.hh));
        }
        Primitive::Ray(r) => {
            push_v(o, r.origin);
            push_v(o, r.dir);
        }
        other => panic!("{other:?}"),
    }
}

fn pw(ty: i32) -> usize {
    match ty {
        0 => 16,
        3 => 6,
        4 => 4,
        _ => 8,
    }
}

/// Reads input tokens sequentially.
struct In<'a> {
    c: &'a Case,
    i: usize,
}

impl<'a> In<'a> {
    fn new(c: &'a Case) -> Self {
        Self { c, i: 0 }
    }
    fn w(&mut self) -> u32 {
        let w = self.c.iw(self.i);
        self.i += 1;
        w
    }
    fn f(&mut self) -> f32 {
        f32::from_bits(self.w())
    }
    fn v(&mut self) -> Vec3 {
        Vec3::new(self.f(), self.f(), self.f())
    }
    fn int(&mut self) -> i32 {
        let v = self.c.int(self.i);
        self.i += 1;
        v
    }
    fn ws(&mut self, n: usize) -> Vec<u32> {
        (0..n).map(|_| self.w()).collect()
    }
    fn tok(&mut self) -> String {
        let s = self.c.inp[self.i].clone();
        self.i += 1;
        s
    }
    /// A gtest with the probe's transform (`R`, offset, scale, iCaller).
    fn gtest(&mut self) -> GeomUnderTest {
        let r: Vec<f32> = (0..9).map(|_| self.f()).collect();
        let off = self.v();
        let s = self.f();
        let ic = self.int();
        let mut gt = GeomUnderTest::new(ic);
        gt.r = Mat33::from_array(r.try_into().unwrap());
        gt.offset = off;
        gt.scale = s;
        gt
    }
    fn unproj_result(&mut self) -> UnprojResult {
        let w = self.ws(12);
        unproj_from(&w)
    }
}

fn unproj_from(w: &[u32]) -> UnprojResult {
    UnprojResult {
        t: f64::from_bits(u64::from(w[0]) | (u64::from(w[1]) << 32)),
        cos_t: f64::from_bits(u64::from(w[2]) | (u64::from(w[3]) << 32)),
        pt: Vec3::new(
            f32::from_bits(w[4]),
            f32::from_bits(w[5]),
            f32::from_bits(w[6]),
        ),
        n: Vec3::new(
            f32::from_bits(w[7]),
            f32::from_bits(w[8]),
            f32::from_bits(w[9]),
        ),
        i_feature: [w[10] as i32, w[11] as i32],
    }
}

fn push_d(o: &mut Vec<String>, d: f64) {
    let b = d.to_bits();
    o.push(format!("{:08x}", b as u32));
    o.push(format!("{:08x}", (b >> 32) as u32));
}

fn push_unproj(o: &mut Vec<String>, r: &UnprojResult) {
    push_d(o, r.t);
    push_d(o, r.cos_t);
    push_v(o, r.pt);
    push_v(o, r.n);
    o.push(hi(r.i_feature[0]));
    o.push(hi(r.i_feature[1]));
}

fn run_all(name: &str, cs: &[Case]) {
    let mut fails = Vec::new();
    for c in cs {
        let got = match c.probe.as_str() {
            "prep" => slot48(c),
            "prep53" => slot53(c),
            "list56" => slot56(c),
            "cand57" => slot57(c),
            "poly58" | "poly59" => slot58_59(c),
            "reg54" => slot54(c),
            "unproj49" => slot49(c),
            p => panic!("unknown probe {p}"),
        };
        check(c, &got, &mut fails);
    }
    finish(name, cs.len(), &fails);
}

// ------------------------------------------------------------------------------ slot 48

fn slot48(c: &Case) -> Vec<String> {
    let a = AnyGeom::of(c);
    let p = a.g().prepare_primitive(&c.gwd(0, false)).unwrap();
    let mut o = vec![p.geom_type().id().to_string()];
    push_prim(&mut o, &p);
    o
}

// ------------------------------------------------------------------------------ slot 53

const A5: i32 = 0xA5A5_A5A5_u32 as i32;

fn slot53(c: &Case) -> Vec<String> {
    let g = G::of(c);
    let mut inp = In::new(c);
    let mut gt = inp.gtest();
    // the probe fills the record with 0xA5 before the call
    gt.stop = A5;
    gt.transform_prims = A5;
    gt.n_prims = A5;
    gt.used.list = Some(Vec::new());
    gt.used.word = A5;
    gt.used.list_cap = A5;
    let sentinel = aaemu_physics::geometry::shapes::protocol::FaceCandidate {
        i_feature: A5,
        ..Default::default()
    };
    gt.face_cands = vec![sentinel];
    gt.edge_cands = Vec::new();
    let r = g
        .p()
        .prepare_for_intersection_test(&mut gt, None, None, false);
    let mut o = vec![
        r.to_string(),
        gt.prim_type.to_string(),
        gt.n_prims.to_string(),
        gt.prim_stride.to_string(),
        hx(gt.min_pt_dist),
    ];
    o.push(
        match gt.used.map {
            UsedNodeMap::Null => 0,
            UsedNodeMap::Inline => 1,
            UsedNodeMap::Words(_) => 2,
        }
        .to_string(),
    );
    o.push(i32::from(gt.used.list.is_some()).to_string());
    o.push(gt.used.word.to_string());
    o.push(gt.used.list_cap.to_string());
    o.push(i32::from(gt.unproj_prims.is_none()).to_string());
    o.push(i32::from(!gt.cand_bufs).to_string());
    o.push(i32::from(!gt.cand_bufs).to_string());
    o.push(hi(gt.stop));
    o.push(hi(gt.transform_prims));
    let untouched = gt.face_cands == vec![sentinel] && gt.edge_cands.is_empty();
    o.push(if untouched {
        hi(A5)
    } else {
        hi(gt.face_cands.len() as i32)
    });
    o.push(if untouched {
        hi(A5)
    } else {
        hi(gt.edge_cands.len() as i32)
    });
    if c.ty == 3 {
        o.push(gt.ids[0].to_string());
        push_prim(&mut o, &gt.prims[0]);
    }
    o
}

// ------------------------------------------------------------------------------ slot 56

fn slot56(c: &Case) -> Vec<String> {
    let g = G::of(c);
    let mut inp = In::new(c);
    let mut gt = inp.gtest();
    g.p()
        .prepare_for_intersection_test(&mut gt, None, None, false);
    gt.transform_prims = inp.int();
    let mut op = GeomUnderTest::new(0);
    op.pt_outside_pivot = inp.v();
    let r = g.p().get_primitive_list(0, 1, 0, None, 0, &mut gt, &op);
    let mut o = vec![r.to_string()];
    push_prim(&mut o, &gt.prims[0]);
    o.push(gt.transform_prims.to_string());
    o.push(gt.ids[0].to_string());
    o
}

// ------------------------------------------------------------------------------ slot 57

fn slot57(c: &Case) -> Vec<String> {
    let g = G::of(c);
    let mut inp = In::new(c);
    let mut gt = inp.gtest();
    g.p()
        .prepare_for_intersection_test(&mut gt, None, None, false);
    let flag = inp.int();
    let i_side = inp.int();
    let contact = inp.unproj_result();
    let n = pw(c.ty);
    let mut prim = prim_from(c.ty, &inp.ws(n));
    let before = prim_from(c.ty, &inp.ws(n));
    *gt.unproj_prim_mut(before) = before;
    gt.transform_prims = flag;
    let r = g
        .p()
        .get_unprojection_candidates(i_side, &contact, &mut prim, 0, &mut gt);
    // where `*prim` points afterwards: the +296 buffer when the slot redirects it
    let redirected = match c.ty {
        4 => true,
        3 => false,
        _ => flag != 0,
    };
    let mut o = vec![r.to_string(), i32::from(redirected).to_string()];
    push_prim(&mut o, &prim);
    push_prim(&mut o, gt.unproj_prim().unwrap());
    o.push(gt.ids[0].to_string());
    o.push(gt.face_cands.len().to_string());
    o.push(gt.edge_cands.len().to_string());
    for f in &gt.face_cands {
        push_v(&mut o, f.n);
        o.push(hi(f.i_prim));
        o.push(hi(f.i_feature));
    }
    for e in &gt.edge_cands {
        push_v(&mut o, e.dir);
        push_v(&mut o, e.n[0]);
        push_v(&mut o, e.n[1]);
        o.push(hi(e.i_prim));
        o.push(hi(e.i_feature));
    }
    o
}

// ------------------------------------------------------------------------------ slots 58/59

fn slot58_59(c: &Case) -> Vec<String> {
    let g = G::of(c);
    let mut inp = In::new(c);
    let mut gt = inp.gtest();
    g.p()
        .prepare_for_intersection_test(&mut gt, None, None, false);
    let mut fr = ContactFrame {
        n: inp.v(),
        origin: inp.v(),
        axes: [inp.v(), inp.v()],
    };
    let feat = inp.int();
    let mut out = FeaturePolygon::default();
    let n = if c.probe == "poly58" {
        g.p().face_polygon(&mut fr, 0, feat, &gt, &mut out)
    } else {
        g.p().edge_segment(&mut fr, 0, feat, &gt, &mut out)
    };
    let mut o = vec![n.to_string()];
    for v in [fr.n, fr.origin, fr.axes[0], fr.axes[1]] {
        push_v(&mut o, v);
    }
    for p in &out.pts[..=n as usize] {
        o.push(hx(p[0]));
        o.push(hx(p[1]));
    }
    o.push(i32::from(out.ids_shared).to_string());
    for k in 0..n as usize {
        o.push(out.ids_a[k].to_string());
    }
    for k in 0..n as usize {
        o.push(out.ids_b[k].to_string());
    }
    o
}

// ------------------------------------------------------------------------------ slot 54

fn slot54(c: &Case) -> Vec<String> {
    let G::Ray(g) = G::of(c) else {
        panic!("reg54 is ray only")
    };
    let mut inp = In::new(c);
    let mut gt = inp.gtest();
    g.prepare_for_intersection_test(&mut gt, None, None, false);
    let cnt = inp.int();
    gt.n_max_contacts = inp.int();
    gt.params_stop_at_first = inp.int() != 0;
    gt.stop = 0;
    let hasb = inp.int() != 0;
    let mut gb = GeomUnderTest::new(0);
    gb.prim_type = inp.int();
    let primb_idx = inp.w() as i32;
    let iw = inp.ws(14);
    let f = |i: usize| f32::from_bits(iw[i]);
    let v = |i: usize| Vec3::new(f(i), f(i + 1), f(i + 2));
    let fb = iw[9].to_le_bytes();
    let idb = iw[11].to_le_bytes();
    let inters = PrimInters {
        pt: [v(0), v(3)],
        n: v(6),
        i_feature: [[fb[0], fb[1]], [fb[2], fb[3]]],
        tolerance: f(10),
        id: [
            i16::from_le_bytes([idb[0], idb[1]]),
            i16::from_le_bytes([idb[2], idb[3]]),
        ],
        i_node: [iw[12] as i32, iw[13] as i32],
        ..Default::default()
    };
    let mut contacts: Vec<GeomContact> = (0..cnt).map(|_| GeomContact::default()).collect();
    let r = ray_register_intersection(
        &g,
        primb_idx,
        &mut gt,
        if hasb { Some(&gb) } else { None },
        &inters,
        &mut contacts,
    );
    let k = contacts.last().unwrap();
    let mut o = vec![
        r.to_string(),
        contacts.len().to_string(),
        gt.stop.to_string(),
    ];
    push_d(&mut o, k.t);
    push_v(&mut o, k.pt);
    push_v(&mut o, k.n);
    push_v(&mut o, k.dir);
    o.push(hi(k.i_unproj_mode));
    o.push(hx(k.vel));
    for x in [k.id, k.i_prim, k.i_feature, k.i_node] {
        o.push(hi(x[0]));
        o.push(hi(x[1]));
    }
    o.push(i32::from(k.border.len() == 1 && k.border[0] == k.pt).to_string());
    o.push(k.border.len().to_string());
    o.push(i32::from(k.area.is_none()).to_string());
    push_v(&mut o, k.center);
    // not written by the slot: the 0xCD fill of the probe's contact array stays
    assert!(k.idx_border.is_empty() && !k.border_consecutive);
    o.extend([
        "cdcdcdcd".to_string(),
        "cdcdcdcd".to_string(),
        "cdcdcdcd".to_string(),
    ]);
    o
}

// ------------------------------------------------------------------------------ slot 49

/// Replays the pair functions the oracle called directly with the same arguments.
struct Replay {
    ty: i32,
    want_box: Option<BoxPrim>,
    want_cyl: Option<CylinderPrim>,
    want_sphere: SpherePrim,
    ret: i32,
    res: UnprojResult,
    dir: Vec3,
    calls: usize,
}

impl Replay {
    fn check_params(&self, p: &UnprojParams) {
        assert_eq!(p.dir, Vec3::ZERO);
        assert_eq!(p.tmin.to_bits(), 0);
        assert_eq!(p.max_cos.to_bits(), 0x3DCC_CCCD);
        assert_eq!(p.flag, 0);
    }
}

impl SphereUnprojectCallees for Replay {
    fn unproject_box_sphere(
        &mut self,
        params: &mut UnprojParams,
        b: &BoxPrim,
        s: &SpherePrim,
        out: &mut UnprojResult,
    ) -> i32 {
        assert_eq!(self.ty, 0);
        self.check_params(params);
        assert_eq!(Some(*b), self.want_box);
        assert_eq!(s.center.to_bits(), self.want_sphere.center.to_bits());
        assert_eq!(s.r.to_bits(), self.want_sphere.r.to_bits());
        self.calls += 1;
        *out = self.res;
        params.dir = self.dir;
        self.ret
    }

    fn unproject_cylinder_sphere(
        &mut self,
        params: &mut UnprojParams,
        cy: &CylinderPrim,
        s: &SpherePrim,
        out: &mut UnprojResult,
    ) -> i32 {
        assert_eq!(self.ty, 5);
        self.check_params(params);
        assert_eq!(Some(*cy), self.want_cyl);
        assert_eq!(s.center.to_bits(), self.want_sphere.center.to_bits());
        assert_eq!(s.r.to_bits(), self.want_sphere.r.to_bits());
        self.calls += 1;
        *out = self.res;
        params.dir = self.dir;
        self.ret
    }
}

fn slot49(c: &Case) -> Vec<String> {
    let g = G::of(c);
    let mut inp = In::new(c);
    let center = inp.v();
    let r = inp.f();
    let rsep = inp.f();
    let mut res = inp.unproj_result();
    let mut rp = Replay {
        ty: c.ty,
        want_box: None,
        want_cyl: None,
        want_sphere: SpherePrim {
            center,
            r: if c.ty == 0 { rsep } else { r },
        },
        ret: 0,
        res: UnprojResult::default(),
        dir: Vec3::ZERO,
        calls: 0,
    };
    let mut expect_call = false;
    if c.ty == 0 || c.ty == 5 {
        assert_eq!(inp.tok(), "C");
        let ov = inp.int();
        rp.ret = inp.int();
        rp.res = inp.unproj_result();
        rp.dir = inp.v();
        match &g {
            G::Box(b) => {
                // the local port of OVERLAP[box][sphere] must agree with the DLL's
                assert_eq!(
                    i32::from(overlap_box_sphere(&b.prim, &rp.want_sphere)),
                    ov,
                    "overlap_box_sphere (line {})",
                    c.line
                );
                rp.want_box = Some(b.prim);
                expect_call = ov != 0;
            }
            G::Cylinder(cy) => {
                rp.want_cyl = Some(cy.0.prim);
                expect_call = true;
            }
            _ => unreachable!(),
        }
    }
    let ret = g.p().unproject_sphere(center, r, rsep, &mut res, &mut rp);
    if c.ty == 0 {
        assert_eq!(rp.calls, usize::from(expect_call));
    } else if c.ty == 5 {
        assert_eq!(rp.calls, usize::from(ret != 0));
        let _ = expect_call;
    }
    let mut o = vec![ret.to_string()];
    push_unproj(&mut o, &res);
    o
}

// ------------------------------------------------------------------------------ tests

#[test]
fn prepare_primitive_slot48_matches_the_oracle() {
    run_all("slot 48", &cases("p1a_protocol_prep48"));
}

#[test]
fn prepare_for_intersection_and_lists_match_the_oracle() {
    run_all("slots 53/56", &cases("p1a_protocol_prep"));
}

#[test]
fn unprojection_candidates_match_the_oracle() {
    run_all("slot 57", &cases("p1a_protocol_cand"));
}

#[test]
fn feature_polygons_match_the_oracle() {
    run_all("slots 58/59", &cases("p1a_protocol_poly"));
}

#[test]
fn unproject_sphere_and_ray_register_match_the_oracle() {
    run_all("slots 49/54", &cases("p1a_protocol_unproj"));
}

/// Large oracle-only runs (no golden): every probe and type with fresh seeds.
#[test]
fn protocol_fuzz_against_the_oracle() {
    let mut text = String::new();
    for (probe, types, mode) in [
        ("prep", "box sphere cylinder capsule ray", "0"),
        ("prep53", "box sphere cylinder capsule ray", "0"),
        ("list56", "box sphere cylinder capsule ray", "0"),
        ("list56", "box", "2"),
        ("cand57", "box sphere cylinder capsule ray", "0"),
        ("cand57", "box cylinder capsule", "1"),
        ("poly58", "box cylinder capsule", "0"),
        ("poly59", "box cylinder capsule", "0"),
        ("reg54", "ray", "0"),
        ("unproj49", "box sphere cylinder capsule", "0"),
    ] {
        for (k, ty) in types.split(' ').enumerate() {
            let cmd = if probe == "prep" { "gprobe" } else { "gproto" };
            text.push_str(&format!(
                "{cmd} probe={probe} type={ty} n=2000 seed={} mode={mode}\n",
                9100 + 17 * k + probe.len() * 131
            ));
        }
    }
    let Some(cs) = adhoc_cases(&text) else {
        eprintln!("protocol fuzz: AAEMU_PHYS_ORACLE not set, skipped");
        return;
    };
    eprintln!("protocol fuzz: {} oracle cases", cs.len());
    run_all("protocol fuzz", &cs);
}

// ------------------------------------------------------------------------------ unit tests

#[test]
fn box_ring_semantics_are_by_value() {
    // slot 48 returns a value: two consecutive calls never alias (the binary's ring of two)
    let b = BoxGeom::new(&BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0)));
    let gwd = aaemu_physics::geometry::GeomWorldData::default();
    let p1 = aaemu_physics::geometry::Geometry::prepare_primitive(&b, &gwd).unwrap();
    let p2 = aaemu_physics::geometry::Geometry::prepare_primitive(&b, &gwd).unwrap();
    let (Primitive::Box(a), Primitive::Box(c)) = (p1, p2) else {
        panic!()
    };
    assert_eq!(a, c);
    assert_eq!(a.oriented, 1, "slot 48 always marks the box oriented");
}

#[test]
fn capsule_side_reject_still_writes_the_normal() {
    let cap = CapsuleGeom::new(&CylinderPrim {
        center: Vec3::ZERO,
        axis: Vec3::Z,
        r: 1.0,
        hh: 2.0,
    });
    let mut out = UnprojResult::default();
    let mut rp = Replay {
        ty: 6,
        want_box: None,
        want_cyl: None,
        want_sphere: SpherePrim::default(),
        ret: 0,
        res: UnprojResult::default(),
        dir: Vec3::ZERO,
        calls: 0,
    };
    let r = cap.unproject_sphere(Vec3::new(5.0, 0.0, 0.5), 0.5, 99.0, &mut out, &mut rp);
    assert_eq!(r, 0);
    assert_eq!(
        out.n,
        Vec3::new(5.0, 0.0, 0.0),
        "quirk: n written before the reject"
    );
    assert_eq!(out.t, 0.0, "t never written");
}

#[test]
fn box_face_near_an_edge_degrades_to_a_vertex() {
    // Face 0x41 (+x) with the point on the +y boundary: the face becomes "edge along y"
    // (0x25, coded along the axis whose test passed), and the edge test then passes on the same
    // axis, so the feature always ends as a vertex (bits: y+ → 2, z+ → 4).
    let g = BoxGeom::new(&BoxPrim::aligned(Vec3::ZERO, Vec3::splat(1.0)));
    let mut gt = GeomUnderTest::new(0);
    g.prepare_for_intersection_test(&mut gt, None, None, false);
    g.get_primitive_list(0, 1, 0, None, 0, &mut gt, &GeomUnderTest::new(0));
    let mut prim = gt.prims[0];
    let contact = UnprojResult {
        pt: Vec3::new(1.0, 1.0, 0.3),
        i_feature: [0x41, 0],
        ..Default::default()
    };
    g.get_unprojection_candidates(0, &contact, &mut prim, 0, &mut gt);
    assert_eq!(gt.face_cands.len(), 3);
    assert_eq!(gt.edge_cands.len(), 3);
    let codes: Vec<i32> = gt.face_cands.iter().map(|f| f.i_feature).collect();
    assert_eq!(codes, vec![0x40, 0x43, 0x45]);
}

#[test]
fn record_defaults() {
    let gt = GeomUnderTest::new(3);
    assert_eq!(gt.i_caller, 3);
    assert!(gt.unproj_prim().is_none());
    let mut out = FeaturePolygon::default();
    let s = SphereGeom::new(&SpherePrim {
        center: Vec3::ZERO,
        r: 1.0,
    });
    let mut fr = ContactFrame::default();
    assert_eq!(
        s.face_polygon(&mut fr, 0, 0x40, &gt, &mut out),
        0,
        "base slot 58"
    );
}

#[test]
fn words_roundtrip() {
    let w: Vec<u32> = (0..16).map(|i| 0x3f80_0000 + i).collect();
    let mut o = Vec::new();
    push_prim(&mut o, &prim_from(0, &w));
    assert_eq!(o.len(), 16);
}
