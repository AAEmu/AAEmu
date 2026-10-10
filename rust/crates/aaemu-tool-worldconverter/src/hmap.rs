//! Port of `Tools/WorldConverter/Models/Hmap.cs` (+ `AABB.cs`): the client's per-cell
//! `client/terrain/heightmap.dat` format.
//!
//! All arithmetic mirrors the C# operand types (float vs double vs integer promotions and
//! saturating casts of .NET 9+), so the generated heights are bit-identical.

use std::cmp::Ordering;

#[derive(Debug, thiserror::Error)]
pub enum HmapError {
    #[error("Unable to read beyond the end of the stream.")]
    EndOfStream,
    #[error("Arithmetic operation resulted in an overflow. (negative node size {0})")]
    NegativeSize(i32),
    #[error("Index was outside the bounds of the array.")]
    IndexOutOfRange,
}

/// `BinaryReader` over a byte slice.
pub struct Reader<'a> {
    data: &'a [u8],
    pos: usize,
}

impl<'a> Reader<'a> {
    pub fn new(data: &'a [u8]) -> Self {
        Self { data, pos: 0 }
    }

    pub fn position(&self) -> usize {
        self.pos
    }

    fn take<const N: usize>(&mut self) -> Result<[u8; N], HmapError> {
        let s = self
            .data
            .get(self.pos..self.pos + N)
            .ok_or(HmapError::EndOfStream)?;
        self.pos += N;
        Ok(s.try_into().unwrap_or([0; N]))
    }

    fn u8(&mut self) -> Result<u8, HmapError> {
        Ok(self.take::<1>()?[0])
    }
    fn u16(&mut self) -> Result<u16, HmapError> {
        Ok(u16::from_le_bytes(self.take()?))
    }
    fn i32(&mut self) -> Result<i32, HmapError> {
        Ok(i32::from_le_bytes(self.take()?))
    }
    fn f32(&mut self) -> Result<f32, HmapError> {
        Ok(f32::from_le_bytes(self.take()?))
    }

    /// `BinaryReader.ReadBytes`: returns fewer bytes at the end of the stream (no error).
    fn skip_bytes(&mut self, count: usize) {
        self.pos = (self.pos + count).min(self.data.len());
    }
}

/// `AABB` (only the fields read from the file).
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct Aabb {
    pub min: [f32; 3],
    pub max: [f32; 3],
}

/// `Hmap`.
#[allow(dead_code)] // fields kept for format fidelity
#[derive(Debug, Clone, Default)]
pub struct Hmap {
    pub version: u8,
    pub dummy: u8,
    pub flags: u8,
    pub flags2: u8,
    pub chunk_size: i32,
    pub height_map_size_in_units: i32,
    pub unit_size_in_meters: i32,
    pub sector_size_in_meters: i32,
    pub sectors_table_size_in_sectors: i32,
    pub heightmap_z_ratio: f32,
    pub ocean_water_level: f32,
    pub nodes: Vec<NodeCell>,
}

impl Hmap {
    /// `Hmap.Read`: header, then nodes until the stream position equals `ChunkSize`.
    pub fn read(data: &[u8], disabled_re_calc: bool) -> Result<Self, HmapError> {
        let mut br = Reader::new(data);
        let mut h = Hmap {
            version: br.u8()?,
            dummy: br.u8()?,
            flags: br.u8()?,
            flags2: br.u8()?,
            chunk_size: br.i32()?,
            height_map_size_in_units: br.i32()?,
            unit_size_in_meters: br.i32()?,
            sector_size_in_meters: br.i32()?,
            sectors_table_size_in_sectors: br.i32()?,
            heightmap_z_ratio: br.f32()?,
            ocean_water_level: br.f32()?,
            nodes: Vec::new(),
        };
        if h.version >= 24 {
            for _ in 0..64 {
                br.u16()?; // unk?
            }
        }
        while i64::try_from(br.position()).unwrap_or(i64::MAX) != i64::from(h.chunk_size) {
            h.nodes.push(NodeCell::read(&mut br, disabled_re_calc)?);
        }
        Ok(h)
    }

    /// The nodes ordered by `BoxHeightmap.Min.X`, then `Min.Y` (stable, .NET float ordering),
    /// keeping only those with height data.
    pub fn sorted_nodes(&self) -> Vec<&NodeCell> {
        let mut nodes: Vec<&NodeCell> = self.nodes.iter().collect();
        nodes.sort_by(|a, b| {
            dotnet_cmp_f32(a.box_heightmap.min[0], b.box_heightmap.min[0])
                .then_with(|| dotnet_cmp_f32(a.box_heightmap.min[1], b.box_heightmap.min[1]))
        });
        nodes.retain(|n| !n.p_hm_data.is_empty());
        nodes
    }
}

/// `Comparer<float>.Default`: NaN sorts before everything else.
pub fn dotnet_cmp_f32(a: f32, b: f32) -> Ordering {
    match (a.is_nan(), b.is_nan()) {
        (true, true) => Ordering::Equal,
        (true, false) => Ordering::Less,
        (false, true) => Ordering::Greater,
        _ => a.partial_cmp(&b).unwrap_or(Ordering::Equal),
    }
}

const INV_5CM: i32 = 20;
const MASK_12BIT: u32 = (1 << 12) - 1;

/// `NodeCell`.
#[allow(dead_code)] // fields kept for format fidelity
#[derive(Debug, Clone, Default)]
pub struct NodeCell {
    pub version: u8,
    pub dummy: u8,
    pub flags: u8,
    pub flags2: u8,
    pub box_heightmap: Aabb,
    pub b_has_holes: u8,
    pub f_offset: f32,
    pub f_range: f32,
    pub n_size: i32,
    pub p_hm_data: Vec<u16>,
    i_offset: i32,
    i_range: i32,
    i_step: i32,
    f_min: f32,
    f_max: f32,
}

impl NodeCell {
    /// `NodeCell.Read`.
    pub fn read(br: &mut Reader<'_>, disabled_re_calc: bool) -> Result<Self, HmapError> {
        let mut n = NodeCell {
            version: br.u8()?,
            dummy: br.u8()?,
            flags: br.u8()?,
            flags2: br.u8()?,
            ..Default::default()
        };
        n.box_heightmap.min = [br.f32()?, br.f32()?, br.f32()?];
        n.box_heightmap.max = [br.f32()?, br.f32()?, br.f32()?];
        n.b_has_holes = br.u8()?;
        n.f_offset = br.f32()?;
        n.f_range = br.f32()?;
        n.n_size = br.i32()?;
        // new ushort[nSize * nSize] (int multiplication wraps; a negative length throws)
        let len = n.n_size.wrapping_mul(n.n_size);
        if len < 0 {
            return Err(HmapError::NegativeSize(n.n_size));
        }
        let unk_count = br.i32()?;
        n.p_hm_data = (0..len).map(|_| br.u16()).collect::<Result<_, _>>()?;
        let _i1 = br.i32()?;
        let _f1 = br.f32()?;
        let _f2 = br.f32()?;
        let _f3 = br.f32()?;
        let _f4 = br.f32()?;
        if unk_count >= 1 {
            br.skip_bytes(unk_count as usize);
        }
        br.skip_bytes(36);

        n.init();
        if !disabled_re_calc && n.version < 7 {
            n.rescale_to_int();
        }
        n.up_scale();
        Ok(n)
    }

    /// `RawDataToHeight`.
    pub fn raw_data_to_height(&self, data: u32) -> f32 {
        0.05f32 * self.i_offset as f32
            + (i64::from(data >> 4) * i64::from(self.i_step)) as f32 * 0.05f32
    }

    /// `RawDataByIndex(uint)`.
    pub fn raw_data_by_flat_index(&self, i: u32) -> Result<u16, HmapError> {
        self.p_hm_data
            .get(i as usize)
            .copied()
            .ok_or(HmapError::IndexOutOfRange)
    }

    /// `RawDataByIndex(ushort, ushort)`: `pHMData[nX * nSize + nY]`, 0 when out of range.
    ///
    /// Note: after [`NodeCell::up_scale`] the data is 33x33 but `nSize` keeps the original
    /// size, so this indexes with the wrong stride for upscaled nodes (C# behaviour, kept).
    pub fn raw_data_by_index(&self, n_x: u16, n_y: u16) -> u16 {
        if self.n_size > 0 {
            let index = i32::from(n_x)
                .wrapping_mul(self.n_size)
                .wrapping_add(i32::from(n_y));
            if index as i64 >= self.p_hm_data.len() as i64 || index < 0 {
                return 0;
            }
            return self.p_hm_data[index as usize];
        }
        0
    }

    /// `GetHeightByIndex`.
    pub fn get_height_by_index(&self, i: u32) -> Result<f32, HmapError> {
        Ok(self.raw_data_to_height(u32::from(self.raw_data_by_flat_index(i)?)))
    }

    /// `GetHeight`.
    pub fn get_height(&self, n_x: u16, n_y: u16) -> Result<f32, HmapError> {
        if self.n_size > 0 {
            let index = i32::from(n_x)
                .wrapping_mul(self.n_size)
                .wrapping_add(i32::from(n_y));
            return self.get_height_by_index(index as u32);
        }
        Ok(0.0)
    }

    fn init(&mut self) {
        self.f_min = self.f_offset;
        self.f_max = self.f_min + 0xFFF0 as f32 * self.f_range;
        self.i_offset = (self.f_min * INV_5CM as f32) as i32;
        self.i_range = ((self.f_max - self.f_min) * INV_5CM as f32) as i32;
        self.i_step = if self.i_range > 0 {
            (i64::from(self.i_range) + i64::from(MASK_12BIT) - 1) / i64::from(MASK_12BIT)
        } else {
            1
        } as i32;
    }

    fn rescale_to_int(&mut self) {
        for i in 0..self.p_hm_data.len() {
            let hraw = self.p_hm_data[i];
            let height = self.f_min + (0xFFF0 & i32::from(hraw)) as f32 * self.f_range;
            let scaled = ((height - self.f_min) * INV_5CM as f32) as i32;
            // iStep is >= 1 here (C# would throw DivideByZeroException otherwise)
            let hdec = scaled.wrapping_div(self.i_step.max(1)) as u16;
            let res = (i32::from(hraw) & 0xF) | (i32::from(hdec) << 4);
            self.p_hm_data[i] = res as u16;
        }
    }

    fn lerp(s: f32, e: f32, t: f32) -> f32 {
        s + (e - s) * t
    }

    fn blerp(c_x0y0: f32, c_x1y0: f32, c_x0y1: f32, c_x1y1: f32, tx: f32, ty: f32) -> f32 {
        Self::lerp(
            Self::lerp(c_x0y0, c_x1y0, tx),
            Self::lerp(c_x0y1, c_x1y1, tx),
            ty,
        )
    }

    /// `UpScale`: bilinearly resamples nodes smaller than 33x33 to 33x33.
    fn up_scale(&mut self) {
        if self.n_size > 0 && self.n_size < 33 {
            let n_size = self.n_size;
            let mut result = vec![0u16; 33 * 33];
            for target_x in 0..=32i32 {
                for target_y in 0..=32i32 {
                    let target_index = (target_x * 33 + target_y) as usize;
                    let source_xf = target_x as f32 / 33f32 * n_size as f32;
                    let source_yf = target_y as f32 / 33f32 * n_size as f32;
                    let source_x =
                        i32::from(f64::from(source_xf).floor() as u16).clamp(0, n_size - 1) as u16;
                    let source_y =
                        i32::from(f64::from(source_yf).floor() as u16).clamp(0, n_size - 1) as u16;
                    let tl = f32::from(self.raw_data_by_index(source_x, source_y));
                    let tr = f32::from(self.raw_data_by_index(source_x.wrapping_add(1), source_y));
                    let bl = f32::from(self.raw_data_by_index(source_x, source_y.wrapping_add(1)));
                    let br = f32::from(
                        self.raw_data_by_index(source_x.wrapping_add(1), source_y.wrapping_add(1)),
                    );
                    let off_x = source_xf - f32::from(source_x);
                    let off_y = source_yf - f32::from(source_y);
                    let height = Self::blerp(tl, tr, bl, br, off_x, off_y);
                    result[target_index] = f64::from(height).round_ties_even() as u16;
                }
            }
            self.p_hm_data = result;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn node_bytes(
        version: u8,
        n_size: i32,
        offset: f32,
        range: f32,
        data: &[u16],
        min_xy: (f32, f32),
    ) -> Vec<u8> {
        let mut v = vec![version, 0, 0, 0];
        for f in [min_xy.0, min_xy.1, 0.0, 1.0, 1.0, 1.0] {
            v.extend_from_slice(&f32::to_le_bytes(f));
        }
        v.push(0);
        v.extend_from_slice(&offset.to_le_bytes());
        v.extend_from_slice(&range.to_le_bytes());
        v.extend_from_slice(&n_size.to_le_bytes());
        v.extend_from_slice(&2i32.to_le_bytes()); // unkCount
        for d in data {
            v.extend_from_slice(&d.to_le_bytes());
        }
        v.extend_from_slice(&[0u8; 4 + 16]);
        v.extend_from_slice(&[9, 9]); // unknown data
        v.extend_from_slice(&[0u8; 36]);
        v
    }

    #[test]
    fn reads_nodes_until_chunk_size() {
        let n1 = node_bytes(7, 2, 10.0, 0.01, &[16, 32, 48, 64], (5.0, 0.0));
        let n2 = node_bytes(7, 0, 0.0, 0.0, &[], (1.0, 0.0));
        let mut file = vec![23u8, 0, 0x12, 0x34];
        let chunk = 4 + 5 * 4 + 8 + n1.len() + n2.len();
        file.extend_from_slice(&(chunk as i32).to_le_bytes());
        for v in [0i32, 0, 0, 0] {
            file.extend_from_slice(&v.to_le_bytes());
        }
        file.extend_from_slice(&1.5f32.to_le_bytes());
        file.extend_from_slice(&0.0f32.to_le_bytes());
        file.extend_from_slice(&n1);
        file.extend_from_slice(&n2);
        let h = Hmap::read(&file, false).unwrap();
        assert_eq!(h.nodes.len(), 2);
        assert_eq!((h.flags, h.flags2), (0x12, 0x34));
        let sorted = h.sorted_nodes();
        assert_eq!(sorted.len(), 1); // n2 has no data
        let n = sorted[0];
        assert_eq!(n.p_hm_data.len(), 33 * 33); // upscaled
        assert_eq!(n.n_size, 2);
        assert_eq!(n.p_hm_data[0], 16);
        // iOffset = 200, iRange = (0xFFF0 * 0.01 * 20) = 13106, iStep = 4
        assert_eq!(n.i_step, 4);
        assert!((n.raw_data_to_height(16) - (10.0 + 0.2)).abs() < 1e-4);
        assert!(Hmap::read(&file[..file.len() - 1], false).is_err());
    }

    #[test]
    fn nan_sorts_first() {
        assert_eq!(dotnet_cmp_f32(f32::NAN, -1e30), Ordering::Less);
        assert_eq!(dotnet_cmp_f32(-0.0, 0.0), Ordering::Equal);
    }
}
