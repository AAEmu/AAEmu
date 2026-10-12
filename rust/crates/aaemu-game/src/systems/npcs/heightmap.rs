//! `WorldCell` heightmaps and the height queries of `WorldTemplate` / `WorldInstance` /
//! `WorldManager` (`GetRawHeightMapHeight`, `GetHeight(x, y)`, `GetHeight(zoneKey, x, y, z)`,
//! `GetHeight(transform)`, `GetReferenceHeight`) plus `WorldManager.LoadHeightmaps`.
//!
//! The per-cell `client/terrain/heightmap.dat` reader ([`hmap`]) is the game's own
//! `Models/ClientData/{Hmap,NodeCell,AABB}.cs` (which differs from the WorldConverter tool's copy
//! in `UpScale` and the trailing-bytes skip); the binary reader and the bit-exact arithmetic were
//! taken from `aaemu-tool-worldconverter/src/hmap.rs` (port of `Tools/WorldConverter/Models/Hmap.cs`).
//!
//! Cells are loaded on demand (`WorldCell.VerifyCellLoaded`) from the client sources
//! (`ClientData.Sources`, read through [`ClientFileManager`]: the 24 GB game_pak is never
//! scanned unless `World.PreLoadTerrain` is set). With `GeoDataMode` the navmesh height of the
//! AI subsystem (`systems::ai::geodata`, installed with [`HeightmapWorlds::install_geo_data`])
//! is asked first, like `WorldTemplate.GeoData.GetHeight`. `object.dat` is not read here.
//!
//! C# exceptions inside the height lookups (`IndexOutOfRangeException` for the `cellX > CellX`
//! bound check, negative indices, a failing cell load) are [`HeightError`]s; the callers that
//! catch them in C# (`WorldManager.GetHeight`) turn them into the same fallback values.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, OnceLock, RwLock};

use tracing::{debug, error, info};

use crate::data::client_files::ClientFileManager;
use crate::data::world_templates::{WorldTemplate, WorldTemplates, CELL_SIZE, SECTORS_PER_CELL};
use crate::server::GameServer;
use crate::world::{WorldInstance, WorldObject};

/// `WorldManager.SECTOR_HMAP_RESOLUTION` (`REGION_SIZE / 2`).
pub const SECTOR_HMAP_RESOLUTION: i32 = 32;
/// `WorldManager.CELL_HMAP_RESOLUTION` (`CELL_SIZE / 2`).
pub const CELL_HMAP_RESOLUTION: i32 = CELL_SIZE / 2;

const CELL_POINTS: usize = (CELL_HMAP_RESOLUTION * CELL_HMAP_RESOLUTION) as usize;

/// An exception thrown inside a C# height lookup.
#[derive(Debug, Clone, PartialEq, thiserror::Error)]
pub enum HeightError {
    /// `IndexOutOfRangeException` (cell / heightmap array index).
    #[error("Index was outside the bounds of the array.")]
    IndexOutOfRange,
    /// An exception escaping `WorldCell.LoadCellHeightMapFromClientData`.
    #[error("loading heightmap cell {cell_x},{cell_y} of {world} failed: {reason}")]
    CellLoad {
        world: String,
        cell_x: i32,
        cell_y: i32,
        reason: String,
    },
}

// ---------------------------------------------------------------------------------------------
// Models/ClientData: Hmap / NodeCell / AABB
// ---------------------------------------------------------------------------------------------

/// `Models/ClientData/Hmap.cs` + `NodeCell.cs` + `AABB.cs` (the game server's copies).
///
/// Reader and arithmetic adapted from `aaemu-tool-worldconverter/src/hmap.rs` (port of
/// `Tools/WorldConverter/Models/Hmap.cs`); `UpScale` and the trailing-bytes handling follow the
/// game's `NodeCell.cs`. All operand types mirror the C# (float vs double vs integer promotions,
/// saturating casts of .NET 9+).
pub mod hmap {
    use std::cmp::Ordering;

    /// An exception of `BinaryReader` / `NodeCell.Read`.
    #[derive(Debug, Clone, PartialEq, thiserror::Error)]
    pub enum HmapError {
        #[error("Unable to read beyond the end of the stream.")]
        EndOfStream,
        #[error("Arithmetic operation resulted in an overflow. (negative node size {0})")]
        NegativeSize(i32),
        #[error("Non-negative number required. (ReadBytes({0}))")]
        NegativeCount(i32),
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

        fn remaining(&self) -> usize {
            self.data.len() - self.pos
        }

        fn take<const N: usize>(&mut self) -> Result<[u8; N], HmapError> {
            let s = self
                .data
                .get(self.pos..self.pos + N)
                .ok_or(HmapError::EndOfStream)?;
            self.pos += N;
            let mut out = [0u8; N];
            out.copy_from_slice(s);
            Ok(out)
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

        /// `BinaryReader.ReadBytes(count)`: fewer bytes at the end of the stream (no error), a
        /// negative count throws.
        fn read_bytes(&mut self, count: i32) -> Result<(), HmapError> {
            if count < 0 {
                return Err(HmapError::NegativeCount(count));
            }
            self.pos = (self.pos + count as usize).min(self.data.len());
            Ok(())
        }
    }

    /// `AABB`.
    #[derive(Debug, Clone, Copy, Default, PartialEq)]
    pub struct Aabb {
        pub min: [f32; 3],
        pub max: [f32; 3],
    }

    /// `Hmap`.
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
        /// `Hmap.Read`. `Err` = an exception from the header reads (it escapes `Read`);
        /// `Ok(None)` = `Read` returned `-1` (a node failed to read; the C# catches that);
        /// `Ok(Some)` = success.
        pub fn read(data: &[u8], disabled_re_calc: bool) -> Result<Option<Self>, HmapError> {
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
                br.read_bytes(128)?; // unk?
            }
            while i64::try_from(br.position()).unwrap_or(i64::MAX) != i64::from(h.chunk_size) {
                match NodeCell::read(&mut br, disabled_re_calc) {
                    Ok(node) => h.nodes.push(node),
                    Err(_) => return Ok(None),
                }
            }
            Ok(Some(h))
        }

        /// `Nodes.OrderBy(BoxHeightmap.Min.X).ThenBy(BoxHeightmap.Min.Y)
        /// .Where(pHMData.Length > 0)` (stable, .NET float ordering).
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
            // The reads below would hit the end of the stream: fail before allocating.
            if (len as usize).saturating_mul(2) > br.remaining() {
                return Err(HmapError::EndOfStream);
            }
            n.p_hm_data = (0..len).map(|_| br.u16()).collect::<Result<_, _>>()?;
            br.i32()?;
            br.f32()?;
            br.f32()?;
            br.f32()?;
            br.f32()?;
            br.read_bytes(36i32.wrapping_add(unk_count))?;

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

        /// `RawDataByIndex(ushort, ushort)`: `pHMData[nX * nSize + nY]`, 0 when past the end.
        pub fn raw_data_by_index(&self, n_x: u16, n_y: u16) -> u16 {
            if self.n_size > 0 {
                let index = i32::from(n_x)
                    .wrapping_mul(self.n_size)
                    .wrapping_add(i32::from(n_y));
                if index as i64 >= self.p_hm_data.len() as i64 {
                    return 0;
                }
                return self.p_hm_data[index as usize];
            }
            0
        }

        /// `GetHeightByIndex(uint)`.
        pub fn get_height_by_index(&self, i: u32) -> Result<f32, HmapError> {
            let raw = self
                .p_hm_data
                .get(i as usize)
                .copied()
                .ok_or(HmapError::IndexOutOfRange)?;
            Ok(self.raw_data_to_height(u32::from(raw)))
        }

        /// `GetHeight(nX, nY)`. Note (C#): after `UpScale` the data is 33x33 but `nSize` keeps
        /// the original size, so upscaled nodes are indexed with the wrong stride (mirrored).
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
                // iStep >= 1 here (C# would throw DivideByZeroException otherwise)
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

        /// `UpScale` (game version). Note (C#): `FindNearestSignificantPoints(x, y)` is
        /// `Rectangle(x / nSize, y / nSize, 1, 1)` and `sourceX < nSize`, so every target point
        /// interpolates between the raw points (0,0), (1,0), (0,1), (1,1) (mirrored).
        fn up_scale(&mut self) {
            if self.n_size > 0 && self.n_size < 33 {
                let n_size = self.n_size;
                let source_scale = n_size as f32 / 33f32;
                let mut result = vec![0u16; 33 * 33];
                for target_x in 0..=32i32 {
                    for target_y in 0..=32i32 {
                        let index = (target_x * 33 + target_y) as usize;
                        let source_x = f64::from(target_x as f32 * source_scale).floor() as u16;
                        let source_y = f64::from(target_y as f32 * source_scale).floor() as u16;
                        let left = i32::from(source_x) / n_size;
                        let top = i32::from(source_y) / n_size;
                        let (right, bottom) = (left + 1, top + 1);
                        let raw =
                            |x: i32, y: i32| f32::from(self.raw_data_by_index(x as u16, y as u16));
                        let tl = raw(left, top);
                        let tr = raw(right, top);
                        let bl = raw(left, bottom);
                        let br = raw(right, bottom);
                        let off_x = target_x as f32 * source_scale - f32::from(source_x);
                        let off_y = target_y as f32 * source_scale - f32::from(source_y);
                        let height = Self::blerp(tl, tr, bl, br, off_x, off_y);
                        result[index] = f64::from(height).round_ties_even() as u16;
                    }
                }
                self.p_hm_data = result;
            }
        }
    }
}

// ---------------------------------------------------------------------------------------------
// WorldCell
// ---------------------------------------------------------------------------------------------

/// Result of `WorldCell.LoadCellHeightMapFromClientData`.
#[derive(Debug)]
enum CellLoad {
    /// Returned `true`: the heightmap (`None` = all zeros: no file in the client data).
    Loaded(Option<Box<[u16]>>),
    /// Returned `false` (`Hmap.Read` failed): `Loaded` stays false, the zeroed array is used
    /// and the next lookup tries again.
    Failed,
    /// An exception escaped the load.
    Exception(String),
}

/// `WorldCell.LoadCellHeightMapFromClientData` (heightmap part).
fn load_cell_height_map(
    files: &ClientFileManager,
    world_name: &str,
    cell_x: i32,
    cell_y: i32,
    height_max_coefficient: f64,
) -> CellLoad {
    let file = format!(
        "game/worlds/{world_name}/cells/{cell_x:03}_{cell_y:03}/client/terrain/heightmap.dat"
    );
    if !files.file_exists(&file) {
        return CellLoad::Loaded(None);
    }
    let Some(bytes) = files.read_file(&file) else {
        return CellLoad::Loaded(None);
    };
    let hmap = match hmap::Hmap::read(&bytes, false) {
        Ok(Some(h)) => h,
        Ok(None) => {
            error!("Error reading {file}");
            return CellLoad::Failed;
        }
        Err(e) => return CellLoad::Exception(e.to_string()),
    };
    let sorted = hmap.sorted_nodes();
    let mut heights = vec![0u16; CELL_POINTS].into_boxed_slice();
    for sector_x in 0..SECTORS_PER_CELL {
        for sector_y in 0..SECTORS_PER_CELL {
            // sortedNodes[sectorX * SECTORS_PER_CELL + sectorY] (ArgumentOutOfRange when short)
            let Some(node) = sorted.get((sector_x * SECTORS_PER_CELL + sector_y) as usize) else {
                return CellLoad::Exception(format!(
                    "only {} heightmap nodes in {file}",
                    sorted.len()
                ));
            };
            for unit_x in 0..SECTOR_HMAP_RESOLUTION {
                for unit_y in 0..SECTOR_HMAP_RESOLUTION {
                    let o_x = sector_x * SECTOR_HMAP_RESOLUTION + unit_x;
                    let o_y = sector_y * SECTOR_HMAP_RESOLUTION + unit_y;
                    let height = match node.get_height(unit_x as u16, unit_y as u16) {
                        Ok(h) => h,
                        Err(e) => return CellLoad::Exception(format!("{e} ({file})")),
                    };
                    // (ushort)(height * Template.HeightMaxCoefficient): float * double,
                    // saturating conversion (.NET 9+).
                    let value = (f64::from(height) * height_max_coefficient) as u16;
                    heights[(o_x * CELL_HMAP_RESOLUTION + o_y) as usize] = value;
                }
            }
        }
    }
    CellLoad::Loaded(Some(heights))
}

/// `WorldCell` (heightmap part).
#[derive(Debug, Default)]
struct CellSlot {
    /// `Loaded` + `HeightMap` (`None` inside = all zeros).
    loaded: OnceLock<Option<Box<[u16]>>>,
    /// `_loadLock`.
    load_lock: Mutex<()>,
}

/// The heightmap side of one `WorldTemplate`: its `WorldCell[CellX, CellY]`.
#[derive(Debug)]
pub struct WorldHeightmap {
    pub template_id: u32,
    pub name: String,
    /// World size in cells.
    pub cell_x: i32,
    pub cell_y: i32,
    /// `HeightMaxCoefficient`.
    pub height_max_coefficient: f64,
    /// `Cells[x, y]` at `x * cell_y + y`.
    cells: Vec<CellSlot>,
}

impl WorldHeightmap {
    pub fn new(template: &WorldTemplate) -> Self {
        Self::with_size(
            template.id,
            &template.name,
            template.cell_x,
            template.cell_y,
            template.height_max_coefficient,
        )
    }

    pub fn with_size(
        template_id: u32,
        name: &str,
        cell_x: i32,
        cell_y: i32,
        height_max_coefficient: f64,
    ) -> Self {
        let count = (cell_x.max(0) as usize) * (cell_y.max(0) as usize);
        Self {
            template_id,
            name: name.to_string(),
            cell_x,
            cell_y,
            height_max_coefficient,
            cells: (0..count).map(|_| CellSlot::default()).collect(),
        }
    }

    fn slot(&self, cell_x: i32, cell_y: i32) -> Option<&CellSlot> {
        if cell_x < 0 || cell_y < 0 || cell_x >= self.cell_x || cell_y >= self.cell_y {
            return None;
        }
        self.cells.get((cell_x * self.cell_y + cell_y) as usize)
    }

    /// Installs a cell's heightmap directly (`HeightMap[x, y]` at `x * 512 + y`), marking it
    /// loaded (tests and tools).
    pub fn insert_cell(&self, cell_x: i32, cell_y: i32, heights: Vec<u16>) -> bool {
        match self.slot(cell_x, cell_y) {
            Some(s) if heights.len() == CELL_POINTS => {
                s.loaded.set(Some(heights.into_boxed_slice())).is_ok()
            }
            _ => false,
        }
    }

    /// `WorldCell.Loaded`.
    pub fn is_cell_loaded(&self, cell_x: i32, cell_y: i32) -> bool {
        self.slot(cell_x, cell_y)
            .is_some_and(|s| s.loaded.get().is_some())
    }

    /// `WorldCell.Loaded && HeightMap != null` (the cell's `heightmap.dat` was read; only then
    /// does `LoadCell` read the cell's `object.dat` — systems::ai water bodies).
    pub fn cell_has_height_map(&self, cell_x: i32, cell_y: i32) -> bool {
        self.slot(cell_x, cell_y)
            .is_some_and(|s| matches!(s.loaded.get(), Some(Some(_))))
    }

    /// Number of loaded cells.
    pub fn loaded_cell_count(&self) -> usize {
        self.cells
            .iter()
            .filter(|s| s.loaded.get().is_some())
            .count()
    }

    /// `WorldCell.VerifyCellLoaded()` → its `HeightMap` (`None` = all zeros). `Err` when the
    /// load throws (the cell stays unloaded and is retried next time, as in C#).
    fn verify_cell_loaded(
        &self,
        files: &ClientFileManager,
        cell_x: i32,
        cell_y: i32,
    ) -> Result<Option<&[u16]>, HeightError> {
        let slot = self
            .slot(cell_x, cell_y)
            .ok_or(HeightError::IndexOutOfRange)?;
        if let Some(h) = slot.loaded.get() {
            return Ok(h.as_deref());
        }
        let _guard = slot.load_lock.lock().unwrap_or_else(|e| e.into_inner());
        if let Some(h) = slot.loaded.get() {
            return Ok(h.as_deref());
        }
        match load_cell_height_map(
            files,
            &self.name,
            cell_x,
            cell_y,
            self.height_max_coefficient,
        ) {
            CellLoad::Loaded(h) => {
                let _ = slot.loaded.set(h);
                Ok(slot.loaded.get().and_then(|h| h.as_deref()))
            }
            CellLoad::Failed => Ok(None),
            CellLoad::Exception(reason) => Err(HeightError::CellLoad {
                world: self.name.clone(),
                cell_x,
                cell_y,
                reason,
            }),
        }
    }

    /// `WorldCell.GetHeightMapDataInCell(x, y)` (the cell must have been verified; an unloaded
    /// cell's `HeightMap` is null → 0). Note (C#): the bound check is `> CELL_HMAP_RESOLUTION`,
    /// so index 512 throws.
    pub fn get_height_map_data_in_cell(
        &self,
        cell_x: i32,
        cell_y: i32,
        data_x: i32,
        data_y: i32,
    ) -> Result<f32, HeightError> {
        let Some(slot) = self.slot(cell_x, cell_y) else {
            return Ok(0.0);
        };
        let Some(map) = slot.loaded.get() else {
            return Ok(0.0);
        };
        if !(0..=CELL_HMAP_RESOLUTION).contains(&data_x)
            || !(0..=CELL_HMAP_RESOLUTION).contains(&data_y)
        {
            return Ok(0.0);
        }
        if data_x == CELL_HMAP_RESOLUTION || data_y == CELL_HMAP_RESOLUTION {
            return Err(HeightError::IndexOutOfRange);
        }
        let v = map
            .as_deref()
            .map_or(0, |m| m[(data_x * CELL_HMAP_RESOLUTION + data_y) as usize]);
        Ok((f64::from(v) / self.height_max_coefficient) as f32)
    }

    /// `WorldCell.GetHeight(x, y)` (world position, not smoothed).
    pub fn get_cell_height(
        &self,
        cell_x: i32,
        cell_y: i32,
        x: i32,
        y: i32,
    ) -> Result<f32, HeightError> {
        // (int)(x - CellOffset.X) / 2 with a float CellOffset
        let xx = csharp_f32_to_i32(x as f32 - (cell_x * CELL_SIZE) as f32) / 2;
        let yy = csharp_f32_to_i32(y as f32 - (cell_y * CELL_SIZE) as f32) / 2;
        self.get_height_map_data_in_cell(cell_x, cell_y, xx, yy)
    }

    /// `WorldTemplate.GetRawHeightMapHeight(x, y)`. Note (C#): the bound check is
    /// `cellX > CellX`, so `cellX == CellX` throws `IndexOutOfRange`; so does a negative
    /// sub-cell index (`x % 1024 / 2 < 0` for `x <= -2`).
    pub fn get_raw_height_map_height(
        &self,
        files: &ClientFileManager,
        x: i32,
        y: i32,
    ) -> Result<f32, HeightError> {
        let cell_x = x / CELL_SIZE;
        let cell_y = y / CELL_SIZE;
        if cell_x < 0 || cell_x > self.cell_x || cell_y < 0 || cell_y > self.cell_y {
            return Ok(0.0); // out of bounds
        }
        let map = self.verify_cell_loaded(files, cell_x, cell_y)?;
        let sx = x % CELL_SIZE / 2;
        let sy = y % CELL_SIZE / 2;
        if sx < 0 || sy < 0 {
            return Err(HeightError::IndexOutOfRange);
        }
        let v = map.map_or(0, |m| m[(sx * CELL_HMAP_RESOLUTION + sy) as usize]);
        Ok((f64::from(v) / self.height_max_coefficient) as f32)
    }

    /// `WorldInstance.GetHeight(x, y)` / `WorldTemplate.GetHeight(x, y)`: bilinear
    /// interpolation between the 2 m grid points around the position.
    pub fn get_height(
        &self,
        files: &ClientFileManager,
        x: f32,
        y: f32,
    ) -> Result<f32, HeightError> {
        let (left, top) = find_nearest_significant_points(
            csharp_f64_to_i32(f64::from(x).floor()),
            csharp_f64_to_i32(f64::from(y).floor()),
        );
        let (right, bottom) = (left.wrapping_add(2), top.wrapping_add(2));
        let height_tl = self.get_raw_height_map_height(files, left, top)?;
        let height_tr = self.get_raw_height_map_height(files, right, top)?;
        let height_bl = self.get_raw_height_map_height(files, left, bottom)?;
        let height_br = self.get_raw_height_map_height(files, right, bottom)?;
        let off_x = (x - left as f32) / 2.0;
        let off_y = (y - top as f32) / 2.0;
        Ok(blerp(
            height_tl, height_tr, height_bl, height_br, off_x, off_y,
        ))
    }

    /// The height of grid point `(ix, iy)` of the whole world (2 m grid, world position
    /// `(2·ix, 2·iy)`), loading its cell on demand. 0 outside the map, in a cell without
    /// heightmap and for a failing cell (`systems::ai::collision` terrain heightfield).
    pub fn grid_sample(&self, files: &ClientFileManager, ix: i32, iy: i32) -> f32 {
        if ix < 0 || iy < 0 {
            return 0.0;
        }
        let (cx, cy) = (ix / CELL_HMAP_RESOLUTION, iy / CELL_HMAP_RESOLUTION);
        if cx >= self.cell_x || cy >= self.cell_y {
            return 0.0;
        }
        match self.verify_cell_loaded(files, cx, cy) {
            Ok(Some(map)) => {
                let (sx, sy) = (ix % CELL_HMAP_RESOLUTION, iy % CELL_HMAP_RESOLUTION);
                let v = map[(sx * CELL_HMAP_RESOLUTION + sy) as usize];
                (f64::from(v) / self.height_max_coefficient) as f32
            }
            _ => 0.0,
        }
    }

    /// `VerifyCellLoaded` for every cell (`PreLoadTerrain`). Returns the number of cells whose
    /// load threw.
    pub fn preload_all(&self, files: &ClientFileManager) -> usize {
        let mut failed = 0;
        for cell_y in 0..self.cell_y {
            for cell_x in 0..self.cell_x {
                if let Err(e) = self.verify_cell_loaded(files, cell_x, cell_y) {
                    error!("{e}");
                    failed += 1;
                }
            }
        }
        failed
    }
}

/// `FindNearestSignificantPoints(x, y)` → (`Left`, `Top`) of `Rectangle(x - x % 2, y - y % 2, 2, 2)`.
fn find_nearest_significant_points(x: i32, y: i32) -> (i32, i32) {
    (x.wrapping_sub(x % 2), y.wrapping_sub(y % 2))
}

/// `Lerp`.
fn lerp(start: f32, end: f32, target: f32) -> f32 {
    start + (end - start) * target
}

/// `Blerp` (bottom-left, bottom-right, top-left, top-right).
fn blerp(c_x0y0: f32, c_x1y0: f32, c_x0y1: f32, c_x1y1: f32, tx: f32, ty: f32) -> f32 {
    lerp(lerp(c_x0y0, c_x1y0, tx), lerp(c_x0y1, c_x1y1, tx), ty)
}

/// `(int)double` with the saturating .NET 9+ conversion (NaN → 0).
fn csharp_f64_to_i32(v: f64) -> i32 {
    v as i32
}

/// `(int)float` with the saturating .NET 9+ conversion (NaN → 0).
fn csharp_f32_to_i32(v: f32) -> i32 {
    v as i32
}

// ---------------------------------------------------------------------------------------------
// WorldManager height queries
// ---------------------------------------------------------------------------------------------

/// The configuration the C# height code reads (`AppConfiguration`).
#[derive(Debug, Clone, Copy, Default, PartialEq, Eq)]
pub struct HeightmapConfig {
    /// `HeightMapsEnable`.
    pub heightmaps_enable: bool,
    /// `World.GeoDataMode` (navmesh heights first, see [`HeightmapWorlds::install_geo_data`]).
    pub geo_data_mode: bool,
    /// `World.PreLoadTerrain`.
    pub pre_load_terrain: bool,
}

/// What `WorldManager.GetReferenceHeight` reads from an `NpcAi` (`ai != null`).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ReferenceHeightAi {
    /// `ai.Owner.CanFly`.
    pub can_fly: bool,
    /// `ai.GetCurrentBehavior() is HoldPositionBehavior or IdleBehavior`.
    pub hold_or_idle: bool,
    /// `ai.Owner.Spawner?.Position.Z` (`None` = no spawner).
    pub spawner_z: Option<f32>,
    /// `ai.Owner.Transform.World.Position.Z`.
    pub world_z: f32,
}

/// `WorldTemplate.GeoData.GetHeight(position)` (navmesh height, `GeoDataMode` only), installed
/// by the AI subsystem: (template id, x, y, z) -> height.
pub type GeoDataHeight = dyn Fn(u32, f32, f32, f32) -> f32 + Send + Sync;

/// Every world template's heightmap plus the client files they load from.
pub struct HeightmapWorlds {
    pub config: HeightmapConfig,
    files: Arc<ClientFileManager>,
    /// By world template id.
    templates: HashMap<u32, WorldHeightmap>,
    /// `GetWorldTemplateByZoneKey(zoneKey).Id`.
    zone_worlds: HashMap<u32, u32>,
    /// The navmesh height lookup (`None` until the AI subsystem installs it).
    geo_data: RwLock<Option<Arc<GeoDataHeight>>>,
}

impl std::fmt::Debug for HeightmapWorlds {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("HeightmapWorlds")
            .field("config", &self.config)
            .field("templates", &self.templates.len())
            .finish()
    }
}

impl HeightmapWorlds {
    /// Heightmaps for every world template of `worlds` (nothing is loaded yet).
    pub fn new(
        files: Arc<ClientFileManager>,
        worlds: &WorldTemplates,
        config: HeightmapConfig,
    ) -> Self {
        let mut templates = HashMap::new();
        let mut zone_worlds = HashMap::new();
        for t in worlds.templates() {
            templates.insert(t.id, WorldHeightmap::new(t));
            for &zone_key in &t.zone_keys {
                if let Some(w) = worlds.get_world_template_by_zone_key(zone_key) {
                    zone_worlds.insert(zone_key, w.id);
                }
            }
        }
        Self {
            config,
            files,
            templates,
            zone_worlds,
            geo_data: RwLock::new(None),
        }
    }

    /// Installs the navmesh height lookup used with `GeoDataMode` (AI subsystem).
    pub fn install_geo_data(&self, f: Arc<GeoDataHeight>) {
        *self.geo_data.write().unwrap_or_else(|e| e.into_inner()) = Some(f);
    }

    /// `world.GeoData.GetHeight(position)` when `GeoDataMode` is on and the navmesh is
    /// installed (0 otherwise, as without navmesh data).
    fn geo_data_height(&self, template_id: u32, x: f32, y: f32, z: f32) -> f32 {
        if !self.config.geo_data_mode {
            return 0.0;
        }
        match self
            .geo_data
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .as_ref()
        {
            Some(f) => f(template_id, x, y, z),
            None => 0.0,
        }
    }

    /// From explicit parts (tests / tools).
    pub fn from_parts(
        files: Arc<ClientFileManager>,
        templates: Vec<WorldHeightmap>,
        zone_worlds: HashMap<u32, u32>,
        config: HeightmapConfig,
    ) -> Self {
        Self {
            config,
            files,
            templates: templates.into_iter().map(|t| (t.template_id, t)).collect(),
            zone_worlds,
            geo_data: RwLock::new(None),
        }
    }

    /// The client files the cells are read from.
    pub fn files(&self) -> &Arc<ClientFileManager> {
        &self.files
    }

    /// One world template's heightmap.
    pub fn template(&self, template_id: u32) -> Option<&WorldHeightmap> {
        self.templates.get(&template_id)
    }

    /// `GetWorldTemplateByZoneKey` (logs like the C# when unknown).
    fn template_by_zone_key(&self, zone_key: u32) -> Option<&WorldHeightmap> {
        match self.zone_worlds.get(&zone_key) {
            Some(id) => self.templates.get(id),
            None => {
                error!("GetWorldByZone(): No world template defined for ZoneId {zone_key}");
                None
            }
        }
    }

    /// `WorldInstance.GetHeight(x, y)` of a world template.
    pub fn world_height(&self, template_id: u32, x: f32, y: f32) -> Result<f32, HeightError> {
        match self.templates.get(&template_id) {
            Some(t) => t.get_height(&self.files, x, y),
            None => Ok(0.0),
        }
    }

    /// `WorldManager.GetHeight(zoneKey, x, y, z)`: floor height, 0 when unknown / disabled.
    pub fn get_height(&self, zone_key: u32, x: f32, y: f32, z: f32) -> f32 {
        let mut height = 0f32;
        let world = self.template_by_zone_key(zone_key);
        // GeoDataMode: `world?.GeoData.GetHeight(position)` (systems::ai)
        if let Some(w) = world {
            height = self.geo_data_height(w.template_id, x, y, z);
        }
        if height != 0.0 || !self.config.heightmaps_enable {
            return height;
        }
        match world {
            Some(w) => w.get_height(&self.files, x, y).unwrap_or(0.0),
            None => 0.0,
        }
    }

    /// `WorldManager.GetHeight(transform)` with the transform's world template (`None` = no
    /// such instance) and world position. Falls back to the transform's own Z.
    pub fn get_height_at(&self, template_id: Option<u32>, x: f32, y: f32, z: f32) -> f32 {
        let Some(template_id) = template_id else {
            return 0.0;
        };
        // GeoDataMode: `world.Template.GeoData?.GetHeight(...)` (systems::ai)
        let height = self.geo_data_height(template_id, x, y, z);
        if height != 0.0 {
            return height;
        }
        if self.config.heightmaps_enable {
            self.world_height(template_id, x, y).unwrap_or(z)
        } else {
            z
        }
    }

    /// `WorldManager.GetReferenceHeight(ai, x, y, z, zoneId)`.
    pub fn get_reference_height(
        &self,
        ai: Option<&ReferenceHeightAi>,
        x: f32,
        y: f32,
        z: f32,
        zone_id: u32,
    ) -> f32 {
        // 0. Just in case.
        let Some(ai) = ai else {
            return self.get_height(zone_id, x, y, z);
        };
        // 1. If an NPC can fly, the height is taken from the spawner's position.
        // 2. For HoldPositionBehavior and IdleBehavior, the height is taken from the spawner.
        // (C#: `Spawner.Position.Z` throws without a spawner; the unit's Z is used then.)
        if ai.can_fly || ai.hold_or_idle {
            return ai.spawner_z.unwrap_or(ai.world_z);
        }
        // 3. Terrain height retrieval
        let final_height = self.get_height(zone_id, x, y, z);
        if final_height != 0.0 {
            return final_height;
        }
        // 4. Take the default height
        ai.spawner_z.unwrap_or(ai.world_z)
    }

    /// `WorldManager.LoadHeightmaps` (after the templates are known): logs, and with
    /// `PreLoadTerrain` loads every cell. Returns the number of templates with client data.
    pub fn load_heightmaps(&self, worlds: &WorldTemplates) -> usize {
        if !self.config.heightmaps_enable {
            return 0;
        }
        info!("Loading heightmaps...");
        let mut loaded = 0;
        for t in worlds.templates() {
            info!("Loading heightmap of {}", t.name);
            // LoadZoneBaiFiles: GeoDataMode only (`systems::ai::geodata`).
            let world_xml = format!("game/worlds/{}/world.xml", t.name);
            if !self.files.file_exists(&world_xml) {
                continue;
            }
            if !self.config.pre_load_terrain {
                debug!(
                    "PreLoadTerrain disabled, heightmaps for {} will get loaded on demand.",
                    t.name
                );
                loaded += 1;
                continue;
            }
            if let Some(h) = self.templates.get(&t.id) {
                h.preload_all(&self.files);
            }
            info!("{} heightmap loaded", t.name);
            loaded += 1;
        }
        info!("Loaded {loaded}/{} heightmaps", worlds.templates().len());
        loaded
    }
}

/// `server.systems.npcs.heightmaps`: set once by [`start`].
#[derive(Debug, Default)]
pub struct Heightmaps {
    worlds: RwLock<Option<Arc<HeightmapWorlds>>>,
}

impl Heightmaps {
    /// The loaded heightmaps (`None` before [`start`]).
    pub fn get(&self) -> Option<Arc<HeightmapWorlds>> {
        self.worlds
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .clone()
    }

    /// Installs `worlds` (startup / tests).
    pub fn install(&self, worlds: Arc<HeightmapWorlds>) {
        *self.worlds.write().unwrap_or_else(|e| e.into_inner()) = Some(worlds);
    }

    /// The client files opened at startup (`None` before [`start`]).
    pub fn client_files(&self) -> Option<Arc<ClientFileManager>> {
        self.get().map(|w| w.files.clone())
    }
}

/// `WorldManager.GetHeight(zoneKey, x, y, z)` on `server`'s heightmaps (0 before startup).
pub fn get_height(server: &GameServer, zone_key: u32, x: f32, y: f32, z: f32) -> f32 {
    server
        .systems
        .npcs
        .heightmaps
        .get()
        .map_or(0.0, |h| h.get_height(zone_key, x, y, z))
}

/// `WorldManager.GetHeight(obj.Transform)`: the floor height under the object's world position
/// (its own Z when no heightmap value is found).
pub fn get_height_of(server: &GameServer, obj: &WorldObject) -> f32 {
    let instance_id = obj.transform().instance_id;
    let world = server.world.world(instance_id);
    let pos = match &world {
        Some(w) => w.world_pos_rot(obj),
        None => return 0.0,
    };
    match server.systems.npcs.heightmaps.get() {
        Some(h) => h.get_height_at(world.as_ref().map(|w| w.template.id), pos.x, pos.y, pos.z),
        None => pos.z,
    }
}

/// `WorldInstance.GetHeight(x, y)` of `world` (`Err` = the C# exception).
pub fn world_height(
    server: &GameServer,
    world: &WorldInstance,
    x: f32,
    y: f32,
) -> Result<f32, HeightError> {
    match server.systems.npcs.heightmaps.get() {
        Some(h) => h.world_height(world.template.id, x, y),
        None => Ok(0.0),
    }
}

/// Startup (`WorldManager.LoadHeightmaps`): opens the client sources, builds the per-template
/// cell tables (loaded on demand, or all of them with `PreLoadTerrain`).
pub async fn start(server: &Arc<GameServer>) -> anyhow::Result<()> {
    let config = HeightmapConfig {
        heightmaps_enable: server.config.heightmaps_enable,
        geo_data_mode: server.config.world.geo_data_mode,
        pre_load_terrain: server.config.world.pre_load_terrain,
    };
    let sources = server.config.client_data.sources.clone();
    let app_path = server.config.app_path.clone();
    let game_data = server.game_data.clone();
    let worlds = tokio::task::spawn_blocking(move || {
        let files = Arc::new(ClientFileManager::from_sources(&sources, &app_path));
        let worlds = Arc::new(HeightmapWorlds::new(files, &game_data.worlds, config));
        worlds.load_heightmaps(&game_data.worlds);
        worlds
    })
    .await?;
    server.systems.npcs.heightmaps.install(worlds);
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    const COEF: f64 = 65535.0 / (4096.0 / 4.0);

    fn synthetic() -> HeightmapWorlds {
        let t = WorldHeightmap::with_size(0, "test_world", 2, 2, COEF);
        // Cell (0,0): height(x, y) = x_index (in metres: 2 m grid → value = index units).
        let mut h = vec![0u16; CELL_POINTS];
        for x in 0..CELL_HMAP_RESOLUTION {
            for y in 0..CELL_HMAP_RESOLUTION {
                h[(x * CELL_HMAP_RESOLUTION + y) as usize] = (f64::from(x + 2 * y) * COEF) as u16;
            }
        }
        assert!(t.insert_cell(0, 0, h));
        let mut zones = HashMap::new();
        zones.insert(100, 0);
        HeightmapWorlds::from_parts(
            Arc::new(ClientFileManager::new()),
            vec![t],
            zones,
            HeightmapConfig {
                heightmaps_enable: true,
                ..Default::default()
            },
        )
    }

    #[test]
    fn raw_and_interpolated_heights() {
        let w = synthetic();
        let t = w.template(0).unwrap();
        let f = w.files().clone();
        // grid point (5, 7) -> world (10, 14)
        let raw = t.get_raw_height_map_height(&f, 10, 14).unwrap();
        let expect = ((f64::from(5 + 14) * COEF) as u16) as f64 / COEF;
        assert_eq!(raw, expect as f32);
        // odd coordinates use the same grid point (x % 1024 / 2)
        assert_eq!(t.get_raw_height_map_height(&f, 11, 15).unwrap(), raw);
        // Interpolation half-way between (10,14),(12,14),(10,16),(12,16)
        let h = t.get_height(&f, 11.0, 15.0).unwrap();
        let tl = t.get_raw_height_map_height(&f, 10, 14).unwrap();
        let tr = t.get_raw_height_map_height(&f, 12, 14).unwrap();
        let bl = t.get_raw_height_map_height(&f, 10, 16).unwrap();
        let br = t.get_raw_height_map_height(&f, 12, 16).unwrap();
        assert_eq!(h, lerp(lerp(tl, tr, 0.5), lerp(bl, br, 0.5), 0.5));
        assert!((h - 20.5).abs() < 0.05, "{h}");
        // Exactly on a grid point: offsets 0.
        assert_eq!(t.get_height(&f, 10.0, 14.0).unwrap(), tl);
    }

    #[test]
    fn csharp_bounds_quirks() {
        let w = synthetic();
        let t = w.template(0).unwrap();
        let f = w.files().clone();
        // cellX > CellX -> 0, cellX == CellX -> IndexOutOfRange (C# bound check bug)
        assert_eq!(t.get_raw_height_map_height(&f, 3 * 1024, 0), Ok(0.0));
        assert_eq!(
            t.get_raw_height_map_height(&f, 2 * 1024, 0),
            Err(HeightError::IndexOutOfRange)
        );
        // negative: x = -1 -> cell 0, sx 0; x = -2 -> sx -1 -> throws
        assert!(t.get_raw_height_map_height(&f, -1, 0).is_ok());
        assert_eq!(
            t.get_raw_height_map_height(&f, -2, 0),
            Err(HeightError::IndexOutOfRange)
        );
        assert_eq!(t.get_raw_height_map_height(&f, -1024, 0), Ok(0.0));
        // A cell without client data is "loaded" with zeros.
        assert_eq!(t.get_raw_height_map_height(&f, 1500, 100), Ok(0.0));
        assert!(t.is_cell_loaded(1, 0));
        // WorldManager.GetHeight catches -> 0; unknown zone -> 0
        assert_eq!(w.get_height(100, 2048.0, 5.0, 1.0), 0.0);
        assert_eq!(w.get_height(999, 10.0, 14.0, 1.0), 0.0);
        assert!(w.get_height(100, 10.0, 14.0, 1.0) > 0.0);
        // GetHeight(transform) falls back to Z on exception.
        assert_eq!(w.get_height_at(Some(0), 2048.0, 5.0, 77.0), 77.0);
        assert_eq!(w.get_height_at(None, 10.0, 14.0, 77.0), 0.0);
        // cell data accessors
        assert_eq!(
            t.get_height_map_data_in_cell(0, 0, 512, 0),
            Err(HeightError::IndexOutOfRange)
        );
        assert_eq!(t.get_height_map_data_in_cell(0, 0, 513, 0), Ok(0.0));
        assert_eq!(
            t.get_cell_height(0, 0, 10, 14).unwrap(),
            t.get_raw_height_map_height(&f, 10, 14).unwrap()
        );
    }

    #[test]
    fn disabled_and_reference_height() {
        let mut w = synthetic();
        let terrain = w.get_height(100, 10.0, 14.0, 0.0);
        // no AI -> terrain
        assert_eq!(w.get_reference_height(None, 10.0, 14.0, 50.0, 100), terrain);
        let ai = ReferenceHeightAi {
            can_fly: false,
            hold_or_idle: false,
            spawner_z: Some(33.0),
            world_z: 40.0,
        };
        assert_eq!(
            w.get_reference_height(Some(&ai), 10.0, 14.0, 50.0, 100),
            terrain
        );
        // terrain 0 -> spawner
        assert_eq!(
            w.get_reference_height(Some(&ai), 1500.0, 14.0, 50.0, 100),
            33.0
        );
        let fly = ReferenceHeightAi {
            can_fly: true,
            ..ai
        };
        assert_eq!(
            w.get_reference_height(Some(&fly), 10.0, 14.0, 50.0, 100),
            33.0
        );
        let idle = ReferenceHeightAi {
            hold_or_idle: true,
            spawner_z: None,
            ..ai
        };
        assert_eq!(
            w.get_reference_height(Some(&idle), 10.0, 14.0, 50.0, 100),
            40.0
        );
        // HeightMapsEnable = false: GetHeight is 0 (C# returns before reading heightmaps)
        w.config.heightmaps_enable = false;
        assert_eq!(w.get_height(100, 10.0, 14.0, 0.0), 0.0);
        assert_eq!(w.get_reference_height(None, 10.0, 14.0, 50.0, 100), 0.0);
        assert_eq!(w.get_height_at(Some(0), 10.0, 14.0, 9.0), 9.0);
    }

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
        v.extend_from_slice(&[0u8; 36 + 2]);
        v
    }

    fn file(nodes: &[Vec<u8>]) -> Vec<u8> {
        let mut f = vec![23u8, 0, 0, 0];
        let chunk = 4 + 5 * 4 + 8 + nodes.iter().map(Vec::len).sum::<usize>();
        f.extend_from_slice(&(chunk as i32).to_le_bytes());
        for v in [0i32, 0, 0, 0] {
            f.extend_from_slice(&v.to_le_bytes());
        }
        f.extend_from_slice(&1.5f32.to_le_bytes());
        f.extend_from_slice(&0.0f32.to_le_bytes());
        for n in nodes {
            f.extend_from_slice(n);
        }
        f
    }

    #[test]
    fn hmap_game_upscale_and_errors() {
        // 2x2 node, version 7: upscaled to 33x33 from points (0,0),(1,0),(0,1),(1,1)
        let n1 = node_bytes(7, 2, 10.0, 0.01, &[16, 32, 48, 64], (5.0, 0.0));
        let n2 = node_bytes(7, 0, 0.0, 0.0, &[], (1.0, 0.0));
        let data = file(&[n1, n2]);
        let h = hmap::Hmap::read(&data, false).unwrap().unwrap();
        assert_eq!(h.nodes.len(), 2);
        let sorted = h.sorted_nodes();
        assert_eq!(sorted.len(), 1);
        let n = sorted[0];
        assert_eq!(n.p_hm_data.len(), 33 * 33);
        assert_eq!(n.p_hm_data[0], 16);
        // target (32, 0): sourceScale 2/33, source 1.939 -> sourceX 1, off 0.939
        // blerp(tl=16 (0,0), tr=48 (1,0), ...) at tx 0.939 ty 0 -> 16 + 32 * 0.939 = 46.06 -> 46
        let src = 32f32 * (2f32 / 33f32);
        let expect = (16f32 + (48f32 - 16f32) * (src - 1.0)).round();
        assert_eq!(f32::from(n.p_hm_data[32 * 33]), expect);
        // truncated file: a node fails -> Read returns -1 (Ok(None))
        assert!(hmap::Hmap::read(&data[..data.len() - 1], false)
            .unwrap()
            .is_none());
        // truncated header -> exception
        assert!(hmap::Hmap::read(&data[..10], false).is_err());
    }
}

#[cfg(test)]
mod real_data_tests {
    //! `AAEMU_GAME_PAK=D:\AA1.2\game_pak cargo test -p aaemu-game real_heightmaps -- --ignored
    //! --nocapture`: loads main_world cells from the client pak and compares the interpolated
    //! height with the Z of the NPC spawns of `Data/Worlds/main_world/npc_spawns.json`.
    use std::collections::BTreeMap;
    use std::path::PathBuf;

    use super::*;
    use crate::systems::npcs::json;

    pub(crate) fn app_path() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app")
    }

    #[test]
    #[ignore = "needs AAEMU_GAME_PAK"]
    fn real_heightmaps() {
        let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
            return;
        };
        let app = app_path();
        let files = Arc::new(ClientFileManager::from_sources(&[pak], &app));
        let worlds = WorldTemplates::load(&files, &app).expect("world templates");
        let main = worlds.get_world_template_by_name("main_world").unwrap();
        let h = HeightmapWorlds::new(
            files.clone(),
            &worlds,
            HeightmapConfig {
                heightmaps_enable: true,
                ..Default::default()
            },
        );
        let text =
            std::fs::read_to_string(app.join("Data/Worlds/main_world/npc_spawns.json")).unwrap();
        let doc = json::parse(&text).unwrap();
        // Group the spawns by cell and keep the 12 cells with the most spawns.
        let mut by_cell: BTreeMap<(i32, i32), Vec<[f32; 3]>> = BTreeMap::new();
        for e in json::array(&doc).unwrap() {
            let Some(p) = e
                .as_object()
                .and_then(|o| json::prop(o, "Position"))
                .and_then(|p| p.as_object())
            else {
                continue;
            };
            let x = json::f32_or(p, "X", 0.0).unwrap();
            let y = json::f32_or(p, "Y", 0.0).unwrap();
            let z = json::f32_or(p, "Z", 0.0).unwrap();
            by_cell
                .entry((x as i32 / CELL_SIZE, y as i32 / CELL_SIZE))
                .or_default()
                .push([x, y, z]);
        }
        let mut cells: Vec<_> = by_cell.into_iter().collect();
        cells.sort_by_key(|(_, v)| std::cmp::Reverse(v.len()));
        cells.truncate(12);
        let t = h.template(main.id).unwrap();
        let started = std::time::Instant::now();
        let mut errors = Vec::new();
        let mut failed_cells = 0;
        for ((cx, cy), spawns) in &cells {
            if t.verify_cell_loaded(&files, *cx, *cy).is_err() {
                failed_cells += 1;
                continue;
            }
            for s in spawns {
                let terrain = t.get_height(&files, s[0], s[1]).unwrap();
                errors.push(s[2] - terrain);
            }
        }
        let elapsed = started.elapsed();
        assert_eq!(failed_cells, 0);
        let mut abs: Vec<f32> = errors.iter().map(|e| e.abs()).collect();
        abs.sort_by(|a, b| a.partial_cmp(b).unwrap());
        let n = abs.len();
        let pct = |p: f64| abs[((n as f64 - 1.0) * p) as usize];
        let within = |d: f32| abs.iter().filter(|&&e| e <= d).count() as f64 / n as f64 * 100.0;
        eprintln!(
            "{} cells loaded in {elapsed:?}, {n} spawns: |spawnZ - height| median {:.3} p90 {:.3} p99 {:.3} max {:.3}; <=0.5m {:.1}% <=2m {:.1}% <=10m {:.1}%; mean signed {:.3}",
            t.loaded_cell_count(),
            pct(0.5),
            pct(0.9),
            pct(0.99),
            abs[n - 1],
            within(0.5),
            within(2.0),
            within(10.0),
            errors.iter().sum::<f32>() / n as f32,
        );
        assert!(n > 1000);
        assert!(pct(0.5) < 1.0, "median error {}", pct(0.5));
        // WorldManager.GetHeight(zoneKey) path on the same data.
        let s = cells[0].1[0];
        let zone = worlds.get_zone_id(Some(main), s[0], s[1]);
        assert_eq!(
            h.get_height(zone, s[0], s[1], s[2]),
            t.get_height(&files, s[0], s[1]).unwrap()
        );
    }
}
