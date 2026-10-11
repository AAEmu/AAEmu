//! Terrain: the physics heightfield of a world (CryPhysics `ent_terrain`, world slot 17
//! `SetHeightfieldData`, created by cry3dengine `Seamless_LoadCellTerrain` 0x3015D4A0) and
//! `I3DEngine::GetTerrainElevation` (vt+1064, 0x30136B60 → `sub_300D4E00`).
//!
//! The heightfield primitive takes plain `fn(ix, iy)` callbacks (`getHeight` / `getSurfType`)
//! without a context pointer. Each physics world therefore borrows one of [`MAX_SLOTS`]
//! callback slots ([`TerrainSlot`]); slot `k` has its own monomorphised callback that reads the
//! [`TerrainSource`] registered in slot `k`. The heights come from the server's lazily loaded
//! heightmap cells (`systems::npcs::heightmap`, the same `heightmap.dat` samples the zone uses,
//! 2 m grid, `unitSize = 2`).

use std::sync::{Arc, Mutex, RwLock};

use aaemu_physics::geometry::primitives::{HeightFn, SurfTypeFn};

/// Height samples of a world on the 2 m grid.
pub trait TerrainSource: Send + Sync {
    /// The height of grid point `(ix, iy)` (world `(2·ix, 2·iy)`); 0 outside the map or in a
    /// cell without heightmap (the engine's unloaded terrain).
    fn sample(&self, ix: i32, iy: i32) -> f32;
    /// Grid size `(nx, ny)` in samples.
    fn size(&self) -> (i32, i32);
}

/// Number of terrain callback slots (concurrent physics worlds with terrain).
pub const MAX_SLOTS: usize = 256;

#[allow(clippy::declare_interior_mutable_const)]
const EMPTY: RwLock<Option<Arc<dyn TerrainSource>>> = RwLock::new(None);
static SOURCES: [RwLock<Option<Arc<dyn TerrainSource>>>; MAX_SLOTS] = [EMPTY; MAX_SLOTS];
static USED: Mutex<[bool; MAX_SLOTS]> = Mutex::new([false; MAX_SLOTS]);

fn height_cb<const K: usize>(ix: i32, iy: i32) -> f32 {
    match SOURCES[K].read() {
        Ok(g) => g.as_ref().map_or(0.0, |s| s.sample(ix, iy)),
        Err(_) => 0.0,
    }
}

/// No terrain holes (`typemask & surftype == 0` everywhere; `cover.ctc` is not read).
fn surf_cb(_ix: i32, _iy: i32) -> u16 {
    0
}

macro_rules! height_table {
    ($($k:literal)*) => { [$(height_cb::<$k> as HeightFn),*] };
}

macro_rules! seq256 {
    ($m:ident) => {
        $m!(
            0 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31
            32 33 34 35 36 37 38 39 40 41 42 43 44 45 46 47 48 49 50 51 52 53 54 55 56 57 58 59 60 61 62 63
            64 65 66 67 68 69 70 71 72 73 74 75 76 77 78 79 80 81 82 83 84 85 86 87 88 89 90 91 92 93 94 95
            96 97 98 99 100 101 102 103 104 105 106 107 108 109 110 111 112 113 114 115 116 117 118 119 120 121 122 123 124 125 126 127
            128 129 130 131 132 133 134 135 136 137 138 139 140 141 142 143 144 145 146 147 148 149 150 151 152 153 154 155 156 157 158 159
            160 161 162 163 164 165 166 167 168 169 170 171 172 173 174 175 176 177 178 179 180 181 182 183 184 185 186 187 188 189 190 191
            192 193 194 195 196 197 198 199 200 201 202 203 204 205 206 207 208 209 210 211 212 213 214 215 216 217 218 219 220 221 222 223
            224 225 226 227 228 229 230 231 232 233 234 235 236 237 238 239 240 241 242 243 244 245 246 247 248 249 250 251 252 253 254 255
        )
    };
}

static HEIGHT_FNS: [HeightFn; MAX_SLOTS] = seq256!(height_table);

/// A borrowed callback slot; released on drop.
#[derive(Debug)]
pub struct TerrainSlot {
    index: usize,
}

impl TerrainSlot {
    /// Registers `source` in a free slot (`None` when all slots are in use).
    pub fn acquire(source: Arc<dyn TerrainSource>) -> Option<Self> {
        let mut used = USED.lock().unwrap_or_else(|e| e.into_inner());
        let index = used.iter().position(|u| !u)?;
        used[index] = true;
        *SOURCES[index].write().unwrap_or_else(|e| e.into_inner()) = Some(source);
        Some(Self { index })
    }

    pub fn height_fn(&self) -> HeightFn {
        HEIGHT_FNS[self.index]
    }

    pub fn surf_fn(&self) -> SurfTypeFn {
        surf_cb
    }

    /// The registered source.
    pub fn source(&self) -> Option<Arc<dyn TerrainSource>> {
        SOURCES[self.index]
            .read()
            .unwrap_or_else(|e| e.into_inner())
            .clone()
    }
}

impl Drop for TerrainSlot {
    fn drop(&mut self) {
        *SOURCES[self.index]
            .write()
            .unwrap_or_else(|e| e.into_inner()) = None;
        USED.lock().unwrap_or_else(|e| e.into_inner())[self.index] = false;
    }
}

/// `1 / unitSize` (`dword_302D6D3C`; `unitSize = 2` for every AA world).
pub const INV_UNIT_SIZE: f32 = 0.5;

/// `GetTerrainElevation(x, y)` (`sub_300D4E00` with `bIncludeVoxels = 0`): the height on the
/// triangle of the 2 m cell (split on `dx + dy = 1`), 0 when
///
/// * `x / 2 < 1` or `y / 2 < 1`, or the cell is outside the terrain (`>= size`);
/// * any of the 4 corners is exactly 0 (the engine's "not loaded" test);
/// * the interpolated height is negative.
///
/// Operation order follows the decompile (single precision).
pub fn terrain_elevation(src: &dyn TerrainSource, x: f32, y: f32) -> f32 {
    let fx = INV_UNIT_SIZE * x;
    let fy = INV_UNIT_SIZE * y;
    if fx < 1.0 || fy < 1.0 {
        return 0.0;
    }
    let ix = fx.floor() as i32;
    let iy = fy.floor() as i32;
    let (nx, ny) = src.size();
    if ix < 1 || iy < 1 || ix >= nx || iy >= ny {
        return 0.0;
    }
    let dx = fx - ix as f32;
    let dy = fy - iy as f32;
    let z00 = src.sample(ix, iy);
    let z10 = src.sample(ix + 1, iy);
    let z01 = src.sample(ix, iy + 1);
    let z11 = src.sample(ix + 1, iy + 1);
    if z00 == 0.0 || z10 == 0.0 || z01 == 0.0 || z11 == 0.0 {
        return 0.0;
    }
    let z = if dy + dx >= 1.0 {
        (((dy + dx) - 1.0) * z11 + (1.0 - dx) * z01) + (1.0 - dy) * z10
    } else {
        (((1.0 - dx) - dy) * z00 + z10 * dx) + z01 * dy
    };
    if z < 0.0 {
        0.0
    } else {
        z
    }
}

/// The terrain normal of the triangle under `(x, y)` (unnormalised cross product of the
/// triangle edges, z up).
pub fn terrain_normal(src: &dyn TerrainSource, x: f32, y: f32) -> [f32; 3] {
    let fx = INV_UNIT_SIZE * x;
    let fy = INV_UNIT_SIZE * y;
    let ix = fx.floor() as i32;
    let iy = fy.floor() as i32;
    let dx = fx - ix as f32;
    let dy = fy - iy as f32;
    let z00 = src.sample(ix, iy);
    let z10 = src.sample(ix + 1, iy);
    let z01 = src.sample(ix, iy + 1);
    let z11 = src.sample(ix + 1, iy + 1);
    let (gx, gy) = if dx + dy < 1.0 {
        (z10 - z00, z01 - z00)
    } else {
        (z11 - z01, z11 - z10)
    };
    // edges (2, 0, gx) and (0, 2, gy): n = (-2·gx, -2·gy, 4) / 2
    let n = [-gx, -gy, 2.0];
    let l = (n[0] * n[0] + n[1] * n[1] + n[2] * n[2]).sqrt();
    [n[0] / l, n[1] / l, n[2] / l]
}

#[cfg(test)]
pub(crate) mod tests {
    use super::*;

    /// A plane `z = a + b·x + c·y` (in metres) on an `n × n` grid.
    pub(crate) struct Plane {
        pub a: f32,
        pub b: f32,
        pub c: f32,
        pub n: i32,
    }

    impl TerrainSource for Plane {
        fn sample(&self, ix: i32, iy: i32) -> f32 {
            if ix < 0 || iy < 0 || ix > self.n || iy > self.n {
                return 0.0;
            }
            self.a + self.b * (2 * ix) as f32 + self.c * (2 * iy) as f32
        }
        fn size(&self) -> (i32, i32) {
            (self.n, self.n)
        }
    }

    #[test]
    fn elevation_on_a_plane_and_engine_zero_rules() {
        let p = Plane {
            a: 100.0,
            b: 0.5,
            c: -0.25,
            n: 64,
        };
        let z = terrain_elevation(&p, 10.5, 20.25);
        assert!((z - (100.0 + 5.25 - 5.0625)).abs() < 1e-4, "{z}");
        // x / 2 < 1 → 0
        assert_eq!(terrain_elevation(&p, 1.9, 20.0), 0.0);
        // outside the grid → 0
        assert_eq!(terrain_elevation(&p, 129.0, 20.0), 0.0);
        let flat0 = Plane {
            a: 0.0,
            b: 0.0,
            c: 0.0,
            n: 64,
        };
        assert_eq!(terrain_elevation(&flat0, 10.0, 10.0), 0.0);
        let n = terrain_normal(&p, 10.5, 20.25);
        assert!((n[0] + 0.5 / (1.0f32 + 0.25 + 0.0625).sqrt()).abs() < 1e-5);
    }

    #[test]
    fn slots_dispatch_to_their_source_and_are_released() {
        let a = TerrainSlot::acquire(Arc::new(Plane {
            a: 1.0,
            b: 0.0,
            c: 0.0,
            n: 8,
        }))
        .unwrap();
        let b = TerrainSlot::acquire(Arc::new(Plane {
            a: 2.0,
            b: 0.0,
            c: 0.0,
            n: 8,
        }))
        .unwrap();
        assert_ne!(a.index, b.index);
        assert_eq!((a.height_fn())(1, 1), 1.0);
        assert_eq!((b.height_fn())(1, 1), 2.0);
        let ia = a.index;
        drop(a);
        assert_eq!((HEIGHT_FNS[ia])(1, 1), 0.0);
    }
}
