//! `pe_geomparams*` (AddGeometry). x64 offsets; the three variants share the first 112 bytes.

use super::pe_struct;
use crate::ids::{geomparams_id as id, part_flags};
use crate::math::{Quat, Vec3};
use crate::sentinel::{RawPtr, UNUSED_F32 as U, UNUSED_I32 as UI, UNUSED_VEC3 as UV};

/// Default part flags used by the oracle scenarios and [`GeomParams::default`]: the low
/// collision-class bits 0x9F the original harness used, plus [`part_flags::COLLTYPE_RAY`]
/// (hit by default rays) and [`part_flags::FLOATS`] (buoyancy). Callers normally pass their
/// own flags; the binary has no default (the struct is filled by the game code).
pub const DEFAULT_PART_FLAGS: u32 = 0x9F | part_flags::COLLTYPE_RAY | part_flags::FLOATS;

pe_struct! {
    /// Plain geometry params (id 0), as read by `CPhysicalEntity::AddGeometry` (0x35055FF0).
    /// `mass <= 0` means `mass = density · V · (scale · entityScale)³`.
    GeomParams: GeomParams(id::PLAIN, size 112) {
        4 => density: f32 = 0.0,
        8 => mass: f32 = 0.0,
        /// Multiplied by the entity scale.
        12 => pos: Vec3 = Vec3::ZERO,
        24 => q: Quat = Quat::IDENTITY,
        /// Multiplied by the entity scale.
        40 => scale: f32 = 1.0,
        48 => mtx3x4: RawPtr = RawPtr::NULL,
        56 => mtx3x3: RawPtr = RawPtr::NULL,
        /// Unused = take it from the `phys_geometry`.
        64 => surface_idx: i32 = UI,
        68 => flags: u32 = DEFAULT_PART_FLAGS,
        72 => flags_collider: u32 = 1,
        /// Unused = auto (`mid(0.03·maxDim, k·minDim)`).
        76 => min_contact_dist: f32 = U,
        /// Unused: automatic from the geometry when `flags & 0xFFB`, else -1. The harness and
        /// the oracle pass -1.
        80 => idmat_breakable: i32 = -1,
        /// Only for meshes.
        88 => lattice: RawPtr = RawPtr::NULL,
        96 => mat_mapping: RawPtr = RawPtr::NULL,
        104 => n_mats: i32 = 0,
        /// The entity bbox and grid cells are only updated when this is set.
        108 => recalc_bbox: i32 = 1,
    }
}

pe_struct! {
    /// Car (wheel) geometry params (id 1, wheeled). `driving` unused = hull part.
    GeomParamsCar: GeomParams(id::CAR, size 184) {
        4 => density: f32 = 0.0,
        8 => mass: f32 = 0.0,
        12 => pos: Vec3 = Vec3::ZERO,
        24 => q: Quat = Quat::IDENTITY,
        40 => scale: f32 = 1.0,
        48 => mtx3x4: RawPtr = RawPtr::NULL,
        56 => mtx3x3: RawPtr = RawPtr::NULL,
        64 => surface_idx: i32 = UI,
        68 => flags: u32 = DEFAULT_PART_FLAGS,
        72 => flags_collider: u32 = 1,
        76 => min_contact_dist: f32 = U,
        80 => idmat_breakable: i32 = -1,
        88 => lattice: RawPtr = RawPtr::NULL,
        96 => mat_mapping: RawPtr = RawPtr::NULL,
        104 => n_mats: i32 = 0,
        108 => recalc_bbox: i32 = 1,
        112 => driving: i32 = UI,
        116 => i_axle: i32 = UI,
        128 => pivot: Vec3 = UV,
        140 => len_max: f32 = U,
        144 => len_initial: f32 = U,
        148 => unk_148: f32 = U,
        156 => unk_156: f32 = U,
        /// Friction terms (defaults 0, 1, 1, 1, 0 when unused).
        160 => friction: [f32; 5] = [U; 5],
        /// Default 0.1745 rad when unused (>= 0.001).
        180 => unk_180: f32 = U,
    }
}

pe_struct! {
    /// Articulated geometry params (id 2): the plain block plus a body / joint id.
    GeomParamsArticulated: GeomParams(id::ARTICULATED, size 120) {
        4 => density: f32 = 0.0,
        8 => mass: f32 = 0.0,
        12 => pos: Vec3 = Vec3::ZERO,
        24 => q: Quat = Quat::IDENTITY,
        40 => scale: f32 = 1.0,
        48 => mtx3x4: RawPtr = RawPtr::NULL,
        56 => mtx3x3: RawPtr = RawPtr::NULL,
        64 => surface_idx: i32 = UI,
        68 => flags: u32 = DEFAULT_PART_FLAGS,
        72 => flags_collider: u32 = 1,
        76 => min_contact_dist: f32 = U,
        80 => idmat_breakable: i32 = -1,
        88 => lattice: RawPtr = RawPtr::NULL,
        96 => mat_mapping: RawPtr = RawPtr::NULL,
        104 => n_mats: i32 = 0,
        108 => recalc_bbox: i32 = 1,
        /// *(inferred)* id of the articulated body (joint) the part belongs to.
        112 => id_body: i32 = UI,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ids::GEOMPARAMS_SIZES;
    use crate::params::{layout_problems, PeStruct};

    fn check<T: PeStruct>() {
        let p = layout_problems::<T>();
        assert!(p.is_empty(), "{}: {p:?}", std::any::type_name::<T>());
        assert_eq!(T::SIZE as u32, GEOMPARAMS_SIZES[T::TYPE_ID as usize]);
        let b = T::default().to_bytes();
        assert_eq!(T::read(&b).to_bytes(), b);
    }

    #[test]
    fn layouts() {
        check::<GeomParams>();
        check::<GeomParamsCar>();
        check::<GeomParamsArticulated>();
        // the shared prefix is byte-identical
        assert_eq!(
            GeomParams::default().to_bytes()[4..112],
            GeomParamsCar::default().to_bytes()[4..112]
        );
        assert_eq!(DEFAULT_PART_FLAGS, 0x1809F);
    }
}
