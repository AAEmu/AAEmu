//! Shared by the P1b integration tests: walking CGF files to their physics blobs
//! (chunk 0xCCCC0018) and a trimesh blob (`CTriMesh::Load`, 0x351D83B0) to its BV tree.
//! The corpus directory comes from `AAEMU_PHYS_CGF_DIR` (default: the samples under
//! `D:\aa-re\zone\agent_geom\data\samples`); game files are never committed.
#![allow(dead_code)]

use aaemu_physics::geometry::bvtree::MemReader;
use aaemu_physics::math::Vec3;
use std::path::{Path, PathBuf};

/// `AAEMU_PHYS_CGF_DIR`, or the sample directory.
pub fn corpus_dir() -> PathBuf {
    std::env::var_os("AAEMU_PHYS_CGF_DIR")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(r"D:\aa-re\zone\agent_geom\data\samples"))
}

/// All `.cgf` files under `dir`, sorted.
pub fn cgf_files(dir: &Path, out: &mut Vec<PathBuf>) {
    let Ok(rd) = std::fs::read_dir(dir) else {
        return;
    };
    let mut entries: Vec<_> = rd.flatten().map(|e| e.path()).collect();
    entries.sort();
    for p in entries {
        if p.is_dir() {
            cgf_files(&p, out);
        } else if p.extension().is_some_and(|e| e.eq_ignore_ascii_case("cgf")) {
            out.push(p);
        }
    }
}

fn u32_at(b: &[u8], o: usize) -> u32 {
    u32::from_le_bytes([b[o], b[o + 1], b[o + 2], b[o + 3]])
}

/// The physics blobs of a CGF (chunk table 0x744 / 0x745; blob = chunk +40, size at +16).
pub fn phys_blobs(b: &[u8]) -> Vec<Vec<u8>> {
    let mut out = Vec::new();
    if b.len() < 20 || &b[..6] != b"CryTek" {
        return out;
    }
    let ver = u32_at(b, 12);
    let to = u32_at(b, 16) as usize;
    let n = u32_at(b, to) as usize;
    let es = if ver == 0x744 { 16 } else { 20 };
    for i in 0..n {
        let e = to + 4 + i * es;
        if e + 16 > b.len() {
            break;
        }
        if u32_at(b, e) == 0xCCCC_0018 {
            let o = u32_at(b, e + 8) as usize;
            let size = u32_at(b, o + 16) as usize;
            if o + 40 + size <= b.len() {
                out.push(b[o + 40..o + 40 + size].to_vec());
            }
        }
    }
    out
}

/// What the tests need from a trimesh blob.
pub struct MeshBlob {
    /// Mesh flags as stored (+152).
    pub flags: u32,
    pub n_tris: i32,
    /// `None` when the vertices come from the render mesh (flag 1 / shared).
    pub verts: Option<Vec<Vec3>>,
    pub idx: Option<Vec<i32>>,
    /// The blob has a vertex map: its mesh was welded after the tree was built.
    pub has_vtx_map: bool,
    /// Serialized tree type (0 OBB, 1 AABB, 2 SingleBox).
    pub tree_type: i32,
    /// Offset of the tree's own data (after the type id) in the blob.
    pub tree_off: usize,
}

/// Walks a phys-geometry blob (`LoadPhysGeometry` 0x3514B340 → `CTriMesh::Load`) to its tree.
/// `None` for non-trimesh blobs and for the degenerate single box built at load time.
pub fn parse_trimesh(b: &[u8]) -> Option<MeshBlob> {
    let mut r = MemReader::new(b);
    if r.u32().ok()? != 1 {
        return None;
    }
    r.skip(64).ok()?;
    if r.u32().ok()? != 1 {
        return None;
    }
    let nv = r.u32().ok()? as usize;
    let nt = r.u32().ok()? as usize;
    let _valency = r.u32().ok()?;
    let flags = r.u32().ok()?;
    let has_vtx_map = r.u8().ok()? != 0;
    if has_vtx_map {
        r.skip(2 * nv).ok()?;
    }
    let (mut verts, mut idx) = (None, None);
    let has_foreign = r.u8().ok()? != 0;
    let shared = has_foreign && flags & 0x40_0000 == 0;
    if has_foreign {
        r.skip(2 * nt).ok()?;
    }
    if !shared {
        if flags & 1 == 0 {
            verts = Some(
                (0..nv)
                    .map(|_| r.vec3())
                    .collect::<Result<Vec<_>, _>>()
                    .ok()?,
            );
        }
        idx = Some(
            (0..3 * nt)
                .map(|_| r.u16().map(i32::from))
                .collect::<Result<Vec<_>, _>>()
                .ok()?,
        );
        if r.u8().ok()? != 0 {
            r.skip(nt).ok()?;
        }
    }
    r.skip(16).ok()?;
    if r.u32().ok()? != 0 {
        return None;
    }
    r.skip(32).ok()?;
    let tree_type = r.i32().ok()?;
    Some(MeshBlob {
        flags,
        n_tris: nt as i32,
        verts,
        idx,
        has_vtx_map,
        tree_type,
        tree_off: r.pos,
    })
}

/// Every trimesh blob of the corpus: `(name, blob, parsed)`.
pub fn all_meshes() -> Vec<(String, Vec<u8>, MeshBlob)> {
    let mut files = Vec::new();
    cgf_files(&corpus_dir(), &mut files);
    let mut out = Vec::new();
    for f in files {
        let Ok(data) = std::fs::read(&f) else {
            continue;
        };
        for (k, blob) in phys_blobs(&data).into_iter().enumerate() {
            if let Some(m) = parse_trimesh(&blob) {
                let name = format!(
                    "{}#{k}",
                    f.file_name().unwrap_or_default().to_string_lossy()
                );
                out.push((name, blob, m));
            }
        }
    }
    out
}
