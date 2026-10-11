//! The per-cell object files the zone loads with `Seamless_LoadCellObject` (cry3dengine
//! 0x30163AC0) and the world's `vegetation.xml`:
//!
//! | file | layout | engine |
//! |---|---|---|
//! | `cells/X_Y/client/statobjs.dat` | `u32 n; char name[256] × n` | brush model table (index = `m_nObjectTypeID`) |
//! | `cells/X_Y/client/brush.dat` | `u32 ver = 1; u32 off[256]; u32 size[256]`; 16×16 sectors of 64 m; 128-byte `SBrushChunk` | cell load 0x3017CC30 → sector 0x3017BA50 → `_LoadBrush` 0x3021E840 |
//! | `cells/X_Y/client/vegetation.dat` | `u32 ver = 1; u32 off[16]; u32 size[16]`; 4×4 sectors of 256 m; 64-byte `SVegetationChunk` | `CVegetation` |
//! | `worlds/W/vegetation.xml` | `<group id modelFileName …/>` | vegetation groups |
//!
//! Record layouts: `sub_cgf.md` §3 (typeinfo-confirmed). Positions in both files are
//! sector-local; the loader adds `(sx·64, sy·64, 0)` (brushes, 0x3017BA50) and the cell origin.
//! The sector index order (`i = sx·16 + sy` or `sy·16 + sx`) is not visible in the decompile
//! (the sector table is filled by the stream loader); [`SECTOR_X_MAJOR`] was fixed with the
//! real data (see `real_data_tests`: brushes and trees sit on the terrain only with this order).

use aaemu_physics::math::{Mat33, Vec3};

/// Sector index → (sx, sy): `i / n` is x when true (the data check in `real_data_tests`).
pub const SECTOR_X_MAJOR: bool = true;

/// `(sx, sy)` of sector `i` in a cell of `n × n` sectors.
pub fn sector_xy(i: usize, n: usize) -> (usize, usize) {
    if SECTOR_X_MAJOR {
        (i / n, i % n)
    } else {
        (i % n, i / n)
    }
}

/// Normalises a model path from a `.dat` / `.xml` reference the way the pak names it:
/// backslashes → `/`, lower case, trimmed (three statobjs.dat names carry a trailing space),
/// doubled slashes collapsed, `game/` prefixed.
pub fn normalize_model_path(p: &str) -> String {
    let mut s = p.trim().replace('\\', "/").to_ascii_lowercase();
    while s.contains("//") {
        s = s.replace("//", "/");
    }
    let s = s.trim_start_matches('/').to_string();
    if s.starts_with("game/") {
        s
    } else {
        format!("game/{s}")
    }
}

fn u32_at(d: &[u8], o: usize) -> Option<u32> {
    d.get(o..o + 4)
        .map(|b| u32::from_le_bytes([b[0], b[1], b[2], b[3]]))
}

fn f32_at(d: &[u8], o: usize) -> Option<f32> {
    u32_at(d, o).map(f32::from_bits)
}

fn vec3_at(d: &[u8], o: usize) -> Option<Vec3> {
    Some(Vec3::new(
        f32_at(d, o)?,
        f32_at(d, o + 4)?,
        f32_at(d, o + 8)?,
    ))
}

/// `statobjs.dat`: the cell's model names (normalised, see [`normalize_model_path`]).
pub fn read_statobjs(d: &[u8]) -> Vec<String> {
    let Some(n) = u32_at(d, 0) else {
        return Vec::new();
    };
    (0..n as usize)
        .map_while(|i| {
            let rec = d.get(4 + i * 256..4 + i * 256 + 256)?;
            let end = rec.iter().position(|&c| c == 0).unwrap_or(256);
            Some(normalize_model_path(&String::from_utf8_lossy(&rec[..end])))
        })
        .collect()
}

/// One `SBrushChunk` (128 bytes) with its sector offset applied (cell-local metres).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct BrushRecord {
    /// `m_WSBBox` (cell-local).
    pub bbox: [Vec3; 2],
    /// `m_dwRndFlags` (+32).
    pub rnd_flags: u32,
    /// `m_Matrix` rotation·scale rows (+67, row-major 3×4).
    pub rot_scale: Mat33,
    /// `m_Matrix` translation (cell-local).
    pub pos: Vec3,
    /// `m_nMaterialId` (+115).
    pub material_id: i32,
    /// `m_nObjectTypeID` (+123): index into `statobjs.dat`.
    pub statobj: i32,
}

/// Errors of the `.dat` readers.
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum DatError {
    #[error("bad version {0}")]
    Version(u32),
    #[error("truncated")]
    Truncated,
}

/// `brush.dat`.
pub fn read_brushes(d: &[u8]) -> Result<Vec<BrushRecord>, DatError> {
    let ver = u32_at(d, 0).ok_or(DatError::Truncated)?;
    if ver != 1 {
        return Err(DatError::Version(ver));
    }
    let mut out = Vec::new();
    for i in 0..256 {
        let off = u32_at(d, 4 + 4 * i).ok_or(DatError::Truncated)? as usize;
        let size = u32_at(d, 1028 + 4 * i).ok_or(DatError::Truncated)? as usize;
        let (sx, sy) = sector_xy(i, 16);
        let so = Vec3::new(sx as f32 * 64.0, sy as f32 * 64.0, 0.0);
        for k in 0..size / 128 {
            let r = off + k * 128;
            let rec = d.get(r..r + 128).ok_or(DatError::Truncated)?;
            let m: Vec<f32> = (0..12)
                .map(|j| f32_at(rec, 67 + 4 * j).unwrap_or(0.0))
                .collect();
            let bmin = vec3_at(rec, 0).ok_or(DatError::Truncated)?;
            let bmax = vec3_at(rec, 12).ok_or(DatError::Truncated)?;
            out.push(BrushRecord {
                bbox: [bmin + so, bmax + so],
                rnd_flags: u32_at(rec, 32).ok_or(DatError::Truncated)?,
                rot_scale: Mat33::from_array([
                    m[0], m[1], m[2], m[4], m[5], m[6], m[8], m[9], m[10],
                ]),
                pos: Vec3::new(m[3], m[7], m[11]) + so,
                material_id: u32_at(rec, 115).ok_or(DatError::Truncated)? as i32,
                statobj: u32_at(rec, 123).ok_or(DatError::Truncated)? as i32,
            });
        }
    }
    Ok(out)
}

/// One `SVegetationChunk` (64 bytes) with its sector offset applied (cell-local metres).
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct VegetationRecord {
    pub bbox: [Vec3; 2],
    pub rnd_flags: u32,
    /// `m_vPos` (+39).
    pub pos: Vec3,
    /// `m_fScale` (+51).
    pub scale: f32,
    /// `m_nObjectTypeID` (+55): the `vegetation.xml` group id.
    pub group: u16,
    /// `m_ucAngleX` (+57), `m_ucAngleY` (+58), `m_ucAngle` (+60, z).
    pub angle_x: u8,
    pub angle_y: u8,
    pub angle_z: u8,
}

/// `vegetation.dat`.
pub fn read_vegetation(d: &[u8]) -> Result<Vec<VegetationRecord>, DatError> {
    let ver = u32_at(d, 0).ok_or(DatError::Truncated)?;
    if ver != 1 {
        return Err(DatError::Version(ver));
    }
    let mut out = Vec::new();
    for i in 0..16 {
        let off = u32_at(d, 4 + 4 * i).ok_or(DatError::Truncated)? as usize;
        let size = u32_at(d, 68 + 4 * i).ok_or(DatError::Truncated)? as usize;
        let (sx, sy) = sector_xy(i, 4);
        let so = Vec3::new(sx as f32 * 256.0, sy as f32 * 256.0, 0.0);
        for k in 0..size / 64 {
            let r = off + k * 64;
            let rec = d.get(r..r + 64).ok_or(DatError::Truncated)?;
            let bmin = vec3_at(rec, 0).ok_or(DatError::Truncated)?;
            let bmax = vec3_at(rec, 12).ok_or(DatError::Truncated)?;
            out.push(VegetationRecord {
                bbox: [bmin + so, bmax + so],
                rnd_flags: u32_at(rec, 32).ok_or(DatError::Truncated)?,
                pos: vec3_at(rec, 39).ok_or(DatError::Truncated)? + so,
                scale: f32_at(rec, 51).ok_or(DatError::Truncated)?,
                group: u16::from_le_bytes([rec[55], rec[56]]),
                angle_x: rec[57],
                angle_y: rec[58],
                angle_z: rec[60],
            });
        }
    }
    Ok(out)
}

/// The rotation of a vegetation instance (`CVegetation` matrix, 0x30102840): each byte angle
/// is `b · 1.4117647° (= 360/255)`, `Matrix33::CreateRotationXYZ(Ang3(x, y, z))`
/// (`sub_30106900`), then the uniform scale. The terrain alignment of groups with flag 8
/// (`+52 & 8`, only when the x / y angles are 0) is not applied (see `PORTING.md`).
pub fn vegetation_rotation(r: &VegetationRecord) -> Mat33 {
    let ang = |b: u8| -> f32 {
        if b == 0 {
            0.0
        } else {
            (f32::from(b) * 1.411_764_7) * 0.017_453_292
        }
    };
    let (ax, ay, az) = (ang(r.angle_x), ang(r.angle_y), ang(r.angle_z));
    rotation_xyz(ax, ay, az)
}

/// `Matrix33::CreateRotationXYZ` (`Rz · Ry · Rx`).
pub fn rotation_xyz(ax: f32, ay: f32, az: f32) -> Mat33 {
    let (sx, cx) = ax.sin_cos();
    let (sy, cy) = ay.sin_cos();
    let (sz, cz) = az.sin_cos();
    Mat33::from_array([
        cy * cz,
        cz * sx * sy - cx * sz,
        cx * cz * sy + sx * sz,
        cy * sz,
        cx * cz + sx * sy * sz,
        -cz * sx + cx * sy * sz,
        -sy,
        cy * sx,
        cx * cy,
    ])
}

/// A `vegetation.xml` group.
#[derive(Debug, Clone, PartialEq)]
pub struct VegetationGroup {
    pub id: u16,
    /// Normalised model path (empty = none).
    pub model: String,
    /// `bHideability` / `bHideabilitySecondary` (passed through to the physics in the engine,
    /// *(inferred)* group flag byte +24 bits 0/1).
    pub hideability: bool,
    pub hideability_secondary: bool,
    /// `bAlignToTerrain` (*(inferred)* the group flag `+52 & 8` of 0x30102840).
    pub align_to_terrain: bool,
}

/// Parses `vegetation.xml` (`<group id="…" modelFileName="…" …/>` anywhere in the document).
pub fn read_vegetation_groups(xml: &str) -> Vec<VegetationGroup> {
    let Ok(doc) = roxmltree::Document::parse(xml) else {
        return Vec::new();
    };
    doc.descendants()
        .filter(|n| n.has_tag_name("group") || n.has_tag_name("Group"))
        .filter_map(|n| {
            let id = n.attribute("id")?.trim().parse::<u16>().ok()?;
            let model = n
                .attribute("modelFileName")
                .map(str::trim)
                .filter(|s| !s.is_empty())
                .map(normalize_model_path)
                .unwrap_or_default();
            let flag = |a: &str| n.attribute(a).is_some_and(|v| v.trim() == "1");
            Some(VegetationGroup {
                id,
                model,
                hideability: flag("bHideability"),
                hideability_secondary: flag("bHideabilitySecondary"),
                align_to_terrain: flag("bAlignToTerrain"),
            })
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn model_paths_normalise() {
        assert_eq!(
            normalize_model_path("Objects\\Env\\Rock.cgf "),
            "game/objects/env/rock.cgf"
        );
        assert_eq!(
            normalize_model_path("game/worlds/main_world/cells/007_010//brush/solid_a.cgf"),
            "game/worlds/main_world/cells/007_010/brush/solid_a.cgf"
        );
    }

    fn brush_file(sector: usize, recs: &[(Vec3, u32, i32)]) -> Vec<u8> {
        let mut d = vec![0u8; 4 + 2048];
        d[..4].copy_from_slice(&1u32.to_le_bytes());
        let off = d.len() as u32;
        d[4 + 4 * sector..8 + 4 * sector].copy_from_slice(&off.to_le_bytes());
        let size = (recs.len() * 128) as u32;
        d[1028 + 4 * sector..1032 + 4 * sector].copy_from_slice(&size.to_le_bytes());
        for (p, flags, so) in recs {
            let mut r = vec![0u8; 128];
            r[32..36].copy_from_slice(&flags.to_le_bytes());
            let m = [
                1.0f32, 0.0, 0.0, p.x, 0.0, 1.0, 0.0, p.y, 0.0, 0.0, 1.0, p.z,
            ];
            for (j, v) in m.iter().enumerate() {
                r[67 + 4 * j..71 + 4 * j].copy_from_slice(&v.to_le_bytes());
            }
            r[123..127].copy_from_slice(&so.to_le_bytes());
            d.extend_from_slice(&r);
        }
        d
    }

    #[test]
    fn brush_records_get_the_sector_offset() {
        let d = brush_file(17, &[(Vec3::new(1.0, 2.0, 3.0), 0x2000_0008, 4)]);
        let b = read_brushes(&d).unwrap();
        assert_eq!(b.len(), 1);
        let (sx, sy) = sector_xy(17, 16);
        assert_eq!(
            b[0].pos,
            Vec3::new(1.0 + 64.0 * sx as f32, 2.0 + 64.0 * sy as f32, 3.0)
        );
        assert_eq!(b[0].rnd_flags, 0x2000_0008);
        assert_eq!(b[0].statobj, 4);
        assert!(b[0].rot_scale.is_identity());
        assert_eq!(
            read_brushes(&[2, 0, 0, 0]).unwrap_err(),
            DatError::Version(2)
        );
    }

    #[test]
    fn vegetation_records_and_groups() {
        let mut d = vec![0u8; 132];
        d[..4].copy_from_slice(&1u32.to_le_bytes());
        d[4 + 4 * 5..8 + 4 * 5].copy_from_slice(&132u32.to_le_bytes());
        d[68 + 4 * 5..72 + 4 * 5].copy_from_slice(&64u32.to_le_bytes());
        let mut r = vec![0u8; 64];
        for (j, v) in [10.0f32, 20.0, 30.0, 1.5].iter().enumerate() {
            r[39 + 4 * j..43 + 4 * j].copy_from_slice(&v.to_le_bytes());
        }
        r[55..57].copy_from_slice(&77u16.to_le_bytes());
        r[60] = 64;
        d.extend_from_slice(&r);
        let v = read_vegetation(&d).unwrap();
        assert_eq!(v.len(), 1);
        let (sx, sy) = sector_xy(5, 4);
        assert_eq!(
            v[0].pos,
            Vec3::new(10.0 + 256.0 * sx as f32, 20.0 + 256.0 * sy as f32, 30.0)
        );
        assert_eq!(v[0].scale, 1.5);
        assert_eq!(v[0].group, 77);
        // 64 · 360/255 ≈ 90.35°
        let m = vegetation_rotation(&v[0]);
        assert!((m.row(0).x - (90.353f32.to_radians()).cos()).abs() < 1e-4);

        let g = read_vegetation_groups(
            r#"<Vegetation><group id="77" modelFileName="Objects\Tree.cgf" bHideability="0"/><group id="3"/></Vegetation>"#,
        );
        assert_eq!(g.len(), 2);
        assert_eq!(g[0].model, "game/objects/tree.cgf");
        assert_eq!(g[1].model, "");
    }

    #[test]
    fn statobjs_names() {
        let mut d = 2u32.to_le_bytes().to_vec();
        for n in ["Objects/A.cgf", "objects\\b.cgf "] {
            let mut rec = vec![0u8; 256];
            rec[..n.len()].copy_from_slice(n.as_bytes());
            d.extend_from_slice(&rec);
        }
        assert_eq!(
            read_statobjs(&d),
            vec![
                "game/objects/a.cgf".to_string(),
                "game/objects/b.cgf".to_string()
            ]
        );
    }
}
