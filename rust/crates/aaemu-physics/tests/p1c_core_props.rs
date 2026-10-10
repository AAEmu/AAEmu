//! P1c `CTriMesh` property / accessor slots against the DLL (`tests/p1c_core_props_probe.c`):
//! GetBBox (6), CalcPhysicalProperties (7), accessors 16/17/18/19/20/28/34/35/37/38/39,
//! GetVolume (31), GetCenter (32), GetExtent (42), RemapForeignIdx (40), AppendVertices (41),
//! GetRandomPos (43), CompactVertices (45), GetTriMesh (50).
//!
//! The probe builds (or loads) a mesh in the DLL, prints its full dump, then runs a command
//! script printing every slot result as raw bits. The port parses the dump into a `TriMesh`,
//! runs the same script on it and must print the identical text.
//!
//! | env | effect |
//! |---|---|
//! | none | the synthetic goldens `tests/golden/p1c_core_props_*.trace` are replayed |
//! | `AAEMU_PHYS_P1C_PROBE_DIR` | goldens regenerated from the DLL + randomised synthetic meshes compared live |
//! | + `AAEMU_PHYS_CGF_DIR` | also every (non-shared) trimesh of the CGF corpus, live only |

mod p1b_common;
mod p1c_common;

use aaemu_physics::geometry::bvtree::select::MeshTree;
use aaemu_physics::geometry::bvtree::MemWriter;
use aaemu_physics::geometry::trimesh::props::{
    jacobi_eigen3, mass_integrals, CryRandom, FORCED_VOLUME_BITS,
};
use aaemu_physics::geometry::trimesh::TriMesh;
use aaemu_physics::math::Vec3;
use aaemu_physics::sentinel::RawPtr;
use std::fmt::Write as _;
use std::path::Path;

const PROBE: &str = "p1c_core_props_probe";
const UNUSED: u32 = 0xFFBF_FFFF;

// ---------------------------------------------------------------- port side: printing

fn w(f: f32) -> String {
    format!("{:08x}", f.to_bits())
}

fn ws(fs: &[f32]) -> String {
    fs.iter().map(|f| format!(" {:08x}", f.to_bits())).collect()
}

fn v3(v: Vec3) -> [f32; 3] {
    [v.x, v.y, v.z]
}

/// `p1c_mesh_dump` of the port's mesh.
fn dump_mesh(m: &TriMesh) -> String {
    let mut s = String::new();
    let _ = writeln!(
        s,
        "mesh nt {} nv {} flags {:08x} valency {} mvd {} prio {} convex {} multi {} raygrid {} vmin {}",
        m.n_tris,
        m.n_verts,
        m.flags,
        m.max_vertex_valency,
        w(m.min_vtx_dist),
        m.priority,
        m.is_convex,
        m.multi_island,
        m.ray_grid_ready,
        m.i_vtx_min
    );
    let nt = m.n_tris.max(0) as usize;
    let nv = m.n_verts.max(0) as usize;
    s += "idx";
    for i in &m.indices[..3 * nt] {
        let _ = write!(s, " {i}");
    }
    s += "\nvtx";
    for v in &m.vertices[..nv] {
        s += &ws(&v3(*v));
    }
    s += "\nnrm";
    for v in &m.normals[..nt.min(m.normals.len())] {
        s += &ws(&v3(*v));
    }
    s += "\ntopo";
    for t in &m.topology[..nt.min(m.topology.len())] {
        let _ = write!(s, " {} {} {}", t[0], t[1], t[2]);
    }
    s += "\n";
    let ints = |s: &mut String, tag: &str, v: Option<Vec<i32>>| match v {
        Some(v) => {
            *s += tag;
            for x in v {
                let _ = write!(s, " {x}");
            }
            *s += "\n";
        }
        None => {
            let _ = writeln!(s, "{tag} -");
        }
    };
    ints(
        &mut s,
        "mats",
        m.mats
            .as_ref()
            .map(|v| v[..nt].iter().map(|&x| i32::from(x)).collect()),
    );
    ints(
        &mut s,
        "fid",
        m.foreign_idx.as_ref().map(|v| v[..nt].to_vec()),
    );
    ints(&mut s, "vmap", m.vtx_map.as_ref().map(|v| v[..nv].to_vec()));
    ints(&mut s, "aux", m.aux.as_ref().map(|v| v[..nt].to_vec()));
    match &m.islands {
        Some(isl) => {
            let _ = write!(s, "islands {}", isl.len());
            for i in isl {
                for x in i.words {
                    let _ = write!(s, " {x:08x}");
                }
            }
            s += "\n";
        }
        None => s += "islands -\n",
    }
    match &m.tri_island {
        Some(t) => {
            s += "tisl";
            for x in &t[..nt] {
                let _ = write!(s, " {x:08x}");
            }
            s += "\n";
        }
        None => s += "tisl -\n",
    }
    s += "cache";
    for x in m.convex_cache {
        let _ = write!(s, " {:08x}", x as u32);
    }
    s += &ws(&m.convex_tol);
    let _ = write!(s, " {:08x}", m.multi_island as u32);
    s += &ws(&[
        m.cached_volume,
        m.cached_extent[0],
        m.cached_extent[1],
        m.cached_center.x,
        m.cached_center.y,
        m.cached_center.z,
    ]);
    let _ = writeln!(s, "\nmisc {} {}", m.n_bad_edges, m.n_errors);
    match &m.tree {
        Some(t) => {
            let ty = match t {
                MeshTree::Obb(_) => 0,
                MeshTree::Aabb(_) => 1,
                MeshTree::SingleBox(_) => 2,
            };
            let mut wr = MemWriter::new();
            t.as_dyn().save(&mut wr);
            let _ = write!(s, "tree {ty} bytes {} ", wr.pos);
            for b in &wr.buf[..wr.pos] {
                let _ = write!(s, "{b:02x}");
            }
            s += "\n";
        }
        None => s += "tree -\n",
    }
    s
}

/// Words to mask (replace by `?`) in a line of the output: `(line, first word, count)`; word 0
/// is the tag.
type Mask = (usize, usize, usize);

/// Runs a probe command script on the port's mesh; the output and the masks for words the
/// original leaves uninitialised.
fn run_script(m: &mut TriMesh, cmds: &[String]) -> (String, Vec<Mask>) {
    let mut out = String::new();
    let mut masks = Vec::new();
    let mut rng: Option<CryRandom> = None;
    let mut it = cmds.iter().map(String::as_str);
    let lines = |s: &String| s.matches('\n').count();
    let next_i = |it: &mut dyn Iterator<Item = &str>| -> i64 {
        let t = it.next().expect("token");
        if let Some(h) = t.strip_prefix("0x") {
            i64::from_str_radix(h, 16).unwrap()
        } else {
            t.parse().unwrap()
        }
    };
    while let Some(c) = it.next() {
        match c {
            "dump" => out += &dump_mesh(m),
            "bbox" => {
                let b = m.get_bbox();
                let mut f: Vec<f32> = b.basis.to_array().to_vec();
                f.push(f32::from_bits(b.oriented as u32));
                f.extend(v3(b.center));
                f.extend(v3(b.size));
                let _ = writeln!(out, "bbox{}", ws(&f));
            }
            "props" => {
                let p = m.calc_physical_properties();
                let mut f = v3(p.inertia).to_vec();
                f.extend(p.q.to_array());
                f.extend(v3(p.origin));
                f.push(p.volume);
                let _ = writeln!(out, "props 1{}", ws(&f));
            }
            "vol" => {
                let _ = writeln!(out, "vol {}", w(m.get_volume()));
            }
            "center" => {
                let _ = writeln!(out, "center{}", ws(&v3(m.get_center())));
            }
            "ext" => {
                let f = next_i(&mut it) as i32;
                let _ = writeln!(out, "ext {f} {}", w(m.get_extent(f)));
            }
            "cache" => {
                let _ = writeln!(
                    out,
                    "cache{}",
                    ws(&[
                        m.cached_volume,
                        m.cached_extent[0],
                        m.cached_extent[1],
                        m.cached_center.x,
                        m.cached_center.y,
                        m.cached_center.z
                    ])
                );
            }
            "reset" => {
                let u = f32::from_bits(UNUSED);
                m.cached_volume = u;
                m.cached_extent = [u, u];
                m.cached_center.x = u;
            }
            "setflags" => {
                m.flags = u32::from_str_radix(it.next().unwrap(), 16).unwrap();
            }
            "prim" => {
                let i = next_i(&mut it) as i32;
                match m.get_primitive(i) {
                    Some(t) => {
                        let mut f = Vec::new();
                        for p in t.pt {
                            f.extend(v3(p));
                        }
                        f.extend(v3(t.n));
                        let _ = writeln!(out, "prim {i} 48{}", ws(&f));
                    }
                    None => {
                        let _ = writeln!(out, "prim {i} 0");
                    }
                }
            }
            "feat" => {
                let i = next_i(&mut it) as i32;
                let code = next_i(&mut it) as i32;
                let (n, pts) = m.get_feature(i, code);
                let mut f = Vec::new();
                for p in pts {
                    f.extend(v3(p));
                }
                let _ = writeln!(out, "feat {i} {code} {n}{}", ws(&f));
            }
            "acc" => {
                let i = next_i(&mut it) as i32;
                let _ = writeln!(
                    out,
                    "acc {i} {} {}{}",
                    m.get_primitive_mat_id(i),
                    m.get_foreign_idx(i),
                    ws(&v3(m.get_normal(i)))
                );
            }
            "misc" => {
                let ifd = m.i_foreign_data;
                let _ = writeln!(
                    out,
                    "misc {} {} {} {:x} {:x} {:x} {:x}",
                    m.get_primitive_count(),
                    m.get_error_count(),
                    m.get_bad_edge_count(),
                    m.get_foreign_data(-1).0,
                    m.get_foreign_data(0).0,
                    m.get_foreign_data(ifd).0,
                    m.get_foreign_data(ifd.wrapping_add(1)).0
                );
            }
            "setfd" => {
                let p = u64::from_str_radix(it.next().unwrap(), 16).unwrap();
                let id = next_i(&mut it) as i32;
                m.set_foreign_data(RawPtr(p), id);
            }
            "destroy" => {
                let k = next_i(&mut it) as i32;
                m.destroy_aux_data(k);
            }
            "compact" => m.compact_vertices(),
            "remap" => {
                let n = next_i(&mut it) as usize;
                let cur: Vec<i32> = (0..n).map(|_| next_i(&mut it) as i32).collect();
                let new: Vec<i32> = (0..n).map(|_| next_i(&mut it) as i32).collect();
                m.remap_foreign_idx(&cur, &new);
            }
            "append" => {
                let n = next_i(&mut it) as usize;
                let wm = next_i(&mut it) != 0;
                let v: Vec<Vec3> = (0..n)
                    .map(|_| {
                        let mut c = [0f32; 3];
                        for x in &mut c {
                            *x = f32::from_bits(
                                u32::from_str_radix(it.next().unwrap(), 16).unwrap(),
                            );
                        }
                        Vec3::new(c[0], c[1], c[2])
                    })
                    .collect();
                let map: Vec<i32> = if wm {
                    (0..n).map(|_| next_i(&mut it) as i32).collect()
                } else {
                    Vec::new()
                };
                let k = m.i_vtx_min.max(0) as usize;
                if k > 0 {
                    // The next dump's first k vertices / map entries are uninitialised in the
                    // original: mask them (dump line 2 = vtx, line 7 = vmap).
                    let base = lines(&out);
                    masks.push((base + 2, 1, 3 * k));
                    masks.push((base + 7, 1, k));
                }
                m.append_vertices(&v, wm.then_some(&map[..]));
            }
            "seed" => {
                let s = next_i(&mut it) as u32;
                rng = Some(CryRandom::new(s));
            }
            "rand" => {
                let f = next_i(&mut it) as i32;
                let n = next_i(&mut it);
                let r = rng.as_mut().expect("seed before rand");
                for _ in 0..n {
                    let (p, nn) = m.get_random_pos(r, f);
                    let _ = writeln!(out, "rand {f}{}{}", ws(&v3(p)), ws(&v3(nn)));
                }
            }
            "clone" => {
                let c = m.get_tri_mesh(true).unwrap();
                // The copy's centre cache .y/.z (+532/+536) are never written: heap garbage.
                masks.push((lines(&out) + 12, 14, 2));
                out += "clone\n";
                out += &dump_mesh(&c);
            }
            "self" => {
                let _ = writeln!(out, "self {}", i32::from(m.get_tri_mesh(false).is_none()));
            }
            other => panic!("unknown command {other}"),
        }
    }
    (out, masks)
}

fn apply_masks(text: &str, masks: &[Mask]) -> String {
    let mut lines: Vec<String> = text.lines().map(str::to_string).collect();
    for &(l, a, n) in masks {
        if let Some(line) = lines.get_mut(l) {
            let mut f: Vec<String> = line.split(' ').map(str::to_string).collect();
            for x in f.iter_mut().skip(a).take(n) {
                *x = "?".into();
            }
            *line = f.join(" ");
        }
    }
    let mut s = lines.join("\n");
    s.push('\n');
    s
}

/// `text` with the hex of every `tree` line dropped (kept: type and size).
fn no_tree_bytes(text: &str) -> String {
    text.lines()
        .map(|l| {
            if l.starts_with("tree ") {
                l.rsplit_once(' ').map_or(l, |(a, _)| a).to_string()
            } else {
                l.to_string()
            }
        })
        .collect::<Vec<_>>()
        .join(
            "
",
        )
}

/// The probe output with the script part masked where the original leaves memory
/// uninitialised (what the goldens store).
fn mask_after_dump(dll: &str, cmds: &[String]) -> String {
    let lines: Vec<&str> = dll.lines().collect();
    let (mut m, used) = p1c_common::parse_mesh_dump(&lines);
    let head: String = lines[..used]
        .iter()
        .map(|l| {
            format!(
                "{l}
"
            )
        })
        .collect();
    let rest: String = lines[used..]
        .iter()
        .map(|l| {
            format!(
                "{l}
"
            )
        })
        .collect();
    let (_, masks) = run_script(&mut m, cmds);
    head + &apply_masks(&rest, &masks)
}

/// Splits a probe output (dump + script output) and checks the port against it.
fn check_case(what: &str, dll: &str, cmds: &[String]) {
    let lines: Vec<&str> = dll.lines().collect();
    let (mut m, used) = p1c_common::parse_mesh_dump(&lines);
    assert!(used > 0, "{what}: no dump in the probe output");
    let rest: String = lines[used..].iter().map(|l| format!("{l}\n")).collect();
    let (port, masks) = run_script(&mut m, cmds);
    p1c_common::assert_same_text(
        what,
        &apply_masks(&port, &masks),
        &apply_masks(&rest, &masks),
    );
}

// ---------------------------------------------------------------- scripts

fn script(m: &TriMesh, seed: u32, mutate: bool) -> Vec<String> {
    let nt = m.n_tris;
    let mut c: Vec<String> = Vec::new();
    let mut push = |s: &str| c.extend(s.split_whitespace().map(str::to_string));
    // GetBBox and the copy constructor dereference the tree: only with one.
    let has_tree = m.tree.is_some();
    if has_tree {
        push("bbox");
    }
    push("props cache vol center ext -1 ext 0 ext 1 ext 2 ext 3 ext 4 ext 5");
    push("reset props cache ext 3 ext 2 ext 1 center vol cache");
    push("reset ext 1 ext 2 center vol props cache reset vol props cache");
    let f = m.flags;
    for extra in [0x4000_0000u32, 0x10_0000, 0x4010_0000] {
        push(&format!(
            "setflags {:x} props cache reset props cache vol",
            f | extra
        ));
    }
    push(&format!("setflags {f:x}"));
    let mut tris = vec![0, nt / 2, nt - 1, 1, nt / 3];
    tris.retain(|&t| t >= 0 && t < nt);
    tris.dedup();
    for i in [-1, 0, nt - 1, nt, nt + 1, i32::MIN, (nt / 2).max(0)] {
        push(&format!("prim {i}"));
    }
    for &i in &tris {
        push(&format!("acc {i}"));
        for code in [0, 1, 2, 32, 33, 34, 64, 65, 66, 96, 97, 257, 0x7FFF_FF21] {
            push(&format!("feat {i} {code}"));
        }
    }
    if nt >= 2 {
        for code in [3, 4, 35, 36, 67] {
            push(&format!("feat 0 {code}"));
        }
    }
    // +544 must end null: RemapForeignIdx walks it as a boolean-op record chain.
    push("misc setfd ab12 3 misc setfd 0 -1 misc setfd 77 -1 misc setfd 5 0 misc setfd 0 -1 misc self");
    if nt > 0 {
        push(&format!("seed {seed} rand 0 4 rand 1 4 rand 2 4 rand 3 4"));
        push(&format!("seed {} rand 2 3", seed ^ 0x5A5A));
    }
    if mutate {
        if has_tree {
            push("clone");
        }
        if m.foreign_idx.is_some() {
            push("remap 5 1 3 134217740 5 3 100 -5 7 134217729 9 dump");
        }
        push("destroy 1 acc 0 destroy 6 misc dump");
        push("compact dump props cache");
        let wm = i32::from(m.vtx_map.is_some());
        let mut a = format!("append 2 {wm} 3f800000 c0000000 40400000 bdcccccd 00000000 7f800000");
        if wm == 1 {
            a += " 4 1";
        }
        push(&a);
        push("dump compact dump");
    }
    c
}

// ---------------------------------------------------------------- synthetic meshes

struct Mesh {
    v: Vec<Vec3>,
    idx: Vec<i32>,
}

fn lcg(s: &mut u32) -> f32 {
    *s = s.wrapping_mul(1_664_525).wrapping_add(1_013_904_223);
    (*s >> 8) as f32 / 16_777_216.0
}

fn cube(c: Vec3, h: Vec3, rot: f32) -> Mesh {
    let (sn, cs) = (rot.sin(), rot.cos());
    let mut v = Vec::new();
    for k in 0..8 {
        let p = Vec3::new(
            if k & 1 != 0 { h.x } else { -h.x },
            if k & 2 != 0 { h.y } else { -h.y },
            if k & 4 != 0 { h.z } else { -h.z },
        );
        // rotate about (1,1,0)/√2-ish axis: x/y then y/z
        let q = Vec3::new(p.x * cs - p.y * sn, p.x * sn + p.y * cs, p.z);
        let r = Vec3::new(q.x, q.y * cs - q.z * sn, q.y * sn + q.z * cs);
        v.push(Vec3::new(r.x + c.x, r.y + c.y, r.z + c.z));
    }
    let idx = vec![
        0, 2, 3, 3, 1, 0, 4, 5, 7, 7, 6, 4, 0, 1, 5, 5, 4, 0, 2, 6, 7, 7, 3, 2, 0, 4, 6, 6, 2, 0,
        1, 3, 7, 7, 5, 1,
    ];
    Mesh { v, idx }
}

fn merge(a: Mesh, b: Mesh) -> Mesh {
    let n = a.v.len() as i32;
    let mut v = a.v;
    v.extend(b.v);
    let mut idx = a.idx;
    idx.extend(b.idx.iter().map(|i| i + n));
    Mesh { v, idx }
}

fn grid(nx: usize, ny: usize, seed: u32) -> Mesh {
    let mut s = seed;
    let mut v = Vec::new();
    for j in 0..=ny {
        for i in 0..=nx {
            v.push(Vec3::new(
                i as f32 * 0.7,
                j as f32 * 0.45,
                lcg(&mut s) * 0.6 - 0.3,
            ));
        }
    }
    let mut idx = Vec::new();
    let w = (nx + 1) as i32;
    for j in 0..ny as i32 {
        for i in 0..nx as i32 {
            let a = j * w + i;
            idx.extend([a, a + 1, a + w + 1, a, a + w + 1, a + w]);
        }
    }
    Mesh { v, idx }
}

/// A closed ellipsoid-like UV sphere, off-centre and rotated (non-trivial principal axes).
fn ellipsoid(nu: usize, nv: usize, seed: u32) -> Mesh {
    let mut s = seed;
    let (a, b, c) = (1.0 + lcg(&mut s), 0.5 + lcg(&mut s), 2.0 + lcg(&mut s));
    let rot = lcg(&mut s) * 3.0;
    let mut v = vec![Vec3::new(0.0, 0.0, c)];
    for j in 1..nv {
        let th = std::f32::consts::PI * j as f32 / nv as f32;
        for i in 0..nu {
            let ph = 2.0 * std::f32::consts::PI * i as f32 / nu as f32;
            v.push(Vec3::new(
                a * th.sin() * ph.cos(),
                b * th.sin() * ph.sin(),
                c * th.cos(),
            ));
        }
    }
    v.push(Vec3::new(0.0, 0.0, -c));
    let (sn, cs) = (rot.sin(), rot.cos());
    for p in &mut v {
        let q = Vec3::new(p.x * cs - p.z * sn, p.y, p.x * sn + p.z * cs);
        *p = Vec3::new(
            q.x * cs - q.y * sn + 3.0,
            q.x * sn + q.y * cs - 1.5,
            q.z + 0.25,
        );
    }
    let mut idx = Vec::new();
    let nu = nu as i32;
    let ring = |j: i32, i: i32| 1 + (j - 1) * nu + (i % nu);
    for i in 0..nu {
        idx.extend([0, ring(1, i), ring(1, i + 1)]);
    }
    for j in 1..nv as i32 - 1 {
        for i in 0..nu {
            idx.extend([ring(j, i), ring(j + 1, i), ring(j + 1, i + 1)]);
            idx.extend([ring(j, i), ring(j + 1, i + 1), ring(j, i + 1)]);
        }
    }
    let last = v.len() as i32 - 1;
    for i in 0..nu {
        idx.extend([last, ring(nv as i32 - 1, i + 1), ring(nv as i32 - 1, i)]);
    }
    Mesh { v, idx }
}

/// A random soup with duplicate vertices, repeated and degenerate triangles.
fn soup(n_tris: usize, seed: u32) -> Mesh {
    let mut s = seed;
    let nv = n_tris + 3;
    let mut v: Vec<Vec3> = (0..nv)
        .map(|_| {
            Vec3::new(
                lcg(&mut s) * 4.0 - 2.0,
                lcg(&mut s) * 3.0,
                lcg(&mut s) * 2.0 - 1.0,
            )
        })
        .collect();
    for k in 0..nv / 5 {
        v[k * 5] = v[(k * 5 + 3) % nv];
    }
    let mut idx = Vec::new();
    for t in 0..n_tris {
        let r = |s: &mut u32| ((lcg(s) * nv as f32) as i32).min(nv as i32 - 1);
        let (a, b, c) = (r(&mut s), r(&mut s), r(&mut s));
        if t % 7 == 3 {
            idx.extend([a, a, b]);
        } else {
            idx.extend([a, b, c]);
        }
    }
    Mesh { v, idx }
}

struct Case {
    name: String,
    mesh: Mesh,
    flags: u32,
    aux: u32,
}

fn case_args(c: &Case) -> Vec<String> {
    let p = p1c_common::write_mesh_file(&c.name, &c.mesh.v, &c.mesh.idx);
    vec![
        "mesh".into(),
        p.to_string_lossy().into_owned(),
        format!("{:x}", c.flags),
        "2".into(),
        "4".into(),
        "3f800000".into(),
        c.aux.to_string(),
    ]
}

/// First pass: dump only (no commands), to learn the DLL mesh the script is generated from.
fn dll_mesh(exe: &Path, args: &[String]) -> TriMesh {
    let a: Vec<&str> = args.iter().map(String::as_str).collect();
    let out = p1c_common::run_probe(exe, &a).unwrap_or_else(|e| panic!("{e}"));
    let lines: Vec<&str> = out.lines().collect();
    p1c_common::parse_mesh_dump(&lines).0
}

/// Golden cases: `tests/golden/p1c_core_props_<name>.trace` holds the probe's
/// output, its first line the command script.
fn golden_case(c: &Case, seed: u32) {
    let gname = format!("p1c_core_props_{}", c.name);
    let gpath = p1c_common::golden_dir().join(format!("{gname}.trace"));
    let args = case_args(c);
    let script_line;
    let dll_out;
    if let Some(exe) = p1c_common::probe(PROBE) {
        let m = dll_mesh(&exe, &args);
        let cmds = script(&m, seed, true);
        let mut a: Vec<&str> = args.iter().map(String::as_str).collect();
        a.extend(cmds.iter().map(String::as_str));
        let out = p1c_common::run_probe(&exe, &a).unwrap_or_else(|e| panic!("{e}"));
        script_line = cmds.join(" ");
        dll_out = mask_after_dump(&out, &cmds);
        let text = format!("script {script_line}\n{dll_out}");
        let stored = std::fs::read_to_string(&gpath)
            .ok()
            .map(|s| s.replace("\r\n", "\n"));
        let bless = std::env::var_os("AAEMU_PHYS_BLESS").is_some_and(|v| v != "0");
        // The DLL's trees carry uninitialised node bytes (P1b): compare goldens without them.
        if stored.as_deref().map(no_tree_bytes) != Some(no_tree_bytes(&text)) {
            if bless || stored.is_none() {
                std::fs::write(&gpath, &text).unwrap();
            } else {
                panic!(
                    "{} differs from the DLL; set AAEMU_PHYS_BLESS=1",
                    gpath.display()
                );
            }
        }
    } else {
        let Ok(text) = std::fs::read_to_string(&gpath) else {
            panic!("missing golden {} (needs the probe)", gpath.display());
        };
        let text = text.replace("\r\n", "\n");
        let (first, rest) = text.split_once('\n').unwrap();
        script_line = first.strip_prefix("script ").unwrap().to_string();
        dll_out = rest.to_string();
    }
    let cmds: Vec<String> = script_line.split_whitespace().map(str::to_string).collect();
    check_case(&c.name, &dll_out, &cmds);
}

fn golden_cases() -> Vec<Case> {
    vec![
        Case {
            name: "cube".into(),
            mesh: cube(Vec3::new(0.2, -0.1, 3.0), Vec3::new(1.3, 0.7, 2.1), 0.0),
            flags: 0x42,
            aux: 3,
        },
        Case {
            name: "two_boxes".into(),
            mesh: merge(
                cube(Vec3::new(1.0, 2.0, 0.5), Vec3::new(0.5, 1.5, 0.25), 0.4),
                cube(Vec3::new(-3.0, 0.5, 1.0), Vec3::new(1.0, 0.3, 0.8), 1.1),
            ),
            flags: 0x22,
            aux: 2,
        },
        Case {
            name: "grid".into(),
            mesh: grid(4, 3, 11),
            flags: 0x42,
            aux: 1,
        },
        Case {
            name: "ellipsoid".into(),
            mesh: ellipsoid(6, 4, 5),
            flags: 0x22,
            aux: 0,
        },
        Case {
            name: "soup".into(),
            mesh: soup(14, 77),
            flags: 0x42,
            aux: 3,
        },
    ]
}

#[test]
fn p1c_core_props_goldens_match_dll() {
    for (k, c) in golden_cases().iter().enumerate() {
        golden_case(c, 1000 + k as u32);
    }
}

/// Randomised synthetic meshes (live only).
#[test]
fn p1c_core_props_random_meshes_match_dll() {
    let Some(exe) = p1c_common::probe(PROBE) else {
        eprintln!("p1c_core_props: random meshes need AAEMU_PHYS_P1C_PROBE_DIR — skipped");
        return;
    };
    let mut s = 0x1234_5678u32;
    let mut n = 0;
    let flag_set = [0x42u32, 0x22, 0x82, 0x2, 0x4_0042, 0x10_0042, 0x4000_0042];
    for k in 0..60u32 {
        let fl = flag_set[k as usize % flag_set.len()];
        let mesh = match k % 6 {
            0 => soup(5 + (lcg(&mut s) * 60.0) as usize, k * 31 + 7),
            1 => grid(
                1 + (lcg(&mut s) * 7.0) as usize,
                1 + (lcg(&mut s) * 5.0) as usize,
                k,
            ),
            2 => ellipsoid(
                3 + (lcg(&mut s) * 12.0) as usize,
                2 + (lcg(&mut s) * 8.0) as usize,
                k,
            ),
            3 => {
                let mut m = cube(
                    Vec3::new(
                        lcg(&mut s) * 10.0 - 5.0,
                        lcg(&mut s) * 10.0,
                        lcg(&mut s) * 3.0,
                    ),
                    Vec3::new(
                        0.1 + lcg(&mut s) * 3.0,
                        0.1 + lcg(&mut s),
                        0.1 + lcg(&mut s) * 5.0,
                    ),
                    lcg(&mut s) * 6.0,
                );
                for _ in 0..(k % 4) {
                    m = merge(
                        m,
                        ellipsoid(4 + (k as usize % 5), 3 + (k as usize % 3), k * 3 + 1),
                    );
                }
                m
            }
            4 => {
                // flat: a single quad / triangle fan in a plane (zero volume)
                let mut m = grid(2 + (k as usize % 3), 1, k);
                for v in &mut m.v {
                    v.z = 1.5;
                }
                m
            }
            _ => merge(soup(8, k), cube(Vec3::ZERO, Vec3::new(1e-3, 2.0, 1e3), 0.3)),
        };
        let c = Case {
            name: format!("rnd{k}"),
            mesh,
            flags: fl,
            aux: k % 4,
        };
        let args = case_args(&c);
        let m = dll_mesh(&exe, &args);
        let cmds = script(&m, k * 977, true);
        let mut a: Vec<&str> = args.iter().map(String::as_str).collect();
        a.extend(cmds.iter().map(String::as_str));
        let out = p1c_common::run_probe(&exe, &a).unwrap_or_else(|e| panic!("{e}"));
        check_case(&c.name, &out, &cmds);
        n += 1;
    }
    eprintln!("p1c_core_props: {n} random synthetic meshes identical to the DLL");
}

/// Every non-shared trimesh of the CGF corpus, loaded by the DLL (live only).
#[test]
fn p1c_core_props_corpus_matches_dll() {
    let (Some(exe), Some(_)) = (
        p1c_common::probe(PROBE),
        std::env::var_os("AAEMU_PHYS_CGF_DIR"),
    ) else {
        eprintln!(
            "p1c_core_props: corpus needs AAEMU_PHYS_P1C_PROBE_DIR and AAEMU_PHYS_CGF_DIR — skipped (vacuous)"
        );
        return;
    };
    let all = p1b_common::all_meshes();
    let usable: Vec<_> = all
        .iter()
        .filter(|(_, _, mb)| mb.verts.is_some() && mb.idx.is_some())
        .collect();
    let skipped = all.len() - usable.len();
    let dir = std::env::temp_dir().join(format!("aaemu-p1c-props-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    let mut ok = 0;
    let mut stats = [0usize; 5];
    for (chunk_no, chunk) in usable.chunks(200).enumerate() {
        // pass 1: dumps only
        let file = dir.join(format!("blobs{chunk_no}.bin"));
        let write = |cmds: &[Vec<String>]| {
            let mut b = Vec::new();
            for ((_, blob, _), c) in chunk.iter().zip(cmds) {
                b.extend((blob.len() as u32).to_le_bytes());
                b.extend(blob);
                let t = c.join(" ");
                b.extend((t.len() as u32).to_le_bytes());
                b.extend(t.as_bytes());
            }
            std::fs::write(&file, b).unwrap();
        };
        write(&vec![Vec::new(); chunk.len()]);
        let f = file.to_string_lossy().into_owned();
        let out = p1c_common::run_probe(&exe, &["blobs", &f]).unwrap_or_else(|e| panic!("{e}"));
        let cases = split_cases(&out);
        assert_eq!(cases.len(), chunk.len());
        let cmds: Vec<Vec<String>> = cases
            .iter()
            .enumerate()
            .map(|(k, text)| {
                let lines: Vec<&str> = text.lines().collect();
                let m = p1c_common::parse_mesh_dump(&lines).0;
                stats[0] += usize::from(m.i_vtx_min > 0);
                stats[1] += usize::from(m.foreign_idx.is_some());
                stats[2] += usize::from(m.mats.is_some());
                stats[3] += usize::from(m.vtx_map.is_some());
                stats[4] += usize::from(m.flags & 0x4010_0000 != 0);
                script(&m, 31 * (chunk_no * 200 + k) as u32 + 5, true)
            })
            .collect();
        write(&cmds);
        let out = p1c_common::run_probe(&exe, &["blobs", &f]).unwrap_or_else(|e| panic!("{e}"));
        let cases = split_cases(&out);
        assert_eq!(cases.len(), chunk.len());
        for (k, text) in cases.iter().enumerate() {
            check_case(&chunk[k].0, text, &cmds[k]);
            ok += 1;
        }
    }
    eprintln!(
        "p1c_core_props corpus: {ok} meshes identical to the DLL ({skipped} shared / render-mesh blobs skipped); with trimmed vertices {}, foreign ids {}, materials {}, vertex map {}, flags 0x40100000 {}",
        stats[0], stats[1], stats[2], stats[3], stats[4]
    );
}

fn split_cases(out: &str) -> Vec<String> {
    let mut v: Vec<String> = Vec::new();
    for line in out.lines() {
        if line.starts_with("case ") {
            v.push(String::new());
        } else if let Some(c) = v.last_mut() {
            c.push_str(line);
            c.push('\n');
        }
    }
    v
}

// ---------------------------------------------------------------- port-only checks

/// The generator is MT19937: the reference first outputs for the default seed 5489.
#[test]
fn cry_random_is_mt19937() {
    let mut r = CryRandom::new(5489);
    let first: Vec<u32> = (0..3).map(|_| r.next_u32()).collect();
    assert_eq!(first, [3_499_211_612, 581_869_302, 3_890_346_734]);
    let mut r = CryRandom::new(5489);
    for _ in 0..9999 {
        r.next_u32();
    }
    assert_eq!(r.next_u32(), 4_123_659_995);
}

/// Unit cube: V = 1, centre ½, inertia 1/6 per axis, identity orientation.
#[test]
fn unit_cube_mass_properties() {
    let m = cube(Vec3::splat(0.5), Vec3::splat(0.5), 0.0);
    let (v, c, mut t) = mass_integrals(&m.v, &m.idx, 12);
    assert!((v - 1.0).abs() < 1e-12);
    for x in c {
        assert!((x - 0.5).abs() < 1e-12);
    }
    let (eig, _) = jacobi_eigen3(&mut t, 0.0);
    for e in eig {
        assert!((e - 1.0 / 6.0).abs() < 1e-9, "{e}");
    }
    assert!((f32::from_bits(FORCED_VOLUME_BITS) + 1e10).abs() < 1e4);
}
