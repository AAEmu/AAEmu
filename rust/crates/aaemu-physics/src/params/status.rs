//! `pe_status_*` structs (GetStatus). x64 offsets. There is no size table for status structs;
//! `SIZE` is the end of the last known field rounded up to 8.

use super::pe_struct;
use crate::ids::status_id as id;
use crate::math::{Mat33, Quat, Vec3};
use crate::sentinel::{
    RawPtr, UNUSED_F32 as U, UNUSED_I32 as UI, UNUSED_QUAT as UQ, UNUSED_VEC3 as UV,
};

pe_struct! {
    /// Position status (id 1). In: `part_id` / `ipart` (-1 = whole entity; the oracle uses
    /// -1/-1), `flags` (1 local/part frame, 2 skip lock, 4 AddRef geometries). The bbox is
    /// relative to `pos`.
    StatusPos: Status(id::POS, size 120) {
        4 => part_id: i32 = -1,
        8 => ipart: i32 = -1,
        12 => flags: u32 = 0,
        16 => flags_or: u32 = 0,
        20 => flags_and: u32 = 0,
        24 => pos: Vec3 = Vec3::ZERO,
        36 => bbox_min: Vec3 = Vec3::ZERO,
        48 => bbox_max: Vec3 = Vec3::ZERO,
        60 => q: Quat = Quat::IDENTITY,
        76 => scale: f32 = 1.0,
        80 => sim_class: i32 = 0,
        /// Optional out `Matrix34*`.
        88 => mtx3x4: RawPtr = RawPtr::NULL,
        /// Optional out `Matrix33*`.
        96 => mtx3x3: RawPtr = RawPtr::NULL,
        104 => geom: RawPtr = RawPtr::NULL,
        112 => geom_proxy: RawPtr = RawPtr::NULL,
    }
}

pe_struct! {
    /// Living status (id 2) *(inferred names)*.
    StatusLiving: Status(id::LIVING, size 168) {
        4 => is_flying: i32 = 0,
        8 => time_flying: f32 = 0.0,
        12 => cam_offset: Vec3 = Vec3::ZERO,
        24 => vel: Vec3 = Vec3::ZERO,
        36 => vel_unconstrained: Vec3 = Vec3::ZERO,
        48 => vel_requested: Vec3 = Vec3::ZERO,
        60 => vel_ground: Vec3 = Vec3::ZERO,
        72 => vel_actual: Vec3 = Vec3::ZERO,
        84 => ground_mat_id: i32 = 0,
        88 => ground_height: f32 = 0.0,
        92 => ground_slope: Vec3 = Vec3::ZERO,
        104 => ground_surface0: i32 = 0,
        108 => ground_surface1: i32 = 0,
        112 => ground_collider: RawPtr = RawPtr::NULL,
        120 => ground_collider_part: i32 = 0,
        124 => ground_collider_part_id: i32 = 0,
        128 => pos_in_ground: Vec3 = Vec3::ZERO,
        140 => unk_140: f32 = 0.0,
        144 => stuck: i32 = 0,
        152 => step_lock: RawPtr = RawPtr::NULL,
        164 => collision_counter: i32 = 0,
    }
}

pe_struct! {
    /// Vehicle status (id 4, wheeled) *(inferred names)*.
    StatusVehicle: Status(id::VEHICLE, size 56) {
        4 => steer: f32 = 0.0,
        8 => pedal: f32 = 0.0,
        12 => handbrake: i32 = 0,
        16 => footbrake: i32 = 0,
        20 => drive_value: f32 = 0.0,
        24 => vel: Vec3 = Vec3::ZERO,
        36 => n_active_contacts: i32 = 0,
        40 => cur_gear: i32 = 0,
        44 => engine_rpm: f32 = 0.0,
        48 => clutch: f32 = 0.0,
        52 => drive_torque: f32 = 0.0,
    }
}

pe_struct! {
    /// Wheel status (id 5, wheeled) *(inferred names)*.
    StatusWheel: Status(id::WHEEL, size 144) {
        4 => i_wheel: i32 = UI,
        8 => part_id: i32 = UI,
        12 => contact: i32 = 0,
        16 => pos: Vec3 = Vec3::ZERO,
        28 => q: Quat = Quat::IDENTITY,
        44 => pt_contact: Vec3 = Vec3::ZERO,
        56 => normal: Vec3 = Vec3::ZERO,
        68 => w: f32 = 0.0,
        72 => slip: i32 = 0,
        76 => vel_slip: Vec3 = Vec3::ZERO,
        88 => contact_surface: i32 = 0,
        92 => unk_92: i32 = 0,
        96 => unk_96: i32 = 0,
        100 => susp_len: f32 = 0.0,
        104 => susp_len_full: f32 = 0.0,
        108 => susp_len0: f32 = 0.0,
        112 => r: f32 = 0.0,
        116 => torque: f32 = 0.0,
        120 => steer: f32 = 0.0,
        124 => rot_angle: f32 = 0.0,
        128 => coll_entity: RawPtr = RawPtr::NULL,
        136 => mass_share: f32 = 0.0,
    }
}

pe_struct! {
    /// Joint status (id 6, articulated).
    StatusJoint: Status(id::JOINT, size 72) {
        4 => id_child_body: i32 = UI,
        8 => part_id: i32 = UI,
        12 => flags: i32 = 0,
        16 => q: Vec3 = Vec3::ZERO,
        28 => qext: Vec3 = Vec3::ZERO,
        40 => dq: Vec3 = Vec3::ZERO,
        52 => quat0: Quat = Quat::IDENTITY,
    }
}

pe_struct! {
    /// Awake status (id 7): the return value is the answer. `lag` (rigid): also report bodies
    /// that had an external contact within the last `lag` steps.
    StatusAwake: Status(id::AWAKE, size 8) {
        4 => lag: i32 = 0,
    }
}

pe_struct! {
    /// Dynamics status (id 8). When asleep, v/w/a/wa are zero.
    StatusDynamics: Status(id::DYNAMICS, size 128) {
        4 => ipart: i32 = -1,
        8 => part_id: i32 = -1,
        12 => v: Vec3 = Vec3::ZERO,
        24 => w: Vec3 = Vec3::ZERO,
        /// gravity + Minv·F_collision.
        36 => a: Vec3 = Vec3::ZERO,
        48 => wa: Vec3 = Vec3::ZERO,
        60 => center_of_mass: Vec3 = Vec3::ZERO,
        /// World inverse inertia (rigid).
        72 => iinv: Mat33 = Mat33::ZERO,
        108 => submerged_fraction: f32 = 0.0,
        112 => mass: f32 = 0.0,
        /// *(inferred)* energy.
        116 => energy: f32 = 0.0,
        /// *(inferred)* contact count (articulated).
        120 => n_contacts: i32 = 0,
    }
}

pe_struct! {
    /// Collisions status (id 9): soft bodies fill a buffer of 80-byte entries; rigid/particle/
    /// living only use the return value.
    StatusCollisions: Status(id::COLLISIONS, size 24) {
        8 => buffer: RawPtr = RawPtr::NULL,
        16 => count: i32 = 0,
    }
}

pe_struct! {
    /// Material-id status (id 10).
    StatusId: Status(id::ID, size 32) {
        4 => ipart: i32 = UI,
        8 => part_id: i32 = UI,
        12 => i_prim: i32 = 0,
        16 => i_feature: i32 = 0,
        20 => use_proxy: i32 = 0,
        24 => id: i32 = 0,
    }
}

pe_struct! {
    /// Part count (id 12): the return value.
    StatusNParts: Status(id::NPARTS, size 8) {}
}

pe_struct! {
    /// Point containment (id 13): the return value.
    StatusContainsPoint: Status(id::CONTAINS_POINT, size 16) {
        4 => pt: Vec3 = Vec3::ZERO,
    }
}

pe_struct! {
    /// Rope status (id 14) *(inferred names)*.
    StatusRope: Status(id::ROPE, size 120) {
        4 => n_segments: i32 = 0,
        8 => points: RawPtr = RawPtr::NULL,
        16 => velocities: RawPtr = RawPtr::NULL,
        24 => n_coll_static: i32 = 0,
        28 => n_coll_dyn: i32 = 0,
        32 => target_pose_active: i32 = 0,
        36 => stiffness_anim: f32 = 0.0,
        40 => contact_ents: RawPtr = RawPtr::NULL,
        48 => contact_ents_stride: i32 = 0,
        56 => n_vtx: i32 = 0,
        64 => vtx: RawPtr = RawPtr::NULL,
        72 => contact_normals: RawPtr = RawPtr::NULL,
        80 => time_last_active: f32 = 0.0,
        84 => pos_host: Vec3 = Vec3::ZERO,
        96 => q_host: Quat = Quat::IDENTITY,
        112 => local: u8 = 0,
    }
}

pe_struct! {
    /// Vehicle abilities (id 15, wheeled) *(inferred)*.
    StatusVehicleAbilities: Status(id::VEHICLE_ABILITIES, size 24) {
        4 => steer: f32 = U,
        8 => rot_pivot: Vec3 = Vec3::ZERO,
        20 => max_velocity: f32 = 0.0,
    }
}

pe_struct! {
    /// Soft-body vertices (id 17) *(inferred names)*.
    StatusSoftVertices: Status(id::SOFT_VERTICES, size 112) {
        4 => n_vtx: i32 = 0,
        8 => vtx: RawPtr = RawPtr::NULL,
        16 => vtx_stride: i32 = 0,
        24 => normals: RawPtr = RawPtr::NULL,
        32 => normals_stride: i32 = 0,
        40 => mesh: RawPtr = RawPtr::NULL,
        48 => flags: i32 = 0,
        52 => q_host: Quat = Quat::IDENTITY,
        68 => pos_host: Vec3 = Vec3::ZERO,
        80 => pos: Vec3 = Vec3::ZERO,
        92 => q: Quat = Quat::IDENTITY,
    }
}

pe_struct! {
    /// Contact-patch fit (id 19, rigid) *(inferred)*.
    StatusContactPatch: Status(id::CONTACT_PATCH, size 32) {
        4 => pt: Vec3 = Vec3::ZERO,
        16 => n: Vec3 = Vec3::ZERO,
    }
}

pe_struct! {
    /// Check stance (id 21, living) *(inferred)*: returns 1 when no unprojection is needed.
    StatusCheckStance: Status(id::CHECK_STANCE, size 72) {
        4 => pos: Vec3 = UV,
        /// Tested on `w` (+28).
        16 => q: Quat = UQ,
        32 => size: Vec3 = UV,
        44 => height_collider: f32 = U,
        48 => dir_unproj: Vec3 = UV,
        60 => unproj: f32 = 0.0,
        64 => use_capsule: i32 = UI,
    }
}

pe_struct! {
    /// Extent (id 24) *(inferred)*.
    StatusExtent: Status(id::EXTENT, size 16) {
        4 => form: i32 = 0,
        8 => ctx: RawPtr = RawPtr::NULL,
    }
}

pe_struct! {
    /// Random position (id 25) *(inferred)*.
    StatusRandomPos: Status(id::RANDOM_POS, size 40) {
        4 => form: i32 = 0,
        8 => ctx: RawPtr = RawPtr::NULL,
        16 => pos: Vec3 = Vec3::ZERO,
        28 => n: Vec3 = Vec3::ZERO,
    }
}

pe_struct! {
    /// Constraint status (id 26, rigid).
    StatusConstraint: Status(id::CONSTRAINT, size 56) {
        4 => id: i32 = UI,
        /// Unused = search by id.
        8 => idx: i32 = UI,
        12 => flags: i32 = 0,
        16 => pt0: Vec3 = Vec3::ZERO,
        28 => pt1: Vec3 = Vec3::ZERO,
        40 => n: Vec3 = Vec3::ZERO,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::params::{layout_problems, PeStruct};

    fn check<T: PeStruct>() {
        let p = layout_problems::<T>();
        assert!(p.is_empty(), "{}: {p:?}", std::any::type_name::<T>());
        let b = T::default().to_bytes();
        assert_eq!(T::read(&b).to_bytes(), b);
    }

    #[test]
    fn layouts() {
        check::<StatusPos>();
        check::<StatusLiving>();
        check::<StatusVehicle>();
        check::<StatusWheel>();
        check::<StatusJoint>();
        check::<StatusAwake>();
        check::<StatusDynamics>();
        check::<StatusCollisions>();
        check::<StatusId>();
        check::<StatusNParts>();
        check::<StatusContainsPoint>();
        check::<StatusRope>();
        check::<StatusVehicleAbilities>();
        check::<StatusSoftVertices>();
        check::<StatusContactPatch>();
        check::<StatusCheckStance>();
        check::<StatusExtent>();
        check::<StatusRandomPos>();
        check::<StatusConstraint>();
    }
}
