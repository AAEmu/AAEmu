//! P1c-core: the `CTriMesh` constructor (0x351CF1B0), topology (sub_351BFA60), islands
//! (sub_351C0D80), `IsConvex` (slot 21), `Save` (slot 25) and `Load` (slot 26) against the DLL.
//!
//! The reference is `tests/p1c_core_probe.c` (`p1c_core_probe.exe` in
//! `AAEMU_PHYS_P1C_PROBE_DIR`; goldens `tests/golden/p1c_core_ctor_*.trace` otherwise). Each case
//! builds a mesh with the DLL and with [`TriMesh::from_data`] and compares:
//! the full mesh dump (tree bytes the DLL leaves uninitialised masked), the bad-edge count of the
//! counting topology pass, the `Save` bytes (the 16 uninitialised pad bytes and the tree's
//! uninitialised bytes masked), then the DLL's saved bytes loaded by the DLL and by
//! [`TriMesh::load`] (identical dump, identical re-save, pad included), and a sequence of
//! `IsConvex` calls with the resulting cache.

mod p1c_common;

use aaemu_physics::geometry::bvtree::{MemReader, MemWriter};
use aaemu_physics::geometry::trimesh::build::{MeshDesc, MeshIndices};
use aaemu_physics::geometry::trimesh::stream::RenderArrays;
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::math::Vec3;
use p1c_common::{assert_same_text, dump_mesh, hex, mask_dump, mask_tree_hex, unhex};
use std::fmt::Write;

const PROBE: &str = "p1c_core_probe";

/// One constructor case.
struct Case {
    name: String,
    v: Vec<Vec3>,
    idx: Vec<i32>,
    flags: u32,
    min_tris: i32,
    max_tris: i32,
    obb: f32,
    i32_idx: bool,
    extras: &'static str,
}

fn lcg(seed: &mut u32) -> u32 {
    *seed = seed.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
    *seed >> 8
}

fn cube(c: Vec3, h: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for k in 0..8 {
        v.push(Vec3::new(
            c.x + if k & 1 != 0 { h } else { -h },
            c.y + if k & 2 != 0 { h } else { -h },
            c.z + if k & 4 != 0 { h } else { -h },
        ));
    }
    let q = [
        [0, 2, 3, 1],
        [4, 5, 7, 6],
        [0, 1, 5, 4],
        [2, 6, 7, 3],
        [0, 4, 6, 2],
        [1, 3, 7, 5],
    ];
    let mut idx = Vec::new();
    for f in q {
        idx.extend([f[0], f[1], f[2], f[0], f[2], f[3]]);
    }
    (v, idx)
}

/// The cube with every face's vertices duplicated (24 vertices; welding merges them).
fn split_cube() -> (Vec<Vec3>, Vec<i32>) {
    let (v, idx) = cube(Vec3::new(0.5, -1.0, 2.0), 1.5);
    let nv: Vec<Vec3> = idx.iter().map(|&i| v[i as usize]).collect();
    let ni = (0..idx.len() as i32).collect();
    (nv, ni)
}

/// A closed sphere-like mesh (latitude/longitude, poles shared).
fn sphere(n_lat: i32, n_lon: i32, r: f32) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = vec![Vec3::new(0.0, 0.0, r)];
    for i in 1..n_lat {
        let th = std::f64::consts::PI * f64::from(i) / f64::from(n_lat);
        for j in 0..n_lon {
            let ph = 2.0 * std::f64::consts::PI * f64::from(j) / f64::from(n_lon);
            v.push(Vec3::new(
                (f64::from(r) * th.sin() * ph.cos()) as f32,
                (f64::from(r) * th.sin() * ph.sin() * 0.7) as f32,
                (f64::from(r) * th.cos()) as f32,
            ));
        }
    }
    v.push(Vec3::new(0.0, 0.0, -r));
    let last = v.len() as i32 - 1;
    let at = |i: i32, j: i32| 1 + (i - 1) * n_lon + (j % n_lon);
    let mut idx = Vec::new();
    for j in 0..n_lon {
        idx.extend([0, at(1, j), at(1, j + 1)]);
        idx.extend([last, at(n_lat - 1, j + 1), at(n_lat - 1, j)]);
    }
    for i in 1..n_lat - 1 {
        for j in 0..n_lon {
            idx.extend([at(i, j), at(i + 1, j), at(i + 1, j + 1)]);
            idx.extend([at(i, j), at(i + 1, j + 1), at(i, j + 1)]);
        }
    }
    (v, idx)
}

/// An open height grid with quantised heights (repeated coordinates).
fn grid(nx: i32, ny: i32, seed: u32) -> (Vec<Vec3>, Vec<i32>) {
    let mut s = seed;
    let mut v = Vec::new();
    for y in 0..=ny {
        for x in 0..=nx {
            let h = (lcg(&mut s) % 5) as f32 * 0.25;
            v.push(Vec3::new(x as f32 * 0.5, y as f32 * 0.5, h));
        }
    }
    let w = nx + 1;
    let mut idx = Vec::new();
    for y in 0..ny {
        for x in 0..nx {
            let a = y * w + x;
            idx.extend([a, a + 1, a + w + 1, a, a + w + 1, a + w]);
        }
    }
    (v, idx)
}

/// A random soup on a coarse lattice: duplicate vertices (incl. −0.0), degenerate triangles,
/// shared and non-manifold edges.
fn soup(nv: usize, nt: usize, seed: u32) -> (Vec<Vec3>, Vec<i32>) {
    let mut s = seed;
    let c = |s: &mut u32| {
        let q = (lcg(s) % 7) as i32 - 3;
        if q == 0 && lcg(s).is_multiple_of(2) {
            -0.0
        } else {
            q as f32 * 0.75
        }
    };
    let v: Vec<Vec3> = (0..nv)
        .map(|_| Vec3::new(c(&mut s), c(&mut s), c(&mut s)))
        .collect();
    let idx = (0..3 * nt)
        .map(|_| (lcg(&mut s) % nv as u32) as i32)
        .collect();
    (v, idx)
}

fn merge(a: (Vec<Vec3>, Vec<i32>), b: (Vec<Vec3>, Vec<i32>)) -> (Vec<Vec3>, Vec<i32>) {
    let (mut v, mut i) = a;
    let off = v.len() as i32;
    v.extend(b.0);
    i.extend(b.1.into_iter().map(|x| x + off));
    (v, i)
}

/// A fin: three triangles on one edge, plus an open strip.
fn fin() -> (Vec<Vec3>, Vec<i32>) {
    let v = vec![
        Vec3::new(0.0, 0.0, 0.0),
        Vec3::new(0.0, 0.0, 1.0),
        Vec3::new(1.0, 0.0, 0.0),
        Vec3::new(-1.0, 0.0, 0.5),
        Vec3::new(0.0, 1.0, 0.25),
        Vec3::new(0.5, -1.0, 0.75),
        Vec3::new(2.0, 0.5, 0.0),
    ];
    let idx = vec![0, 1, 2, 1, 0, 3, 0, 1, 4, 5, 1, 0, 2, 1, 6, 6, 1, 2];
    (v, idx)
}

#[allow(clippy::too_many_arguments)]
fn case(
    name: &'static str,
    m: (Vec<Vec3>, Vec<i32>),
    flags: u32,
    min_tris: i32,
    max_tris: i32,
    obb: f32,
    i32_idx: bool,
    extras: &'static str,
) -> Case {
    Case {
        name: name.to_string(),
        v: m.0,
        idx: m.1,
        flags,
        min_tris,
        max_tris,
        obb,
        i32_idx,
        extras,
    }
}

fn cases() -> Vec<Case> {
    let c0 = Vec3::new(0.0, 0.0, 0.0);
    vec![
        case("cube_aabb", cube(c0, 1.0), 0x40, 2, 4, 1.0, false, "-"),
        case("cube_obb_mats", cube(c0, 1.0), 0x20, 2, 4, 1.0, false, "m"),
        case(
            "cube_single",
            cube(Vec3::new(1.0, 2.0, 3.0), 0.5),
            0x80,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case(
            "split_cube_map",
            split_cube(),
            0x8040,
            2,
            4,
            1.0,
            false,
            "mf",
        ),
        case(
            "split_cube_map2",
            split_cube(),
            0x10060,
            1,
            3,
            1.5,
            false,
            "-",
        ),
        case(
            "split_cube_noweld",
            split_cube(),
            0x20040,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case(
            "split_cube_novalency",
            split_cube(),
            0x80040,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case(
            "sphere_principal",
            sphere(5, 8, 2.0),
            0x40000,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case(
            "sphere_all",
            sphere(6, 9, 1.5),
            0x40060,
            2,
            6,
            1.0,
            false,
            "f",
        ),
        case(
            "sphere_single",
            sphere(4, 7, 1.0),
            0x80,
            2,
            4,
            1.0,
            true,
            "-",
        ),
        case(
            "grid_aabb_flat",
            grid(6, 5, 7),
            0x4140,
            2,
            4,
            1.0,
            false,
            "m",
        ),
        case(
            "grid_obb_noskip",
            grid(5, 4, 9),
            0x420,
            1,
            2,
            0.8,
            true,
            "-",
        ),
        case(
            "grid_i32_inplace",
            grid(4, 4, 3),
            0x42,
            2,
            4,
            1.0,
            true,
            "-",
        ),
        case("soup_a", soup(24, 40, 1), 0x40, 2, 4, 1.0, false, "mf"),
        case(
            "soup_keep_degenerate",
            soup(24, 40, 2),
            0x2000040,
            2,
            4,
            1.0,
            false,
            "m",
        ),
        case(
            "soup_obb_map",
            soup(30, 50, 3),
            0x8020,
            2,
            3,
            1.0,
            false,
            "f",
        ),
        case(
            "soup_shared_foreign",
            soup(20, 30, 4),
            0x4c,
            2,
            4,
            1.0,
            false,
            "m",
        ),
        case(
            "soup_identity_foreign",
            soup(20, 30, 5),
            0x48,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case(
            "two_cubes",
            merge(cube(c0, 1.0), cube(Vec3::new(5.0, 0.0, 0.0), 1.0)),
            0x40,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case(
            "two_cubes_single",
            merge(cube(c0, 1.0), cube(Vec3::new(0.0, 4.0, 1.0), 0.5)),
            0x80,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case("fin", fin(), 0x40, 2, 4, 1.0, false, "-"),
        case(
            "shared_vertices",
            cube(c0, 2.0),
            0x41,
            2,
            4,
            1.0,
            false,
            "-",
        ),
        case("no_tree", cube(c0, 1.0), 0x0, 2, 4, 1.0, false, "-"),
        case(
            "cube_and_sphere_all",
            merge(sphere(4, 6, 1.0), split_cube()),
            0x18060,
            2,
            4,
            1.2,
            false,
            "mf",
        ),
    ]
}

fn desc_mats(nt: usize) -> Vec<i8> {
    (0..nt as i32).map(|t| ((t * 7) % 11 - 5) as i8).collect()
}

fn line<'a>(lines: &mut impl Iterator<Item = &'a str>, tag: &str) -> Vec<&'a str> {
    let l = lines.next().unwrap_or_else(|| panic!("missing {tag} line"));
    let f: Vec<&str> = l.split(' ').collect();
    assert_eq!(f[0], tag, "expected {tag}: {l}");
    f
}

/// The dump block (mesh … tree) from a line iterator.
fn take_dump<'a>(lines: &mut impl Iterator<Item = &'a str>) -> String {
    let mut s = String::new();
    for l in lines.by_ref() {
        s += l;
        s += "\n";
        if l.starts_with("tree") {
            break;
        }
    }
    s
}

/// Saved bytes with the pad (16 bytes before `u32 0`, the convex cache and the tree) and the
/// tree's uninitialised bytes masked; `tree_len` from the port.
fn mask_save(b: &[u8], tree_len: usize, tree_type: i32) -> String {
    let n = b.len();
    if n < tree_len + 56 {
        return hex(b);
    }
    let pad = n - tree_len - 4 - 32 - 4 - 16;
    let mut h = hex(&b[..pad]);
    h += &"-".repeat(32);
    h += &hex(&b[pad + 16..n - tree_len]);
    h += &mask_tree_hex(tree_type, &hex(&b[n - tree_len..]));
    h
}

/// The probe output with every byte the DLL leaves uninitialised masked: tree bytes in the
/// dumps, and in `save` / `resave` lines the 16 pad bytes and the tree's bytes (the tree is the
/// one of the preceding dump).
fn mask_probe_output(text: &str) -> String {
    let mut out = String::new();
    let mut tree: (usize, i32) = (0, -1);
    for l in text.lines() {
        let f: Vec<&str> = l.split(' ').collect();
        if f[0] == "tree" && f.len() == 5 {
            tree = (f[3].parse().unwrap_or(0), f[1].parse().unwrap_or(-1));
            out += &mask_dump(l);
            continue;
        }
        if (f[0] == "save" || f[0] == "resave") && f.len() == 3 {
            out += &format!(
                "{} {} {}\n",
                f[0],
                f[1],
                mask_save(&unhex(f[2]), tree.0, tree.1)
            );
            continue;
        }
        out += l;
        out += "\n";
    }
    out
}

/// Like `p1c_common::golden`, but stored and fresh probe output are compared masked
/// ([`mask_probe_output`]); blessing rewrites only when the masked texts differ.
fn masked_golden(name: &str, args: &[&str]) -> Option<String> {
    let path = p1c_common::golden_dir().join(format!("{name}.trace"));
    let stored = std::fs::read_to_string(&path)
        .ok()
        .map(|s| s.replace("\r\n", "\n"));
    let Some(exe) = p1c_common::probe(PROBE) else {
        return stored;
    };
    let fresh = p1c_common::run_probe(&exe, args).unwrap_or_else(|e| panic!("{e}"));
    let bless = std::env::var_os("AAEMU_PHYS_BLESS").is_some_and(|v| v != "0");
    match &stored {
        Some(s) if mask_probe_output(s) == mask_probe_output(&fresh) => {}
        _ if bless || stored.is_none() => std::fs::write(&path, &fresh).unwrap(),
        _ => assert_same_text(
            &format!("{name} (golden is stale; AAEMU_PHYS_BLESS=1 rewrites)"),
            &mask_probe_output(stored.as_deref().unwrap_or_default()),
            &mask_probe_output(&fresh),
        ),
    }
    Some(fresh)
}

fn tree_len_type(m: &TriMesh) -> (usize, i32) {
    m.tree.as_ref().map_or((0, -1), |t| {
        let mut w = MemWriter::new();
        t.as_dyn().save(&mut w);
        (w.pos, t.as_dyn().tree_type().id())
    })
}

fn ctor_args(c: &Case) -> Vec<String> {
    let path = p1c_common::write_mesh_file(&c.name, &c.v, &c.idx);
    vec![
        "ctor".to_string(),
        path.to_string_lossy().into_owned(),
        format!("{:#x}", c.flags),
        c.min_tris.to_string(),
        c.max_tris.to_string(),
        format!("{:08x}", c.obb.to_bits()),
        if c.i32_idx { "i32" } else { "u16" }.to_string(),
        c.extras.to_string(),
    ]
}

/// A golden-backed case.
fn run_case(c: &Case) {
    let args = ctor_args(c);
    let argv: Vec<&str> = args.iter().map(String::as_str).collect();
    let Some(dll) = masked_golden(&format!("p1c_core_ctor_{}", c.name), &argv) else {
        eprintln!("p1c_core: no golden and no probe for {} — skipped", c.name);
        return;
    };
    check_ctor(c, &dll);
}

/// A live-only case (no golden).
fn run_case_live(c: &Case, exe: &std::path::Path) {
    let args = ctor_args(c);
    let argv: Vec<&str> = args.iter().map(String::as_str).collect();
    let dll = p1c_common::run_probe(exe, &argv).unwrap_or_else(|e| panic!("{}: {e}", c.name));
    check_ctor(c, &dll);
}

/// Compares the port with the probe's `ctor` output.
fn check_ctor(c: &Case, dll: &str) {
    let nt = c.idx.len() / 3;
    let mats = desc_mats(nt);
    let fids: Vec<i32> = (0..nt as i32).map(|t| nt as i32 - 1 - t).collect();
    let idx16: Vec<u16> = c.idx.iter().map(|&i| i as u16).collect();
    let mut port = TriMesh::from_data(&MeshDesc {
        vertices: &c.v,
        indices: if c.i32_idx {
            MeshIndices::I32(&c.idx)
        } else {
            MeshIndices::U16(&idx16)
        },
        mats: c.extras.contains('m').then_some(&mats[..]),
        foreign_idx: c.extras.contains('f').then_some(&fids[..]),
        n_tris: nt as i32,
        flags: c.flags,
        min_tris: c.min_tris,
        max_tris: c.max_tris,
        obb_factor: c.obb,
    });
    let mut lines = dll.lines();
    let what = |s: &str| format!("{} {s}", c.name);
    assert_same_text(
        &what("ctor"),
        &mask_dump(&dump_mesh(&port)),
        &mask_dump(&take_dump(&mut lines)),
    );

    let f = line(&mut lines, "badedges");
    assert_eq!(
        port.count_bad_edges(&port.indices),
        f[1].parse::<i32>().unwrap(),
        "{}",
        what("badedges")
    );

    // Save (pad and uninitialised tree bytes masked).
    let f = line(&mut lines, "save");
    let dll_saved = if f[1] == "-" { Vec::new() } else { unhex(f[2]) };
    let port_saved = port.save_to_vec();
    let (tree_len, tree_type) = tree_len_type(&port);
    if port.tree.is_some() {
        assert_same_text(
            &what("save"),
            &mask_save(&port_saved, tree_len, tree_type),
            &mask_save(&dll_saved, tree_len, tree_type),
        );
    }

    // The DLL's bytes loaded by both.
    let f = line(&mut lines, "loaded");
    let no_tree = f[1] == "-";
    let dll_read: usize = f[1].parse().unwrap_or(0);
    let dll_loaded = if no_tree {
        String::new()
    } else {
        take_dump(&mut lines)
    };
    let f = line(&mut lines, "resave");
    let dll_resaved = f.get(2).map_or(String::new(), |s| (*s).to_string());
    let _ = &dll_resaved;
    if !no_tree {
        let mut r = MemReader::new(&dll_saved);
        let ra = RenderArrays {
            vertices: Some(&c.v),
            indices: Some(&idx16),
            mats: c.extras.contains('m').then_some(&mats[..]),
        };
        let loaded = TriMesh::load(&mut r, &ra).unwrap_or_else(|e| panic!("{}: {e}", what("load")));
        assert_eq!(r.pos, dll_read, "{}", what("load length"));
        // p1c_mesh_dump zeroes the never-written node bytes even of loaded trees: mask both.
        assert_same_text(
            &what("load"),
            &mask_dump(&dump_mesh(&loaded)),
            &mask_dump(&dll_loaded),
        );
        // The DLL writes fresh stack garbage as the pad; the port re-writes the loaded pad.
        let (tl, tt) = tree_len_type(&loaded);
        let resaved = loaded.save_to_vec();
        assert_same_text(
            &what("resave"),
            &mask_save(&resaved, tl, tt),
            &mask_save(&unhex(&dll_resaved), tl, tt),
        );
    }

    // IsConvex sequence and cache.
    let f = line(&mut lines, "convex");
    let mut s = String::from("convex");
    for t in [0.02f32, 0.1, 0.0, 0.5, 0.02, 1.0, 0.1, 0.25, 0.3, 0.0] {
        let _ = write!(s, " {}", port.is_convex(t));
    }
    assert_eq!(s, f.join(" "), "{}", what("convex"));
    let f = line(&mut lines, "convcache");
    let mut s = String::from("convcache");
    for k in 0..4 {
        let _ = write!(s, " {:08x}", port.convex_cache[k] as u32);
    }
    for k in 0..4 {
        let _ = write!(s, " {:08x}", port.convex_tol[k].to_bits());
    }
    assert_eq!(s, f.join(" "), "{}", what("convcache"));
}

#[test]
fn p1c_core_constructor_save_load_match_dll() {
    let cs = cases();
    for c in &cs {
        run_case(c);
    }
    eprintln!("p1c_core: {} constructor cases", cs.len());
}

/// Deterministic random meshes × flag combinations, compared live with the DLL (no goldens;
/// skipped without the probe). `AAEMU_P1C_RANDOM=<n>` sets the case count (default 400).
#[test]
fn p1c_core_random_constructor_live() {
    let Some(exe) = p1c_common::probe(PROBE) else {
        eprintln!("p1c_core: random cases need the probe (AAEMU_PHYS_P1C_PROBE_DIR) — skipped");
        return;
    };
    let n: u32 = std::env::var("AAEMU_P1C_RANDOM")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(400);
    let trees = [
        0x40u32, 0x20, 0x80, 0x40000, 0x60, 0x40040, 0x40060, 0x400a0, 0x0,
    ];
    let extra_flags = [
        0u32, 0x8000, 0x10000, 0x20000, 0x80000, 0x2000000, 0x100, 0x400, 0x4000, 0x4, 0x8,
        0x100000,
    ];
    let mut seed = 0x1234_5678u32;
    for k in 0..n {
        let kind = lcg(&mut seed) % 6;
        let m = match kind {
            0 => soup(
                4 + (lcg(&mut seed) % 60) as usize,
                1 + (lcg(&mut seed) % 120) as usize,
                lcg(&mut seed),
            ),
            1 => grid(
                1 + (lcg(&mut seed) % 12) as i32,
                1 + (lcg(&mut seed) % 12) as i32,
                lcg(&mut seed),
            ),
            2 => sphere(
                3 + (lcg(&mut seed) % 6) as i32,
                3 + (lcg(&mut seed) % 9) as i32,
                0.5 + (lcg(&mut seed) % 8) as f32 * 0.5,
            ),
            3 => merge(split_cube(), soup(10, 15, lcg(&mut seed))),
            4 => merge(
                cube(Vec3::new(0.0, 0.0, 0.0), 1.0),
                sphere(4, 5, 0.5 + (lcg(&mut seed) % 4) as f32),
            ),
            _ => merge(grid(3, 3, lcg(&mut seed)), fin()),
        };
        let mut flags = trees[(lcg(&mut seed) % trees.len() as u32) as usize];
        for _ in 0..2 {
            flags |= extra_flags[(lcg(&mut seed) % extra_flags.len() as u32) as usize];
        }
        let i32_idx = lcg(&mut seed).is_multiple_of(3);
        if i32_idx && lcg(&mut seed).is_multiple_of(2) {
            flags |= 2;
        }
        let extras = ["-", "m", "f", "mf"][(lcg(&mut seed) % 4) as usize];
        let min_tris = 1 + (lcg(&mut seed) % 3) as i32;
        let max_tris = min_tris + (lcg(&mut seed) % 6) as i32;
        let obb = [1.0f32, 0.5, 1.5, 2.0][(lcg(&mut seed) % 4) as usize];
        let mut c = case("random", m, flags, min_tris, max_tris, obb, i32_idx, extras);
        c.name = format!("random{k}_{flags:x}");
        run_case_live(&c, &exe);
    }
    eprintln!("p1c_core: {n} random constructor cases identical to the DLL");
}

// ---------------------------------------------------------------- corpus

/// A trimesh phys blob of a CGF with the render-mesh arrays of the mesh chunk referencing it.
struct CorpusBlob {
    name: String,
    blob: Vec<u8>,
    pos: Vec<Vec3>,
    idx: Vec<u16>,
    mats: Vec<i8>,
}

fn rd32(b: &[u8], o: usize) -> u32 {
    u32::from_le_bytes([b[o], b[o + 1], b[o + 2], b[o + 3]])
}

/// Chunk table: `(type, offset, id)`.
fn chunks(d: &[u8]) -> Vec<(u32, usize, i32)> {
    if d.len() < 20 || &d[..6] != b"CryTek" {
        return Vec::new();
    }
    let ver = rd32(d, 12);
    let to = rd32(d, 16) as usize;
    let n = rd32(d, to) as usize;
    let es = if ver == 0x744 { 16 } else { 20 };
    (0..n)
        .filter_map(|i| {
            let e = to + 4 + i * es;
            (e + 16 <= d.len())
                .then(|| (rd32(d, e), rd32(d, e + 8) as usize, rd32(d, e + 12) as i32))
        })
        .collect()
}

fn walk_cgf(d: &std::path::Path, out: &mut Vec<std::path::PathBuf>) {
    let Ok(rd) = std::fs::read_dir(d) else {
        return;
    };
    let mut v: Vec<_> = rd.flatten().map(|e| e.path()).collect();
    v.sort();
    for p in v {
        if p.is_dir() {
            walk_cgf(&p, out);
        } else if p.extension().is_some_and(|e| e.eq_ignore_ascii_case("cgf")) {
            out.push(p);
        }
    }
}

/// Every mesh chunk's physics blobs (slots 0..3) that hold a trimesh, with the render arrays
/// `CStatObj` passes (`sub_30047F40`): positions (stream 0, stride 12), indices (stream 5,
/// 16-bit) and per-face material ids from the subsets (`matId` of each subset's faces).
fn corpus_blobs(dir: &std::path::Path) -> Vec<CorpusBlob> {
    let mut files = Vec::new();
    walk_cgf(dir, &mut files);
    let mut out = Vec::new();
    for f in files {
        let Ok(d) = std::fs::read(&f) else {
            continue;
        };
        let ch = chunks(&d);
        let by_id = |id: i32| ch.iter().find(|c| c.2 == id).copied();
        for &(t, o, cid) in &ch {
            if t != 0xCCCC_0000 || o + 16 + 108 > d.len() {
                continue;
            }
            let h = o + 16;
            let n_idx = rd32(&d, h + 12) as usize;
            let sub_id = rd32(&d, h + 20) as i32;
            let sid = |k: usize| rd32(&d, h + 28 + 4 * k) as i32;
            let stream = |k: usize| -> Option<(usize, usize, usize)> {
                let (_, so, _) = by_id(sid(k)).filter(|_| sid(k) > 0)?;
                Some((
                    rd32(&d, so + 24) as usize,
                    rd32(&d, so + 28) as usize,
                    so + 40,
                ))
            };
            let pos: Vec<Vec3> = stream(0)
                .filter(|s| s.1 == 12)
                .map(|(n, _, at)| {
                    (0..n)
                        .map(|i| {
                            let f = |k: usize| f32::from_bits(rd32(&d, at + 12 * i + 4 * k));
                            Vec3::new(f(0), f(1), f(2))
                        })
                        .collect()
                })
                .unwrap_or_default();
            let idx: Vec<u16> = stream(5)
                .filter(|s| s.1 == 2)
                .map(|(n, _, at)| {
                    (0..n)
                        .map(|i| u16::from_le_bytes([d[at + 2 * i], d[at + 2 * i + 1]]))
                        .collect()
                })
                .unwrap_or_default();
            let mut mats = vec![0i8; n_idx / 3];
            if let Some((_, so, _)) = by_id(sub_id).filter(|_| sub_id > 0) {
                let ns = rd32(&d, so + 20) as usize;
                for k in 0..ns {
                    let r = so + 32 + 36 * k;
                    if r + 36 > d.len() {
                        break;
                    }
                    let first = rd32(&d, r) as usize / 3;
                    let num = rd32(&d, r + 4) as usize / 3;
                    let mat = rd32(&d, r + 16);
                    for m in mats.iter_mut().skip(first).take(num) {
                        *m = mat as i8;
                    }
                }
            }
            for slot in 0..4 {
                let pid = rd32(&d, h + 92 + 4 * slot) as i32;
                let Some((_, po, _)) = by_id(pid).filter(|_| pid > 0) else {
                    continue;
                };
                let size = rd32(&d, po + 16) as usize;
                if po + 40 + size > d.len() || size < 72 {
                    continue;
                }
                let blob = d[po + 40..po + 40 + size].to_vec();
                if rd32(&blob, 0) != 1 || rd32(&blob, 68) != 1 {
                    continue;
                }
                out.push(CorpusBlob {
                    name: format!(
                        "{}#mesh{cid}#slot{slot}",
                        f.file_name().unwrap_or_default().to_string_lossy()
                    ),
                    blob,
                    pos: pos.clone(),
                    idx: idx.clone(),
                    mats: mats.clone(),
                });
            }
        }
    }
    out
}

fn corpus_dir() -> Option<std::path::PathBuf> {
    std::env::var_os("AAEMU_PHYS_CGF_DIR").map(std::path::PathBuf::from)
}

/// Does the blob take its geometry from the render mesh (foreign ids, no 0x400000)?
fn blob_is_shared(b: &[u8]) -> bool {
    let mut r = MemReader::new(&b[72..]);
    let nv = r.u32().unwrap_or(0) as usize;
    let _ = r.u32();
    let _ = r.u32();
    let flags = r.u32().unwrap_or(0);
    let has_map = r.u8().unwrap_or(0) != 0;
    if has_map && r.skip(2 * nv).is_err() {
        return false;
    }
    r.u8().unwrap_or(0) != 0 && flags & 0x40_0000 == 0
}

fn batch_file(batch: &[CorpusBlob]) -> Vec<u8> {
    let mut f = Vec::new();
    f.extend((batch.len() as u32).to_le_bytes());
    for b in batch {
        let sh = blob_is_shared(&b.blob);
        f.extend((b.blob.len() as u32).to_le_bytes());
        f.extend(&b.blob);
        let (pos, idx, mats): (&[Vec3], &[u16], &[i8]) = if sh {
            (&b.pos, &b.idx, &b.mats)
        } else {
            (&[], &[], &[])
        };
        f.extend((pos.len() as u32).to_le_bytes());
        for p in pos {
            for c in [p.x, p.y, p.z] {
                f.extend(c.to_bits().to_le_bytes());
            }
        }
        f.extend((idx.len() as u32).to_le_bytes());
        for i in idx {
            f.extend(i.to_le_bytes());
        }
        f.extend((mats.len() as u32).to_le_bytes());
        f.extend(mats.iter().map(|&m| m as u8));
    }
    f
}

/// Why a port load+save does not reproduce the original blob.
fn diff_reason(m: &TriMesh, saved: &[u8], orig: &[u8]) -> String {
    let mut norm = saved.to_vec();
    if norm.len() >= 16 && orig.len() >= 16 {
        norm[12..16].copy_from_slice(&orig[12..16]);
    }
    if norm == orig {
        "identical except the flags word (Load adds 0x8000)".into()
    } else if m.n_tris == 0 {
        "degenerate single box (Load stops early; nTris saved as 0)".into()
    } else if rd32(orig, 0) as i32 != m.n_verts {
        "vertex trimming (vertices below +600 dropped, indices shifted)".into()
    } else if m.vtx_map.is_some() {
        "vertex map applied to the indices (and flags | 0x8000)".into()
    } else if saved[8..12] != orig[8..12] {
        "valency below 3 recounted".into()
    } else {
        let at = saved
            .iter()
            .zip(orig)
            .position(|(x, y)| x != y)
            .unwrap_or(saved.len());
        format!("other (first difference at mesh byte {at})")
    }
}

/// Every trimesh blob of the corpus loaded (slot 26, render arrays for the shared ones) and
/// saved (slot 25) by the DLL and the port: identical loaded state and identical saved bytes
/// (the DLL's 16 pad bytes are fresh stack garbage, masked); also counts how many port saves
/// reproduce the original blob bytes.
#[test]
fn p1c_core_corpus_load_save_match_dll() {
    use aaemu_physics::geometry::shapes::stream::{
        load_phys_geometry_any, save_phys_geometry_mesh, AnyGeom,
    };
    let (Some(exe), Some(dir)) = (p1c_common::probe(PROBE), corpus_dir()) else {
        eprintln!("p1c_core: corpus Load/Save needs the probe and AAEMU_PHYS_CGF_DIR — skipped");
        return;
    };
    let blobs = corpus_blobs(&dir);
    let tmp = std::env::temp_dir().join(format!("aaemu-p1c-core-{}", std::process::id()));
    std::fs::create_dir_all(&tmp).unwrap();
    let (mut same_dll, mut same_orig, mut shared) = (0usize, 0usize, 0usize);
    let mut reasons: std::collections::BTreeMap<String, usize> = std::collections::BTreeMap::new();
    for (bi, batch) in blobs.chunks(64).enumerate() {
        let path = tmp.join(format!("batch{bi}.bin"));
        std::fs::write(&path, batch_file(batch)).unwrap();
        let text = p1c_common::run_probe(&exe, &["blobs", &path.to_string_lossy()]).unwrap();
        let _ = std::fs::remove_file(&path);
        let mut lines = text.lines();
        for b in batch {
            let what = |s: &str| format!("{} {s}", b.name);
            let hdr = line(&mut lines, "blob");
            let dll_read: usize = hdr[3].parse().unwrap();
            let dll_dump = take_dump(&mut lines);
            let dll_saved = unhex(line(&mut lines, "save")[2]);
            let sh = blob_is_shared(&b.blob);
            shared += usize::from(sh);
            let ra = RenderArrays {
                vertices: sh.then_some(&b.pos[..]),
                indices: sh.then_some(&b.idx[..]),
                mats: sh.then_some(&b.mats[..]),
            };
            let pg = load_phys_geometry_any(&b.blob, &ra)
                .unwrap_or_else(|e| panic!("{}: {e:?}", what("load")));
            let AnyGeom::TriMesh(m) = &pg.geom else {
                panic!("{}: not a mesh", b.name)
            };
            assert_same_text(
                &what("load"),
                &mask_dump(&dump_mesh(m)),
                &mask_dump(&dll_dump),
            );
            if m.n_tris > 0 || dll_read == pg.len {
                assert_eq!(pg.len, dll_read, "{}", what("bytes read"));
            }
            let saved = m.save_to_vec();
            let (tl, tt) = tree_len_type(m);
            assert_same_text(
                &what("save"),
                &mask_save(&saved, tl, tt),
                &mask_save(&dll_saved, tl, tt),
            );
            same_dll += 1;
            let mut full = Vec::new();
            save_phys_geometry_mesh(&pg.raw_header, m, &mut full);
            if full.len() <= b.blob.len() && full == b.blob[..full.len()] {
                same_orig += 1;
            } else {
                let end = pg.len.max(72);
                *reasons
                    .entry(diff_reason(m, &saved, &b.blob[72..end]))
                    .or_default() += 1;
            }
        }
    }
    eprintln!(
        "p1c_core corpus: {} trimesh blobs ({shared} with render-mesh geometry); load+save identical to the DLL: {same_dll}; port load+save reproduces the original blob: {same_orig}",
        blobs.len()
    );
    for (r, n) in &reasons {
        eprintln!("  differs from the original: {r}: {n}");
    }
    assert_eq!(same_dll, blobs.len());
}

/// Every non-shared corpus mesh rebuilt from its stored vertices / indices with its stored
/// flags (minus 0x8000) by the DLL constructor and [`TriMesh::from_data`] (tree parameters of
/// the stored tree), plus Save / Load / IsConvex as in the synthetic cases.
/// `AAEMU_P1C_CORPUS_LIMIT` caps the count.
#[test]
fn p1c_core_corpus_constructor_match_dll() {
    use aaemu_physics::geometry::bvtree::select::MeshTree;
    let (Some(exe), Some(dir)) = (p1c_common::probe(PROBE), corpus_dir()) else {
        eprintln!("p1c_core: corpus constructor needs the probe and AAEMU_PHYS_CGF_DIR — skipped");
        return;
    };
    let limit: usize = std::env::var("AAEMU_P1C_CORPUS_LIMIT")
        .ok()
        .and_then(|s| s.parse().ok())
        .unwrap_or(usize::MAX);
    let mut n = 0usize;
    for b in corpus_blobs(&dir) {
        if n >= limit || blob_is_shared(&b.blob) {
            continue;
        }
        let Ok(m) = TriMesh::load(&mut MemReader::new(&b.blob[72..]), &RenderArrays::default())
        else {
            continue;
        };
        // The stored arrays (indices before the vertex map).
        let mut r = MemReader::new(&b.blob[72..]);
        let nv = r.u32().unwrap() as usize;
        let nt = r.u32().unwrap() as usize;
        let _ = r.u32();
        let flags = r.u32().unwrap();
        if r.u8().unwrap() != 0 {
            r.skip(2 * nv).unwrap();
        }
        if r.u8().unwrap() != 0 {
            r.skip(2 * nt).unwrap();
        }
        let v: Vec<Vec3> = (0..nv).map(|_| r.vec3().unwrap()).collect();
        let idx: Vec<i32> = (0..3 * nt).map(|_| i32::from(r.u16().unwrap())).collect();
        if nt == 0 {
            continue;
        }
        let (min_t, max_t) = match &m.tree {
            Some(MeshTree::Aabb(t)) => (t.min_tris_per_side, t.max_tris_per_leaf),
            Some(MeshTree::Obb(t)) => (t.min_tris_per_side, t.max_tris_per_leaf),
            _ => (2, 4),
        };
        let mut c = case(
            "corpus",
            (v, idx),
            flags & !0x8000,
            min_t,
            max_t,
            1.0,
            false,
            "-",
        );
        c.name = format!("corpus{n}");
        run_case_live(&c, &exe);
        n += 1;
    }
    eprintln!(
        "p1c_core corpus: {n} meshes rebuilt identically by the DLL constructor and the port"
    );
}
