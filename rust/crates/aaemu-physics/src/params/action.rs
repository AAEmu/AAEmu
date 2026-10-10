//! `pe_action_*` structs (Action). x64 offsets.

use super::pe_struct;
use crate::ids::action_id as id;
use crate::math::{Quat, Vec3};
use crate::sentinel::{
    RawPtr, UNUSED_F32 as U, UNUSED_I32 as UI, UNUSED_QUAT as UQ, UNUSED_VEC3 as UV,
};

pe_struct! {
    /// `action_move` (id 1, living; string "CLivingEntity:Action(action_move)").
    ActionMove: Action(id::MOVE, size 24) {
        4 => dir: Vec3 = UV,
        /// 0 requested velocity, 1 jump (set velocity), 2 add velocity, 3 raw velocity next step.
        16 => i_jump: i32 = 0,
        /// Cap for the next StartStep dt (validated to [0, 2]).
        20 => dt: f32 = U,
    }
}

pe_struct! {
    /// `action_impulse` (id 2; field strings "impulse", "angImpulse", "point", "ipart").
    /// Rigid: `i_apply_time != 0` accumulates into the deferred P/L applied in the next Step;
    /// `i_source == 1` clamps |Minv·P| and |Iinv·L| to 5 and does not wake the body; living
    /// scales by 0.3 for `i_source == 2`.
    ActionImpulse: Action(id::IMPULSE, size 80) {
        /// Not optional: the rigid handler adds it without an unused test (an unused value
        /// would put NaN into the momentum), so the default is zero.
        4 => impulse: Vec3 = Vec3::ZERO,
        /// Optional.
        16 => ang_impulse: Vec3 = UV,
        /// *(inferred)* velocity change, added as m·dv.
        28 => dv: Vec3 = UV,
        /// *(inferred)* angular velocity change, added as Ibody·dw.
        40 => dw: Vec3 = UV,
        /// Adds (point − com) × impulse to the angular impulse.
        52 => point: Vec3 = UV,
        64 => part_id: i32 = UI,
        68 => ipart: i32 = UI,
        72 => i_apply_time: i32 = 0,
        76 => i_source: i32 = 0,
    }
}

pe_struct! {
    /// Drive (id 3, wheeled; strings "pedal", "steer", "handbrake", "curGear").
    ActionDrive: Action(id::DRIVE, size 36) {
        4 => pedal: f32 = U,
        8 => dpedal: f32 = U,
        12 => steer: f32 = U,
        16 => dsteer: f32 = U,
        20 => clutch: f32 = U,
        24 => handbrake: i32 = UI,
        28 => footbrake: i32 = UI,
        32 => gear: i32 = UI,
    }
}

pe_struct! {
    /// Reset (id 4): zero momenta; rigid `clear_contacts` also drops contacts and constraints.
    ActionReset: Action(id::RESET, size 8) {
        4 => clear_contacts: i32 = 0,
    }
}

pe_struct! {
    /// Add constraint (id 5, rigid) *(field names from the slot-15 serializer strings)*.
    ActionAddConstraint: Action(id::ADD_CONSTRAINT, size 128) {
        /// Unused = auto id.
        4 => id: i32 = UI,
        /// Constraint partner; [`RawPtr::WORLD`] = the static world.
        8 => buddy: RawPtr = RawPtr::NULL,
        16 => pt0: Vec3 = UV,
        /// Unused = pt0.
        28 => pt1: Vec3 = UV,
        40 => part_id0: i32 = UI,
        44 => part_id1: i32 = UI,
        48 => qframe0: Quat = UQ,
        64 => qframe1: Quat = UQ,
        80 => xlim: [f32; 2] = [U; 2],
        88 => yzlim: [f32; 2] = [U; 2],
        /// 1 local frames, 4 relative to parts, 0x400 line, 0x800 plane, 0x1000 ignore buddy,
        /// 0x2000 full rotation lock.
        96 => flags: i32 = 0,
        100 => damping: f32 = U,
        104 => sensor_radius: f32 = U,
        108 => max_pull_force: f32 = U,
        112 => max_bend_torque: f32 = U,
        120 => constraint_entity: RawPtr = RawPtr::NULL,
    }
}

pe_struct! {
    /// Update constraint (id 6, rigid).
    ActionUpdateConstraint: Action(id::UPDATE_CONSTRAINT, size 48) {
        /// Unused = all constraints.
        4 => id: i32 = UI,
        8 => flags_or: u32 = 0,
        12 => flags_and: u32 = u32::MAX,
        16 => remove: i32 = 0,
        20 => pt0: Vec3 = UV,
        32 => pt1: Vec3 = UV,
        /// bit 2: points are in world coordinates.
        44 => flags: i32 = 0,
    }
}

pe_struct! {
    /// Register a collision event (id 7, rigid) *(inferred)*.
    ActionRegisterCollEvent: Action(id::REGISTER_COLL_EVENT, size 88) {
        4 => pt: Vec3 = UV,
        16 => n: Vec3 = UV,
        /// Unused: computed from the body at the point.
        28 => v0: Vec3 = UV,
        40 => v1: Vec3 = UV,
        52 => unk_52: f32 = U,
        56 => collider: RawPtr = RawPtr::NULL,
        64 => part_id: i32 = UI,
        68 => unk_68: i32 = UI,
        72 => unk_72: i32 = 0,
        76 => unk_76: i32 = 0,
        /// Two int16 material / part ids.
        80 => ids16: u32 = 0,
    }
}

pe_struct! {
    /// Awake (id 8).
    ActionAwake: Action(id::AWAKE, size 12) {
        4 => awake: i32 = 1,
        /// Rigid: minimum awake time.
        8 => min_awake_time: f32 = U,
    }
}

pe_struct! {
    /// Remove all parts (id 9).
    ActionRemoveAllParts: Action(id::REMOVE_ALL_PARTS, size 4) {}
}

pe_struct! {
    /// Set velocity (id 10).
    ActionSetVelocity: Action(id::SET_VELOCITY, size 40) {
        4 => ipart: i32 = UI,
        8 => part_id: i32 = UI,
        12 => v: Vec3 = UV,
        24 => w: Vec3 = UV,
        /// v += w × (com − pos).
        36 => rotation_around_pivot: i32 = 0,
    }
}

pe_struct! {
    /// Attach points (id 11, soft) *(inferred)*.
    ActionAttachPoints: Action(id::ATTACH_POINTS, size 48) {
        /// [`RawPtr::WORLD`] = world; null with unused `vtx` detaches all.
        8 => entity: RawPtr = RawPtr::NULL,
        16 => part_id: i32 = UI,
        24 => vtx: RawPtr = RawPtr::NULL,
        32 => points: RawPtr = RawPtr::NULL,
        40 => n: i32 = 0,
    }
}

pe_struct! {
    /// Target vertices (id 12, rope / soft) *(inferred)*.
    ActionTargetVtx: Action(id::TARGET_VTX, size 48) {
        4 => n: i32 = 0,
        8 => points: RawPtr = RawPtr::NULL,
        16 => pos_host: Vec3 = UV,
        28 => q_host: Quat = UQ,
    }
}

pe_struct! {
    /// Reset part matrix (id 13) *(inferred)*.
    ActionResetPartMtx: Action(id::RESET_PART_MTX, size 12) {
        4 => ipart: i32 = UI,
        8 => part_id: i32 = UI,
    }
}

pe_struct! {
    /// Auto part detachment (id 15) *(inferred)*.
    ActionAutoPartDetachment: Action(id::AUTO_PART_DETACHMENT, size 12) {
        4 => threshold: f32 = U,
        8 => auto_detachment_dist: f32 = U,
    }
}

pe_struct! {
    /// Move parts (id 16) *(inferred)*.
    ActionMoveParts: Action(id::MOVE_PARTS, size 72) {
        4 => id_start: i32 = 0,
        8 => id_end: i32 = 0,
        12 => id_offset: i32 = 0,
        16 => target: RawPtr = RawPtr::NULL,
        /// `Matrix34` (row-major 3×4).
        24 => mtx_rel: [f32; 12] = [1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0],
    }
}

pe_struct! {
    /// Set part poses (id 17, articulated) *(inferred)*.
    ActionSetPartPoses: Action(id::SET_PART_POSES, size 104) {
        8 => part_ids: RawPtr = RawPtr::NULL,
        16 => q: RawPtr = RawPtr::NULL,
        24 => q_stride: i32 = 16,
        32 => pos: RawPtr = RawPtr::NULL,
        40 => pos_stride: i32 = 12,
        48 => n: i32 = 0,
        56 => n_out: RawPtr = RawPtr::NULL,
        64 => q_offset: Quat = Quat::IDENTITY,
        80 => pos_offset: Vec3 = Vec3::ZERO,
        96 => validator: RawPtr = RawPtr::NULL,
    }
}

pe_struct! {
    /// Net sync (id 18, rigid; "CRigidEntity::Action() sync"). Re-issues SetParams(pos) and
    /// Action(set_velocity).
    ActionNetSync: Action(id::NET_SYNC, size 88) {
        8 => pos: Vec3 = UV,
        20 => vel: Vec3 = UV,
        /// Tested on `w` (+56).
        44 => rot: Quat = UQ,
        60 => ang_vel: Vec3 = UV,
        /// Smoothing (lerp 0.1) when set.
        74 => smooth: u8 = 0,
        76 => min_pos_diff: f32 = U,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ids::ACTION_SIZES;
    use crate::params::{layout_problems, PeStruct};

    fn check<T: PeStruct>() {
        let p = layout_problems::<T>();
        assert!(p.is_empty(), "{}: {p:?}", std::any::type_name::<T>());
        assert_eq!(
            T::SIZE as u32,
            ACTION_SIZES[T::TYPE_ID as usize],
            "{}",
            std::any::type_name::<T>()
        );
        let b = T::default().to_bytes();
        assert_eq!(T::read(&b).to_bytes(), b);
    }

    #[test]
    fn layouts() {
        check::<ActionMove>();
        check::<ActionImpulse>();
        check::<ActionDrive>();
        check::<ActionReset>();
        check::<ActionAddConstraint>();
        check::<ActionUpdateConstraint>();
        check::<ActionRegisterCollEvent>();
        check::<ActionAwake>();
        check::<ActionRemoveAllParts>();
        check::<ActionSetVelocity>();
        check::<ActionAttachPoints>();
        check::<ActionTargetVtx>();
        check::<ActionResetPartMtx>();
        check::<ActionAutoPartDetachment>();
        check::<ActionMoveParts>();
        check::<ActionSetPartPoses>();
        check::<ActionNetSync>();
    }
}
