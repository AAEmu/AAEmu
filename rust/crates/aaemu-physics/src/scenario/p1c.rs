//! P1c commands of the scenario interpreter (the oracle's `oracle/p1c_mesh.c` and the
//! heightfield part of `box_prim_geom` in `oracle/physoracle.c`):
//!
//! * `geom … type=mesh` from `file=`, `gen=` (the oracle's generators, rebuilt bit for bit in
//!   [`gen`]) or `cgf=` (the k-th `MeshPhysicsData` chunk of a corpus model through
//!   `LoadPhysGeometry`); file / gen meshes go through the geometry manager's `CreateMesh`
//!   slot 4 ([`World::create_mesh_ex`]) and `RegisterGeometry`; prints `geominfo` (+ `cgfload`,
//!   `meshv` / `mesht` / `meshf` with `dump=1`);
//! * `geom … type=heightfield` (`CreatePrimitive(2)` + `RegisterGeometry`) with `worldhf=1`
//!   also `SetHeightfieldData` (world slot 17) with the oracle's height callbacks;
//! * `geominfo name=`.

use super::{pf, pu, pv, v3, Scenario};
use crate::geometry::primitives::{HeightfieldPrim, Primitive};
use crate::geometry::shapes::stream::{load_phys_geometry_any, AnyGeom};
use crate::geometry::trimesh::build::MeshIndices;
use crate::geometry::trimesh::stream::RenderArrays;
use crate::geometry::PhysGeometry;
use crate::math::{Mat33, Vec3};
use crate::world::geoman::MeshSource;
use std::cell::Cell;
use std::collections::HashMap;
use std::fmt::Write;
use std::sync::Arc;

/// The oracle's mesh generators (`p1c_gen_*` in `oracle/p1c_mesh.c`), single precision with
/// sin/cos as a double Taylor series, so the vertices are bit-identical.
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
    #[must_use]
    pub fn sin(x: f64) -> f64 {
        let (x2, mut term, mut sum) = (x * x, x, x);
        for k in 1..=12 {
            term = term * -x2 / f64::from((2 * k) * (2 * k + 1));
            sum += term;
        }
        sum
    }
    /// Taylor series to x^24.
    #[must_use]
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
    #[derive(Clone, Copy, Debug)]
    pub enum Height {
        Flat,
        Noise(u64, f32),
        /// amp, kx, ky
        Terrain(f32, f32, f32),
    }
    /// `gen=grid` / `gen=terrainmesh`.
    #[must_use]
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
    /// `gen=box`.
    #[must_use]
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
    /// `gen=icosphere`.
    #[must_use]
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
    /// `gen=cylinder`.
    #[must_use]
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

/// The oracle's heightfield callbacks (`hf_height` / `hf_surf`): one setting per thread, as
/// the oracle's global `g_hf` (the last `geom type=heightfield` wins).
#[derive(Clone, Copy, Debug, Default)]
struct HfFn {
    mode: i32,
    h0: f32,
    kx: f32,
    ky: f32,
    amp: f32,
}

thread_local! {
    static HF: Cell<HfFn> = const { Cell::new(HfFn { mode: 0, h0: 0.0, kx: 0.0, ky: 0.0, amp: 0.0 }) };
}

fn hf_height(ix: i32, iy: i32) -> f32 {
    let g = HF.with(Cell::get);
    match g.mode {
        1 => g.h0 + g.kx * ix as f32 + g.ky * iy as f32,
        2 => g.h0 + g.amp * (ix.wrapping_mul(7).wrapping_add(iy.wrapping_mul(13)) % 5) as f32,
        _ => g.h0,
    }
}

fn hf_surf(_ix: i32, _iy: i32) -> u16 {
    0
}

fn u32_at(b: &[u8], o: usize) -> u32 {
    u32::from_le_bytes(b[o..o + 4].try_into().unwrap_or([0; 4]))
}

/// `p1c_cgf_blob`: the k-th 0xCCCC0018 chunk's payload (chunk + 40, size at chunk + 16) of
/// `%AAEMU_PHYS_CGF_DIR%\rel`.
fn cgf_blob(rel: &str, k: usize) -> Result<Vec<u8>, String> {
    let dir = std::env::var_os("AAEMU_PHYS_CGF_DIR").ok_or("cgf= needs AAEMU_PHYS_CGF_DIR")?;
    let path = std::path::PathBuf::from(dir).join(rel.replace('\\', "/"));
    let b = std::fs::read(&path).map_err(|e| format!("{}: {e}", path.display()))?;
    if b.len() < 20 || &b[..6] != b"CryTek" {
        return Err(format!("not a CryTek file: {}", path.display()));
    }
    let (ver, to) = (u32_at(&b, 12), u32_at(&b, 16) as usize);
    let nch = u32_at(&b, to);
    let es = if ver == 0x744 { 16 } else { 20 };
    let mut seen = 0;
    for i in 0..nch as usize {
        let e = to + 4 + i * es;
        if e + 16 > b.len() {
            break;
        }
        if u32_at(&b, e) != 0xCCCC_0018 {
            continue;
        }
        seen += 1;
        if seen - 1 != k {
            continue;
        }
        let o = u32_at(&b, e + 8) as usize;
        let size = u32_at(&b, o + 16) as usize;
        if o + 40 + size > b.len() {
            return Err(format!("truncated physics chunk in {}", path.display()));
        }
        return Ok(b[o + 40..o + 40 + size].to_vec());
    }
    Err(format!("no such physics chunk in {}", path.display()))
}

/// `p1c_blob_shared`: the blob's trimesh refers to the render mesh's vertices.
fn blob_shared(b: &[u8]) -> bool {
    let len = b.len();
    if len < 72 + 18 || u32_at(b, 0) != 1 || u32_at(b, 68) != 1 {
        return false;
    }
    let (nv, flags) = (u32_at(b, 72) as usize, u32_at(b, 84));
    let mut r = 88;
    let has_map = b[r] != 0;
    r += 1;
    if has_map {
        r += 2 * nv;
    }
    if r >= len {
        return false;
    }
    b[r] != 0 && flags & 0x40_0000 == 0
}

/// `p1c_read_meshfile`: u32 nVerts, u32 nTris, f32 verts[3·nV], i32 idx[3·nT] (LE).
fn read_meshfile(path: &std::path::Path) -> Result<gen::Mesh, String> {
    let b = std::fs::read(path).map_err(|e| format!("{}: {e}", path.display()))?;
    let short = || format!("short mesh file {}", path.display());
    if b.len() < 8 {
        return Err(short());
    }
    let (nv, nt) = (u32_at(&b, 0) as usize, u32_at(&b, 4) as usize);
    if nv == 0 || nv > 65536 || nt == 0 || nt > 1 << 22 {
        return Err(format!("bad mesh file header {}", path.display()));
    }
    if b.len() < 8 + 12 * nv + 12 * nt {
        return Err(short());
    }
    let f = |o: usize| f32::from_bits(u32_at(&b, o));
    let v = (0..nv)
        .map(|i| [f(8 + 12 * i), f(12 + 12 * i), f(16 + 12 * i)])
        .collect();
    let o = 8 + 12 * nv;
    let t = (0..nt)
        .map(|i| {
            [
                u32_at(&b, o + 12 * i) as i32,
                u32_at(&b, o + 12 * i + 4) as i32,
                u32_at(&b, o + 12 * i + 8) as i32,
            ]
        })
        .collect();
    Ok((v, t))
}

fn hex(x: f32) -> String {
    format!(" {:08x}", x.to_bits())
}

impl Scenario {
    /// `geom … type=mesh` (oracle `p1c_geom_mesh`).
    pub(super) fn cmd_geom_mesh(&mut self, kv: &HashMap<&str, &str>) -> Result<(), String> {
        let get = |k: &str, d: &str| -> String { kv.get(k).copied().unwrap_or(d).to_string() };
        let need = |k: &str| -> Result<String, String> {
            kv.get(k)
                .map(|s| (*s).to_string())
                .ok_or_else(|| format!("geom type=mesh: missing {k}"))
        };
        let name = need("name")?;
        let pg = if let Some(rel) = kv.get("cgf") {
            let slot = get("slot", "0").parse::<usize>().unwrap_or(0);
            let blob = cgf_blob(rel, slot)?;
            if blob_shared(&blob) {
                return Err(format!(
                    "cgf blob uses the render mesh's vertices (not supported): {rel}"
                ));
            }
            let l = load_phys_geometry_any(&blob, &RenderArrays::default())
                .map_err(|e| format!("LoadPhysGeometry failed: {rel}: {e:?}"))?;
            let _ = writeln!(self.out, "cgfload {name} used {} of {}", l.len, blob.len());
            let geom = match l.geom {
                AnyGeom::TriMesh(m) => AnyGeom::TriMesh(m).into_arc(),
                g @ AnyGeom::Primitive(_) => g.into_arc(),
            };
            Arc::new(PhysGeometry {
                geom,
                inertia: l.header.inertia,
                q: l.header.q,
                origin: l.header.origin,
                volume: l.header.volume,
                surface_idx: l.header.surface_idx,
                mat_mapping: Vec::new(),
            })
        } else {
            let (mut v, t) = if let Some(file) = kv.get("file") {
                let p = std::path::Path::new(file);
                let path = if p.is_absolute() {
                    p.to_path_buf()
                } else {
                    self.base_dir.clone().unwrap_or_default().join(p)
                };
                read_meshfile(&path)?
            } else {
                let g = kv
                    .get("gen")
                    .ok_or_else(|| format!("type=mesh needs file=, gen= or cgf=: {name}"))?;
                self.gen_mesh(g, kv)?
            };
            let sc = pf(&get("scale", "1"));
            let mut off = [0.0f32; 3];
            if let Some(o) = kv.get("offset") {
                for (k, x) in pv(o).into_iter().take(3).enumerate() {
                    off[k] = x;
                }
            }
            if sc != 1.0 || off[0] != 0.0 || off[1] != 0.0 || off[2] != 0.0 {
                for p in &mut v {
                    for k in 0..3 {
                        p[k] = p[k] * sc + off[k];
                    }
                }
            }
            if v.len() > 65536 {
                return Err(format!(
                    "mesh has more than 65536 vertices (u16 indices): {name}"
                ));
            }
            let mut i16 = Vec::with_capacity(3 * t.len());
            for tri in &t {
                for &i in tri {
                    if i < 0 || i as usize >= v.len() {
                        return Err(format!("mesh index out of range: {name}"));
                    }
                    i16.push(i as u16);
                }
            }
            let mats: Option<Vec<i8>> = kv
                .get("mat")
                .map(|m| vec![m.parse::<i32>().unwrap_or(0) as i8; t.len()]);
            let verts: Vec<Vec3> = v.iter().map(|p| Vec3::new(p[0], p[1], p[2])).collect();
            let src = MeshSource {
                vertices: &verts,
                indices: MeshIndices::U16(&i16),
                mats: mats.as_deref(),
                foreign_idx: None,
                n_tris: t.len() as i32,
                flags: pu(&get("flags", "0x40")),
            };
            let created = self
                .world
                .create_mesh_ex(
                    &src,
                    pf(&get("tol", "0")),
                    get("mintris", "2").parse::<i32>().unwrap_or(0),
                    get("maxtris", "4").parse::<i32>().unwrap_or(0),
                    pf(&get("obb", "1")),
                )
                .ok_or_else(|| format!("CreateMesh failed: {name}"))?;
            created.register(0, None)
        };
        self.geominfo(&name, &pg);
        if get("dump", "0").parse::<i32>().unwrap_or(0) != 0 {
            self.dump_mesh(&name, &pg);
        }
        self.geoms.insert(name, pg);
        Ok(())
    }

    /// The generator meshes (`gen=`).
    fn gen_mesh(&self, g: &str, kv: &HashMap<&str, &str>) -> Result<gen::Mesh, String> {
        let get = |k: &str, d: &str| -> String { kv.get(k).copied().unwrap_or(d).to_string() };
        let need = |k: &str| -> Result<String, String> {
            kv.get(k)
                .map(|s| (*s).to_string())
                .ok_or_else(|| format!("gen={g}: missing {k}"))
        };
        let atoi = |s: &str| s.parse::<i32>().unwrap_or(0);
        Ok(match g {
            "grid" | "terrainmesh" => {
                let (nx, ny) = (atoi(&need("nx")?), atoi(&need("ny")?));
                if nx < 1 || ny < 1 {
                    return Err("grid needs nx, ny >= 1".into());
                }
                let st = pv(&need("step")?);
                let (sx, sy) = (st[0], *st.get(1).unwrap_or(&st[0]));
                let z0 = pf(&get("z", "0"));
                let h = if g == "terrainmesh" {
                    let amp = pf(&need("amp")?);
                    let (mut kx, mut ky) = (0.5f32, 0.5f32);
                    if let Some(k) = kv.get("k") {
                        let k = pv(k);
                        kx = k[0];
                        ky = *k.get(1).unwrap_or(&k[0]);
                    }
                    gen::Height::Terrain(amp, kx, ky)
                } else if let Some(n) = kv.get("noise") {
                    let (s, a) = n.split_once(',').ok_or("noise=seed,amp")?;
                    let seed = s.strip_prefix("0x").map_or_else(
                        || s.parse::<u64>().unwrap_or(0),
                        |h| u64::from_str_radix(h, 16).unwrap_or(0),
                    );
                    gen::Height::Noise(seed, pf(a))
                } else {
                    gen::Height::Flat
                };
                gen::grid(nx, ny, sx, sy, z0, h)
            }
            "box" => {
                let h = pv(&need("size")?);
                if h.len() != 3 {
                    return Err("box needs size=hx,hy,hz".into());
                }
                gen::boxm([h[0], h[1], h[2]])
            }
            "icosphere" => gen::icosphere(pf(&need("r")?), atoi(&get("sub", "1"))),
            "cylinder" => {
                let seg = atoi(&get("seg", "16"));
                if !(3..=1024).contains(&seg) {
                    return Err("cylinder needs 3 <= seg <= 1024".into());
                }
                gen::cylinder(pf(&need("r")?), pf(&need("hh")?), seg)
            }
            g => return Err(format!("unknown mesh generator {g}")),
        })
    }

    /// `geom … type=heightfield` (the oracle's `box_prim_geom` case 2): the 120-byte primitive,
    /// `CreatePrimitive(2)` + `RegisterGeometry`; with `worldhf=` also `SetHeightfieldData`.
    pub(super) fn cmd_geom_heightfield(&mut self, kv: &HashMap<&str, &str>) -> Result<(), String> {
        let get = |k: &str, d: &str| -> String { kv.get(k).copied().unwrap_or(d).to_string() };
        let need = |k: &str| -> Result<String, String> {
            kv.get(k)
                .map(|s| (*s).to_string())
                .ok_or_else(|| format!("geom type=heightfield: missing {k}"))
        };
        let n: Vec<i32> = need("n")?
            .split(',')
            .map(|x| x.parse::<i32>().unwrap_or(0))
            .collect();
        let st = pv(&need("step")?);
        let fnm = get("fn", "flat");
        HF.with(|h| {
            h.set(HfFn {
                mode: match fnm.as_str() {
                    "slope" => 1,
                    "bumps" => 2,
                    _ => 0,
                },
                h0: pf(&get("h0", "0")),
                kx: pf(&get("kx", "0")),
                ky: pf(&get("ky", "0")),
                amp: pf(&get("amp", "0")),
            });
        });
        let prim = HeightfieldPrim {
            basis: Mat33::IDENTITY,
            oriented: 0,
            origin: v3(&get("center", "0,0,0")),
            step: [st[0], st[1]],
            stepr: [1.0 / st[0], 1.0 / st[1]],
            size: [n[0], n[1]],
            stride: [1, n[0]],
            height_scale: pf(&get("hscale", "1")),
            type_mask: 0x8000,
            height_mask: 0xFFFF,
            type_hole: 1,
            type_power: 15,
            get_height: Some(hf_height),
            get_surf_type: Some(hf_surf),
        };
        let g = self
            .world
            .register_primitive(&Primitive::Heightfield(prim))
            .ok_or("CreatePrimitive failed")?;
        if kv.contains_key("worldhf") {
            self.world.set_heightfield_data(Some(&prim), None);
        }
        self.geoms.insert(need("name")?, g);
        Ok(())
    }

    /// `geominfo NAME type T nprims N nv N flags F bbox <16 words> vol V pg <V> <Ibody 3>
    /// <q 4> <origin 3> <surface>` (oracle `p1c_geominfo`).
    pub(super) fn geominfo(&mut self, name: &str, pg: &PhysGeometry) {
        let g = &*pg.geom;
        let ty = g.geom_type() as i32;
        let (nv, flags) = g.as_trimesh().map_or((0, 0), |m| (m.n_verts, m.flags));
        let b = g.bbox();
        let mut s = format!(
            "geominfo {name} type {ty} nprims {} nv {nv} flags {flags:08x} bbox",
            g.primitive_count()
        );
        for r in 0..3 {
            for c in 0..3 {
                s += &hex(b.basis.m[r][c]);
            }
        }
        let _ = write!(s, " {:08x}", b.oriented as u32);
        for x in [
            b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z,
        ] {
            s += &hex(x);
        }
        let _ = write!(s, " vol {:08x} pg", g.volume().to_bits());
        s += &hex(pg.volume);
        for x in [pg.inertia.x, pg.inertia.y, pg.inertia.z] {
            s += &hex(x);
        }
        for x in pg.q.to_array() {
            s += &hex(x);
        }
        for x in [pg.origin.x, pg.origin.y, pg.origin.z] {
            s += &hex(x);
        }
        let _ = writeln!(s, " {}", pg.surface_idx);
        self.out += &s;
    }

    /// `meshv` / `mesht` / `meshf` (oracle `p1c_dump_mesh`).
    fn dump_mesh(&mut self, name: &str, pg: &PhysGeometry) {
        let Some(m) = pg.geom.as_trimesh() else {
            return;
        };
        let mut s = format!("meshv {name}");
        for v in &m.vertices[..m.n_verts as usize] {
            s += &(hex(v.x) + &hex(v.y) + &hex(v.z));
        }
        let _ = write!(s, "\nmesht {name}");
        for i in &m.indices[..3 * m.n_tris as usize] {
            let _ = write!(s, " {i}");
        }
        if let Some(f) = &m.foreign_idx {
            let _ = write!(s, "\nmeshf {name}");
            for i in &f[..m.n_tris as usize] {
                let _ = write!(s, " {i}");
            }
        }
        s.push('\n');
        self.out += &s;
    }

    /// `geominfo name=`.
    pub(super) fn cmd_geominfo(&mut self, kv: &HashMap<&str, &str>) -> Result<(), String> {
        let name = kv.get("name").ok_or("geominfo: missing name")?;
        let pg = self
            .geoms
            .get(*name)
            .cloned()
            .ok_or_else(|| format!("unknown geom {name}"))?;
        self.geominfo(name, &pg);
        Ok(())
    }
}
