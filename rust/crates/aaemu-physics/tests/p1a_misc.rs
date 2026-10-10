//! P1a: the miscellaneous primitive slots against the oracle, bit for bit — box GetFeature
//! (slot 20), GetRandomPos (slot 43) with xlcommon's CryRandom, the occlusion-cubemap
//! rasteriser (slot 51 and `sub_35131AB0`), DrawWireframe (slot 15) and GetMemoryStatistics
//! (slot 24).
//!
//! Scenarios: `tests/scenarios/p1a_misc*.scn` (probes in `oracle/p1a_probes_misc.c`). The
//! `*_fuzz` tests run thousands of extra cases straight through the oracle when
//! `AAEMU_PHYS_ORACLE` is set (no goldens).

mod p1a_support;

use aaemu_physics::geometry::shapes::misc::cubemap::{
    box_draw_to_cubemap, cylinder_draw_to_cubemap, rasterize_polygon, sphere_draw_to_cubemap,
    Cubemap,
};
use aaemu_physics::geometry::shapes::misc::feature::BOX_TREE_VPTR_LO;
use aaemu_physics::geometry::shapes::misc::wireframe::{
    base_draw_wireframe, primitive_draw_wireframe, PhysRenderer,
};
use aaemu_physics::geometry::shapes::misc::{
    box_get_feature, memory_statistics, CryRandom, CrySizer, PosNorm,
};
use aaemu_physics::geometry::shapes::BoxGeom;
use aaemu_physics::geometry::{GeomWorldData, Geometry};
use aaemu_physics::math::Vec3;
use p1a_support::*;

// ---------------------------------------------------------------- slot 20

fn run_feature(cs: &[Case]) -> Vec<String> {
    let mut fails = Vec::new();
    for c in cs {
        let g = BoxGeom::new(&box_prim(&c.prim));
        assert_eq!(
            c.iw(2),
            BOX_TREE_VPTR_LO,
            "the DLL is not at its preferred base"
        );
        let mut pts = [Vec3::ZERO; 5];
        for (k, p) in pts.iter_mut().enumerate() {
            *p = c.v(3 + 3 * k);
        }
        let r = box_get_feature(&g, c.int(0), c.int(1), &mut pts);
        let mut o = vec![r.to_string()];
        for p in pts {
            push_v(&mut o, p);
        }
        o.push(c.inp[3 + 15].clone());
        check(c, &o, &mut fails);
    }
    fails
}

#[test]
fn feature_matches_the_oracle() {
    let cs: Vec<Case> = cases("p1a_misc")
        .into_iter()
        .filter(|c| c.probe == "feat")
        .collect();
    let fails = run_feature(&cs);
    finish("GetFeature", cs.len(), &fails);
}

#[test]
fn feature_fuzz() {
    if let Some(cs) = adhoc_cases("gfeat n=20000 seed=17001\n") {
        let fails = run_feature(&cs);
        finish("GetFeature fuzz", cs.len(), &fails);
    }
}

// ---------------------------------------------------------------- CryRandom, slot 24

#[test]
fn cryrandom_matches_xlcommon() {
    let cs: Vec<Case> = cases("p1a_misc")
        .into_iter()
        .filter(|c| c.probe == "cryrand")
        .collect();
    let mut fails = Vec::new();
    for c in &cs {
        let mut r = CryRandom::new(c.inp[0].parse().unwrap());
        let mut o = Vec::new();
        for _ in 0..c.out.len() / 2 {
            o.push(format!("{:08x}", r.next_u32()));
            o.push(hx(r.next_f32()));
        }
        check(c, &o, &mut fails);
    }
    finish("CryRandom", cs.len(), &fails);
}

struct Sizer(Vec<String>);
impl CrySizer for Sizer {
    fn add_object(&mut self, size: usize) {
        self.0.push("call6".into());
        self.0.push("1".into());
        self.0.push(format!("{size:x}"));
    }
}

#[test]
fn memory_statistics_match_the_oracle() {
    let cs: Vec<Case> = cases("p1a_misc")
        .into_iter()
        .filter(|c| c.probe == "mem")
        .collect();
    let mut fails = Vec::new();
    for c in &cs {
        let a = AnyGeom::of(c);
        let mut s = Sizer(Vec::new());
        memory_statistics(a.g(), &mut s);
        check(c, &s.0, &mut fails);
    }
    finish("GetMemoryStatistics", cs.len(), &fails);
}

// ---------------------------------------------------------------- slot 43

fn run_rpos(cs: &[Case]) -> Vec<String> {
    let mut fails = Vec::new();
    for c in cs {
        let a = AnyGeom::of(c);
        let mut rng = CryRandom::new(c.inp[0].parse().unwrap());
        let form = c.int(1);
        let mut o = Vec::new();
        for _ in 0..2 {
            let mut pn = PosNorm::default();
            a.g().get_random_pos(&mut pn, &mut rng, form);
            push_v(&mut o, pn.pt);
            push_v(&mut o, pn.n);
        }
        check(c, &o, &mut fails);
    }
    fails
}

#[test]
fn random_pos_matches_the_oracle() {
    let cs = cases("p1a_misc_rpos");
    let fails = run_rpos(&cs);
    finish("GetRandomPos", cs.len(), &fails);
}

#[test]
fn random_pos_fuzz() {
    let text = "grandpos type=box n=3000 seed=17101\ngrandpos type=ray n=1000 seed=17102\n\
                grandpos type=sphere n=3000 seed=17103\ngrandpos type=cylinder n=3000 seed=17104\n\
                grandpos type=capsule n=2000 seed=17105\n";
    if let Some(cs) = adhoc_cases(text) {
        let fails = run_rpos(&cs);
        finish("GetRandomPos fuzz", cs.len(), &fails);
    }
}

// ---------------------------------------------------------------- slot 51

/// `p1a_cube_fill` of the probe file.
fn cube_fill(fill: u32, k: u32) -> u32 {
    let mut x = k.wrapping_mul(0x9E37_79B1) ^ fill;
    x ^= x >> 16;
    x = x.wrapping_mul(0x85EB_CA6B);
    x ^= x >> 13;
    match fill & 3 {
        0 => 0x7FFF_FFFF,
        1 => x,
        2 => x & 0x8000_FFFF,
        _ => (x & 0x8000_0000) | 0x7FFF_FFFF,
    }
}

fn cube_init(res: i32, fill: u32) -> Cubemap {
    let mut cube = Cubemap::new(res, 0);
    let n = (res * res) as u32;
    for (f, face) in cube.faces.iter_mut().enumerate() {
        for (k, p) in face.iter_mut().enumerate() {
            *p = cube_fill(fill, f as u32 * n + k as u32);
        }
    }
    cube
}

fn cube_diff(before: &Cubemap, after: &Cubemap, o: &mut Vec<String>) {
    let n = (after.res * after.res) as usize;
    let mut pairs = Vec::new();
    for f in 0..6 {
        for k in 0..n {
            if after.faces[f][k] != before.faces[f][k] {
                pairs.push((
                    (f * n + k).to_string(),
                    format!("{:08x}", after.faces[f][k]),
                ));
            }
        }
    }
    o.push(pairs.len().to_string());
    for (a, b) in pairs {
        o.push(a);
        o.push(b);
    }
}

/// Rasteriser cases; returns the failures and the number of cases skipped because the
/// original wrote outside the cubemap (`oob`), crashed (`crash`) or did not finish within the
/// probe's timeout (`hang`) — only degenerate, non-planar point sets do that.
fn run_rast(cs: &[Case]) -> (Vec<String>, usize) {
    let mut fails = Vec::new();
    let mut skipped = 0;
    for c in cs {
        if c.out[0] == "oob" || c.out[0] == "crash" || c.out[0] == "hang" {
            skipped += 1;
            continue;
        }
        let pass: u32 = c.inp[0].parse().unwrap();
        let res = c.int(1);
        let fill: u32 = c.inp[2].parse().unwrap();
        let np = c.int(6) as usize;
        let pts: Vec<Vec3> = (0..np).map(|k| c.v(7 + 3 * k)).collect();
        let before = cube_init(res, fill);
        let mut cube = before.clone();
        rasterize_polygon(&pts, pass, &mut cube, c.f(3), c.f(4), c.f(5));
        let mut o = Vec::new();
        cube_diff(&before, &cube, &mut o);
        check(c, &o, &mut fails);
    }
    (fails, skipped)
}

#[test]
fn rasterizer_matches_the_oracle() {
    let cs = cases("p1a_misc_rast");
    let (fails, _) = run_rast(&cs);
    finish("sub_35131AB0", cs.len(), &fails);
}

#[test]
fn rasterizer_fuzz() {
    // planar convex polygons (ordinary and axis-aligned with exact zeros)
    let text = "grast n=6000 seed=17201 kinds=4\ngrast n=2000 seed=17202 res=3 kinds=4\n\
                grast n=1000 seed=17203 res=16 kinds=4\n";
    if let Some(cs) = adhoc_cases(text) {
        let (fails, skipped) = run_rast(&cs);
        assert_eq!(skipped, 0);
        finish("sub_35131AB0 fuzz", cs.len(), &fails);
    }
}

/// Degenerate input (non-planar point sets around the origin, antipodal / zero vertices,
/// 1e-6-sized polygons): some of these make the original spin for 2³² iterations of its
/// edge-advance loop (seconds in release, much longer in a debug build), so this runs only on
/// request: `cargo test --release -p aaemu-physics --test p1a_misc -- --ignored`.
#[test]
#[ignore = "slow: degenerate polygons; run with --release --ignored"]
fn rasterizer_degenerate_fuzz() {
    let text = "grast n=6000 seed=17211 kinds=0xF timeout=5000\n\
                grast n=3000 seed=17212 res=2 kinds=0xB timeout=5000\n\
                grast n=1000 seed=17221 kinds=16 timeout=5000\n";
    if let Some(cs) = adhoc_cases(text) {
        let (fails, skipped) = run_rast(&cs);
        eprintln!(
            "{} cases, {skipped} skipped (original wrote out of bounds, crashed or hung)",
            cs.len()
        );
        finish("sub_35131AB0 degenerate fuzz", cs.len(), &fails);
    }
}

fn run_cube(cs: &[Case]) -> Vec<String> {
    let mut fails = Vec::new();
    for c in cs {
        let a = AnyGeom::of(c);
        let pass: u32 = c.inp[0].parse().unwrap();
        let res = c.int(1);
        let fill: u32 = c.inp[2].parse().unwrap();
        let (rmin, rmax, zs) = (c.f(3), c.f(4), c.f(5));
        let gwd = c.gwd(6, false);
        let before = cube_init(res, fill);
        let mut cube = before.clone();
        let r = match &a {
            AnyGeom::Box(g) => box_draw_to_cubemap(g, &gwd, pass, &mut cube, rmin, rmax, zs),
            AnyGeom::Sphere(g) => sphere_draw_to_cubemap(g, &gwd, pass, &mut cube, rmin, rmax, zs),
            AnyGeom::Cylinder(g) => {
                cylinder_draw_to_cubemap(&g.0, &gwd, pass, &mut cube, rmin, rmax, zs)
            }
            AnyGeom::Capsule(g) => {
                cylinder_draw_to_cubemap(&g.0, &gwd, pass, &mut cube, rmin, rmax, zs)
            }
            AnyGeom::Ray(_) => 0,
        };
        let mut o = vec![r.to_string()];
        cube_diff(&before, &cube, &mut o);
        check(c, &o, &mut fails);
    }
    fails
}

#[test]
fn cubemap_slot_matches_the_oracle() {
    let cs = cases("p1a_misc_cube");
    let fails = run_cube(&cs);
    finish("slot 51", cs.len(), &fails);
}

#[test]
fn cubemap_slot_fuzz() {
    let text = "gcube type=box n=1500 seed=17301\ngcube type=sphere n=1500 seed=17302\n\
                gcube type=cylinder n=800 seed=17303\ngcube type=capsule n=800 seed=17304\n";
    if let Some(cs) = adhoc_cases(text) {
        let fails = run_cube(&cs);
        finish("slot 51 fuzz", cs.len(), &fails);
    }
}

// ---------------------------------------------------------------- slot 15

struct Rec<'a> {
    me: *const (),
    gwd: &'a GeomWorldData,
    out: Vec<String>,
}
impl PhysRenderer for Rec<'_> {
    fn draw_geometry(
        &mut self,
        geom: &dyn Geometry,
        gwd: &GeomWorldData,
        idx_color: i32,
        slow_fadein: i32,
        sweep_dir: Vec3,
    ) {
        let same = std::ptr::eq(geom as *const dyn Geometry as *const (), self.me);
        self.out.push("geom".into());
        self.out.push(i32::from(same).to_string());
        self.out
            .push(i32::from(std::ptr::eq(gwd, self.gwd)).to_string());
        self.out.push(idx_color.to_string());
        self.out.push(slow_fadein.to_string());
        push_v(&mut self.out, sweep_dir);
    }
    fn draw_line(&mut self, pt0: Vec3, pt1: Vec3, idx_color: i32, slow_fadein: i32) {
        self.out.push("line".into());
        self.out.push(idx_color.to_string());
        self.out.push(slow_fadein.to_string());
        push_v(&mut self.out, pt0);
        push_v(&mut self.out, pt1);
    }
}

fn run_wire(cs: &[Case]) -> Vec<String> {
    let mut fails = Vec::new();
    for c in cs {
        let a = AnyGeom::of(c);
        let (level, color) = (c.int(0), c.int(1));
        let gwd = c.gwd(2, false);
        let g = a.g();
        let mut r = Rec {
            me: g as *const dyn Geometry as *const (),
            gwd: &gwd,
            out: Vec::new(),
        };
        match &a {
            AnyGeom::Box(b) => primitive_draw_wireframe(g, &b.bv.box_, &mut r, &gwd, level, color),
            AnyGeom::Sphere(s) => {
                primitive_draw_wireframe(g, &s.bv.box_, &mut r, &gwd, level, color);
            }
            AnyGeom::Cylinder(y) => {
                primitive_draw_wireframe(g, &y.0.bv.box_, &mut r, &gwd, level, color);
            }
            AnyGeom::Capsule(y) => {
                primitive_draw_wireframe(g, &y.0.bv.box_, &mut r, &gwd, level, color);
            }
            AnyGeom::Ray(_) => base_draw_wireframe(g, &mut r, &gwd, color),
        }
        check(c, &r.out, &mut fails);
    }
    fails
}

#[test]
fn wireframe_matches_the_oracle() {
    let cs = cases("p1a_misc_wire");
    let fails = run_wire(&cs);
    finish("DrawWireframe", cs.len(), &fails);
}

#[test]
fn wireframe_fuzz() {
    let text = "gwire type=box n=1000 seed=17401\ngwire type=sphere n=600 seed=17402\n\
                gwire type=cylinder n=600 seed=17403\ngwire type=capsule n=400 seed=17404\n\
                gwire type=ray n=100 seed=17405\n";
    if let Some(cs) = adhoc_cases(text) {
        let fails = run_wire(&cs);
        finish("DrawWireframe fuzz", cs.len(), &fails);
    }
}
