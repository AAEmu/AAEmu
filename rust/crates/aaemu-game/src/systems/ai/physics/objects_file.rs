//! `Models/CryEngine/Objects` (`ObjectsFile` + the prefab readers): a cell's
//! `client/object.dat`. Only the water volumes (`ObjectDataType11Water`) are kept — they are the
//! only prefabs the server uses (`WaterBodies.AddFromCellData`) — but every prefab is walked with
//! the C# size rules so the same water volumes are found, in the same order (the prefab index is
//! part of the water area names).

/// `ObjectDataType`.
pub mod object_type {
    pub const BRUSH: i32 = 1;
    pub const VOXEL: i32 = 6;
    pub const WATER_VOLUME: i32 = 11;
    pub const ROAD: i32 = 13;
}

/// `WaterObjectVolumeType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WaterVolumeType {
    Area,
    Sector,
    River,
    Ocean,
    /// Any other byte value (not a known enum member).
    Other(u8),
}

impl WaterVolumeType {
    fn from_u8(v: u8) -> Self {
        match v {
            0 => Self::Area,
            1 => Self::Sector,
            2 => Self::River,
            3 => Self::Ocean,
            o => Self::Other(o),
        }
    }
}

type V3 = [f32; 3];

/// `ObjectDataType11Water` (the fields the water bodies use).
#[derive(Debug, Clone, PartialEq)]
pub struct WaterPrefab {
    pub volume_type: WaterVolumeType,
    pub start_pos: V3,
    pub end_pos: V3,
    pub shape_points: Vec<V3>,
    pub physics_contour_points: Vec<V3>,
    pub depth: f32,
    pub speed: f32,
    /// `Max(EndPos.Z, StartPos.Z)`.
    pub surface_height: f32,
}

/// One entry of `ObjectsFile.PrefabsList` (only water volumes carry data).
#[derive(Debug, Clone, PartialEq)]
pub enum Prefab {
    Water(WaterPrefab),
    Other(i32),
}

/// `ObjectsFile.ReadFile()`: the prefabs read (also on failure, like `LoadedObjectDat`) and
/// whether the whole file was read.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct ObjectsFile {
    pub prefabs: Vec<Prefab>,
    pub ok: bool,
}

/// A `BinaryReader` / `BitConverter` read past the end (the C# exception).
#[derive(Debug)]
struct OutOfRange;

fn i32_at(b: &[u8], o: usize) -> Result<i32, OutOfRange> {
    b.get(o..o + 4)
        .map(|s| i32::from_le_bytes([s[0], s[1], s[2], s[3]]))
        .ok_or(OutOfRange)
}

fn f32_at(b: &[u8], o: usize) -> Result<f32, OutOfRange> {
    i32_at(b, o).map(|v| f32::from_bits(v as u32))
}

fn v3_at(b: &[u8], o: usize) -> Result<V3, OutOfRange> {
    Ok([f32_at(b, o)?, f32_at(b, o + 4)?, f32_at(b, o + 8)?])
}

/// `ObjectDataBase.ObjectTotalSizesByType`.
fn generic_size(t: i32) -> usize {
    match t {
        1 => 132,
        2 => 68,
        4 => 100,
        5 => 124,
        8 => 196,
        9 => 115,
        14 => 83,
        27 => 652,
        _ => 0,
    }
}

/// `prefab.ReadData(blockData, offset)`: bytes used (0 = unreadable) and the prefab.
fn read_prefab(block: &[u8], offset: usize, t: i32) -> Result<(usize, Prefab), OutOfRange> {
    let len = block.len();
    match t {
        object_type::BRUSH => {
            // No size check: the field reads throw past the end.
            v3_at(block, offset + 0x04)?;
            v3_at(block, offset + 0x10)?;
            for i in 0..12 {
                f32_at(block, offset + 0x47 + i * 4)?;
            }
            i32_at(block, offset + 0x77)?;
            i32_at(block, offset + 0x7F)?;
            Ok((0x84.min(len - offset), Prefab::Other(t)))
        }
        object_type::VOXEL => Ok((read_voxel(block, offset), Prefab::Other(t))),
        object_type::WATER_VOLUME => read_water(block, offset),
        object_type::ROAD => {
            const HEADER: usize = 67;
            if i32_at(block, offset)? != t || offset + HEADER > len {
                return Ok((0, Prefab::Other(t)));
            }
            let count = block[offset + 43] as usize;
            let total = count * 12 + HEADER;
            if offset + total > len {
                return Ok((len - offset, Prefab::Other(t)));
            }
            if count == 0 {
                return Ok((HEADER, Prefab::Other(t)));
            }
            Ok((total, Prefab::Other(t)))
        }
        _ => {
            let n = generic_size(t);
            if n + offset > len {
                return Ok((0, Prefab::Other(t)));
            }
            Ok((n, Prefab::Other(t)))
        }
    }
}

/// `ObjectDataType6Voxel.ReadData` (only its size: every failure returns 0).
fn read_voxel(block: &[u8], offset: usize) -> usize {
    let num_ranges_offset = offset + 198_779;
    let Ok(num_ranges) = i32_at(block, num_ranges_offset) else {
        return 0;
    };
    let mut pos = num_ranges_offset as i64 + 4;
    for _ in 0..num_ranges.max(0) {
        if pos < 0 || pos as usize + 4 > block.len() {
            return 0;
        }
        let Ok(size) = i32_at(block, pos as usize) else {
            return 0;
        };
        pos += 4 + i64::from(size);
    }
    let total = pos - offset as i64;
    // Data = blockData.Skip(offset).Take(total)
    let data_len = if total <= 0 {
        0
    } else {
        (total as usize).min(block.len() - offset)
    };
    let data = &block[offset..offset + data_len];
    let Ok(t) = i32_at(data, 0) else {
        return 0;
    };
    if t != object_type::VOXEL {
        return 0;
    }
    // header fields up to LodUnCompressedSize (BitConverter throws past the end)
    if data.len() < 198_791 {
        return 0;
    }
    let lod_compressed = i32_at(data, 198_783).unwrap_or(0);
    let to_read = i64::from(lod_compressed) - 4;
    if to_read < 0 || (data.len() as i64) < 198_791 + to_read {
        return 0;
    }
    data.len()
}

/// `ObjectDataType11Water.ReadData`.
fn read_water(block: &[u8], offset: usize) -> Result<(usize, Prefab), OutOfRange> {
    const START: i32 = 0x7B;
    let t = object_type::WATER_VOLUME;
    let len = block.len();
    if i32_at(block, offset)? != t || offset + START as usize > len {
        return Ok((0, Prefab::Other(t)));
    }
    let start_pos = v3_at(block, offset + 0x04)?;
    let end_pos = v3_at(block, offset + 0x10)?;
    let volume_type = WaterVolumeType::from_u8(block[offset + 0x2B]);
    let shape_count = i32_at(block, offset + 0x6B)?;
    let depth = f32_at(block, offset + 0x6F)?;
    let speed = f32_at(block, offset + 0x73)?;
    let contour_count = i32_at(block, offset + 0x77)?;
    let surface_height = end_pos[2].max(start_pos[2]);
    let mut w = WaterPrefab {
        volume_type,
        start_pos,
        end_pos,
        shape_points: Vec::new(),
        physics_contour_points: Vec::new(),
        depth,
        speed,
        surface_height,
    };
    let total = shape_count
        .wrapping_mul(12)
        .wrapping_add(contour_count.wrapping_mul(12))
        .wrapping_add(START);
    if offset as i64 + i64::from(total) > len as i64 {
        return Ok((len - offset, Prefab::Water(w)));
    }
    for i in 0..shape_count.max(0) {
        let o = offset as i64 + i64::from(START) + i64::from(i) * 12;
        w.shape_points
            .push(v3_at(block, usize::try_from(o).map_err(|_| OutOfRange)?)?);
    }
    let start2 = i64::from(START) + i64::from(shape_count) * 12;
    for i in 0..contour_count.max(0) {
        let o = offset as i64 + start2 + i64::from(i) * 12;
        w.physics_contour_points
            .push(v3_at(block, usize::try_from(o).map_err(|_| OutOfRange)?)?);
    }
    if shape_count <= 0 && contour_count <= 0 {
        return Ok((START as usize, Prefab::Water(w)));
    }
    // Data = blockData.Skip(offset).Take(total) → its length
    let used = if total <= 0 {
        0
    } else {
        (total as usize).min(len - offset)
    };
    Ok((used, Prefab::Water(w)))
}

/// `ParseObjectBlockData`.
fn parse_block(block: &[u8], out: &mut Vec<Prefab>) -> Result<(), OutOfRange> {
    let mut offset = 0usize;
    while offset < block.len() {
        if offset + 4 > block.len() {
            break;
        }
        let t = i32_at(block, offset)?;
        let (size, prefab) = read_prefab(block, offset, t)?;
        if size == 0 {
            tracing::warn!("Unknown/unreadable type {t} @ 0x{offset:X} — stopping block parse");
            break;
        }
        out.push(prefab);
        offset += size;
    }
    Ok(())
}

const NODE_HEADER_SIZE: usize = 33;

/// `ReadPrefabNode`: the offset after the node (and its children) and success.
fn read_node(
    data: &[u8],
    start: usize,
    out: &mut Vec<Prefab>,
) -> Result<(usize, bool), OutOfRange> {
    let len = data.len();
    if start + NODE_HEADER_SIZE > len {
        return Ok((start, false));
    }
    let size = i32_at(data, start + 28)?;
    let mask = data[start + 32];
    let content = start + NODE_HEADER_SIZE;
    let mut child_start = content;
    if size > 0 {
        let size = size as usize;
        if content + size > len {
            return Ok((len, false));
        }
        parse_block(&data[content..content + size], out)?;
        child_start = content + size;
    }
    let mut current = child_start;
    if mask > 0 {
        for i in 0..8 {
            if mask & (1 << i) != 0 {
                let (next, ok) = read_node(data, current, out)?;
                current = next;
                if !ok {
                    return Ok((len, false));
                }
            }
        }
    }
    Ok((current, true))
}

impl ObjectsFile {
    /// `ObjectsFile.ReadFile()` on the file's bytes.
    pub fn read(data: &[u8]) -> ObjectsFile {
        let mut out = ObjectsFile::default();
        if data.len() <= 8 {
            out.ok = true;
            return out;
        }
        let res = (|| -> Result<bool, OutOfRange> {
            let asset_count = i32_at(data, 0)? as u32 as usize;
            let mut pos = 4usize;
            for _ in 0..asset_count {
                if pos + 260 > data.len() {
                    return Err(OutOfRange);
                }
                pos += 260;
            }
            let prefab_count = i32_at(data, pos)? as u32 as usize;
            pos += 4;
            // Seek past the prefab paths (seeking beyond the end is allowed)
            pos = pos.saturating_add(prefab_count.saturating_mul(260));
            for _ in 0..prefab_count {
                if pos >= data.len() {
                    return Ok(true);
                }
                let (next, ok) = read_node(data, pos, &mut out.prefabs)?;
                if !ok {
                    return Ok(false);
                }
                pos = next;
            }
            Ok(true)
        })();
        match res {
            Ok(ok) => out.ok = ok,
            Err(_) => {
                tracing::warn!("Error reading object.dat: read past the end");
                out.ok = false;
            }
        }
        out
    }
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    /// A water volume record (`ObjectDataType11Water`).
    pub fn water_record(
        volume: u8,
        start: V3,
        end: V3,
        depth: f32,
        shape: &[V3],
        contour: &[V3],
    ) -> Vec<u8> {
        let mut b = vec![0u8; 0x7B];
        b[0..4].copy_from_slice(&11i32.to_le_bytes());
        for (i, v) in start.iter().enumerate() {
            b[4 + i * 4..8 + i * 4].copy_from_slice(&v.to_le_bytes());
        }
        for (i, v) in end.iter().enumerate() {
            b[0x10 + i * 4..0x14 + i * 4].copy_from_slice(&v.to_le_bytes());
        }
        b[0x2B] = volume;
        b[0x6B..0x6F].copy_from_slice(&(shape.len() as i32).to_le_bytes());
        b[0x6F..0x73].copy_from_slice(&depth.to_le_bytes());
        b[0x77..0x7B].copy_from_slice(&(contour.len() as i32).to_le_bytes());
        for p in shape.iter().chain(contour) {
            for v in p {
                b.extend_from_slice(&v.to_le_bytes());
            }
        }
        b
    }

    /// An object.dat with one root prefab node holding `block`.
    pub fn object_dat(block: &[u8]) -> Vec<u8> {
        let mut f = Vec::new();
        f.extend_from_slice(&1u32.to_le_bytes()); // one asset path
        f.extend_from_slice(&[0u8; 260]);
        f.extend_from_slice(&1u32.to_le_bytes()); // one prefab path
        f.extend_from_slice(&[0u8; 260]);
        f.extend_from_slice(&0i32.to_le_bytes());
        f.extend_from_slice(&[0u8; 24]);
        f.extend_from_slice(&(block.len() as i32).to_le_bytes());
        f.push(0);
        f.extend_from_slice(block);
        f
    }

    #[test]
    fn walks_generic_prefabs_and_reads_water() {
        let mut block = vec![0u8; 68];
        block[0..4].copy_from_slice(&2i32.to_le_bytes()); // vegetation, 68 bytes
        let contour = [[0.0, 0.0, 0.0], [100.0, 0.0, 0.0], [100.0, 100.0, 0.0]];
        block.extend(water_record(
            0,
            [0.0, 0.0, 120.0],
            [0.0, 0.0, 125.0],
            6.0,
            &[],
            &contour,
        ));
        let f = ObjectsFile::read(&object_dat(&block));
        assert!(f.ok);
        assert_eq!(f.prefabs.len(), 2);
        let Prefab::Water(w) = &f.prefabs[1] else {
            panic!("water expected")
        };
        assert_eq!(w.volume_type, WaterVolumeType::Area);
        assert_eq!(w.surface_height, 125.0);
        assert_eq!(w.physics_contour_points.len(), 3);
        assert_eq!(w.depth, 6.0);
    }

    #[test]
    fn unknown_type_stops_the_block_and_truncated_files_fail() {
        let mut block = vec![0u8; 16];
        block[0..4].copy_from_slice(&3i32.to_le_bytes()); // unknown size
        let f = ObjectsFile::read(&object_dat(&block));
        assert!(f.ok && f.prefabs.is_empty());
        let mut data = object_dat(&[0u8; 68]);
        data.truncate(data.len() - 10);
        let f = ObjectsFile::read(&data);
        assert!(!f.ok);
        assert!(ObjectsFile::read(&[0u8; 8]).ok);
    }
}
