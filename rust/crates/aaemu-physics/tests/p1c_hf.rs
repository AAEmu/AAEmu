//! P1c heightfield against the DLL: `tests/p1c_hf_probe.c` creates `CHeightfield`s on generated
//! grids (flat / slope / bumpy / random heights, random surface types with holes, several
//! steps, height scales and type masks), then prints, as raw bits, `SetData`'s result,
//! `PointInsideStatus` on random points, sequences of slot-53 region builds (with the region
//! mesh, BV slots 2/3/4/9/11, slot-25 marks and slot-23 calls) and ray `Intersect`s. Every line
//! is `inputs | outputs`; the test replays the inputs on the port and compares the outputs.
//!
//! | env | effect |
//! |---|---|
//! | none | the goldens `tests/golden/p1c_hf_seed*.trace` |
//! | `AAEMU_PHYS_P1C_PROBE_DIR=<dir>` (with `p1c_hf_probe.exe`) | goldens regenerated and must match |
//! | + `AAEMU_PHYS_BLESS=1` | goldens rewritten |
//! | + `AAEMU_PHYS_P1C_HF_STRESS=<n>` | also `n` extra seeds × 8 grids straight from the DLL |

use std::cell::RefCell;

use aaemu_physics::collision::contact::{GeomContact, IntersectionParams};
use aaemu_physics::collision::swept::UnprojTables;
use aaemu_physics::geometry::bvtree::{BvArena, BvTestState, BvTree};
use aaemu_physics::geometry::heightfield::region::{HfFrame, OtherSweep, RegionOutcome};
use aaemu_physics::geometry::heightfield::{Heightfield, RayIntersectInput};
use aaemu_physics::geometry::primitives::{BoxPrim, HeightfieldPrim, RayPrim};
use aaemu_physics::geometry::shapes::RayGeom;
use aaemu_physics::geometry::GeomWorldData;
use aaemu_physics::math::{Mat33, Vec3};

mod p1c_common;

const PAD: i32 = 3;

struct Tables {
    nx: i32,
    ny: i32,
    h: Vec<f32>,
    s: Vec<u16>,
}

thread_local! {
    static GRID: RefCell<Option<Tables>> = const { RefCell::new(None) };
}

fn tidx(t: &Tables, x: i32, y: i32) -> usize {
    let x = x.clamp(-PAD, t.nx + PAD);
    let y = y.clamp(-PAD, t.ny + PAD);
    ((y + PAD) * (t.nx + 2 * PAD + 1) + (x + PAD)) as usize
}

fn cb_height(x: i32, y: i32) -> f32 {
    GRID.with(|g| {
        let g = g.borrow();
        let t = g.as_ref().unwrap();
        t.h[tidx(t, x, y)]
    })
}

fn cb_surf(x: i32, y: i32) -> u16 {
    GRID.with(|g| {
        let g = g.borrow();
        let t = g.as_ref().unwrap();
        t.s[tidx(t, x, y)]
    })
}

/// Token reader.
struct Tok<'a> {
    t: Vec<&'a str>,
    i: usize,
}

impl<'a> Tok<'a> {
    fn new(s: &'a str) -> Self {
        Self {
            t: s.split_whitespace().collect(),
            i: 0,
        }
    }
    fn s(&mut self) -> &'a str {
        let v = self.t[self.i];
        self.i += 1;
        v
    }
    fn i(&mut self) -> i32 {
        self.s().parse().unwrap()
    }
    fn w(&mut self) -> u32 {
        u32::from_str_radix(self.s(), 16).unwrap()
    }
    fn f(&mut self) -> f32 {
        f32::from_bits(self.w())
    }
    fn v(&mut self) -> Vec3 {
        Vec3::new(self.f(), self.f(), self.f())
    }
    fn m(&mut self) -> Mat33 {
        Mat33::from_array(std::array::from_fn(|_| self.f()))
    }
    fn bx(&mut self) -> BoxPrim {
        let basis = self.m();
        let oriented = self.w() as i32;
        BoxPrim {
            basis,
            oriented,
            center: self.v(),
            size: self.v(),
        }
    }
    fn frame(&mut self) -> HfFrame {
        HfFrame {
            stop: self.i(),
            r: self.m(),
            r_rel: self.m(),
            offset: self.v(),
            offset_rel: self.v(),
            scale_rel_r: self.f(),
        }
    }
    fn gwd(&mut self) -> GeomWorldData {
        GeomWorldData {
            offset: self.v(),
            r: self.m(),
            scale: self.f(),
            v: self.v(),
            ..GeomWorldData::default()
        }
    }
}

fn hx(out: &mut String, w: u32) {
    use std::fmt::Write as _;
    let _ = write!(out, " {w:08x}");
}
fn hf32(out: &mut String, f: f32) {
    hx(out, f.to_bits());
}
fn hv(out: &mut String, v: Vec3) {
    for c in [v.x, v.y, v.z] {
        hf32(out, c);
    }
}
fn dec(out: &mut String, v: i64) {
    use std::fmt::Write as _;
    let _ = write!(out, " {v}");
}

/// The 25 words of a heightfield primitive as the DLL stores it (callback pointers excluded).
fn prim_words(out: &mut String, p: &HeightfieldPrim) {
    for f in p.basis.to_array() {
        hf32(out, f);
    }
    hx(out, p.oriented as u32);
    hv(out, p.origin);
    for f in p.step.iter().chain(p.stepr.iter()) {
        hf32(out, *f);
    }
    for i in p.size.iter().chain(p.stride.iter()) {
        hx(out, *i as u32);
    }
    hf32(out, p.height_scale);
    hx(
        out,
        u32::from(p.type_mask) | (u32::from(p.height_mask) << 16),
    );
    hx(out, p.type_hole as u32);
    hx(out, p.type_power as u32);
}

fn box_words(out: &mut String, b: &BoxPrim) {
    for f in b.basis.to_array() {
        hf32(out, f);
    }
    hx(out, b.oriented as u32);
    hv(out, b.center);
    hv(out, b.size);
}

#[derive(Default)]
struct Stats {
    lines: usize,
    by_kind: std::collections::BTreeMap<&'static str, usize>,
    ray_hits: usize,
}

/// Replays one probe trace; panics at the first differing output.
fn replay(trace: &str, what: &str) -> Stats {
    let tables = UnprojTables::build();
    let mut st = Stats::default();
    let mut hf: Option<Heightfield> = None;
    let mut arena = BvArena::new();
    let mut gt = BvTestState::default();
    let lines: Vec<&str> = trace.lines().collect();
    let mut li = 0;
    let mut src = HeightfieldPrim::default();
    while li < lines.len() {
        let line = lines[li];
        li += 1;
        let (inp, want) = match line.split_once(" |") {
            Some((a, b)) => (a, Some(b.trim_start())),
            None => (line, None),
        };
        let mut t = Tok::new(inp);
        let tag = t.s();
        let mut out = String::new();
        let kind: &'static str = match tag {
            "grid" => {
                let nx = t.i();
                let ny = t.i();
                src = HeightfieldPrim {
                    basis: t.m(),
                    oriented: t.i(),
                    origin: t.v(),
                    step: [t.f(), t.f()],
                    stepr: [t.f(), t.f()],
                    size: [nx, ny],
                    stride: [t.i(), t.i()],
                    height_scale: t.f(),
                    type_mask: t.i() as u16,
                    height_mask: t.i() as u16,
                    type_hole: t.i(),
                    type_power: t.i(),
                    get_height: Some(cb_height),
                    get_surf_type: Some(cb_surf),
                };
                let hl = Tok::new(lines[li]).t[1..]
                    .iter()
                    .map(|s| f32::from_bits(u32::from_str_radix(s, 16).unwrap()))
                    .collect();
                let sl = Tok::new(lines[li + 1]).t[1..]
                    .iter()
                    .map(|s| s.parse().unwrap())
                    .collect();
                li += 2;
                GRID.with(|g| {
                    *g.borrow_mut() = Some(Tables {
                        nx,
                        ny,
                        h: hl,
                        s: sl,
                    });
                });
                hf = Some(Heightfield::create(&mut src).unwrap());
                continue;
            }
            "end" => continue,
            "create" => {
                let h = hf.as_ref().unwrap();
                prim_words(&mut out, h.prim());
                hf32(&mut out, src.stepr[0]);
                hf32(&mut out, src.stepr[1]);
                hf32(&mut out, h.mesh.min_vtx_dist);
                hx(&mut out, h.mesh.priority as u32);
                hf32(&mut out, h.z_limits[0]);
                hf32(&mut out, h.z_limits[1]);
                hx(&mut out, h.vtx_cap as u32);
                hx(&mut out, h.tri_cap as u32);
                dec(&mut out, h.mesh.n_tris.into());
                dec(&mut out, h.mesh.n_verts.into());
                "create"
            }
            "pis" => {
                let p = t.v();
                dec(&mut out, hf.as_ref().unwrap().point_inside_status(p).into());
                "pis"
            }
            "reg" => {
                let h = hf.as_mut().unwrap();
                let b = t.bx();
                let has_frame = t.i() != 0;
                let mut frame = has_frame.then(|| t.frame());
                let sweep = (t.i() != 0).then(|| OtherSweep {
                    dir: t.v(),
                    flag: t.f(),
                    len: t.f(),
                });
                let r = h.prepare_region(&b, sweep, frame.as_mut());
                dec(
                    &mut out,
                    match r {
                        RegionOutcome::Reject => 0,
                        RegionOutcome::Done => 1,
                        RegionOutcome::CallMeshSlot53 => panic!("probe frames have stop = 1"),
                    },
                );
                if let Some(f) = frame {
                    hv(&mut out, f.offset);
                }
                for v in [h.bv.ix, h.bv.iy, h.bv.w, h.bv.h] {
                    dec(&mut out, v.into());
                }
                hf32(&mut out, h.bv.hmin);
                hf32(&mut out, h.bv.hmax);
                for v in [
                    h.mesh.n_tris,
                    h.mesh.n_verts,
                    h.vtx_cap,
                    h.tri_cap,
                    h.mesh.max_vertex_valency,
                ] {
                    dec(&mut out, v.into());
                }
                "region (slot 53)"
            }
            "mesh" => {
                let h = hf.as_ref().unwrap();
                let m = &h.mesh;
                let (nv, nt) = (m.n_verts as usize, m.n_tris as usize);
                for v in &m.vertices[..nv] {
                    hv(&mut out, *v);
                }
                for i in &m.indices[..3 * nt] {
                    hx(&mut out, *i as u32);
                }
                for n in &m.normals[..nt] {
                    hv(&mut out, *n);
                }
                for x in &m.mats.as_ref().unwrap()[..nt] {
                    dec(&mut out, (*x).into());
                }
                for tp in &m.topology[..nt] {
                    for x in tp {
                        hx(&mut out, *x as u32);
                    }
                }
                let words = if nt > 0 { ((nt - 1) >> 5) + 1 } else { 0 };
                for w in &h.bv.used_mask()[..words] {
                    hx(&mut out, *w);
                }
                "region mesh"
            }
            "bv2" => {
                let h = hf.as_ref().unwrap();
                box_words(&mut out, &h.bv.bbox());
                dec(&mut out, h.bv.max_prims_in_node().into());
                hf32(&mut out, h.bv.build());
                "BV slots 2/3/4"
            }
            "bv11" => {
                let h = hf.as_ref().unwrap();
                let _ = h.bv.get_node_bv(&mut arena, 0);
                hx(&mut out, arena.hf_bv.kind as u32);
                hx(&mut out, arena.hf_bv.i_node as u32);
                prim_words(&mut out, &arena.hf_bv.hf);
                dec(&mut out, 0);
                "BV slot 11"
            }
            "bv9" => {
                let h = hf.as_ref().unwrap();
                let r = t.m();
                let off = t.v();
                let s = t.f();
                let _ = h.bv.get_node_bv_transformed(&mut arena, &r, off, s, 0);
                hx(&mut out, arena.hf_bv.kind as u32);
                hx(&mut out, arena.hf_bv.i_node as u32);
                prim_words(&mut out, &arena.hf_bv.hf);
                "BV slot 9"
            }
            "mark" => {
                let h = hf.as_ref().unwrap();
                while t.i < t.t.len() {
                    h.bv.mark_used_triangle(t.i(), &mut gt, &mut arena);
                }
                continue;
            }
            "s23" => {
                let h = hf.as_ref().unwrap();
                let ck = t.i();
                let b = t.bx();
                let used = t.i() != 0;
                let local = t.i() != 0;
                let fr = t.frame();
                let calls = h.bv.node_contents(h.tri_cap, ck, &b, used, local, &fr);
                let ret: i32 = calls.iter().map(|c| c.n_prims & 3).sum();
                dec(&mut out, ret.into());
                dec(&mut out, calls.len() as i64);
                for c in &calls {
                    use std::fmt::Write as _;
                    let _ = write!(out, " {}:{}", c.first_prim, c.n_prims);
                }
                "BV slot 23"
            }
            "ray" => {
                let h = hf.as_mut().unwrap();
                let g1 = t.gwd();
                let has_g2 = t.i() != 0;
                let g2 = t.gwd();
                let rp = RayPrim {
                    origin: t.v(),
                    dir: t.v(),
                };
                let has_ip = t.i() != 0;
                let mode = t.i();
                let ctr = t.v();
                let axis = t.v();
                let ti = t.f();
                let keep = t.i() != 0;
                let swap = t.i() != 0;
                let before = t.i() as usize;
                let ip = IntersectionParams {
                    i_unprojection_mode: mode,
                    center_of_rotation: ctr,
                    axis_of_rotation: axis,
                    time_interval: ti,
                    keep_prev_contacts: keep,
                    ..IntersectionParams::default()
                };
                h.mesh.priority = if swap { 0 } else { 1 };
                let ray = RayGeom::new(&rp);
                let mut pool = vec![GeomContact::default(); before];
                let input = RayIntersectInput {
                    ray: &ray,
                    gwd_hf: &g1,
                    gwd_ray: has_g2.then_some(&g2),
                    ip: has_ip.then_some(&ip),
                    draw_ray_limit: Some(1000),
                };
                let r = h.intersect_ray(&input, &tables, &mut pool);
                h.mesh.priority = 1;
                dec(&mut out, r.into());
                dec(&mut out, pool.len() as i64);
                if r != 0 {
                    st.ray_hits += 1;
                    let c = pool.last().unwrap();
                    dec(&mut out, pool.len() as i64 - 1);
                    let tb = c.t.to_bits();
                    hx(&mut out, tb as u32);
                    hx(&mut out, (tb >> 32) as u32);
                    hv(&mut out, c.pt);
                    hv(&mut out, c.n);
                    hv(&mut out, c.dir);
                    dec(&mut out, c.i_unproj_mode.into());
                    hf32(&mut out, c.vel);
                    for v in [c.id[0], c.id[1], c.i_feature[0], c.i_feature[1]] {
                        dec(&mut out, v.into());
                    }
                    hv(&mut out, c.center);
                    dec(&mut out, c.border.len() as i64);
                    dec(&mut out, i64::from(c.border == [c.center]));
                    if has_ip {
                        dec(&mut out, 1);
                    }
                }
                "ray Intersect"
            }
            other => panic!("{what}: unknown line tag {other}"),
        };
        let want = want.unwrap_or_else(|| panic!("{what}: line {li} has no outputs"));
        let got = out.trim_start();
        if got != want {
            panic!("{what}: line {li} ({kind})\n  in:   {inp}\n  port: {got}\n  dll:  {want}");
        }
        st.lines += 1;
        *st.by_kind.entry(kind).or_default() += 1;
    }
    st
}

fn run_seed(seed: u32, grids: u32) {
    let name = format!("p1c_hf_seed{seed}");
    let (s, g) = (seed.to_string(), grids.to_string());
    let Some(trace) = p1c_common::golden(&name, "p1c_hf_probe", &["run", &s, &g]) else {
        eprintln!("{name}: no golden and no probe; skipped");
        return;
    };
    let st = replay(&trace, &name);
    eprintln!(
        "{name}: {} lines {:?}, {} ray hits",
        st.lines, st.by_kind, st.ray_hits
    );
    assert!(st.lines > 500);
}

#[test]
fn seed1() {
    run_seed(1, 4);
}

#[test]
fn seed2() {
    run_seed(2, 4);
}

#[test]
fn seed3() {
    run_seed(3, 4);
}

#[test]
fn seed4() {
    run_seed(4, 4);
}

/// Extra seeds straight from the DLL (no goldens): `AAEMU_PHYS_P1C_HF_STRESS=<n>`.
#[test]
fn stress() {
    let Some(n) = std::env::var("AAEMU_PHYS_P1C_HF_STRESS")
        .ok()
        .and_then(|v| v.parse::<u32>().ok())
    else {
        return;
    };
    let Some(exe) = p1c_common::probe("p1c_hf_probe") else {
        return;
    };
    let mut total = Stats::default();
    for seed in 100..100 + n {
        let trace = p1c_common::run_probe(&exe, &["run", &seed.to_string(), "8"]).unwrap();
        let st = replay(&trace, &format!("stress seed {seed}"));
        total.lines += st.lines;
        total.ray_hits += st.ray_hits;
        for (k, v) in st.by_kind {
            *total.by_kind.entry(k).or_default() += v;
        }
    }
    eprintln!(
        "stress: {} lines {:?}, {} ray hits",
        total.lines, total.by_kind, total.ray_hits
    );
}
