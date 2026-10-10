//! P1c oracle scenarios: trimesh geometry in the real engine (`geom type=mesh` in the oracle,
//! see the crate README "Scenario language" and `oracle/p1c_mesh.c`).
//!
//! | golden | covers |
//! |---|---|
//! | `p1c_mesh_gen` | every mesh source (generators, mesh file) with `geominfo` + stored vertices / triangles; BV-tree choice flags; CreateMesh primitive approximation |
//! | `p1c_drop_meshgrid` / `_bumpy` / `_terrainmesh` | the primitive drop set (boxes incl. tilted and spinning, sphere, cylinders, spinning capsule) onto a static trimesh floor: flat grid, noisy grid (OBB tree), rolling trimesh terrain |
//! | `p1c_drop_terrain_flat` / `_slope` / `_bumps` | the same set onto heightfield terrain (world SetHeightfieldData) |
//! | `p1c_drop_cgf` * | primitives onto a real rock and a real castle tower |
//! | `p1c_mesh_on_meshgrid` / `_on_terrain` | rigid trimesh bodies (box, spinning box, icosphere by density, cylinder, OBB box) onto a trimesh floor / terrain; ends with their mass properties |
//! | `p1c_mesh_on_meshobj` | rigid trimesh bodies onto static trimesh objects (slab, big icosphere, lying cylinder, box) |
//! | `p1c_mesh_pair` | two rigid meshes colliding (thrown box, head-on icospheres, spinning box into a cylinder) |
//! | `p1c_cgf_rigid` * / `p1c_cgf_terrain` * | real corpus models as rigid bodies onto a trimesh floor, a static corpus rock, terrain |
//! | `p1c_rays_mesh` | RWI ray grids (vertical, slanted, grazing, from inside, from below) against static synthetic meshes, with the extended hit fields |
//! | `p1c_rays_cgf` * | the same against real corpus models |
//! | `p1c_rays_terrain` | the same against heightfield terrain |
//!
//! `*` = needs the model corpus (`AAEMU_PHYS_CGF_DIR`). Without it these scenarios are **not
//! regenerated** (the committed golden is still parsed and checked); game files are never
//! committed, the goldens are derived traces.
//!
//! With `AAEMU_PHYS_ORACLE` set every golden is regenerated from the DLL and must match byte for
//! byte (`AAEMU_PHYS_BLESS=1` rewrites), and each scenario is run twice to check determinism.
//!
//! # How later phases use these goldens
//!
//! (P1c-world) `tests/p1c_world_goldens.rs` replays every one of them on the port through the
//! scenario interpreter and compares the whole trace line by line.
//!
//! * **P1c (trimesh / heightfield port)**: `p1c_mesh_gen` pins what the port's CTriMesh build
//!   must reproduce from the same input: `Trace::geom_info(name)` (bbox from GetBBox, GetVolume,
//!   the phys_geometry V / inertia / q / origin) and `Trace::mesh_data(name)` (the vertices and
//!   the **tree-permuted** triangle order). [`gen`] below rebuilds the generator meshes in Rust
//!   bit for bit (asserted here), so a port test can feed the same arrays to its CreateMesh.
//! * **P3 (world, RWI)**: `p1c_rays_*` — build the world from the scenario (same entities,
//!   positions, part ids), cast the same rays (`org`, `dir`, `types`, `max`) and compare every
//!   [`Hit`](aaemu_physics::trace::Hit) field bit for bit, incl. `ext` (`iprim` is the triangle
//!   index in the stored order).
//! * **P3 + P4 (rigid bodies, solver)**: `p1c_drop_*`, `p1c_mesh_*`, `p1c_cgf_*` — step the port's
//!   world with dt = 1/30 (bits 0x3D088889), flags 0x9F, and compare
//!   `Trace::entity_states(name)` per step (pos, q, v, w, simclass, awake as raw bits; every step
//!   up to 90, then every 5th to 300). The final `ret status … 8` lines hold pe_status_dynamics
//!   words from +60 (centre of mass +60, mass +112). The bodies of one scenario are 3 m apart and
//!   form separate islands, so solver ordering inside an island does not mix them.

use aaemu_physics::math::Vec3;
use aaemu_physics::oracle::{golden_dir, oracle_path, run_scenario, scenario_dir, scenario_trace};
use aaemu_physics::trace::{EntityState, GeomInfo, Trace};

const SCENARIOS: &[&str] = &[
    "p1c_mesh_gen",
    "p1c_drop_meshgrid",
    "p1c_drop_bumpy",
    "p1c_drop_terrainmesh",
    "p1c_drop_terrain_flat",
    "p1c_drop_terrain_slope",
    "p1c_drop_terrain_bumps",
    "p1c_drop_cgf",
    "p1c_mesh_on_meshgrid",
    "p1c_mesh_on_meshobj",
    "p1c_mesh_on_terrain",
    "p1c_mesh_pair",
    "p1c_cgf_rigid",
    "p1c_cgf_terrain",
    "p1c_rays_mesh",
    "p1c_rays_cgf",
    "p1c_rays_terrain",
];

const DROP_SET: &[&str] = &[
    "box", "boxtilt", "boxspin", "ball", "cyl", "cyltilt", "capspin",
];

/// The `cgf=` paths a scenario needs (empty for synthetic scenarios).
fn cgf_paths(name: &str) -> Vec<String> {
    let text = std::fs::read_to_string(scenario_dir().join(format!("{name}.scn")))
        .unwrap_or_else(|e| panic!("{name}.scn: {e}"));
    text.lines()
        .filter(|l| !l.trim_start().starts_with('#'))
        .flat_map(|l| l.split_whitespace())
        .filter_map(|t| t.strip_prefix("cgf=").map(str::to_string))
        .collect()
}

/// The corpus files of a scenario are all present.
fn corpus_present(name: &str) -> bool {
    let paths = cgf_paths(name);
    if paths.is_empty() {
        return true;
    }
    let Some(dir) = std::env::var_os("AAEMU_PHYS_CGF_DIR") else {
        return false;
    };
    let dir = std::path::PathBuf::from(dir);
    paths
        .iter()
        .all(|p| dir.join(p.replace('\\', "/")).is_file())
}

/// The golden trace; regenerated (and byte-compared) through the oracle when it is configured,
/// except for corpus scenarios without the corpus, whose committed golden is used as is.
fn trace(name: &str) -> Trace {
    if oracle_path().is_some() && !corpus_present(name) {
        eprintln!("{name}: model corpus absent, oracle regeneration skipped");
        let text = std::fs::read_to_string(golden_dir().join(format!("{name}.trace")))
            .unwrap_or_else(|e| panic!("{name}: {e}"));
        return Trace::parse(&text.replace("\r\n", "\n")).unwrap_or_else(|e| panic!("{name}: {e}"));
    }
    scenario_trace(name).unwrap_or_else(|e| panic!("{name}: {e}"))
}

fn last(t: &Trace, ent: &str) -> EntityState {
    t.entity_states(ent)
        .last()
        .map(|(_, e)| (*e).clone())
        .unwrap_or_else(|| panic!("no states for {ent}"))
}

fn info(t: &Trace, name: &str) -> GeomInfo {
    t.geom_info(name)
        .unwrap_or_else(|| panic!("no geominfo for {name}"))
}

fn f(b: u32) -> f32 {
    f32::from_bits(b)
}

#[test]
fn every_p1c_golden_parses() {
    for name in SCENARIOS {
        let t = trace(name);
        assert!(
            !t.steps.is_empty() || !t.rays.is_empty() || !t.other.is_empty(),
            "{name}: empty"
        );
    }
}

#[test]
fn p1c_scenarios_are_deterministic() {
    let Some(exe) = oracle_path() else {
        return;
    };
    for name in SCENARIOS {
        if !corpus_present(name) {
            continue;
        }
        let scn = scenario_dir().join(format!("{name}.scn"));
        let a = run_scenario(&exe, &scn).unwrap_or_else(|e| panic!("{name}: {e}"));
        let b = run_scenario(&exe, &scn).unwrap_or_else(|e| panic!("{name}: {e}"));
        assert!(a == b, "{name}: two oracle runs differ");
    }
}

// ------------------------------------------------------------------ geometry

/// The oracle's mesh generators (`oracle/p1c_mesh.c`), rebuilt in Rust with the same single /
/// double precision steps, so a port can create the very same meshes.
pub mod gen {
    /// Vertices (x, y, z) and triangles in input order.
    pub type Mesh = (Vec<[f32; 3]>, Vec<[i32; 3]>);

    fn wrap(mut a: f64) -> f64 {
        let tau = std::f64::consts::TAU;
        while a > std::f64::consts::PI {
            a -= tau;
        }
        while a < -std::f64::consts::PI {
            a += tau;
        }
        a
    }
    /// Taylor series to x^25 (term by term, as the oracle).
    pub fn sin(x: f64) -> f64 {
        let (x2, mut term, mut sum) = (x * x, x, x);
        for k in 1..=12 {
            term = term * -x2 / f64::from((2 * k) * (2 * k + 1));
            sum += term;
        }
        sum
    }
    pub fn cos(x: f64) -> f64 {
        let (x2, mut term, mut sum) = (x * x, 1.0f64, 1.0f64);
        for k in 1..=12 {
            term = term * -x2 / f64::from((2 * k - 1) * (2 * k));
            sum += term;
        }
        sum
    }
    fn noise(seed: u64, i: i32, j: i32, amp: f32) -> f32 {
        let mut z = (seed ^ (u64::from(i as u32) << 32) ^ u64::from(j as u32))
            .wrapping_add(0x9E37_79B9_7F4A_7C15);
        z = (z ^ (z >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
        z = (z ^ (z >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
        z ^= z >> 31;
        let u = (z >> 40) as f32 * (1.0f32 / 16_777_216.0);
        amp * (2.0f32 * u - 1.0)
    }
    /// What the vertex heights come from.
    pub enum Height {
        Flat,
        Noise(u64, f32),
        /// amp, kx, ky
        Terrain(f32, f32, f32),
    }
    pub fn grid(nx: i32, ny: i32, sx: f32, sy: f32, z0: f32, h: Height) -> Mesh {
        let mut v = Vec::new();
        for j in 0..=ny {
            for i in 0..=nx {
                let x = sx * (i as f32 - 0.5f32 * nx as f32);
                let y = sy * (j as f32 - 0.5f32 * ny as f32);
                let z = match h {
                    Height::Flat => z0,
                    Height::Noise(seed, amp) => z0 + noise(seed, i, j, amp),
                    Height::Terrain(amp, kx, ky) => {
                        let (ax, ay) = (kx * x, ky * y);
                        z0 + amp * (sin(wrap(f64::from(ax))) * cos(wrap(f64::from(ay)))) as f32
                    }
                };
                v.push([x, y, z]);
            }
        }
        let mut t = Vec::new();
        for j in 0..ny {
            for i in 0..nx {
                let a = j * (nx + 1) + i;
                let (b, c, d) = (a + 1, a + nx + 2, a + nx + 1);
                t.push([a, b, c]);
                t.push([a, c, d]);
            }
        }
        (v, t)
    }
    pub fn boxm(h: [f32; 3]) -> Mesh {
        let v = (0..8)
            .map(|k| {
                [
                    if k & 1 != 0 { h[0] } else { -h[0] },
                    if k & 2 != 0 { h[1] } else { -h[1] },
                    if k & 4 != 0 { h[2] } else { -h[2] },
                ]
            })
            .collect();
        let t = vec![
            [0, 2, 3],
            [0, 3, 1],
            [4, 5, 7],
            [4, 7, 6],
            [0, 1, 5],
            [0, 5, 4],
            [2, 6, 7],
            [2, 7, 3],
            [0, 4, 6],
            [0, 6, 2],
            [1, 3, 7],
            [1, 7, 5],
        ];
        (v, t)
    }
    fn unit(v: &mut [f32; 3]) {
        let n2 = (v[0] * v[0] + v[1] * v[1]) + v[2] * v[2];
        let inv = 1.0f32 / n2.sqrt();
        for c in v.iter_mut() {
            *c *= inv;
        }
    }
    pub fn icosphere(r: f32, sub: i32) -> Mesh {
        #[allow(clippy::approx_constant)] // the oracle's literal
        let tt = 1.618034f32;
        let base = [
            [-1.0, tt, 0.0],
            [1.0, tt, 0.0],
            [-1.0, -tt, 0.0],
            [1.0, -tt, 0.0],
            [0.0, -1.0, tt],
            [0.0, 1.0, tt],
            [0.0, -1.0, -tt],
            [0.0, 1.0, -tt],
            [tt, 0.0, -1.0],
            [tt, 0.0, 1.0],
            [-tt, 0.0, -1.0],
            [-tt, 0.0, 1.0],
        ];
        let mut v: Vec<[f32; 3]> = base
            .iter()
            .map(|b| {
                let mut p = *b;
                unit(&mut p);
                p
            })
            .collect();
        let mut t: Vec<[i32; 3]> = vec![
            [0, 11, 5],
            [0, 5, 1],
            [0, 1, 7],
            [0, 7, 10],
            [0, 10, 11],
            [1, 5, 9],
            [5, 11, 4],
            [11, 10, 2],
            [10, 7, 6],
            [7, 1, 8],
            [3, 9, 4],
            [3, 4, 2],
            [3, 2, 6],
            [3, 6, 8],
            [3, 8, 9],
            [4, 9, 5],
            [2, 4, 11],
            [6, 2, 10],
            [8, 6, 7],
            [9, 8, 1],
        ];
        for _ in 0..sub {
            let old = std::mem::take(&mut t);
            let mut edges: Vec<(i32, i32, i32)> = Vec::new();
            for tri in &old {
                let mut mid = [0i32; 3];
                for e in 0..3 {
                    let (a, b) = (tri[e], tri[(e + 1) % 3]);
                    let (lo, hi) = (a.min(b), a.max(b));
                    let found = edges.iter().find(|x| x.0 == lo && x.1 == hi).map(|x| x.2);
                    mid[e] = found.unwrap_or_else(|| {
                        let (va, vb) = (v[a as usize], v[b as usize]);
                        let mut m = [
                            (va[0] + vb[0]) * 0.5f32,
                            (va[1] + vb[1]) * 0.5f32,
                            (va[2] + vb[2]) * 0.5f32,
                        ];
                        unit(&mut m);
                        v.push(m);
                        let id = v.len() as i32 - 1;
                        edges.push((lo, hi, id));
                        id
                    });
                }
                let [a, b, c] = *tri;
                t.push([a, mid[0], mid[2]]);
                t.push([b, mid[1], mid[0]]);
                t.push([c, mid[2], mid[1]]);
                t.push([mid[0], mid[1], mid[2]]);
            }
        }
        for p in &mut v {
            for c in p.iter_mut() {
                *c *= r;
            }
        }
        (v, t)
    }
    pub fn cylinder(r: f32, hh: f32, seg: i32) -> Mesh {
        let cs: Vec<(f32, f32)> = (0..seg)
            .map(|k| {
                let a = wrap(f64::from(k) * (std::f64::consts::TAU / f64::from(seg)));
                (cos(a) as f32, sin(a) as f32)
            })
            .collect();
        let mut v = Vec::new();
        for z in [-hh, hh] {
            for (c, s) in &cs {
                v.push([r * c, r * s, z]);
            }
        }
        v.push([0.0, 0.0, -hh]);
        v.push([0.0, 0.0, hh]);
        let (cb, ct) = (2 * seg, 2 * seg + 1);
        let mut t = Vec::new();
        for k in 0..seg {
            let k1 = (k + 1) % seg;
            t.push([k, k1, seg + k1]);
            t.push([k, seg + k1, seg + k]);
            t.push([cb, k1, k]);
            t.push([ct, seg + k, seg + k1]);
        }
        (v, t)
    }
}

/// Triangles as a sorted list, each rotated to start at its smallest index (winding kept).
fn canon(t: &[[i32; 3]]) -> Vec<[i32; 3]> {
    let mut out: Vec<[i32; 3]> = t
        .iter()
        .map(|&[a, b, c]| {
            if a <= b && a <= c {
                [a, b, c]
            } else if b <= a && b <= c {
                [b, c, a]
            } else {
                [c, a, b]
            }
        })
        .collect();
    out.sort_unstable();
    out
}

#[test]
fn generators_match_the_oracle_bit_for_bit() {
    use gen::Height;
    let t = trace("p1c_mesh_gen");
    let cases: Vec<(&str, gen::Mesh)> = vec![
        ("grid", gen::grid(3, 2, 1.5, 2.0, 0.25, Height::Flat)),
        (
            "noisy",
            gen::grid(4, 4, 1.0, 1.0, 0.0, Height::Noise(7, 0.25)),
        ),
        (
            "terrain",
            gen::grid(4, 3, 2.0, 2.0, 0.0, Height::Terrain(1.5, 0.3, 0.3)),
        ),
        ("box", gen::boxm([0.5, 0.7, 0.9])),
        ("ico0", gen::icosphere(1.0, 0)),
        ("ico2", gen::icosphere(0.5, 2)),
        ("cyl", gen::cylinder(0.4, 0.6, 7)),
        ("scaled", {
            let (v, t) = gen::boxm([1.0, 1.0, 1.0]);
            let v = v
                .iter()
                .map(|p| [p[0] * 0.5 + 1.0, p[1] * 0.5 + 2.0, p[2] * 0.5 + 3.0])
                .collect();
            (v, t)
        }),
    ];
    for (name, (v, tris)) in cases {
        let (ov, ot) = t
            .mesh_data(name)
            .unwrap_or_else(|| panic!("{name}: no dump"));
        let ov: Vec<[u32; 3]> = ov.iter().map(|p| p.to_bits()).collect();
        let gv: Vec<[u32; 3]> = v.iter().map(|p| p.map(f32::to_bits)).collect();
        assert_eq!(gv, ov, "{name}: vertices");
        assert_eq!(canon(&tris), canon(&ot), "{name}: triangle set");
        let g = info(&t, name);
        assert_eq!(
            (g.gtype, g.nprims, g.nverts),
            (1, tris.len() as i32, v.len() as i32),
            "{name}"
        );
    }
    // the wedge mesh file
    let (wv, wt) = t.mesh_data("wedge").unwrap();
    assert_eq!((wv.len(), wt.len()), (6, 8));
    assert_eq!(wv[5], Vec3::new(0.0, 0.5, 1.0));
}

#[test]
fn geometry_summaries() {
    let t = trace("p1c_mesh_gen");
    let b = info(&t, "box");
    assert_eq!(b.bbox_size(), Vec3::new(0.5, 0.7, 0.9));
    let v = 8.0 * 0.5 * 0.7 * 0.9;
    assert!((f(b.volume) - v).abs() < 1e-5, "closed box mesh volume");
    let ico = info(&t, "ico2");
    let sphere = 4.0 / 3.0 * std::f32::consts::PI * 0.125;
    assert!(
        f(ico.volume) > 0.9 * sphere && f(ico.volume) < sphere,
        "outward winding"
    );
    // wedge: area 0.5 x height 1
    assert!((f(info(&t, "wedge").volume) - 0.5).abs() < 1e-5);
    // tree choice: the chosen tree's bit is kept in +152; 0x40000 builds an AABB tree in the
    // principal axes (+152 shows 0x40, the bbox is oriented)
    let fl = |n: &str| info(&t, n).flags;
    assert_eq!(fl("t_obb"), 0x20);
    assert_eq!(fl("t_aabb"), 0x40);
    assert_eq!(fl("t_rot"), 0x40);
    assert_eq!(info(&t, "t_rot").bbox[9], 1, "oriented bbox");
    assert_eq!(info(&t, "t_aabb").bbox[9], 0);
    assert_eq!(fl("t_single"), 0x80);
    assert_eq!(fl("t_all"), 0x80);
    // CreateMesh turns a mesh into a primitive only with tol > 0 and the matching flag
    let ty = |n: &str| info(&t, n).gtype;
    assert_eq!((ty("a_box"), ty("a_cyl"), ty("a_sph")), (0, 5, 4));
    assert_eq!((ty("a_none"), ty("a_wrong"), ty("a_tol0")), (1, 1, 1));
    assert_eq!(info(&t, "a_box").bbox_size(), info(&t, "prim").bbox_size());
}

#[test]
fn corpus_models_load_through_load_phys_geometry() {
    let t = trace("p1c_rays_cgf");
    for (name, nt) in [("rock", 344), ("tower", 222), ("wall", 166), ("rrock", 186)] {
        let g = info(&t, name);
        assert_eq!((g.gtype, g.nprims), (1, nt), "{name}");
        // the blob's flags keep 0x8000 (set by the serializer) and the tree bit
        assert_ne!(g.flags & 0x8000, 0, "{name}");
        // the whole blob is consumed
        let l = t
            .other
            .iter()
            .find(|l| l[0] == "cgfload" && l[1] == name)
            .unwrap();
        assert_eq!(l[3], l[5], "{name}: blob fully read");
    }
    // LoadPhysGeometry takes V from the blob header: for open meshes it differs from GetVolume
    let tower = info(&t, "tower");
    assert_ne!(tower.volume, tower.pg_volume);
}

// ------------------------------------------------------------------ motion

/// Every state finite; at the end above `zmin` (+ the floor height at x, y), boxes asleep.
fn check_drop(
    name: &str,
    ents: &[&str],
    floor: impl Fn(f32, f32) -> f32,
    zmin: f32,
    sleepers: &[&str],
) {
    let t = trace(name);
    assert!(t.steps.len() > 100, "{name}: steps");
    for ent in ents {
        let states = t.entity_states(ent);
        assert!(states.len() > 100, "{name}/{ent}: states");
        for (step, s) in &states {
            assert!(
                s.pos_f().is_finite() && s.q_f().w.is_finite(),
                "{name}/{ent}: step {step}"
            );
        }
        let e = last(&t, ent);
        let p = e.pos_f();
        assert!(
            p.z > floor(p.x, p.y) + zmin,
            "{name}/{ent}: below the floor at the end: {p:?}"
        );
    }
    for ent in sleepers {
        let e = last(&t, ent);
        assert_eq!((e.awake, e.sim_class), (0, 1), "{name}/{ent}: not asleep");
        assert_eq!(e.v_f(), Some(Vec3::ZERO), "{name}/{ent}");
    }
}

#[test]
fn primitives_settle_on_trimesh_floors() {
    let boxes = &["box", "boxtilt", "boxspin"];
    check_drop("p1c_drop_meshgrid", DROP_SET, |_, _| 0.0, 0.2, DROP_SET);
    check_drop("p1c_drop_bumpy", DROP_SET, |_, _| -0.25, 0.2, boxes);
    check_drop("p1c_drop_terrainmesh", DROP_SET, |_, _| -1.5, 0.2, boxes);
    // flat mesh floor: rest heights = half extents minus a little penetration
    let t = trace("p1c_drop_meshgrid");
    for (ent, h) in [("box", 0.5), ("ball", 0.5), ("capspin", 0.3)] {
        let z = last(&t, ent).pos_f().z;
        assert!((z - h).abs() < 0.03, "{ent}: rest z {z}");
    }
}

#[test]
fn primitives_settle_on_heightfield_terrain() {
    let boxes = &["box", "boxtilt", "boxspin"];
    check_drop("p1c_drop_terrain_flat", DROP_SET, |_, _| 0.5, 0.2, DROP_SET);
    // h = 1 + 0.15 x + 0.05 y is linear, so exact inside every cell (round bodies roll downhill)
    check_drop(
        "p1c_drop_terrain_slope",
        DROP_SET,
        |x, y| 1.0 + 0.15 * x + 0.05 * y,
        0.0,
        boxes,
    );
    check_drop(
        "p1c_drop_terrain_bumps",
        DROP_SET,
        |_, _| 0.0,
        0.2,
        DROP_SET,
    );
    let z = last(&trace("p1c_drop_terrain_flat"), "box").pos_f().z;
    assert!((z - 1.0).abs() < 0.03, "box on h = 0.5: {z}");
}

#[test]
fn primitives_on_corpus_models() {
    // rock (top ~12 m) and tower stand on a box ground at z = 0; balls roll off onto the ground
    let ents = ["rbox", "rball", "rcap", "rcyl", "tbox", "tball", "tcap"];
    check_drop(
        "p1c_drop_cgf",
        &ents,
        |_, _| 0.0,
        0.2,
        &["rbox", "tbox", "tball"],
    );
}

const MESH_SET: &[&str] = &["mbox", "mboxspin", "mico", "mcyl", "mobb"];

/// pe_status_dynamics words dumped from +60: mass is +112.
fn status_mass(t: &Trace, ent: &str) -> f32 {
    let l = t
        .rets
        .iter()
        .find(|r| r[0] == "status" && r[1] == ent && r[2] == "8")
        .unwrap_or_else(|| panic!("no status for {ent}"));
    f(u32::from_str_radix(&l[4 + 13], 16).unwrap())
}

#[test]
fn rigid_meshes_settle() {
    check_drop("p1c_mesh_on_meshgrid", MESH_SET, |_, _| 0.0, 0.2, MESH_SET);
    check_drop(
        "p1c_mesh_on_terrain",
        MESH_SET,
        |x, y| 1.0 + 0.15 * x + 0.05 * y,
        0.0,
        MESH_SET,
    );
    let objs = ["onslab", "onico", "onlog", "onblock"];
    check_drop("p1c_mesh_on_meshobj", &objs, |_, _| 0.0, 0.2, &objs);
    let pair = ["rest", "thrown", "icoA", "icoB", "spinner", "target"];
    check_drop("p1c_mesh_pair", &pair, |_, _| 0.0, 0.2, &pair);
    let t = trace("p1c_mesh_on_meshgrid");
    // mesh mass: mass= as given, density= times the mesh volume
    assert_eq!(status_mass(&t, "mbox"), 10.0);
    assert_eq!(status_mass(&t, "mcyl"), 20.0);
    let v_ico = f(info(&t, "mico").pg_volume);
    assert!((status_mass(&t, "mico") - 1000.0 * v_ico).abs() < 0.01);
    let v_obb = f(info(&t, "mobb").pg_volume);
    assert!((status_mass(&t, "mobb") - 500.0 * v_obb).abs() < 0.01);
    // a mesh box rests on its half extent like the primitive box
    let z = last(&t, "mbox").pos_f().z;
    assert!((z - 0.5).abs() < 0.03, "mesh box rest z {z}");
    // the thrown box moved the resting one only a little and both sleep
    let t = trace("p1c_mesh_pair");
    assert!(last(&t, "thrown").pos_f().x > 55.0);
}

#[test]
fn corpus_rigid_bodies_settle() {
    let ents = ["rockb", "barrel", "crate", "gas"];
    check_drop("p1c_cgf_rigid", &ents, |_, _| 0.0, 0.05, &ents);
    let ents = ["rockb", "barrel", "crate"];
    check_drop("p1c_cgf_terrain", &ents, |_, _| 0.0, 0.05, &ents);
    let t = trace("p1c_cgf_rigid");
    assert_eq!(status_mass(&t, "barrel"), 30.0);
    let v = f(info(&t, "rockb").pg_volume);
    assert!((status_mass(&t, "rockb") - 2500.0 * v).abs() / (2500.0 * v) < 1e-5);
}

// ------------------------------------------------------------------ rays

fn bary_inside(p: [f32; 2], a: Vec3, b: Vec3, c: Vec3) -> bool {
    let d = |p: [f32; 2], u: Vec3, v: Vec3| (v.x - u.x) * (p[1] - u.y) - (v.y - u.y) * (p[0] - u.x);
    let (d1, d2, d3) = (d(p, a, b), d(p, b, c), d(p, c, a));
    let eps = 1e-4;
    (d1 >= -eps && d2 >= -eps && d3 >= -eps) || (d1 <= eps && d2 <= eps && d3 <= eps)
}

#[test]
fn rays_against_synthetic_meshes() {
    let t = trace("p1c_rays_mesh");
    let (fv, ft) = t.mesh_data("floor").expect("floor dump");
    let mut n_fl = 0;
    for r in t.rays.iter().filter(|r| {
        ["fl_", "flup_", "flsl_"]
            .iter()
            .any(|p| r.name.starts_with(p))
    }) {
        if r.name == "fl_graze" {
            continue;
        }
        assert_eq!(r.n_hits, 1, "{}", r.name);
        let h = &r.hits[0];
        assert_eq!(
            (h.entity.as_str(), h.part_id, h.surface),
            ("floor", 11, -1),
            "{}",
            r.name
        );
        let p = Vec3::from_bits(h.pt);
        assert!(p.z.abs() <= 0.3, "{}: {p:?}", r.name);
        let n = Vec3::from_bits(h.n);
        assert!(
            (n.len() - 1.0).abs() < 1e-3 && n.z > 0.5,
            "{}: normal {n:?}",
            r.name
        );
        // +64 is the triangle in the stored (tree-permuted) order: the hit lies in it
        let x = h.ext.expect("ext");
        let tri = ft[x.iprim as usize];
        let local = [p.x - 64.0, p.y - 64.0];
        assert!(
            bary_inside(
                local,
                fv[tri[0] as usize],
                fv[tri[1] as usize],
                fv[tri[2] as usize]
            ),
            "{}: hit outside stored triangle {}",
            r.name,
            x.iprim
        );
        // a trimesh is two-sided for rays: upward rays from below hit with the same (+z) normal
        n_fl += 1;
    }
    assert_eq!(n_fl, 36 + 9 + 9);
    // rays from inside a closed mesh hit its far side (no inside test)
    for name in ["bx_in_x", "bx_in_y", "bx_in_d", "ic_in", "ic_in2"] {
        let r = t.ray(name).unwrap();
        assert_eq!(r.n_hits, 1, "{name}");
        assert!(f(r.hits[0].dist) < 2.5, "{name}");
    }
    // the outward normal is reported even from inside
    let n = Vec3::from_bits(t.ray("ic_in").unwrap().hits[0].n);
    assert!(n.z > 0.9);
    // straight down the icosphere's centre: top at z <= 3 + r
    let h = &t.ray("ic_2_2").unwrap().hits[0];
    let z = Vec3::from_bits(h.pt).z;
    assert!(h.entity == "ico" && z > 4.8 && z <= 5.0, "ico top {z}");
    assert_eq!(t.ray("no_static").unwrap().n_hits, 0);
    assert_eq!(t.ray("static_only").unwrap().n_hits, 1);
    assert_eq!(
        t.ray("multi").unwrap().hits[0].entity,
        "mbox",
        "closest hit first"
    );
}

#[test]
fn rays_against_heightfield_terrain() {
    let t = trace("p1c_rays_terrain");
    let hgt = |ix: i32, iy: i32| 0.3 * ((ix * 7 + iy * 13) % 5) as f32;
    for r in t.rays.iter().filter(|r| r.name.starts_with("v_")) {
        assert_eq!(r.n_hits, 1, "{}", r.name);
        let h = &r.hits[0];
        assert_eq!(h.entity, "terrain");
        let p = Vec3::from_bits(h.pt);
        let (ix, iy) = (p.x.floor() as i32, p.y.floor() as i32);
        let corners = [
            hgt(ix, iy),
            hgt(ix + 1, iy),
            hgt(ix, iy + 1),
            hgt(ix + 1, iy + 1),
        ];
        let (lo, hi) = corners
            .iter()
            .fold((f32::MAX, f32::MIN), |(a, b), &c| (a.min(c), b.max(c)));
        assert!(
            p.z >= lo - 1e-4 && p.z <= hi + 1e-4,
            "{}: z {} not in [{lo}, {hi}]",
            r.name,
            p.z
        );
        assert_eq!(h.ext.unwrap().w60, 1, "terrain hits carry 1 at +60");
    }
    // the heightfield is one-sided: rays from below the surface going up miss
    for r in t.rays.iter().filter(|r| r.name.starts_with("up_")) {
        assert_eq!(r.n_hits, 0, "{}", r.name);
    }
    assert_eq!(t.ray("terrain_only").unwrap().n_hits, 1);
    assert_eq!(t.ray("no_terrain").unwrap().n_hits, 0);
    assert_eq!(
        t.ray("graze1").unwrap().n_hits,
        0,
        "passes above the highest bump"
    );
}

#[test]
fn rays_against_corpus_models() {
    let t = trace("p1c_rays_cgf");
    let hits = t.rays.iter().filter(|r| r.n_hits > 0).count();
    assert!(hits > 60, "{hits} hits");
    for r in t.rays.iter().filter(|r| r.n_hits > 0) {
        let h = &r.hits[0];
        let x = h.ext.expect("ext");
        // corpus meshes carry per-triangle material ids (both int16s)
        assert_eq!(x.w24, x.w26, "{}", r.name);
        assert!(x.w24 > 0, "{}", r.name);
        let n = Vec3::from_bits(h.n);
        assert!((n.len() - 1.0).abs() < 1e-3, "{}", r.name);
    }
    // a ray from inside the rock hits its top from inside
    let h = &t.ray("rk_in").unwrap().hits[0];
    assert!(h.entity == "rock" && Vec3::from_bits(h.n).z > 0.9);
}
