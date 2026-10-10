//! P1c-core-inside: `CTriMesh` slot 22 `PrepareForRayTest` (0x351D7C40, ray grid builder
//! sub_351C8B10), slot 61 `PointInsideStatus(pt, Triangle*)` (0x351C8600) and slot 8
//! (0x351DB380) against the real DLL (`tests/p1c_core_inside_probe.c`).
//!
//! Each case: the DLL builds (or loads) a mesh and dumps it; the dump is parsed into a
//! `TriMesh`; then both sides optionally run slot 22 with a given ray length, run slots 61 /
//! 61(null) / 8 on a list of points and print the grid (record, cell starts, triangle lists)
//! as raw bits. The texts must be identical.
//!
//! | env | effect |
//! |---|---|
//! | none | the synthetic golden `tests/golden/p1c_core_inside_synth.trace` |
//! | `AAEMU_PHYS_P1C_PROBE_DIR` | golden regenerated and compared; plus random meshes (live only) |
//! | + `AAEMU_PHYS_CGF_DIR` | every non-shared corpus trimesh (live only, never committed) |

mod p1b_common;
mod p1c_common;

use aaemu_physics::geometry::primitives::Triangle;
use aaemu_physics::geometry::trimesh::inside::InsideTri;
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::math::Vec3;
use std::fmt::Write as _;
use std::path::PathBuf;

// ---------------------------------------------------------------------------------------------
// cases
// ---------------------------------------------------------------------------------------------

enum Source {
    Synth {
        flags: u32,
        v: Vec<Vec3>,
        idx: Vec<i32>,
    },
    Blob(Vec<u8>),
}

struct Case {
    src: Source,
    /// `Some(raylen)`: slot 22 is called first with it.
    prep: Option<f32>,
    pts: Vec<Vec3>,
}

fn lcg(s: &mut u32) -> u32 {
    *s = s.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
    *s >> 8
}

/// Uniform in `[0, 1)` (exact: 24-bit integer / 2²⁴).
fn unit(s: &mut u32) -> f32 {
    lcg(s) as f32 / 16_777_216.0
}

fn pick(s: &mut u32, n: usize) -> usize {
    (lcg(s) as usize) % n.max(1)
}

/// Query points around a mesh: random in the (padded) box, vertices, edge midpoints, face
/// centroids and nearby points, box-lattice points (on cell boundaries for power-of-two
/// grids), points sharing a coordinate with a vertex, far points.
fn points(v: &[Vec3], idx: &[i32], n: usize, seed: u32) -> Vec<Vec3> {
    let mut s = seed;
    let (mut lo, mut hi) = (Vec3::splat(f32::MAX), Vec3::splat(-f32::MAX));
    for &i in idx {
        let p = v[i as usize];
        lo = Vec3::new(lo.x.min(p.x), lo.y.min(p.y), lo.z.min(p.z));
        hi = Vec3::new(hi.x.max(p.x), hi.y.max(p.y), hi.z.max(p.z));
    }
    let ext = hi - lo;
    let c = (lo + hi) * 0.5;
    let nt = idx.len() / 3;
    let tri = |s: &mut u32| {
        let t = pick(s, nt);
        [
            v[idx[3 * t] as usize],
            v[idx[3 * t + 1] as usize],
            v[idx[3 * t + 2] as usize],
        ]
    };
    let mut out = Vec::with_capacity(n);
    for i in 0..n {
        let p = match i % 9 {
            0 | 8 => Vec3::new(
                lo.x - 0.2 * ext.x + 1.4 * ext.x * unit(&mut s),
                lo.y - 0.2 * ext.y + 1.4 * ext.y * unit(&mut s),
                lo.z - 0.2 * ext.z + 1.4 * ext.z * unit(&mut s),
            ),
            1 => tri(&mut s)[pick(&mut s, 3)],
            2 => {
                let t = tri(&mut s);
                let k = pick(&mut s, 3);
                (t[k] + t[(k + 1) % 3]) * 0.5
            }
            3 => {
                let t = tri(&mut s);
                (t[0] + t[1] + t[2]) * (1.0 / 3.0)
            }
            4 => {
                let t = tri(&mut s);
                let n = (t[1] - t[0]).cross(t[2] - t[0]);
                let l = n.len();
                let d = if l > 0.0 { n * (1.0 / l) } else { n };
                let e = (ext.x + ext.y + ext.z) * 1e-3 * (unit(&mut s) - 0.5);
                (t[0] + t[1] + t[2]) * (1.0 / 3.0) + d * e
            }
            5 => {
                let q = |s: &mut u32| (lcg(s) % 17) as f32 / 16.0;
                Vec3::new(
                    lo.x + ext.x * q(&mut s),
                    lo.y + ext.y * q(&mut s),
                    lo.z + ext.z * q(&mut s),
                )
            }
            6 => {
                let a = tri(&mut s)[pick(&mut s, 3)];
                let mut p = Vec3::new(
                    lo.x + ext.x * unit(&mut s),
                    lo.y + ext.y * unit(&mut s),
                    lo.z + ext.z * unit(&mut s),
                );
                let k = pick(&mut s, 3);
                p[k] = a[k];
                p
            }
            _ => {
                let d = Vec3::new(unit(&mut s) - 0.5, unit(&mut s) - 0.5, unit(&mut s) - 0.5);
                c + d * (1000.0 * (ext.x + ext.y + ext.z + 1.0))
            }
        };
        out.push(p);
    }
    out
}

fn cube(o: Vec3, e: Vec3, inward: bool) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    for i in 0..8 {
        v.push(
            o + Vec3::new(
                if i & 1 == 0 { -e.x } else { e.x },
                if i & 2 == 0 { -e.y } else { e.y },
                if i & 4 == 0 { -e.z } else { e.z },
            ),
        );
    }
    let mut idx = vec![
        0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6,
        1, 3, 5, 3, 7, 5,
    ];
    if inward {
        for t in idx.chunks_mut(3) {
            t.swap(1, 2);
        }
    }
    (v, idx)
}

/// A closed "sphere": a cube grid of `n×n` per face pushed onto a noisy ellipsoid (sqrt only,
/// so the vertices are platform independent).
fn ball(n: usize, seed: u32, scale: Vec3, off: Vec3) -> (Vec<Vec3>, Vec<i32>) {
    let mut s = seed;
    let mut v = Vec::new();
    let mut idx = Vec::new();
    // faces: (axis, sign)
    for f in 0..6 {
        let ax = f / 2;
        let sg = if f % 2 == 0 { 1.0f32 } else { -1.0 };
        let base = v.len() as i32;
        for i in 0..=n {
            for j in 0..=n {
                let a = -1.0 + 2.0 * i as f32 / n as f32;
                let b = -1.0 + 2.0 * j as f32 / n as f32;
                let mut p = Vec3::ZERO;
                p[ax] = sg;
                p[(ax + 1) % 3] = a;
                p[(ax + 2) % 3] = b;
                let r = 1.0 + 0.1 * (unit(&mut s) - 0.5);
                let l = p.len();
                let q = p * (r / l);
                v.push(Vec3::new(q.x * scale.x, q.y * scale.y, q.z * scale.z) + off);
            }
        }
        let w = (n + 1) as i32;
        for i in 0..n as i32 {
            for j in 0..n as i32 {
                let a = base + i * w + j;
                let (b, c, d) = (a + w, a + 1, a + w + 1);
                if sg > 0.0 {
                    idx.extend([a, b, c, c, b, d]);
                } else {
                    idx.extend([a, c, b, c, d, b]);
                }
            }
        }
    }
    (v, idx)
}

/// An open bumpy sheet of `n×n` quads.
fn sheet(n: usize, seed: u32) -> (Vec<Vec3>, Vec<i32>) {
    let mut s = seed;
    let mut v = Vec::new();
    for i in 0..=n {
        for j in 0..=n {
            v.push(Vec3::new(
                i as f32 * 0.5,
                j as f32 * 0.25,
                unit(&mut s) * 0.3,
            ));
        }
    }
    let mut idx = Vec::new();
    let w = (n + 1) as i32;
    for i in 0..n as i32 {
        for j in 0..n as i32 {
            let a = i * w + j;
            idx.extend([a, a + w, a + 1, a + 1, a + w, a + w + 1]);
        }
    }
    (v, idx)
}

/// A random soup with duplicated vertices and a few degenerate triangles.
fn soup(nt: usize, seed: u32) -> (Vec<Vec3>, Vec<i32>) {
    let mut s = seed;
    let mut v = Vec::new();
    for _ in 0..nt {
        v.push(Vec3::new(
            unit(&mut s) * 4.0 - 1.0,
            unit(&mut s) * 2.0,
            unit(&mut s) * 3.0 - 2.0,
        ));
    }
    let k = v.len();
    for i in 0..k / 5 {
        v.push(v[i * 3 % k]);
    }
    let mut idx = Vec::new();
    for t in 0..nt {
        let a = pick(&mut s, v.len()) as i32;
        let b = pick(&mut s, v.len()) as i32;
        let c = if t % 11 == 5 {
            a
        } else {
            pick(&mut s, v.len()) as i32
        };
        idx.extend([a, b, c]);
    }
    (v, idx)
}

fn merge(parts: &[(Vec<Vec3>, Vec<i32>)]) -> (Vec<Vec3>, Vec<i32>) {
    let mut v = Vec::new();
    let mut idx = Vec::new();
    for (pv, pi) in parts {
        let b = v.len() as i32;
        v.extend_from_slice(pv);
        idx.extend(pi.iter().map(|i| i + b));
    }
    (v, idx)
}

/// Rotation about z by a rational "angle" (`c = (1−t²)/(1+t²)`, `s = 2t/(1+t²)`).
fn rot_z((v, idx): (Vec<Vec3>, Vec<i32>), t: f32) -> (Vec<Vec3>, Vec<i32>) {
    let d = 1.0 + t * t;
    let (c, s) = ((1.0 - t * t) / d, 2.0 * t / d);
    (
        v.iter()
            .map(|p| Vec3::new(c * p.x - s * p.y, s * p.x + c * p.y, p.z))
            .collect(),
        idx,
    )
}

fn synth(flags: u32, m: (Vec<Vec3>, Vec<i32>), prep: Option<f32>, npts: usize, seed: u32) -> Case {
    let pts = points(&m.0, &m.1, npts, seed);
    Case {
        src: Source::Synth {
            flags,
            v: m.0,
            idx: m.1,
        },
        prep,
        pts,
    }
}

/// The committed golden cases.
fn golden_cases() -> Vec<Case> {
    let one = Vec3::new(1.0, 1.0, 1.0);
    vec![
        synth(0x42, cube(Vec3::ZERO, one, false), None, 27, 1),
        synth(
            0x22,
            cube(Vec3::new(0.5, -1.0, 2.0), Vec3::new(2.0, 0.5, 1.0), true),
            Some(0.0),
            18,
            2,
        ),
        synth(
            0x42,
            ball(3, 3, Vec3::new(1.0, 2.0, 0.5), Vec3::new(0.3, -1.2, 2.5)),
            Some(0.5),
            27,
            3,
        ),
        synth(
            0x40042,
            rot_z(ball(2, 4, Vec3::new(2.0, 1.0, 1.0), Vec3::ZERO), 0.4),
            Some(100.0),
            18,
            4,
        ),
        synth(0x42, sheet(4, 5), Some(-0.25), 18, 5),
        synth(0x42, soup(24, 6), None, 18, 6),
        synth(
            0x82,
            merge(&[
                cube(Vec3::ZERO, one, false),
                rot_z(cube(Vec3::new(4.0, 1.0, 0.0), one, false), 0.3),
            ]),
            None,
            18,
            7,
        ),
        synth(
            0x22,
            cube(Vec3::ZERO, Vec3::new(3.0, 3.0, 1e-3), false),
            Some(0.05),
            18,
            8,
        ),
        // slot 22 refuses (raylen 1000: 192 triangles > 6·(2·2 + 2·4)); slot 61 then builds.
        synth(
            0x42,
            ball(4, 9, Vec3::new(1.0, 1.0, 1.0), Vec3::ZERO),
            Some(1000.0),
            9,
            9,
        ),
    ]
}

/// Larger randomized cases (live only).
fn random_cases() -> Vec<Case> {
    let mut out = Vec::new();
    let flags = [0x42u32, 0x22, 0x40042, 0x82];
    let mut s = 0x1234_5678u32;
    for k in 0..48u32 {
        let f = flags[k as usize % 4];
        let m = match k % 6 {
            0 => ball(
                2 + pick(&mut s, 6),
                k,
                Vec3::new(1.0 + unit(&mut s) * 4.0, 1.0, 0.2 + unit(&mut s)),
                Vec3::new(unit(&mut s), 3.0, -1.0),
            ),
            1 => sheet(2 + pick(&mut s, 10), k),
            2 => soup(8 + pick(&mut s, 120), k),
            3 => merge(&[
                ball(2, k, Vec3::new(1.0, 1.0, 1.0), Vec3::ZERO),
                ball(3, k + 1, Vec3::new(0.5, 2.0, 1.0), Vec3::new(5.0, 0.0, 1.0)),
                cube(
                    Vec3::new(-3.0, 2.0, 0.0),
                    Vec3::new(0.5, 0.25, 2.0),
                    k % 2 == 0,
                ),
            ]),
            4 => rot_z(
                ball(4, k, Vec3::new(3.0, 1.0, 1.0), Vec3::new(1.0, 2.0, 3.0)),
                unit(&mut s),
            ),
            _ => rot_z(soup(40, k), unit(&mut s) * 2.0 - 1.0),
        };
        let prep = match k % 5 {
            0 => None,
            1 => Some(0.0),
            2 => Some(unit(&mut s) * 2.0),
            3 => Some(-unit(&mut s)),
            _ => Some(unit(&mut s) * 0.05),
        };
        out.push(synth(f, m, prep, 120, 100 + k));
    }
    out
}

// ---------------------------------------------------------------------------------------------
// job file / port text
// ---------------------------------------------------------------------------------------------

fn write_job(tag: &str, cases: &[Case]) -> PathBuf {
    let mut b: Vec<u8> = Vec::new();
    let u = |b: &mut Vec<u8>, x: u32| b.extend(x.to_le_bytes());
    u(&mut b, cases.len() as u32);
    for c in cases {
        match &c.src {
            Source::Synth { flags, v, idx } => {
                u(&mut b, 0);
                u(&mut b, *flags);
                u(&mut b, v.len() as u32);
                u(&mut b, (idx.len() / 3) as u32);
                for p in v {
                    for w in p.to_bits() {
                        u(&mut b, w);
                    }
                }
                for &i in idx {
                    u(&mut b, i as u32);
                }
            }
            Source::Blob(bl) => {
                u(&mut b, 1);
                u(&mut b, bl.len() as u32);
                b.extend_from_slice(bl);
            }
        }
        u(&mut b, u32::from(c.prep.is_some()));
        u(&mut b, c.prep.unwrap_or(0.0).to_bits());
        u(&mut b, c.pts.len() as u32);
        for p in &c.pts {
            for w in p.to_bits() {
                u(&mut b, w);
            }
        }
    }
    let dir = std::env::temp_dir().join(format!("aaemu-p1c-inside-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    let p = dir.join(format!("{tag}.job"));
    std::fs::write(&p, b).unwrap();
    p
}

/// What the probe prints after the mesh dump, computed by the port.
fn port_text(m: &mut TriMesh, c: &Case) -> String {
    let mut t = String::new();
    if let Some(r) = c.prep {
        let ok = m.prepare_for_ray_test(r);
        writeln!(t, "prep {} ready {}", i32::from(ok), m.ray_grid_ready).unwrap();
    }
    let cd = f32::from_bits(0xCDCD_CDCD);
    for (i, &p) in c.pts.iter().enumerate() {
        let mut o = InsideTri {
            tri: Triangle {
                pt: [Vec3::splat(cd); 3],
                n: Vec3::splat(cd),
            },
            idx: 0xCDCD_CDCDu32 as i32,
        };
        let r61 = m.point_inside_status_tri(p, Some(&mut o));
        let r61n = m.point_inside_status_tri(p, None);
        let r8 = m.point_inside_status(p);
        write!(t, "pt {i} {r61}").unwrap();
        for w in o.words() {
            write!(t, " {w:08x}").unwrap();
        }
        writeln!(t, " {r61n} {r8}").unwrap();
    }
    writeln!(t, "grid ready {}", m.ray_grid_ready).unwrap();
    if let Some(g) = &m.ray_grid {
        for (k, l) in g.layers.iter().enumerate() {
            write!(t, "layer {k} rec").unwrap();
            for w in l.record_words() {
                write!(t, " {w:08x}").unwrap();
            }
            writeln!(t).unwrap();
            write!(t, "cells {k}").unwrap();
            for x in &l.cells {
                write!(t, " {x}").unwrap();
            }
            writeln!(t).unwrap();
            write!(t, "tris {k}").unwrap();
            for x in &l.tris {
                write!(t, " {x}").unwrap();
            }
            writeln!(t).unwrap();
        }
    }
    t
}

/// Splits the probe output into cases and compares each against the port. Returns
/// `(cases, points, inside results, layers, refused slot 22 calls)`.
fn check(what: &str, text: &str, cases: &[Case]) -> (usize, usize, usize, usize, usize) {
    let lines: Vec<&str> = text.lines().collect();
    let starts: Vec<usize> = lines
        .iter()
        .enumerate()
        .filter(|(_, l)| l.starts_with("case "))
        .map(|(i, _)| i)
        .collect();
    assert_eq!(starts.len(), cases.len(), "{what}: case count");
    let (mut npts, mut nin, mut nlay) = (0, 0, 0);
    let nref = lines.iter().filter(|l| l.starts_with("prep 0")).count();
    for (k, c) in cases.iter().enumerate() {
        let a = starts[k] + 1;
        let b = starts.get(k + 1).copied().unwrap_or(lines.len());
        let (mut m, used) = p1c_common::parse_mesh_dump(&lines[a..b]);
        let rest = lines[a + used..b].join("\n") + "\n";
        let port = port_text(&mut m, c);
        p1c_common::assert_same_text(&format!("{what} case {k}"), &port, &rest);
        npts += c.pts.len();
        nin += rest
            .lines()
            .filter(|l| l.starts_with("pt ") && l.split(' ').nth(2) == Some("1"))
            .count();
        nlay += m.ray_grid.as_ref().map_or(0, |g| g.layers.len());
    }
    (cases.len(), npts, nin, nlay, nref)
}

// ---------------------------------------------------------------------------------------------
// tests
// ---------------------------------------------------------------------------------------------

/// The text with the serialized tree lines dropped: a DLL-built AABB / OBB tree contains
/// uninitialised bytes (AABB node 1 and internal nodes' `single_coll`, OBB node 1), so the dump's
/// `tree` line differs from run to run. Only the root box (slot 2) is read by this package.
fn mask_tree(t: &str) -> String {
    t.lines()
        .filter(|l| !l.starts_with("tree "))
        .map(|l| format!("{l}\n"))
        .collect()
}

/// `p1c_common::golden` with the `tree` lines masked in the probe-vs-golden comparison.
fn golden_masked(name: &str, args: &[&str]) -> String {
    let path = p1c_common::golden_dir().join(format!("{name}.trace"));
    let stored = std::fs::read_to_string(&path)
        .ok()
        .map(|s| s.replace("\r\n", "\n"));
    let fresh = p1c_common::probe("p1c_core_inside_probe")
        .map(|exe| p1c_common::run_probe(&exe, args).unwrap_or_else(|e| panic!("{e}")));
    let bless = std::env::var_os("AAEMU_PHYS_BLESS").is_some_and(|v| v != "0");
    match (fresh, stored) {
        (None, Some(s)) => s,
        (None, None) => panic!("{} missing", path.display()),
        (Some(f), Some(s)) if mask_tree(&f) == mask_tree(&s) => s,
        (Some(f), stored) => {
            if bless || stored.is_none() {
                std::fs::write(&path, &f).unwrap();
                return f;
            }
            p1c_common::assert_same_text(
                &format!("{} vs DLL (AAEMU_PHYS_BLESS=1 rewrites)", path.display()),
                &mask_tree(&stored.unwrap_or_default()),
                &mask_tree(&f),
            );
            unreachable!()
        }
    }
}

#[test]
fn synthetic_golden() {
    let cases = golden_cases();
    let job = write_job("synth", &cases);
    let text = golden_masked("p1c_core_inside_synth", &[job.to_str().unwrap()]);
    let (c, p, i, l, r) = check("synth", &text, &cases);
    eprintln!(
        "p1c-inside synth: {c} meshes, {p} points ({i} inside), {l} grid layers, {r} refused preps identical"
    );
}

#[test]
fn random_meshes_live() {
    let Some(exe) = p1c_common::probe("p1c_core_inside_probe") else {
        eprintln!("p1c-inside random: needs AAEMU_PHYS_P1C_PROBE_DIR — skipped");
        return;
    };
    let cases = random_cases();
    let job = write_job("random", &cases);
    let text = p1c_common::run_probe(&exe, &[job.to_str().unwrap()]).unwrap();
    let (c, p, i, l, r) = check("random", &text, &cases);
    eprintln!(
        "p1c-inside random: {c} meshes, {p} points ({i} inside), {l} grid layers, {r} refused preps identical"
    );
}

#[test]
fn corpus_live() {
    let Some(exe) = p1c_common::probe("p1c_core_inside_probe") else {
        eprintln!("p1c-inside corpus: needs AAEMU_PHYS_P1C_PROBE_DIR — skipped");
        return;
    };
    if std::env::var_os("AAEMU_PHYS_CGF_DIR").is_none() {
        eprintln!("p1c-inside corpus: needs AAEMU_PHYS_CGF_DIR — skipped (vacuous pass)");
        return;
    }
    let all = p1b_common::all_meshes();
    let mut skipped = 0;
    let mut cases = Vec::new();
    let mut s = 0x00C0_FFEE_u32;
    for (k, (_name, blob, mb)) in all.into_iter().enumerate() {
        let (Some(v), Some(idx)) = (mb.verts.as_ref(), mb.idx.as_ref()) else {
            skipped += 1;
            continue;
        };
        if idx.is_empty() || idx.iter().any(|&i| i as usize >= v.len()) {
            skipped += 1;
            continue;
        }
        let pts = points(v, idx, 50, k as u32);
        let prep = match k % 8 {
            1 => Some(0.0),
            3 => Some(unit(&mut s) * 4.0),
            5 => Some(-unit(&mut s)),
            _ => None,
        };
        cases.push(Case {
            src: Source::Blob(blob),
            prep,
            pts,
        });
    }
    let mut tot = (0, 0, 0, 0, 0);
    for (n, chunk) in cases.chunks(64).enumerate() {
        let job = write_job(&format!("corpus{n}"), chunk);
        let text = p1c_common::run_probe(&exe, &[job.to_str().unwrap()]).unwrap();
        let r = check(&format!("corpus chunk {n}"), &text, chunk);
        tot = (
            tot.0 + r.0,
            tot.1 + r.1,
            tot.2 + r.2,
            tot.3 + r.3,
            tot.4 + r.4,
        );
        let _ = std::fs::remove_file(job);
    }
    eprintln!(
        "p1c-inside corpus: {} meshes ({skipped} skipped: shared / render vertices), {} points ({} inside), {} grid layers, {} refused preps identical",
        tot.0, tot.1, tot.2, tot.3, tot.4
    );
}

#[test]
fn no_tree_and_ready_states() {
    // No tree: slot 22 fails ("Getting bbox failed"), slot 61 / 8 return 0.
    let mut m = TriMesh::default();
    assert!(!m.prepare_for_ray_test(0.0));
    assert_eq!(m.ray_grid_ready, 0);
    assert_eq!(m.point_inside_status(Vec3::ZERO), 0);
    // `+476 != 0` (also negative): slot 22 reports success without building.
    m.ray_grid_ready = -1;
    assert!(m.prepare_for_ray_test(1.0));
    assert!(m.ray_grid.is_none());
    assert_eq!(m.point_inside_status(Vec3::ZERO), 0);
}
