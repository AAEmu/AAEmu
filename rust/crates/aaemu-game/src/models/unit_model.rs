//! Port of `AAEmu.Game.Models.Game.Units.UnitCustomModelParams` (+ `FaceModel`, `FixedDecalAsset`).

use aaemu_commons::{Marshal, PacketStream, Unmarshal};

/// `UnitCustomModelType`. Stored as the raw byte so unknown values round-trip like the C# enum.
#[derive(Debug, Clone, Copy, PartialEq, Eq, PartialOrd, Ord, Default)]
pub struct UnitCustomModelType(pub u8);

impl UnitCustomModelType {
    pub const NONE: Self = Self(0);
    pub const HAIR: Self = Self(1);
    pub const SKIN: Self = Self(2);
    pub const FACE: Self = Self(3);
}

#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct FixedDecalAsset {
    pub asset_id: u32,
    pub asset_weight: f32,
}

impl Marshal for FixedDecalAsset {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.asset_id).write_f32(self.asset_weight);
    }
}

impl Unmarshal for FixedDecalAsset {
    fn read(s: &mut PacketStream) -> aaemu_commons::packet_stream::Result<Self> {
        Ok(Self {
            asset_id: s.read_u32()?,
            asset_weight: s.read_f32()?,
        })
    }
}

#[derive(Debug, Clone, PartialEq)]
pub struct FaceModel {
    pub movable_decal_asset_id: u32,
    pub movable_decal_weight: f32,
    pub movable_decal_scale: f32,
    pub movable_decal_rotate: f32,
    pub movable_decal_move_x: i16,
    pub movable_decal_move_y: i16,
    pub fixed_decal_asset: [FixedDecalAsset; 4],
    pub diffuse_map_id: u32,
    pub normal_map_id: u32,
    pub eyelash_map_id: u32,
    pub normal_map_weight: f32,
    pub lip_color: u32,
    pub left_pupil_color: u32,
    pub right_pupil_color: u32,
    pub eyebrow_color: u32,
    pub deco_color: u32,
    pub modifier: Vec<u8>,
}

impl Default for FaceModel {
    fn default() -> Self {
        Self {
            movable_decal_asset_id: 0,
            movable_decal_weight: 0.0,
            movable_decal_scale: 0.0,
            movable_decal_rotate: 0.0,
            movable_decal_move_x: 0,
            movable_decal_move_y: 0,
            fixed_decal_asset: [FixedDecalAsset::default(); 4],
            diffuse_map_id: 0,
            normal_map_id: 0,
            eyelash_map_id: 0,
            normal_map_weight: 0.0,
            lip_color: 0,
            left_pupil_color: 0,
            right_pupil_color: 0,
            eyebrow_color: 0,
            deco_color: 0,
            modifier: vec![0; 128],
        }
    }
}

impl Marshal for FaceModel {
    fn write(&self, s: &mut PacketStream) {
        s.write_u32(self.movable_decal_asset_id)
            .write_f32(self.movable_decal_weight)
            .write_f32(self.movable_decal_scale)
            .write_f32(self.movable_decal_rotate)
            .write_i16(self.movable_decal_move_x)
            .write_i16(self.movable_decal_move_y);
        for a in &self.fixed_decal_asset {
            a.write(s);
        }
        s.write_u32(self.diffuse_map_id)
            .write_u32(self.normal_map_id)
            .write_u32(self.eyelash_map_id)
            .write_f32(self.normal_map_weight)
            .write_u32(self.lip_color)
            .write_u32(self.left_pupil_color)
            .write_u32(self.right_pupil_color)
            .write_u32(self.eyebrow_color)
            .write_u32(self.deco_color)
            .write_bytes_sized(&self.modifier);
    }
}

impl Unmarshal for FaceModel {
    fn read(s: &mut PacketStream) -> aaemu_commons::packet_stream::Result<Self> {
        Ok(Self {
            movable_decal_asset_id: s.read_u32()?,
            movable_decal_weight: s.read_f32()?,
            movable_decal_scale: s.read_f32()?,
            movable_decal_rotate: s.read_f32()?,
            movable_decal_move_x: s.read_i16()?,
            movable_decal_move_y: s.read_i16()?,
            fixed_decal_asset: [s.read()?, s.read()?, s.read()?, s.read()?],
            diffuse_map_id: s.read_u32()?,
            normal_map_id: s.read_u32()?,
            eyelash_map_id: s.read_u32()?,
            normal_map_weight: s.read_f32()?,
            lip_color: s.read_u32()?,
            left_pupil_color: s.read_u32()?,
            right_pupil_color: s.read_u32()?,
            eyebrow_color: s.read_u32()?,
            deco_color: s.read_u32()?,
            modifier: s.read_bytes_sized()?,
        })
    }
}

/// `UnitCustomModelParams`: the appearance blob stored in `characters.unit_model_params`.
/// The type byte gates how many of the following fields are present.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct UnitCustomModelParams {
    pub kind: UnitCustomModelType,
    pub hair_color_id: u32,
    pub skin_color_id: u32,
    pub model_id: u32,
    /// Present when `kind >= Face` (C# `SetType` allocates it).
    pub face: Option<FaceModel>,
}

impl Marshal for UnitCustomModelParams {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.kind.0);
        if self.kind < UnitCustomModelType::HAIR {
            return;
        }
        s.write_u32(self.hair_color_id);
        if self.kind < UnitCustomModelType::SKIN {
            return;
        }
        s.write_u32(self.skin_color_id).write_u32(self.model_id);
        if self.kind < UnitCustomModelType::FACE {
            return;
        }
        // C# would throw a NullReferenceException on a null Face here; SetType always creates it.
        self.face.clone().unwrap_or_default().write(s);
    }
}

impl Unmarshal for UnitCustomModelParams {
    fn read(s: &mut PacketStream) -> aaemu_commons::packet_stream::Result<Self> {
        let kind = UnitCustomModelType(s.read_u8()?);
        let mut p = Self {
            kind,
            ..Self::default()
        };
        if kind >= UnitCustomModelType::FACE {
            p.face = Some(FaceModel::default());
        }
        if kind <= UnitCustomModelType::NONE {
            return Ok(p);
        }
        p.hair_color_id = s.read_u32()?;
        if kind <= UnitCustomModelType::HAIR {
            return Ok(p);
        }
        p.skin_color_id = s.read_u32()?;
        p.model_id = s.read_u32()?;
        if kind <= UnitCustomModelType::SKIN {
            return Ok(p);
        }
        p.face = Some(s.read()?);
        Ok(p)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn face_model_roundtrip_is_byte_exact() {
        let mut face = FaceModel {
            lip_color: 0x11223344,
            movable_decal_move_x: -3,
            ..FaceModel::default()
        };
        face.fixed_decal_asset[2] = FixedDecalAsset {
            asset_id: 9,
            asset_weight: 0.5,
        };
        let p = UnitCustomModelParams {
            kind: UnitCustomModelType::FACE,
            hair_color_id: 1,
            skin_color_id: 2,
            model_id: 3,
            face: Some(face),
        };
        let mut s = PacketStream::new();
        p.write(&mut s);
        // 1 + 4 + 8 + (4*4 + 2*2 + 4*8 + 4*4 + 4*4 + 2 + 128)
        assert_eq!(s.len(), 1 + 4 + 8 + 16 + 4 + 32 + 16 + 20 + 2 + 128);
        let bytes = s.as_bytes().to_vec();
        let back: UnitCustomModelParams = s.read().unwrap();
        assert_eq!(back, p);
        let mut again = PacketStream::new();
        back.write(&mut again);
        assert_eq!(again.as_bytes(), &bytes[..]);
    }

    #[test]
    fn short_kinds() {
        let mut s = PacketStream::from_bytes(vec![1, 0xAA, 0, 0, 0]);
        let p: UnitCustomModelParams = s.read().unwrap();
        assert_eq!(p.hair_color_id, 0xAA);
        let mut out = PacketStream::new();
        p.write(&mut out);
        assert_eq!(out.as_bytes(), &[1, 0xAA, 0, 0, 0]);
    }
}
