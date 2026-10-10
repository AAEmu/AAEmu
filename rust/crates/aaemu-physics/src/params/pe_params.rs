//! `pe_params_*` structs (SetParams / GetParams). x64 offsets.

use super::pe_struct;
use crate::ids::params_id as id;
use crate::math::Vec3;
use crate::sentinel::{
    RawPtr, UNUSED_F32 as U, UNUSED_I32 as UI, UNUSED_QUAT as UQ, UNUSED_VEC3 as UV,
};

pe_struct! {
    /// `pe_params_pos` (id 0, string "CPhysicalEntity:SetParams(pe_params_pos)"). Matrices, when
    /// given, are decomposed into pos/q/scale first (0x35135AB0).
    ParamsPos: Params(id::POS, size 72) {
        4 => pos: Vec3 = UV,
        /// `(x, y, z, w)`; tested on `w` (+28).
        16 => q: crate::math::Quat = UQ,
        32 => scale: f32 = U,
        /// `Matrix34*` (null = none).
        40 => mtx3x4: RawPtr = RawPtr::NULL,
        /// `Matrix33*` (null = none).
        48 => mtx3x3: RawPtr = RawPtr::NULL,
        /// New simulation class; only applied while the current one is <= 6.
        56 => sim_class: i32 = UI,
        /// Recompute the bbox and re-grid the entity. Default 1 *(inferred: what callers pass)*.
        60 => recalc_bounds: i32 = 1,
        /// *(inferred)* bEntGridUseOBB.
        64 => ent_grid_use_obb: u8 = 0,
    }
}

pe_struct! {
    /// `player_dimensions` (id 1, living; string "CLivingEntity:SetParams(player_dimensions)").
    ParamsPlayerDimensions: Params(id::PLAYER_DIMENSIONS, size 64) {
        4 => height_pivot: f32 = U,
        8 => height_eye: f32 = U,
        /// x = radius, z = half-height (stored as max(z, 0.001)); tested on `.x`.
        12 => size_collider: Vec3 = UV,
        /// *(inferred)* heightCollider.
        24 => height_collider: f32 = U,
        /// *(inferred)* head sphere radius.
        28 => head_radius: f32 = U,
        /// *(inferred)* heightHead.
        32 => height_head: f32 = U,
        /// *(inferred)* unprojection direction used when the size changes.
        36 => unproj_dir: Vec3 = UV,
        /// *(inferred)* max unprojection distance.
        48 => max_unproj: f32 = U,
        /// *(inferred)* 1 = capsule collider, 0 = cylinder.
        52 => use_capsule: i32 = UI,
        /// *(inferred)* apply even when unprojection fails.
        56 => apply_on_fail: i32 = 0,
        /// *(inferred)* require a collision check (deferred path).
        60 => require_check: i32 = 0,
    }
}

pe_struct! {
    /// Car params (id 2, wheeled) *(inferred names)*. RPM fields are converted by ×π/30 on Set.
    ParamsCar: Params(id::CAR, size 144) {
        4 => axle_friction: f32 = U,
        8 => engine_power: f32 = U,
        12 => unk_12: f32 = U,
        16 => max_steer: f32 = U,
        20 => engine_max_rpm: f32 = U,
        24 => brake_torque: f32 = U,
        28 => integration_type: i32 = UI,
        32 => max_time_step: f32 = U,
        36 => min_energy: f32 = U,
        40 => damping: f32 = U,
        44 => min_braking_friction: f32 = U,
        48 => max_braking_friction: f32 = U,
        52 => stabilizer: f32 = U,
        /// GetParams only: number of wheels.
        56 => n_wheels: i32 = UI,
        60 => engine_min_rpm: f32 = U,
        64 => engine_shift_up_rpm: f32 = U,
        68 => engine_shift_down_rpm: f32 = U,
        72 => engine_idle_rpm: f32 = U,
        76 => engine_start_rpm: f32 = U,
        80 => clutch_speed: f32 = U,
        84 => n_gears: i32 = UI,
        /// `float*` gear ratios (index 0 reverse, 1 neutral).
        88 => gear_ratios: RawPtr = RawPtr::UNUSED,
        96 => max_gear: i32 = UI,
        100 => min_gear: i32 = UI,
        104 => slip_threshold: f32 = U,
        108 => gear_dir_switch_rpm: f32 = U,
        112 => k_dyn_friction: f32 = U,
        116 => steer_track_neutral_turn: f32 = U,
        120 => unk_120: f32 = U,
        124 => unk_124: f32 = U,
        128 => unk_128: i32 = UI,
        132 => engine_braking: f32 = U,
        136 => rear_steer: i32 = UI,
    }
}

pe_struct! {
    /// `pe_params_particle` (id 3, string "CParticleEntity:SetParams(pe_params_particle)").
    ParamsParticle: Params(id::PARTICLE, size 168) {
        4 => flags: i32 = UI,
        8 => mass: f32 = U,
        12 => size: f32 = U,
        16 => thickness: f32 = U,
        20 => heading: Vec3 = UV,
        32 => velocity: f32 = U,
        36 => k_air_resistance: f32 = U,
        40 => k_water_resistance: f32 = U,
        44 => acc_thrust: f32 = U,
        48 => acc_lift: f32 = U,
        52 => surface_idx: i32 = UI,
        56 => wspin: Vec3 = UV,
        68 => gravity: Vec3 = UV,
        80 => water_gravity: Vec3 = UV,
        92 => normal: Vec3 = UV,
        104 => roll_axis: Vec3 = UV,
        116 => q0: crate::math::Quat = UQ,
        132 => min_bounce_vel: f32 = U,
        136 => min_vel: f32 = U,
        144 => collider_to_ignore: RawPtr = RawPtr::UNUSED,
        152 => coll_type_pierce: i32 = UI,
        156 => coll_types: i32 = UI,
        160 => area_check_period: i32 = UI,
    }
}

pe_struct! {
    /// Player dynamics (id 4, living) *(inferred names; fields at +76, +84..+91, +96..+103 not mapped)*.
    ParamsPlayerDynamics: Params(id::PLAYER_DYNAMICS, size 104) {
        4 => k_inertia: f32 = U,
        8 => k_inertia_accel: f32 = U,
        12 => k_air_control: f32 = U,
        16 => k_air_resistance: f32 = U,
        /// If unset but +28 is set, gravity = (0, 0, -value).
        20 => gravity: Vec3 = UV,
        32 => nod_speed: f32 = U,
        36 => swimming: i32 = UI,
        40 => mass: f32 = U,
        44 => surface_idx: i32 = UI,
        48 => min_slide_angle: f32 = U,
        52 => max_climb_angle: f32 = U,
        56 => max_jump_angle: f32 = U,
        60 => min_fall_angle: f32 = U,
        64 => max_vel_ground: f32 = U,
        68 => time_impulse_recover: f32 = U,
        72 => coll_types: i32 = UI,
        80 => living_entity_to_ignore: RawPtr = RawPtr::UNUSED,
        92 => active: i32 = UI,
    }
}

pe_struct! {
    /// `params_joint` (id 5, articulated; string "CArticulatedEntity:SetParams(params_joint)").
    ParamsJoint: Params(id::JOINT, size 208) {
        4 => flags: i32 = UI,
        8 => flags_pivot: i32 = UI,
        12 => pivot: Vec3 = UV,
        24 => q0: crate::math::Quat = UQ,
        /// `Matrix33*` converted to q0.
        40 => mtx0: RawPtr = RawPtr::NULL,
        48 => limits_lo: Vec3 = UV,
        60 => limits_hi: Vec3 = UV,
        72 => bounciness: Vec3 = UV,
        84 => ks: Vec3 = UV,
        96 => kd: Vec3 = UV,
        108 => qdashpot: Vec3 = UV,
        120 => kdashpot: Vec3 = UV,
        132 => q: Vec3 = UV,
        144 => qext: Vec3 = UV,
        156 => qtarget: Vec3 = UV,
        168 => op_parent: i32 = UI,
        172 => op_child: i32 = UI,
        176 => n_self_colliding_parts: i32 = 0,
        184 => self_colliding_parts: RawPtr = RawPtr::NULL,
        192 => no_update: i32 = 0,
        196 => anim_time_step: f32 = U,
        200 => ranim_time_step: f32 = U,
    }
}

pe_struct! {
    /// `pe_params_part` (id 6, string "CPhysicalEntity:SetParams(pe_params_part)"). With both
    /// `part_id` and `ipart` unused the flags/mass/material fields apply to **all** parts
    /// (filtered by `flags_cond`). Part flags become `flags_or | (flags & flags_and)`.
    ParamsPart: Params(id::PART, size 160) {
        4 => part_id: i32 = UI,
        8 => ipart: i32 = UI,
        /// bit 0: recompute the bbox; bit 1: wake entities in the box.
        12 => recalc_bbox: i32 = 1,
        16 => pos: Vec3 = UV,
        /// Tested on `w` (+40).
        28 => q: crate::math::Quat = UQ,
        44 => scale: f32 = U,
        48 => mtx3x4: RawPtr = RawPtr::NULL,
        56 => mtx3x3: RawPtr = RawPtr::NULL,
        /// Only parts with `flags & cond != 0` (unused = all).
        64 => flags_cond: i32 = UI,
        68 => flags_or: u32 = 0,
        72 => flags_and: u32 = u32::MAX,
        76 => flags_collider_or: u32 = 0,
        80 => flags_collider_and: u32 = u32::MAX,
        84 => mass: f32 = U,
        /// mass = scale³ · V · density.
        88 => density: f32 = U,
        92 => min_contact_dist: f32 = U,
        96 => phys_geom: RawPtr = RawPtr::UNUSED,
        104 => phys_geom_proxy: RawPtr = RawPtr::UNUSED,
        112 => idmat_breakable: i32 = UI,
        120 => lattice: RawPtr = RawPtr::UNUSED,
        128 => id_skeleton: i32 = UI,
        136 => mat_mapping: RawPtr = RawPtr::UNUSED,
        144 => n_mats: i32 = 0,
        /// GetParams: AddRef the returned geometries.
        152 => addref_geoms: i32 = 0,
        156 => id_parent: i32 = UI,
    }
}

pe_struct! {
    /// Id 7 (24 B): two vec3 arrays with a count *(inferred: sensors; no entity handler)*.
    ParamsSensors: Params(id::SENSORS, size 24) {
        4 => n: i32 = 0,
        8 => origins: RawPtr = RawPtr::NULL,
        16 => dirs: RawPtr = RawPtr::NULL,
    }
}

pe_struct! {
    /// Articulated body (id 8) *(inferred names)*.
    ParamsArticulatedBody: Params(id::ARTICULATED_BODY, size 176) {
        4 => grounded: i32 = UI,
        8 => unk_8: i32 = UI,
        12 => check_collisions: i32 = UI,
        16 => pivot: Vec3 = UV,
        28 => a: Vec3 = UV,
        40 => wa: Vec3 = UV,
        52 => w: Vec3 = UV,
        64 => v: Vec3 = UV,
        76 => scale_bounce_response: f32 = U,
        80 => apply_dqext: i32 = UI,
        84 => awake: i32 = UI,
        88 => host: RawPtr = RawPtr::UNUSED,
        96 => pos_host_pivot: Vec3 = UV,
        108 => q_host_pivot: crate::math::Quat = UQ,
        124 => unk_124: i32 = UI,
        128 => n_coll_lying_mode: i32 = UI,
        132 => gravity_lying_mode: Vec3 = UV,
        144 => damping_lying_mode: f32 = U,
        148 => min_energy_lying_mode: f32 = U,
        152 => sim_type: i32 = UI,
        156 => sim_type_lying_mode: i32 = UI,
        160 => unk_160: i32 = UI,
        164 => n_joints_alloc: i32 = UI,
        168 => unk_168: i32 = UI,
    }
}

pe_struct! {
    /// Outer entity (id 9) *(inferred)*.
    ParamsOuterEntity: Params(id::OUTER_ENTITY, size 24) {
        8 => outer_entity: RawPtr = RawPtr::UNUSED,
        16 => bounding_geometry: RawPtr = RawPtr::UNUSED,
    }
}

pe_struct! {
    /// Simulation params (id 10; rigid, rope, soft) *(inferred names)*.
    ParamsSimulation: Params(id::SIMULATION, size 84) {
        4 => sim_class: i32 = UI,
        8 => max_time_step: f32 = U,
        12 => min_energy: f32 = U,
        16 => damping: f32 = U,
        /// Also copied to `gravity_freefall` when that is unset.
        20 => gravity: Vec3 = UV,
        32 => damping_freefall: f32 = U,
        36 => gravity_freefall: Vec3 = UV,
        48 => max_rot_vel: f32 = U,
        52 => mass: f32 = U,
        56 => density: f32 = U,
        60 => max_logged_collisions: i32 = UI,
        64 => disable_pre_cg: i32 = UI,
        /// Constant angular acceleration (`L += I·α·dt`).
        68 => ang_accel: Vec3 = UV,
        /// Max friction (rigid +704, default 100).
        80 => max_friction: f32 = U,
    }
}

pe_struct! {
    /// Foreign data (id 11) *(inferred)*.
    ParamsForeignData: Params(id::FOREIGN_DATA, size 32) {
        8 => foreign_data: RawPtr = RawPtr::UNUSED,
        16 => i_foreign_data: i32 = UI,
        20 => i_foreign_flags: i32 = UI,
        24 => i_foreign_flags_and: i32 = UI,
        28 => i_foreign_flags_or: i32 = UI,
    }
}

pe_struct! {
    /// Buoyancy (id 12, 80 B). The same record is used on areas (copied to area+388) and on
    /// rigid entities; areas use the absolute fields, entities the `k*` multipliers
    /// (`k = value / reference`, reference = the global area's density/resistance, default 1000).
    ParamsBuoyancy: Params(id::BUOYANCY, size 80) {
        4 => water_density: f32 = U,
        8 => k_water_density: f32 = U,
        12 => water_damping: f32 = U,
        16 => water_resistance: f32 = U,
        20 => k_water_resistance: f32 = U,
        24 => water_flow: Vec3 = UV,
        /// *(inferred)* flow variance.
        36 => flow_variance: f32 = U,
        /// Water plane normal.
        40 => water_plane_n: Vec3 = UV,
        /// Water plane origin.
        52 => water_plane_origin: Vec3 = UV,
        /// Entity: min energy while floating.
        64 => water_emin: f32 = U,
        /// Entity: air resistance factor.
        68 => k_air_resistance: f32 = U,
        /// Medium (0 water, 1 air, 0..15 medium lists); >= 2 is ignored by buoyancy.
        72 => i_medium: i32 = UI,
        /// Entity: water-level offset (subtracted from the plane origin z).
        76 => water_level_offset: f32 = U,
    }
}

pe_struct! {
    /// Rope params (id 13) *(inferred names)*.
    ParamsRope: Params(id::ROPE, size 248) {
        4 => length: f32 = U,
        8 => mass: f32 = U,
        12 => coll_dist: f32 = U,
        16 => surface_idx: i32 = UI,
        20 => friction: f32 = U,
        24 => tie_force_limit: f32 = U,
        28 => solver_param: f32 = U,
        32 => stiffness_anim: f32 = U,
        36 => target_pose_blend: f32 = U,
        40 => host_follow: f32 = U,
        44 => stiffness_decay_anim: f32 = U,
        48 => damping_anim: f32 = U,
        52 => target_pose_active: i32 = UI,
        56 => wind: Vec3 = UV,
        68 => wind_variance: f32 = U,
        72 => air_resistance: f32 = U,
        76 => water_resistance: f32 = U,
        80 => density: f32 = U,
        84 => length_tol0: f32 = U,
        88 => length_tol1: f32 = U,
        92 => max_retie_dist: f32 = U,
        96 => tie_break_strength: f32 = U,
        100 => max_force: f32 = U,
        104 => contact_coeffs: [f32; 4] = [U; 4],
        120 => solver_iters: i32 = UI,
        124 => n_segments: i32 = UI,
        128 => coll_part_flags: i32 = UI,
        132 => min_vtx_gap: i32 = UI,
        136 => line: [Vec3; 2] = [UV; 2],
        160 => points: RawPtr = RawPtr::UNUSED,
        168 => points_stride: i32 = 12,
        176 => velocities: RawPtr = RawPtr::UNUSED,
        184 => velocities_stride: i32 = 12,
        192 => ent_tied_to: [RawPtr; 2] = [RawPtr::UNUSED; 2],
        208 => local_pt_tied: i32 = 0,
        212 => pt_tied_to: [Vec3; 2] = [UV; 2],
        236 => id_part_tied_to: [i32; 2] = [UI; 2],
    }
}

pe_struct! {
    /// `pe_params_bbox` (id 14, string "CPhysicalEntity::SetParams(pe_params_bbox)").
    ParamsBbox: Params(id::BBOX, size 28) {
        4 => bbox_min: Vec3 = UV,
        16 => bbox_max: Vec3 = UV,
    }
}

pe_struct! {
    /// Flags (id 15) *(inferred)*: applied in the order set, AND, OR.
    ParamsFlags: Params(id::FLAGS, size 16) {
        4 => flags: i32 = UI,
        8 => flags_or: i32 = UI,
        12 => flags_and: i32 = UI,
    }
}

pe_struct! {
    /// Wheel params (id 16, wheeled) *(inferred names)*.
    ParamsWheel: Params(id::WHEEL, size 80) {
        4 => i_wheel: i32 = UI,
        8 => driving: i32 = UI,
        12 => i_axle: i32 = UI,
        16 => can_brake: i32 = UI,
        20 => blocked: i32 = UI,
        24 => susp_len_max: f32 = U,
        28 => susp_len_initial: f32 = U,
        32 => unk_32: f32 = U,
        36 => min_friction: f32 = U,
        40 => max_friction: f32 = U,
        44 => lateral_friction0: f32 = U,
        48 => lateral_friction1: f32 = U,
        52 => surface_idx: i32 = UI,
        56 => ray_cast: i32 = UI,
        60 => k_stiffness: f32 = U,
        64 => k_damping: f32 = U,
        68 => unk_68: f32 = U,
        72 => torque_scale: f32 = U,
        76 => w: f32 = U,
    }
}

pe_struct! {
    /// Soft-body params (id 17) *(inferred names)*.
    ParamsSoftBody: Params(id::SOFTBODY, size 100) {
        4 => thickness: f32 = U,
        8 => friction: f32 = U,
        12 => ks_stretch: f32 = U,
        16 => kd_stretch: f32 = U,
        20 => ks_shape: f32 = U,
        24 => water_resistance: f32 = U,
        28 => air_resistance: f32 = U,
        32 => wind: Vec3 = UV,
        44 => wind_variance: f32 = U,
        48 => n_max_iters: i32 = UI,
        52 => accuracy: f32 = U,
        56 => impulse_scale: f32 = U,
        60 => explosion_scale: f32 = U,
        64 => unk_64: f32 = U,
        68 => max_explosion_impulse: f32 = U,
        72 => coll_types: i32 = UI,
        76 => unk_76: f32 = U,
        80 => volume_coeff: f32 = U,
        84 => pressure_coeff: f32 = U,
        88 => shape_restore: f32 = U,
        92 => restore_falloff: f32 = U,
        96 => min_restore_blend: f32 = U,
    }
}

pe_struct! {
    /// Area params (id 18; `CPhysArea::SetParams`).
    ParamsArea: Params(id::AREA, size 72) {
        4 => gravity: Vec3 = UV,
        /// Ellipsoid falloff extents (inverses stored too).
        16 => size: Vec3 = UV,
        28 => falloff0: f32 = U,
        /// 0 radial/falloff, 1 uniform, 2 axial.
        32 => uniform: i32 = UI,
        /// Evaluate through event 11 (game callback) instead of internally.
        36 => use_callback: i32 = UI,
        40 => damping: f32 = U,
        /// `IGeometry*`; -1 keeps the current one.
        48 => geometry: RawPtr = RawPtr::UNUSED,
        56 => center: Vec3 = UV,
        /// > 0 builds a 12-point circle (30° steps).
        68 => radius: f32 = U,
    }
}

pe_struct! {
    /// Id 19 (36 B): no handler found in the entity classes; layout unknown.
    ParamsUnknown19: Params(id::UNKNOWN_19, size 36) {}
}

pe_struct! {
    /// Ground plane (id 20) *(inferred)*: `i_plane < 0` clears all planes.
    ParamsGroundPlane: Params(id::GROUND_PLANE, size 32) {
        4 => i_plane: i32 = UI,
        8 => origin: Vec3 = UV,
        20 => n: Vec3 = UV,
    }
}

pe_struct! {
    /// Structural joint (id 21) *(inferred; field-name strings exist)*. Forces clamped to 1e15.
    ParamsStructuralJoint: Params(id::STRUCTURAL_JOINT, size 120) {
        4 => id: i32 = UI,
        /// -1 = only update the next-joint id; -2 = rebuild.
        8 => idx: i32 = UI,
        12 => replace_existing: i32 = 0,
        16 => part_id: [i32; 2] = [UI; 2],
        24 => pt: Vec3 = UV,
        36 => n: Vec3 = UV,
        48 => axisx: Vec3 = UV,
        60 => max_force_push: f32 = U,
        64 => max_force_pull: f32 = U,
        68 => max_force_shift: f32 = U,
        72 => max_torque_bend: f32 = U,
        76 => max_torque_twist: f32 = U,
        80 => limit_constr: Vec3 = UV,
        92 => breakable: i32 = UI,
        96 => constraint_ignores_collisions: i32 = UI,
        100 => direct_breaks_only: i32 = UI,
        104 => damping_constr: f32 = U,
        108 => sz_sensor: f32 = U,
        112 => broken: i32 = UI,
        116 => next_joint_id: i32 = UI,
    }
}

pe_struct! {
    /// Water manager params (id 22; world slot 90 `SetWaterManagerParams`); layout not mapped.
    ParamsWaterManager: Params(id::WATER_MANAGER, size 52) {}
}

pe_struct! {
    /// Timeout (id 23).
    ParamsTimeout: Params(id::TIMEOUT, size 12) {
        4 => time_idle: f32 = U,
        8 => max_time_idle: f32 = U,
    }
}

pe_struct! {
    /// Skeleton (id 24) *(inferred)*: builds soft-body params (id 17) for the skeleton entity.
    ParamsSkeleton: Params(id::SKELETON, size 48) {
        4 => part_id: i32 = UI,
        8 => ipart: i32 = UI,
        12 => stiffness: f32 = U,
        16 => thickness: f32 = U,
        20 => max_stretch: f32 = U,
        24 => max_impulse: f32 = U,
        28 => time_step: f32 = U,
        32 => n_steps: i32 = UI,
        36 => hardness: f32 = U,
        40 => explosion_scale: f32 = U,
        44 => reset: i32 = 0,
    }
}

pe_struct! {
    /// Per-part structural vectors (id 25) *(inferred)*.
    ParamsPartVelocity: Params(id::PART_VELOCITY, size 32) {
        4 => part_id: i32 = UI,
        8 => v0: Vec3 = UV,
        20 => v1: Vec3 = UV,
    }
}

pe_struct! {
    /// Byte flag (id 26) *(inferred)*, stored at entity +0x14C.
    ParamsByteFlag: Params(id::BYTE_FLAG, size 8) {
        4 => value: u8 = 0,
    }
}

pe_struct! {
    /// Soft-body ignore-part-id list (id 117, x64 10.x only): `int*` begin/end of a vector.
    ParamsIgnorePartIds: Params(id::IGNORE_PART_IDS, size 24) {
        8 => begin: RawPtr = RawPtr::NULL,
        16 => end: RawPtr = RawPtr::NULL,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::ids::PARAMS_SIZES;
    use crate::params::{layout_problems, PeStruct};

    fn check<T: PeStruct>() {
        let p = layout_problems::<T>();
        assert!(p.is_empty(), "{}: {p:?}", std::any::type_name::<T>());
        if (0..27).contains(&T::TYPE_ID) {
            assert_eq!(
                T::SIZE as u32,
                PARAMS_SIZES[T::TYPE_ID as usize],
                "{}",
                std::any::type_name::<T>()
            );
        }
        let d = T::default();
        let b = d.to_bytes();
        assert_eq!(&b[..4], &T::TYPE_ID.to_le_bytes());
        assert_eq!(
            T::read(&b).to_bytes(),
            b,
            "round trip {}",
            std::any::type_name::<T>()
        );
    }

    #[test]
    fn layouts() {
        check::<ParamsPos>();
        check::<ParamsPlayerDimensions>();
        check::<ParamsCar>();
        check::<ParamsParticle>();
        check::<ParamsPlayerDynamics>();
        check::<ParamsJoint>();
        check::<ParamsPart>();
        check::<ParamsSensors>();
        check::<ParamsArticulatedBody>();
        check::<ParamsOuterEntity>();
        check::<ParamsSimulation>();
        check::<ParamsForeignData>();
        check::<ParamsBuoyancy>();
        check::<ParamsRope>();
        check::<ParamsBbox>();
        check::<ParamsFlags>();
        check::<ParamsWheel>();
        check::<ParamsSoftBody>();
        check::<ParamsArea>();
        check::<ParamsUnknown19>();
        check::<ParamsGroundPlane>();
        check::<ParamsStructuralJoint>();
        check::<ParamsWaterManager>();
        check::<ParamsTimeout>();
        check::<ParamsSkeleton>();
        check::<ParamsPartVelocity>();
        check::<ParamsByteFlag>();
        check::<ParamsIgnorePartIds>();
    }

    #[test]
    fn buoyancy_image_matches_the_harness() {
        // The original harness built the entity-side record as: all floats unused, density and
        // resistance 1000, iMedium unused.
        let b = ParamsBuoyancy {
            water_density: 1000.0,
            water_resistance: 1000.0,
            ..Default::default()
        };
        let img = b.to_bytes();
        let word = |o: usize| u32::from_le_bytes(img[o..o + 4].try_into().unwrap());
        assert_eq!(word(0), 12);
        assert_eq!(word(4), 1000f32.to_bits());
        assert_eq!(word(8), 0xFFBF_FFFF);
        assert_eq!(word(72), 0x8000_0000);
        assert_eq!(word(76), 0xFFBF_FFFF);
    }
}
