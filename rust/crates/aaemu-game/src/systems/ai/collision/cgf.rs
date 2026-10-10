//! The collision side of a CryEngine `.cgf` model: the chunk table, the `Mesh` chunks with
//! their `MeshPhysicsData` slots and, for the physics blobs that share the render mesh's
//! vertices, the `DataStream` positions / indices and the `MeshSubsets` material ids.
//!
//! Format notes: `D:\aa-re\zone\agent_geom\sub_cgf.md` §1 (10.x; the 1.2 pak uses the same
//! container versions 0x744 / 0x745). The engine side (cry3dengine):
//!
//! | step | address |
//! |---|---|
//! | `"CryTek"` check, chunk loop | `CLoaderCGF` 0x301DBF30 |
//! | node load / proxy-name test | 0x301DB9A0 |
//! | mesh chunk (`physicsDataChunkId[4]` at +92) | 0x301D4F20 |
//! | `LoadPhysicsDataChunk` (`data[0] == 0xCCCC0018 && data[1] == 0x800`, size at +16, blob at +40) | 0x301D1FD0 |
//! | compiled physics → `LoadPhysGeometry(stream, positions (stride 12), indices (stride 2), per-face mats)` → `AddPhysGeom(0x1000 + slot)` | 0x30047F40 / 0x300400D0 |
//!
//! `AddPhysGeom` (0x300400D0) stores the geometry in the stat object's frame: no node transform
//! is applied (the compiled models keep physics nodes at identity; see the real-data test).

use aaemu_physics::math::Vec3;

/// Chunk type ids.
pub mod chunk_type {
    pub const MESH: u32 = 0xCCCC_0000;
    pub const NODE: u32 = 0xCCCC_000B;
    pub const DATA_STREAM: u32 = 0xCCCC_0016;
    pub const MESH_SUBSETS: u32 = 0xCCCC_0017;
    pub const MESH_PHYSICS_DATA: u32 = 0xCCCC_0018;
}

/// `DataStream` types used here (`streamChunkId[i]` of the mesh chunk is the stream of type `i`).
pub mod stream_type {
    pub const POSITIONS: usize = 0;
    pub const INDICES: usize = 5;
    /// Compressed positions (`SVF_P3S_C4B_T2S`: 3 half floats + pad, colour, 2 half uvs).
    pub const P3S_C4B_T2S: usize = 15;
}

/// Errors of the CGF reader.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum CgfError {
    #[error("not a CryTek file")]
    Signature,
    #[error("unsupported chunk table version {0:#x}")]
    Version(u32),
    #[error("truncated at {0}")]
    Truncated(usize),
}

/// One chunk table entry.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Chunk {
    pub ctype: u32,
    pub version: u32,
    pub offset: u32,
    pub id: i32,
    pub size: u32,
}

fn u32_at(d: &[u8], o: usize) -> Result<u32, CgfError> {
    d.get(o..o + 4)
        .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
        .ok_or(CgfError::Truncated(o))
}

fn i32_at(d: &[u8], o: usize) -> Result<i32, CgfError> {
    u32_at(d, o).map(|v| v as i32)
}

fn f32_at(d: &[u8], o: usize) -> Result<f32, CgfError> {
    u32_at(d, o).map(f32::from_bits)
}

/// A parsed chunk table over the file bytes.
#[derive(Debug)]
pub struct CgfFile<'a> {
    pub data: &'a [u8],
    pub version: u32,
    pub chunks: Vec<Chunk>,
}

impl<'a> CgfFile<'a> {
    /// Header `"CryTek\0\0"`, file type, version 0x744 (16-byte entries, sizes from the next
    /// offset) or 0x745 (20-byte entries with sizes).
    pub fn parse(data: &'a [u8]) -> Result<Self, CgfError> {
        if data.len() < 20 || &data[..6] != b"CryTek" {
            return Err(CgfError::Signature);
        }
        let version = u32_at(data, 12)?;
        let table = u32_at(data, 16)? as usize;
        let n = u32_at(data, table)? as usize;
        let mut chunks = Vec::with_capacity(n);
        match version {
            0x744 => {
                for i in 0..n {
                    let o = table + 4 + i * 16;
                    chunks.push(Chunk {
                        ctype: u32_at(data, o)?,
                        version: u32_at(data, o + 4)?,
                        offset: u32_at(data, o + 8)?,
                        id: i32_at(data, o + 12)?,
                        size: 0,
                    });
                }
                // sizes: up to the next chunk (or the table / end of file)
                let mut offs: Vec<u32> = chunks.iter().map(|c| c.offset).collect();
                offs.push(table as u32);
                offs.push(data.len() as u32);
                offs.sort_unstable();
                offs.dedup();
                for c in &mut chunks {
                    let next = offs
                        .iter()
                        .copied()
                        .find(|&x| x > c.offset)
                        .unwrap_or(data.len() as u32);
                    c.size = next - c.offset;
                }
            }
            0x745 => {
                for i in 0..n {
                    let o = table + 4 + i * 20;
                    chunks.push(Chunk {
                        ctype: u32_at(data, o)?,
                        version: u32_at(data, o + 4)?,
                        offset: u32_at(data, o + 8)?,
                        id: i32_at(data, o + 12)?,
                        size: u32_at(data, o + 16)?,
                    });
                }
            }
            v => return Err(CgfError::Version(v)),
        }
        Ok(Self {
            data,
            version,
            chunks,
        })
    }

    /// The chunk with an id.
    pub fn chunk(&self, id: i32) -> Option<&Chunk> {
        self.chunks.iter().find(|c| c.id == id)
    }

    /// A chunk's bytes after its 16-byte header copy (`{type, version, offset, id}`).
    pub fn body(&self, c: &Chunk) -> Result<&'a [u8], CgfError> {
        let start = c.offset as usize + 16;
        let end = (c.offset as usize + c.size as usize).min(self.data.len());
        if start > end {
            return Err(CgfError::Truncated(start));
        }
        Ok(&self.data[start..end])
    }
}

/// The physics of one mesh chunk slot.
#[derive(Debug, Clone)]
pub struct PhysBlob {
    /// 0 = default (`0x1000`, collide), 1 = no-collide (`0x1001`), 2 = obstruct (`0x1002`),
    /// 3 = unused (`0x1003`).
    pub slot: usize,
    /// The serialized `phys_geometry` (version word first).
    pub blob: Vec<u8>,
    /// The node that references the mesh (name, for diagnostics).
    pub node: String,
    /// The node's transform is not the identity (the engine ignores it; counted by tests).
    pub node_tm_identity: bool,
    /// Render arrays of the mesh, filled only when the blob uses them
    /// ([`blob_shares_render_mesh`]).
    pub render: Option<RenderMesh>,
}

/// The render mesh arrays `LoadPhysGeometry` receives (positions stride 12, u16 indices,
/// per-face material ids from the subsets).
#[derive(Debug, Clone, Default)]
pub struct RenderMesh {
    pub positions: Vec<Vec3>,
    pub indices: Vec<u16>,
    pub face_mats: Vec<i8>,
}

/// Every `MeshPhysicsData` slot referenced by a node's mesh, in chunk-table order of the
/// nodes. Mesh chunks referenced by several nodes are reported once.
pub fn physics_blobs(data: &[u8]) -> Result<Vec<PhysBlob>, CgfError> {
    let f = CgfFile::parse(data)?;
    let mut out = Vec::new();
    let mut seen_meshes = Vec::new();
    for node in f.chunks.iter().filter(|c| c.ctype == chunk_type::NODE) {
        let b = f.body(node)?;
        if b.len() < 148 {
            continue;
        }
        let name_end = b[..64].iter().position(|&c| c == 0).unwrap_or(64);
        let name = String::from_utf8_lossy(&b[..name_end]).into_owned();
        let object_id = i32_at(b, 64)?;
        let mut tm = [0f32; 16];
        for (i, v) in tm.iter_mut().enumerate() {
            *v = f32_at(b, 84 + 4 * i)?;
        }
        let identity = tm
            .iter()
            .enumerate()
            .all(|(i, &v)| v == if i % 5 == 0 { 1.0 } else { 0.0 });
        let Some(mesh) = f.chunk(object_id) else {
            continue;
        };
        if mesh.ctype != chunk_type::MESH || seen_meshes.contains(&object_id) {
            continue;
        }
        seen_meshes.push(object_id);
        let mb = f.body(mesh)?;
        if mb.len() < 108 {
            continue;
        }
        for slot in 0..4 {
            let pid = i32_at(mb, 92 + 4 * slot)?;
            if pid <= 0 {
                continue;
            }
            let Some(pc) = f.chunk(pid) else {
                continue;
            };
            if pc.ctype != chunk_type::MESH_PHYSICS_DATA {
                continue;
            }
            // LoadPhysicsDataChunk: data[0] == 0xCCCC0018 && data[1] == 0x800 (the 16-byte
            // header copy), blob size at +16, blob at +40.
            let o = pc.offset as usize;
            if u32_at(data, o)? != chunk_type::MESH_PHYSICS_DATA || u32_at(data, o + 4)? != 0x800 {
                continue;
            }
            let size = u32_at(data, o + 16)? as usize;
            let blob = data
                .get(o + 40..o + 40 + size)
                .ok_or(CgfError::Truncated(o + 40 + size))?
                .to_vec();
            let render = if blob_shares_render_mesh(&blob) {
                Some(render_mesh(&f, mb)?)
            } else {
                None
            };
            out.push(PhysBlob {
                slot,
                blob,
                node: name.clone(),
                node_tm_identity: identity,
                render,
            });
        }
    }
    Ok(out)
}

/// A trimesh blob (type 1) whose vertices and indices come from the render mesh:
/// `hasForeignIdx` set and mesh flag 0x400000 clear (`CTriMesh::Load` 0x351D83B0).
pub fn blob_shares_render_mesh(blob: &[u8]) -> bool {
    // u32 version, 64-byte header, u32 type
    if blob.len() < 4 + 64 + 4 + 16 + 1 || u32_at(blob, 68).ok() != Some(1) {
        return false;
    }
    let nverts = u32_at(blob, 72).unwrap_or(0) as usize;
    let flags = u32_at(blob, 84).unwrap_or(0);
    let mut p = 88;
    let has_map = blob.get(p).copied().unwrap_or(0) != 0;
    p += 1;
    if has_map {
        p += 2 * nverts;
    }
    let foreign = blob.get(p).copied().unwrap_or(0) != 0;
    foreign && flags & 0x40_0000 == 0
}

/// A data stream: element count, element size, bytes.
type Stream<'a> = (u32, u32, &'a [u8]);

fn stream<'a>(f: &CgfFile<'a>, mesh: &[u8], ty: usize) -> Result<Option<Stream<'a>>, CgfError> {
    let id = i32_at(mesh, 28 + 4 * ty)?;
    if id <= 0 {
        return Ok(None);
    }
    let Some(c) = f.chunk(id) else {
        return Ok(None);
    };
    if c.ctype != chunk_type::DATA_STREAM {
        return Ok(None);
    }
    let b = f.body(c)?;
    // u32 flags, u32 type, u32 count, u32 elemSize, u32 reserved[2], data
    let count = u32_at(b, 8)?;
    let elem = u32_at(b, 12)?;
    let len = count as usize * elem as usize;
    let d = b.get(24..24 + len).ok_or(CgfError::Truncated(24 + len))?;
    Ok(Some((count, elem, d)))
}

fn half_to_f32(h: u16) -> f32 {
    let sign = u32::from(h >> 15) << 31;
    let exp = u32::from((h >> 10) & 0x1F);
    let mant = u32::from(h & 0x3FF);
    let bits = if exp == 0 {
        if mant == 0 {
            sign
        } else {
            // subnormal half
            let mut e = 127 - 15 + 1;
            let mut m = mant;
            while m & 0x400 == 0 {
                m <<= 1;
                e -= 1;
            }
            sign | ((e as u32) << 23) | ((m & 0x3FF) << 13)
        }
    } else if exp == 31 {
        sign | 0x7F80_0000 | (mant << 13)
    } else {
        sign | ((exp + 127 - 15) << 23) | (mant << 13)
    };
    f32::from_bits(bits)
}

/// The render arrays of a mesh chunk (`CMesh` positions / indices / subsets).
fn render_mesh(f: &CgfFile<'_>, mesh: &[u8]) -> Result<RenderMesh, CgfError> {
    let mut rm = RenderMesh::default();
    if let Some((n, elem, d)) = stream(f, mesh, stream_type::POSITIONS)? {
        for i in 0..n as usize {
            let o = i * elem as usize;
            rm.positions.push(Vec3::new(
                f32_at(d, o)?,
                f32_at(d, o + 4)?,
                f32_at(d, o + 8)?,
            ));
        }
    } else if let Some((n, elem, d)) = stream(f, mesh, stream_type::P3S_C4B_T2S)? {
        for i in 0..n as usize {
            let o = i * elem as usize;
            let h = |k: usize| -> Result<f32, CgfError> {
                let b = d
                    .get(o + 2 * k..o + 2 * k + 2)
                    .ok_or(CgfError::Truncated(o))?;
                Ok(half_to_f32(u16::from_le_bytes([b[0], b[1]])))
            };
            rm.positions.push(Vec3::new(h(0)?, h(1)?, h(2)?));
        }
    }
    if let Some((n, elem, d)) = stream(f, mesh, stream_type::INDICES)? {
        for i in 0..n as usize {
            let v = if elem == 4 {
                u32_at(d, i * 4)? as u16
            } else {
                let b = d.get(i * 2..i * 2 + 2).ok_or(CgfError::Truncated(i * 2))?;
                u16::from_le_bytes([b[0], b[1]])
            };
            rm.indices.push(v);
        }
    }
    // per-face material ids from the subsets (sub_30047F40: face [first/3, first/3 + n/3) gets
    // the subset's material id)
    rm.face_mats = vec![0; rm.indices.len() / 3];
    let subsets_id = i32_at(mesh, 20)?;
    if let Some(c) = f
        .chunk(subsets_id)
        .filter(|c| c.ctype == chunk_type::MESH_SUBSETS)
    {
        let b = f.body(c)?;
        let count = u32_at(b, 4)? as usize;
        for i in 0..count {
            let o = 16 + i * 36;
            let first = i32_at(b, o)?.max(0) as usize / 3;
            let n = i32_at(b, o + 4)?.max(0) as usize / 3;
            let mat = i32_at(b, o + 16)? as i8;
            for fm in rm.face_mats.iter_mut().skip(first).take(n) {
                *fm = mat;
            }
        }
    }
    Ok(rm)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A minimal 0x745 file: one node → one mesh → one box physics blob.
    pub(crate) fn synthetic_cgf(blob: &[u8]) -> Vec<u8> {
        let mut chunks: Vec<(u32, u32, i32, Vec<u8>)> = Vec::new();
        // node (id 1) -> mesh 2
        let mut node = vec![0u8; 204];
        node[..5].copy_from_slice(b"proxy");
        node[64..68].copy_from_slice(&2i32.to_le_bytes());
        node[68..72].copy_from_slice(&(-1i32).to_le_bytes());
        for i in 0..4 {
            let o = 84 + 4 * (i * 5);
            node[o..o + 4].copy_from_slice(&1f32.to_le_bytes());
        }
        chunks.push((chunk_type::NODE, 0x823, 1, node));
        // mesh (id 2): physicsDataChunkId[0] = 3
        let mut mesh = vec![0u8; 140];
        mesh[92..96].copy_from_slice(&3i32.to_le_bytes());
        chunks.push((chunk_type::MESH, 0x800, 2, mesh));
        // physics (id 3): 24 bytes (size at +0 after the header) then the blob
        let mut phys = vec![0u8; 24];
        phys[..4].copy_from_slice(&(blob.len() as u32).to_le_bytes());
        phys.extend_from_slice(blob);
        chunks.push((chunk_type::MESH_PHYSICS_DATA, 0x800, 3, phys));
        let table = 20usize;
        let mut out = Vec::new();
        out.extend_from_slice(b"CryTek\0\0");
        out.extend_from_slice(&0xFFFF_0000u32.to_le_bytes());
        out.extend_from_slice(&0x745u32.to_le_bytes());
        out.extend_from_slice(&(table as u32).to_le_bytes());
        out.extend_from_slice(&(chunks.len() as u32).to_le_bytes());
        let mut off = table + 4 + chunks.len() * 20;
        let mut entries = Vec::new();
        for (t, v, id, body) in &chunks {
            let size = 16 + body.len();
            entries.push((*t, *v, off as u32, *id, size as u32));
            off += size;
        }
        for (t, v, o, id, s) in &entries {
            for w in [*t, *v, *o, *id as u32, *s] {
                out.extend_from_slice(&w.to_le_bytes());
            }
        }
        for ((t, v, id, body), (_, _, o, _, _)) in chunks.iter().zip(&entries) {
            for w in [*t, *v, *o, *id as u32] {
                out.extend_from_slice(&w.to_le_bytes());
            }
            out.extend_from_slice(body);
        }
        out
    }

    #[test]
    fn parses_synthetic_node_mesh_physics() {
        let blob = vec![1u8, 0, 0, 0, 9, 9, 9];
        let file = synthetic_cgf(&blob);
        let f = CgfFile::parse(&file).unwrap();
        assert_eq!(f.version, 0x745);
        assert_eq!(f.chunks.len(), 3);
        let blobs = physics_blobs(&file).unwrap();
        assert_eq!(blobs.len(), 1);
        assert_eq!(blobs[0].slot, 0);
        assert_eq!(blobs[0].blob, blob);
        assert_eq!(blobs[0].node, "proxy");
        assert!(blobs[0].node_tm_identity);
        assert!(blobs[0].render.is_none());
    }

    #[test]
    fn rejects_bad_signature() {
        assert_eq!(
            CgfFile::parse(b"NotCryTek_______________").unwrap_err(),
            CgfError::Signature
        );
    }

    #[test]
    fn half_floats() {
        assert_eq!(half_to_f32(0x3C00), 1.0);
        assert_eq!(half_to_f32(0xC000), -2.0);
        assert_eq!(half_to_f32(0x0000), 0.0);
        assert_eq!(half_to_f32(0x3555), 0.333_251_95);
        assert_eq!(half_to_f32(0x0001), 5.960_464_5e-8);
    }
}
