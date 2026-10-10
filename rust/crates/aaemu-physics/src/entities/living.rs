//! **P5 — `CLivingEntity`** (type 4, 0x468 bytes; vtable 0x351EE1D8; ctor 0x35025530).
//! Characters, NPCs and mounts: a vertical cylinder/capsule collider that walks, jumps, falls,
//! swims and rides moving ground. `entities.md` §4.
//!
//! | slot | role | x64 | port |
//! |---|---|---|---|
//! | ctor | | 0x35025530 | [`construct`] |
//! | 7 | SetParams (1 `player_dimensions`, 4 `player_dynamics`) | 0x35028BF0 | [`set_params`] |
//! | 8 | GetParams | 0x35025040 | [`get_params`] |
//! | 9 | GetStatus (1, 2 living, 8, 9, 11, 21 check stance) | 0x35029A30 | [`get_status`] |
//! | 10 | Action (1 move, 2 impulse, 4 reset, 10 set velocity) | 0x3502A300 | [`action`] |
//! | 11 / 12 | AddGeometry / RemoveGeometry | 0x35024D30 / 0x35024D80 | [`add_geometry`], [`remove_geometry`] |
//! | 16 | snapshot re-attach | 0x3502C780 | [`reattach_ground`] |
//! | 24 | SyncFromNet | 0x3502AA60 | [`sync_from_net`] |
//! | 26 | StartStep | 0x35024DC0 | [`start_step`] |
//! | 34 | Step | 0x3502DED0 | [`step()`] ([`mod@step`]) |
//! | 37 | GetMaxTimeStep | 0x35025390 | [`get_max_time_step`] |
//! | 40 | RegisterContacts | 0x35027190 | [`register_contacts`] ([`solver`]) |
//! | 41 | Update | 0x3502C240 | [`update`] |
//! | 42 | CalcEnergy | 0x35026FB0 | [`calc_energy`] |
//! | 54 | detach hook | 0x35037CD0 | [`detach_hook`] |
//! | 55 | Awake | 0x35024F90 | [`awake`] |
//! | 59 | OnContact (clip velocities) | 0x350279B0 | [`solver::on_contact`] |
//! | 64 | GetRigidBody (proxy body) | 0x35027700 | [`solver::get_rigid_body`] |
//! | 65 | GetRigidBodyData | 0x35037D40 | [`solver::rigid_body_data`] |
//! | 67 / 68 | contact-matrix diagonal | 0x35037DB0 / 0x35037C60 | [`solver::add_mass_inv_3x3`] / [`solver::add_mass_inv_7`] |
//! | 69 | GetMassInv | 0x35037C50 | [`super::mass_inv`] |
//! | 75 | ComputeBBox | 0x35026BC0 | [`compute_bbox`] |
//!
//! Helpers: unprojection test 0x35027E10 ([`unproject::unproject_test`]), ground following
//! 0x3502B3F0 ([`ground::follow_ground`]), ground probe 0x3502CA20 ([`ground::ground_probe`]),
//! push-ground 0x35025DF0, airborne velocity 0x35027BB0, collision event 0x35026300, the
//! validators 0x35038380 / 0x35038670 and the network quantisers 0x350379E0 / 0x35037A90.
//!
//! **Pointers.** `pe_player_dynamics::pLivingEntToIgnore` (`+80`) is accepted as null/unused
//! only through the raw interface; [`set_ignore_entity`] sets it by entity.
//!
//! **Bug kept (x64 only):** the living contact array (`+1088`) is never allocated — the ctor
//! zeroes it and the append helper 0x35026A30 only appends to an existing array — so living
//! entities never record their sweep contacts, never AddCollider the hit entity, and slots 40/41
//! only see the ground-proxy contact.

// NaN-aware comparisons and float literals are written as in the binary (the negated
// comparisons keep its NaN behaviour; the literals are the decompile's single-precision
// constants).
#![allow(
    clippy::manual_range_contains,
    clippy::assign_op_pattern,
    clippy::collapsible_if,
    clippy::needless_late_init,
    clippy::approx_constant,
    clippy::neg_cmp_op_on_partial_ord,
    clippy::excessive_precision,
    clippy::float_cmp,
    clippy::too_many_lines,
    clippy::too_many_arguments
)]

pub mod ground;
pub mod solver;
pub mod step;
pub mod unproject;

pub use solver::{calc_energy, register_contacts, update};
pub use step::step;

use super::base::{Coords, Entity, EntityKind, Part};
use super::interface::{
    action_gate, caller, params_gate, rd_i32, rd_u32, rd_u64, wr_f32, wr_i32, wr_u32, wr_u64,
    wr_v3, Gate,
};
use crate::geometry::primitives::{CylinderPrim, Primitive, SpherePrim};
use crate::geometry::{Geometry, PhysGeometry};
use crate::math::{crt, Quat, Vec3};
use crate::params::GeomParams;
use crate::sentinel::UNUSED_I32 as UI;
use crate::solver::BodyId;
use crate::world::{EntIdx, World};
use std::sync::Arc;

/// `+952` living flags.
pub mod lflags {
    /// Flying (not on the ground).
    pub const FLYING: u32 = 0x1;
    /// Jump requested (action_move iJump 1/2, set_velocity).
    pub const JUMP: u32 = 0x2;
    /// Swimming.
    pub const SWIMMING: u32 = 0x4;
    /// Capsule collider (else cylinder).
    pub const CAPSULE: u32 = 0x8;
    /// Ignore action_move.
    pub const IGNORE_MOVE: u32 = 0x10;
    /// No locks / replay mode.
    pub const NO_LOCK: u32 = 0x20;
    /// Active (`pe_player_dynamics::bActive`).
    pub const ACTIVE: u32 = 0x40;
    /// Awake bit.
    pub const AWAKE: u32 = 0x80;
    /// Stuck.
    pub const STUCK: u32 = 0x100;
    /// A collision event was posted (GetStatus 9 reads and toggles it).
    pub const COLL_EVENT: u32 = 0x200;
}

/// A living contact record (80 bytes at `+1088`): never created in the x64 build (see the
/// module docs); kept for the slots that iterate it.
#[derive(Clone, Debug, Default)]
pub struct LivingContact {
    /// `+0` the other entity, `+8` its part index.
    pub ent: EntIdx,
    pub ipart: i32,
    /// `+12` contact point, `+24`, `+36` normal, `+48` penetration, `+52` centre.
    pub pt: Vec3,
    pub v24: Vec3,
    pub n: Vec3,
    pub depth: f32,
    pub center: Vec3,
    /// `+64` / `+72` the two solver contacts.
    pub solver: [crate::solver::ContactId; 2],
}

/// `CLivingEntity` members (`E+448 … E+0x468`).
#[derive(Clone, Debug)]
pub struct LivingData {
    /// `+448` "vel".
    pub vel: Vec3,
    /// `+460` "velRequested".
    pub vel_requested: Vec3,
    /// `+472` gravity.
    pub gravity: Vec3,
    /// `+484` ground normal.
    pub ground_normal: Vec3,
    /// `+496` displacement velocity of the last step (Δpos/dt).
    pub disp_vel: Vec3,
    /// `+508` ground material id of the step (reset each step).
    pub ground_mat_id: i32,
    /// `+512` dt cap for the next StartStep (action_move `+20`).
    pub dt_cap: f32,
    /// `+516` inertia, `+520` inertia when accelerating, `+524` air control, `+528` air
    /// resistance.
    pub inertia: f32,
    pub inertia_accel: f32,
    pub air_control: f32,
    pub air_resistance: f32,
    /// `+532` heightCollider.
    pub height_collider: f32,
    /// `+536` "heightEye".
    pub height_eye: f32,
    /// `+540` "heightPivot".
    pub height_pivot: f32,
    /// `+544` "sizeCollider": radius, radius, half-height.
    pub size: Vec3,
    /// `+556` eye/step smoothing offset, `+560` its velocity, `+564` acceleration, `+568`
    /// timer.
    pub eye_offset: f32,
    pub eye_vel: f32,
    pub eye_acc: f32,
    pub eye_timer: f32,
    /// `+572` ground height under the pivot.
    pub ground_height: f32,
    /// `+576` landing-nod speed.
    pub nod_speed: f32,
    /// `+580` mass, `+584` 1/mass.
    pub mass: f32,
    pub mass_inv: f32,
    /// `+588` surface index.
    pub surface_idx: i32,
    /// `+592`, `+596`, `+600` ground surface indices.
    pub ground_surface: [i32; 3],
    /// `+604` time flying.
    pub time_flying: f32,
    /// `+608` impulse-recover countdown.
    pub impulse_recover: f32,
    /// `+612 … +624` cos of min slide, max climb, max jump, min fall angles.
    pub cos_slide: f32,
    pub cos_climb: f32,
    pub cos_jump: f32,
    pub cos_fall: f32,
    /// `+628 … +640` the same angles in degrees.
    pub slide_deg: f32,
    pub climb_deg: f32,
    pub jump_deg: f32,
    pub fall_deg: f32,
    /// `+644` max ground velocity.
    pub max_ground_vel: f32,
    /// `+648` impulse recover time.
    pub impulse_recover_time: f32,
    /// `+656` the collider geometry (`CCapsuleGeom` / `CCylinderGeom`, 0xB0 bytes) and the
    /// `phys_geometry` wrapper at `+824` the part points at.
    pub collider: Arc<PhysGeometry>,
    /// `+664` heightHead.
    pub height_head: f32,
    /// `+672` the inline head `CSphereGeom` (radius `+732`).
    pub head: Arc<dyn Geometry>,
    pub head_r: f32,
    /// `+912` lock timer, `+916` time since the eye height changed, `+920` time since pushing
    /// another entity.
    pub lock_timer: f32,
    pub eye_change_time: f32,
    pub push_time: f32,
    /// `+924` / `+928` step dt requested / performed.
    pub step_full: f32,
    pub step_done: f32,
    /// `+932` world-frame change counter, `+936` last frame id.
    pub frame_changes: i32,
    pub last_frame: i32,
    /// `+940` collision types for the queries.
    pub coll_types: i32,
    /// `+944` entity to ignore (`None` = null).
    pub ignore: EntIdx,
    /// `+952` flags ([`lflags`]).
    pub flags: u32,
    /// `+956` recent collision counter (0..5).
    pub coll_count: i32,
    /// `+960` ground collider (ref-counted in the binary).
    pub ground: EntIdx,
    /// `+968` / `+972` ground part index / id.
    pub ground_ipart: i32,
    pub ground_partid: i32,
    /// `+976` position in the ground-part frame, `+988` its anchor, `+1000` z offset.
    pub ground_local: Vec3,
    pub ground_anchor: Vec3,
    pub ground_dz: f32,
    /// `+1004` ground velocity at the feet, `+1016` ground angular velocity.
    pub ground_vel: Vec3,
    pub ground_w: Vec3,
    /// `+1028` net position error, `+1040` net target position.
    pub net_err: Vec3,
    pub net_target: Vec3,
    /// `+1052` Δvel of the last step, `+1064` Δorientation, `+1080` last dt.
    pub dvel: Vec3,
    pub dq: Quat,
    pub last_dt: f32,
    /// `+1088` contact array (`None` in the x64 build, see the module docs), `+1096` count.
    pub contacts: Option<Vec<LivingContact>>,
    pub n_contacts: i32,
    /// `+1104` rigid-body proxy (slot 64).
    pub proxy: Option<BodyId>,
    /// `+1120` use velRequested as the raw velocity of the next step.
    pub use_raw_vel: bool,
}

/// `CCylinderGeom::CreateCylinder` 0x35144DB0 / `CCapsuleGeom::CreateCapsule` 0x35141920 for
/// the collider prim `{centre 0, axis (0,0,1), r, hh}`, wrapped like the `+824`
/// `phys_geometry` (filled by geometry slot 7, no inertia floor).
#[must_use]
pub fn make_collider(capsule: bool, r: f32, hh: f32) -> Arc<PhysGeometry> {
    let c = CylinderPrim {
        center: Vec3::ZERO,
        axis: Vec3::new(0.0, 0.0, 1.0),
        r,
        hh,
    };
    let prim = if capsule {
        Primitive::Capsule(c)
    } else {
        Primitive::Cylinder(c)
    };
    let geom = crate::world::geoman::create_primitive(&prim).expect("cylinder/capsule geometry");
    let pp = geom.calc_physical_properties();
    let (inertia, q, origin, volume) = pp
        .map_or((Vec3::ZERO, Quat::IDENTITY, Vec3::ZERO, 0.0), |p| {
            (p.inertia, p.q, p.origin, p.volume)
        });
    Arc::new(PhysGeometry {
        geom,
        inertia,
        q,
        origin,
        volume,
        surface_idx: -1,
        mat_mapping: Vec::new(),
    })
}

/// `CSphereGeom::CreateSphere` 0x351AE9E0 for the head sphere `{centre 0, r}`.
#[must_use]
pub fn make_head(r: f32) -> Arc<dyn Geometry> {
    crate::world::geoman::create_primitive(&Primitive::Sphere(SpherePrim {
        center: Vec3::ZERO,
        r,
    }))
    .expect("sphere geometry")
}

impl LivingData {
    /// The members as the ctor 0x35025530 leaves them; `gravity` is the world gravity (vars
    /// `+284`).
    #[must_use]
    pub fn new(gravity: Vec3) -> Self {
        let size = Vec3::new(
            f32::from_bits(0x3ECC_CCCD),
            f32::from_bits(0x3ECC_CCCD),
            f32::from_bits(0x3E4C_CCCD),
        );
        Self {
            vel: Vec3::ZERO,
            vel_requested: Vec3::ZERO,
            gravity,
            ground_normal: Vec3::new(0.0, 0.0, 1.0),
            disp_vel: Vec3::ZERO,
            ground_mat_id: 0,
            dt_cap: 0.0,
            inertia: 8.0,
            inertia_accel: 0.0,
            air_control: f32::from_bits(0x3DCC_CCCD),
            air_resistance: 0.0,
            height_collider: f32::from_bits(0x3F99_999A),
            height_eye: f32::from_bits(0x3FD9_999A),
            height_pivot: 0.0,
            size,
            eye_offset: 0.0,
            eye_vel: 0.0,
            eye_acc: 0.0,
            eye_timer: 1.0,
            ground_height: f32::from_bits(0xAEDB_E6FF),
            nod_speed: 60.0,
            mass: 80.0,
            mass_inv: f32::from_bits(0x3CCC_CCCD),
            surface_idx: 0,
            ground_surface: [-1; 3],
            time_flying: 0.0,
            impulse_recover: 0.0,
            // (float)cos(double) of 36°, 54°, 54° and 70.2° in radians
            cos_slide: crt::cos(0.628_318_530_717_958_6) as f32,
            cos_climb: crt::cos(0.942_477_862_655_965_2) as f32,
            cos_jump: crt::cos(0.942_477_862_655_965_2) as f32,
            cos_fall: crt::cos(1.225_221_081_636_797) as f32,
            slide_deg: 36.0,
            climb_deg: f32::from_bits(0x4258_0001),
            jump_deg: f32::from_bits(0x4258_0001),
            fall_deg: f32::from_bits(0x428C_6666),
            max_ground_vel: 10.0,
            impulse_recover_time: 5.0,
            collider: make_collider(true, size.x, size.z),
            height_head: f32::from_bits(0x3FF3_3333),
            head: make_head(0.0),
            head_r: 0.0,
            lock_timer: -1.0,
            eye_change_time: 0.0,
            push_time: 10.0,
            step_full: 0.0,
            step_done: 0.0,
            frame_changes: 0,
            last_frame: -1,
            coll_types: 271,
            ignore: EntIdx::NONE,
            flags: lflags::FLYING | lflags::CAPSULE | lflags::ACTIVE,
            coll_count: 0,
            ground: EntIdx::NONE,
            ground_ipart: 0,
            ground_partid: 0,
            ground_local: Vec3::ZERO,
            ground_anchor: Vec3::ZERO,
            ground_dz: 0.0,
            ground_vel: Vec3::ZERO,
            ground_w: Vec3::ZERO,
            net_err: Vec3::ZERO,
            net_target: Vec3::ZERO,
            dvel: Vec3::ZERO,
            dq: Quat::IDENTITY,
            last_dt: f32::from_bits(0x3E23_D70A),
            contacts: None,
            n_contacts: 0,
            proxy: None,
            use_raw_vel: false,
        }
    }

    /// The collider's part position `(0, 0, heightCollider − heightPivot)`.
    #[must_use]
    pub fn part_pos(&self) -> Vec3 {
        Vec3::new(0.0, 0.0, self.height_collider - self.height_pivot)
    }
}

/// The living payload of `e`.
#[inline]
#[must_use]
pub fn ld(w: &World, e: EntIdx) -> &LivingData {
    w.ent(e).living().expect("living entity")
}

/// The living payload of `e` (mutable).
#[inline]
pub fn lm(w: &mut World, e: EntIdx) -> &mut LivingData {
    w.ent_mut(e).living_mut().expect("living entity")
}

/// Constructor 0x35025530 (after the base ctor): simclass 3, entity flags `0x643`, the
/// capsule collider and its single part (id 100, flags `0x41FDFF`, collider flags 2,
/// minContactDist `size.z·0.1`).
pub fn construct(w: &World, ent: &mut Entity) {
    let ld = LivingData::new(w.gravity());
    let part = Part {
        pos: ld.part_pos(),
        q: Quat::IDENTITY,
        scale: 1.0,
        bbox: [Vec3::ZERO; 2],
        pgeom: ld.collider.clone(),
        pgeom_proxy: ld.collider.clone(),
        id: 100,
        mass: ld.mass,
        flags: 0x0041_FDFF,
        flags_collider: 2,
        max_dim: 0.0,
        min_contact_dist: ld.size.z * 0.1,
        // surface idx (+588) in bits 0-9, idmatBreakable -1
        surface_bits: (ld.surface_idx as u32 & 0x3FF) | 0x00FF_FC00,
        nmats_bits: 0,
        mat_mapping: None,
        has_lattice: false,
        unk128: 0,
        bbox_tmp: None,
    };
    ent.parts = vec![part];
    ent.parts_cap = 1;
    ent.set_sim_class(3);
    ent.flags = 0x643;
    ent.kind = EntityKind::Living(Box::new(ld));
}

/// Release the ground collider (`sub_35024CF0`).
pub fn release_ground(w: &mut World, e: EntIdx) {
    lm(w, e).ground = EntIdx::NONE;
}

/// `pLivingEntToIgnore` (`pe_player_dynamics +80`) by entity.
pub fn set_ignore_entity(w: &mut World, e: EntIdx, other: Option<EntIdx>) {
    lm(w, e).ignore = other.unwrap_or(EntIdx::NONE);
}

// ---------------------------------------------------------------------------------------------
// validators

/// `|x|²` check of the validators: `x·x ≥ 0 && x·x < 1e30`.
#[inline]
fn finite_sq(x: f32) -> bool {
    let s = x * x;
    s >= 0.0 && s < 1.0e30
}

/// `sub_35038380` (player_dimensions validator): heightPivot / heightEye finite, the size
/// vector finite with `|size|² ≤ 10000`. (The binary logs "Validation Error" lines.)
fn validate_dimensions(b: &[u8]) -> bool {
    let f = |o: usize| f32::from_bits(rd_u32(b, o));
    let unused = |o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let mut ok = true;
    if !unused(4) && !finite_sq(f(4)) {
        ok = false;
    }
    if !unused(8) && !finite_sq(f(8)) {
        ok = false;
    }
    if unused(12) {
        return ok;
    }
    let (x, y, z) = (f(12), f(16), f(20));
    let s = ((x * x) + y * y) + z * z;
    let s2 = s * s;
    let l = ((y * y) + x * x) + z * z;
    if s2 >= 0.0 && s2 < 1.0e30 && l >= 0.0 && l <= 10000.0 {
        return ok;
    }
    false
}

/// `sub_35038670` (action_move validator): `dir` finite with `|dir|² ≤ 1e6`, dt in `[0, 2]`.
fn validate_move(b: &[u8]) -> bool {
    let f = |o: usize| f32::from_bits(rd_u32(b, o));
    let unused = |o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let mut ok = true;
    if !unused(4) {
        let (x, y, z) = (f(4), f(8), f(12));
        let s = ((x * x) + y * y) + z * z;
        let s2 = s * s;
        let l = ((y * y) + x * x) + z * z;
        if s2 < 0.0 || s2 >= 1.0e30 || l < 0.0 || l > 1_000_000.0 {
            ok = false;
        }
    }
    if unused(20) {
        return ok;
    }
    let dt = f(20);
    if finite_sq(dt) && dt >= 0.0 && dt <= 2.0 {
        return ok;
    }
    false
}

/// `sub_35021B10` (action_impulse validator, shared with the soft entity): impulse,
/// angImpulse and point finite.
fn validate_impulse(b: &[u8]) -> bool {
    let f = |o: usize| f32::from_bits(rd_u32(b, o));
    let unused = |o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let vec_ok = |o: usize| {
        let (x, y, z) = (f(o), f(o + 4), f(o + 8));
        let s = ((x * x) + y * y) + z * z;
        let s2 = s * s;
        s2 >= 0.0 && s2 < 1.0e30
    };
    let mut ok = true;
    for o in [4usize, 16, 52] {
        if !unused(o) && !vec_ok(o) {
            ok = false;
        }
    }
    ok
}

// ---------------------------------------------------------------------------------------------
// network quantisers (entity flag 4)

/// `sub_350379E0`: velocity as 3 × int16 (`v·327.67` clamped to ±100, rounded with the
/// `12582912` trick) and back (`·0.0030518509`).
#[must_use]
pub fn quantize_vel(v: Vec3) -> Vec3 {
    let clamp = |x: f32| -> f32 {
        if x >= -100.0 {
            if x > 100.0 {
                100.0
            } else {
                x
            }
        } else {
            -100.0
        }
    };
    let (z, y) = (clamp(v.z), clamp(v.y));
    // x: `if (x < -100 || (t = x, x <= 100)) r = t` with t = -100 initially, else 100
    let x = if v.x < -100.0 {
        -100.0
    } else if v.x <= 100.0 {
        v.x
    } else {
        100.0
    };
    let q = |a: f32| -> f32 {
        let w = (a * 327.670_01_f32 + 12_582_912.0).to_bits() as u16 as i16;
        f32::from(w) * 0.003_051_850_9
    };
    Vec3::new(q(x), q(y), q(z))
}

/// `sub_35037A90` + the decode of action_move: velRequested as yaw int16, pitch int8 and
/// magnitude uint8 (|v| clamped to 20).
#[must_use]
pub fn quantize_vel_requested(v: Vec3) -> Vec3 {
    let mut packed: u32 = 0;
    let l2 = ((v.y * v.y) + v.x * v.x) + v.z * v.z;
    if l2 > 0.000_003_814_697_3 {
        let len = l2.sqrt();
        let yaw = crt::atan2f(v.y, v.x) * 10_430.060_040_584_27_f64 as f32;
        let yaw = (yaw + 12_582_912.0).to_bits() as u16;
        let s = ((v.z / len + 1.0) - (v.z / len - 1.0).abs()) * 0.5;
        let p = crt::asinf(((s - -1.0).abs() + (s - 1.0)) * 0.5) * 40.425_355_545_341_42_f64 as f32;
        let p = (p + 12_582_912.0).to_bits() as u8;
        let m =
            ((((len + 20.0) - (20.0 - len).abs()) * 0.5) * 12.75 + 12_582_912.0).to_bits() as u8;
        packed = u32::from(yaw) | (u32::from(p) << 16) | (u32::from(m) << 24);
    }
    let mag = f32::from((packed >> 24) as u8) * 0.078_431_375;
    let yaw = f64::from(packed as u16 as i16) * 0.000_095_876_725_168_303_27;
    let pitch = f64::from((packed >> 16) as u8 as i8) * 0.024_736_950_028_266_09;
    let c = crt::cosf(pitch as f32);
    let x = crt::cosf(yaw as f32) * c;
    let y = crt::sinf(yaw as f32) * c;
    let z = crt::sinf(pitch as f32);
    Vec3::new(x * mag, y * mag, z * mag)
}

// ---------------------------------------------------------------------------------------------
// slot 7: SetParams

/// Slot 7 `SetParams` (0x35028BF0): the base first (ids it handles; after `pe_params_pos`
/// without `bRecalcBounds & 0x20` a jump larger than `size.z·0.01` drops the ground collider,
/// sets flying and zeroes the velocity and eye offset, and the orientation is cleaned up and
/// renormalised), then `player_dimensions` (1) and `player_dynamics` (4). A deferred
/// `player_dimensions` with `bRequireCheck` (`+60`) runs the stance check on the caller's
/// thread and returns 0 when it fails.
pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    let ty = rd_i32(buf, 0);
    if let Gate::Skip { .. } = params_gate(w, e, buf, ts) {
        if ty == 1 && buf.len() >= 64 && w.ent(e).sim_class() < 7 && rd_i32(buf, 60) != 0 {
            return deferred_dimensions_check(w, e, buf);
        }
        return 1;
    }
    let pos0 = w.ent(e).pos;
    let r = super::interface::set::class_set_params(w, e, buf, 1);
    if r != 0 {
        if ty == 0 && buf.len() >= 64 && rd_u32(buf, 60) & 0x20 == 0 {
            let p = w.ent(e).pos;
            let (dx, dy, dz) = (pos0.x - p.x, pos0.y - p.y, pos0.z - p.z);
            let k = ld(w, e).size.z * 0.01;
            if (dx * dx + dy * dy) + dz * dz > k * k {
                release_ground(w, e);
                let l = lm(w, e);
                l.flags |= lflags::FLYING;
                l.vel = Vec3::ZERO;
                l.eye_offset = 0.0;
            }
            let ent = w.ent_mut(e);
            let mut q = ent.q;
            for c in [&mut q.x, &mut q.y, &mut q.z, &mut q.w] {
                if c.abs() < 1.0e-10 {
                    *c = 0.0;
                }
            }
            let n = ((((q.x * q.x) + q.w * q.w) + q.y * q.y) + q.z * q.z).sqrt();
            let inv = 1.0 / n;
            ent.q = Quat::new(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }
        return r;
    }
    match ty {
        1 if buf.len() >= 64 => set_dimensions(w, e, buf),
        4 if buf.len() >= 104 => {
            set_dynamics(w, e, buf);
            1
        }
        _ => 0,
    }
}

/// The deferred `player_dimensions` check (the `bThreadSafe` queue path of 0x35028BF0).
fn deferred_dimensions_check(w: &mut World, e: EntIdx, b: &[u8]) -> i32 {
    let f = |o: usize| f32::from_bits(rd_u32(b, o));
    let unused = |o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let cl = caller(w) as i32;
    let l = ld(w, e);
    let (hc0, pivot, size0, flags) = (l.height_collider, l.height_pivot, l.size, l.flags);
    let ent = w.ent(e);
    let (pos, q) = (ent.pos, ent.q);
    let size = if unused(12) {
        size0
    } else {
        Vec3::new(f(12), f(16), f(20))
    };
    let hc = if unused(24) { hc0 } else { f(24) };
    let mut v22 = 0.0f32;
    let mut n = 0;
    if (hc != hc0 || size.x != size0.x || size.z != size0.z) && flags & lflags::ACTIVE != 0 {
        let mut dir = Vec3::new(f(36), f(40), f(44));
        let max = f(48);
        let mut v25;
        loop {
            let p = Vec3::new(
                dir.x * v22 + pos.x,
                dir.y * v22 + pos.y,
                dir.z * v22 + pos.z,
            );
            v25 = unproject::unproject_test(
                w,
                e,
                p,
                q,
                hc,
                pivot,
                size,
                (flags >> 3) & 1 != 0,
                &mut dir,
                cl,
            );
            v22 += v25 * 1.01;
            if v22 >= max {
                break;
            }
            n += 1;
            if n >= 8 || !(v25 > 0.0) {
                break;
            }
        }
        if v22 > max || n == 8 {
            return 0;
        }
    }
    1
}

/// `player_dimensions` (id 1) on the execute path of 0x35028BF0. Unused fields of the caller's
/// struct are filled in from the entity (the binary writes them into the struct).
fn set_dimensions(w: &mut World, e: EntIdx, b: &mut [u8]) -> i32 {
    if !(validate_dimensions(b) || w.vars.validation_strict == 0) {
        return 0;
    }
    let unused = |b: &[u8], o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let f = |b: &[u8], o: usize| f32::from_bits(rd_u32(b, o));
    {
        let l = ld(w, e);
        let (size, hc, pivot, cap) = (l.size, l.height_collider, l.height_pivot, l.flags & 8 != 0);
        if unused(b, 12) {
            wr_v3(b, 12, size);
        }
        if unused(b, 24) {
            wr_f32(b, 24, hc);
        }
        if unused(b, 4) {
            wr_f32(b, 4, pivot);
        }
        if rd_i32(b, 52) == UI {
            wr_i32(b, 52, i32::from(cap));
        }
    }
    let changed = |w: &World, b: &[u8]| -> bool {
        let l = ld(w, e);
        l.height_collider != f(b, 24)
            || l.height_pivot != f(b, 4)
            || l.size.x != f(b, 12)
            || l.size.z != f(b, 20)
            || rd_i32(b, 52) != i32::from(l.flags & 8 != 0)
    };
    let mut v37 = 0.0f32;
    if changed(w, b) && ld(w, e).flags & lflags::ACTIVE != 0 {
        let mut v36 = 0;
        let cl = caller(w) as i32;
        let v44;
        loop {
            let hc = f(b, 24);
            let pos = w.ent(e).pos;
            let mut dir = Vec3::new(f(b, 36), f(b, 40), f(b, 44));
            let p = Vec3::new(
                dir.x * v37 + pos.x,
                dir.y * v37 + pos.y,
                dir.z * v37 + pos.z,
            );
            let q = w.ent(e).q;
            let size = Vec3::new(f(b, 12), f(b, 16), f(b, 20));
            let v43 = unproject::unproject_test(
                w,
                e,
                p,
                q,
                hc,
                f(b, 4),
                size,
                rd_i32(b, 52) != 0,
                &mut dir,
                cl,
            );
            wr_v3(b, 36, dir);
            v37 += v43 * 1.01;
            if v37 >= f(b, 48) {
                v44 = v37 <= f(b, 48);
                break;
            }
            v36 += 1;
            if v36 >= 8 || !(v43 > 0.0) {
                v44 = v37 <= f(b, 48);
                break;
            }
        }
        if !v44 || v36 == 8 {
            if rd_i32(b, 56) != 0 {
                wr_i32(b, 60, 0);
                // the vtable SetParams with bThreadSafe = -1 (queued)
                set_params(w, e, b, -1);
            }
            return 0;
        }
    }
    {
        let d = Vec3::new(f(b, 36), f(b, 40), f(b, 44));
        let ent = w.ent_mut(e);
        let p = ent.pos;
        let y = d.y * v37 + p.y;
        ent.pos.x = d.x * v37 + p.x;
        let z = d.z * v37 + p.z;
        ent.pos.y = y;
        ent.pos.z = z;
    }
    if changed(w, b) {
        let cap = rd_i32(b, 52) != 0;
        let hc = f(b, 24);
        let (r, hh) = (f(b, 12), f(b, 20));
        let col = make_collider(cap, r, hh);
        {
            let l = lm(w, e);
            l.height_pivot = f(b, 4);
            l.flags = l.flags ^ ((l.flags ^ (8 * rd_i32(b, 52) as u32)) & 8);
            l.collider = col.clone();
            l.height_collider = hc;
            l.size = Vec3::new(r, r, ((hh - 0.001).abs() + (hh + 0.001)) * 0.5);
        }
        let pp = Vec3::new(0.0, 0.0, hc - ld(w, e).height_pivot);
        let ent = w.ent_mut(e);
        if let Some(p) = ent.parts.get_mut(0) {
            p.pgeom = col.clone();
            p.pgeom_proxy = col;
            p.pos = pp;
        }
    }
    if !unused(b, 8) {
        let l = lm(w, e);
        l.eye_change_time = 0.0;
        l.height_eye = f(b, 8);
    }
    if !unused(b, 32) {
        lm(w, e).height_head = f(b, 32);
    }
    if !unused(b, 28) {
        let r = f(b, 28);
        let l = lm(w, e);
        l.head = make_head(r);
        l.head_r = r;
    }
    let bb = super::base::compute_bbox(w, e, 2, -1, None);
    w.ent_mut(e).bbox = bb;
    w.reposition_entity(e, 1, None);
    1
}

/// `player_dynamics` (id 4).
fn set_dynamics(w: &mut World, e: EntIdx, b: &[u8]) {
    let unused = |o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let f = |o: usize| f32::from_bits(rd_u32(b, o));
    let surface = rd_i32(b, 44);
    let l = lm(w, e);
    if !unused(4) {
        l.inertia = f(4);
    }
    if !unused(8) {
        l.inertia_accel = f(8);
    }
    if !unused(12) {
        l.air_control = f(12);
    }
    if !unused(16) {
        l.air_resistance = f(16);
    }
    if unused(20) {
        if !unused(28) {
            l.gravity = Vec3::new(0.0, 0.0, -f(28));
        }
    } else {
        l.gravity = Vec3::new(f(20), f(24), f(28));
    }
    if !unused(32) {
        l.nod_speed = f(32);
    }
    let v65 = rd_i32(b, 36);
    if v65 != UI {
        l.flags &= !lflags::SWIMMING;
        l.flags |= 4 * (v65 as u32 & 1);
    }
    if !unused(40) {
        let m = f(40);
        l.mass = m;
        l.mass_inv = if m > 0.0 { 1.0 / m } else { 0.0 };
    }
    // the angles: (float)cos((double)(deg · π/180))
    let cosd = |d: f32| crt::cos(f64::from(d) * 0.017_453_292_519_943_3) as f32;
    if !unused(48) {
        l.slide_deg = f(48);
        l.cos_slide = cosd(f(48));
    }
    if !unused(52) {
        l.climb_deg = f(52);
        l.cos_climb = cosd(f(52));
    }
    if !unused(56) {
        l.jump_deg = f(56);
        l.cos_jump = cosd(f(56));
    }
    if !unused(60) {
        l.fall_deg = f(60);
        l.cos_fall = cosd(f(60));
    }
    if !unused(64) {
        l.max_ground_vel = f(64);
    }
    if !unused(68) {
        l.impulse_recover_time = f(68);
    }
    let v74 = rd_i32(b, 72);
    if v74 != UI {
        l.coll_types = v74;
    }
    let p = rd_u64(b, 80);
    if p != u64::MAX {
        // only "no entity" is representable through the raw interface (see the module docs)
        l.ignore = EntIdx::NONE;
    }
    let v76 = rd_i32(b, 92);
    if v76 != UI {
        if i32::from(l.flags & lflags::ACTIVE != 0) + 2 * v76 == 2 {
            l.flags |= lflags::FLYING;
        }
        l.flags ^= (l.flags ^ ((v76 as u32) << 6)) & 0x40;
    }
    if surface != UI {
        let ent = w.ent_mut(e);
        if let Some(p) = ent.parts.get_mut(0) {
            p.surface_bits ^= (p.surface_bits ^ surface as u32) & 0x3FF;
            let s = ((p.surface_bits << 22) as i32) >> 22;
            lm(w, e).surface_idx = s;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// slot 8: GetParams

/// Slot 8 `GetParams` (0x35025040): the base first, then ids 1 and 4.
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let r = super::interface::get::class_get_params(w, e, buf);
    if r != 0 {
        return r;
    }
    let l = ld(w, e);
    match rd_i32(buf, 0) {
        1 if buf.len() >= 64 => {
            wr_f32(buf, 4, l.height_pivot);
            wr_v3(buf, 12, l.size);
            wr_f32(buf, 24, l.height_collider);
            wr_f32(buf, 8, l.height_eye);
            wr_f32(buf, 28, l.head_r);
            wr_f32(buf, 32, l.height_head);
            wr_i32(buf, 52, i32::from(l.flags & 8 != 0));
            1
        }
        4 if buf.len() >= 104 => {
            wr_f32(buf, 4, l.inertia);
            wr_f32(buf, 8, l.inertia_accel);
            wr_f32(buf, 12, l.air_control);
            wr_f32(buf, 16, l.air_resistance);
            wr_v3(buf, 20, l.gravity);
            wr_f32(buf, 32, l.nod_speed);
            wr_i32(buf, 36, i32::from(l.flags & 4 != 0));
            wr_f32(buf, 40, l.mass);
            wr_i32(buf, 44, l.surface_idx);
            wr_f32(buf, 48, l.slide_deg);
            wr_f32(buf, 52, l.climb_deg);
            wr_f32(buf, 56, l.jump_deg);
            wr_f32(buf, 60, l.fall_deg);
            wr_f32(buf, 64, l.max_ground_vel);
            wr_f32(buf, 68, l.impulse_recover_time);
            wr_i32(buf, 72, l.coll_types);
            wr_u64(buf, 80, 0);
            wr_i32(buf, 92, i32::from(l.flags & 0x40 != 0));
            1
        }
        _ => 0,
    }
}

// ---------------------------------------------------------------------------------------------
// slot 9: GetStatus

/// Slot 9 `GetStatus` (0x35029A30).
///
/// * 1 (pos): the base with `flags |= 2`, the position temporarily moved by the network
///   smoothing `err·(lastDt·3.3333333) − q·0` while active.
/// * 2 (living), 8 (dynamics; ω from the last Δq, `acosf`), 9 (collision-event bit, toggled),
///   11 (0), 21 (check stance: 1 when the collider needs no unprojection, `+60` = the
///   distance).
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    let ty = rd_i32(buf, 0);
    if ty == 1 {
        if buf.len() < 16 {
            return 0;
        }
        let fl = rd_u32(buf, 12);
        wr_u32(buf, 12, fl | 2);
        let saved = w.ent(e).pos;
        if ld(w, e).flags & lflags::ACTIVE != 0 {
            let l = ld(w, e);
            let k = l.last_dt * f32::from_bits(0x4055_5555);
            let (a, b2, c) = (l.net_err.x * k, l.net_err.y * k, l.net_err.z * k);
            let r = w.ent(e).q.rotate(Vec3::ZERO);
            let ent = w.ent_mut(e);
            ent.pos.x = (a - r.x) + ent.pos.x;
            let y = (b2 - r.y) + ent.pos.y;
            ent.pos.z = (c - r.z) + ent.pos.z;
            ent.pos.y = y;
        }
        let res = super::interface::status::class_get_status(w, e, buf);
        w.ent_mut(e).pos = saved;
        wr_u32(buf, 12, fl);
        return res;
    }
    if super::interface::status::class_get_status(w, e, buf) != 0 {
        return 1;
    }
    match ty {
        2 if buf.len() >= 168 => {
            let l = ld(w, e);
            let q = w.ent(e).q;
            wr_i32(
                buf,
                4,
                ((l.flags & 0xFF) & u32::from(l.flags & 0x40 != 0)) as i32,
            );
            wr_f32(buf, 8, l.time_flying);
            let eye = q.rotate(Vec3::new(0.0, 0.0, -l.eye_offset));
            wr_v3(buf, 12, eye);
            let mut v = l.vel;
            if !l.ground.is_none() {
                v = Vec3::new(
                    l.ground_vel.x + v.x,
                    l.ground_vel.y + v.y,
                    l.ground_vel.z + v.z,
                );
            }
            wr_v3(buf, 24, v);
            wr_v3(buf, 36, l.vel);
            wr_v3(buf, 48, l.vel_requested);
            wr_v3(buf, 60, l.ground_vel);
            wr_v3(buf, 72, l.disp_vel);
            wr_i32(buf, 84, l.ground_mat_id);
            wr_f32(buf, 88, l.ground_height);
            wr_v3(buf, 92, l.ground_normal);
            wr_i32(buf, 104, l.ground_surface[0]);
            wr_i32(buf, 108, l.ground_surface[1]);
            wr_u64(
                buf,
                112,
                if l.ground.is_none() {
                    0
                } else {
                    u64::from(l.ground.0) + 1
                },
            );
            wr_i32(buf, 120, l.ground_ipart);
            wr_i32(buf, 124, l.ground_partid);
            wr_v3(buf, 128, l.ground_local);
            wr_f32(buf, 140, l.eye_change_time);
            wr_i32(buf, 144, i32::from(l.flags & 0x100 != 0));
            wr_u64(buf, 152, 0);
            wr_i32(buf, 164, l.coll_count);
            1
        }
        8 if buf.len() >= 116 => {
            let l = ld(w, e);
            let mut v = l.vel;
            if !l.ground.is_none() {
                v = Vec3::new(
                    l.ground_vel.x + v.x,
                    l.ground_vel.y + v.y,
                    l.ground_vel.z + v.z,
                );
            }
            wr_v3(buf, 12, v);
            let dq = l.dq;
            let inv = 1.0 / l.last_dt;
            wr_v3(
                buf,
                36,
                Vec3::new(l.dvel.x * inv, l.dvel.y * inv, l.dvel.z * inv),
            );
            let n2 = ((dq.x * dq.x) + dq.y * dq.y) + dq.z * dq.z;
            let wv = if n2 >= 0.002_500_000_2 {
                let c = ((dq.w + 1.0) - (dq.w - 1.0).abs()) * 0.5;
                let ang = crt::acosf(((c - -1.0).abs() + (c - 1.0)) * 0.5);
                let (ax, ay, az) = if n2 <= 9.999_999_7e-21 {
                    (0.0, 0.0, 1.0)
                } else {
                    let r = 1.0 / n2.sqrt();
                    (r * dq.x, dq.y * r, dq.z * r)
                };
                let k = (ang * 2.0) * inv;
                Vec3::new(k * ax, ay * k, az * k)
            } else {
                let k = inv * 2.0;
                Vec3::new(k * dq.x, dq.y * k, dq.z * k)
            };
            wr_v3(buf, 24, wv);
            let bb = w.ent(e).bbox;
            wr_v3(
                buf,
                60,
                Vec3::new(
                    (bb[1].x + bb[0].x) * 0.5,
                    (bb[0].y + bb[1].y) * 0.5,
                    (bb[0].z + bb[1].z) * 0.5,
                ),
            );
            wr_f32(buf, 112, l.mass);
            1
        }
        11 => 0,
        9 => {
            let l = lm(w, e);
            let r = i32::from(l.flags & 0x200 != 0);
            let f = l.flags;
            l.flags ^= (f ^ ((((f >> 9) as u16).wrapping_sub(1) as u32) << 9)) & 0x200;
            r
        }
        21 if buf.len() >= 72 => {
            let f = |o: usize| f32::from_bits(rd_u32(buf, o));
            let unused = |o: usize| rd_u32(buf, o) & 0xFFA0_0000 == 0xFFA0_0000;
            let l = ld(w, e);
            let ent = w.ent(e);
            let mut pos = ent.pos;
            let mut q = ent.q;
            let mut size = l.size;
            let mut hc = l.height_collider;
            let mut cap = l.flags & 8 != 0;
            let pivot = l.height_pivot;
            if !unused(4) {
                pos = Vec3::new(f(4), f(8), f(12));
            }
            if !unused(28) {
                q = Quat::new(f(16), f(20), f(24), f(28));
            }
            if !unused(32) {
                size = Vec3::new(f(32), f(36), f(40));
            }
            if !unused(44) {
                hc = f(44);
            }
            if rd_i32(buf, 64) != UI {
                cap = rd_i32(buf, 64) != 0;
            }
            let cl = caller(w) as i32;
            let mut dir = Vec3::new(f(48), f(52), f(56));
            let t = unproject::unproject_test(w, e, pos, q, hc, pivot, size, cap, &mut dir, cl);
            wr_v3(buf, 48, dir);
            wr_f32(buf, 60, t);
            // `ucomiss; jne`: a NaN distance counts as "free" (no parity check)
            i32::from(t == 0.0 || t.is_nan())
        }
        _ => 0,
    }
}

// ---------------------------------------------------------------------------------------------
// slot 10: Action

/// Slot 10 `Action` (0x3502A300): 1 `action_move`, 2 `action_impulse`, 4 reset, 10 set
/// velocity; others go to the base.
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    if let Gate::Skip { .. } = action_gate(w, e, buf, ts) {
        return 1;
    }
    let f = |b: &[u8], o: usize| f32::from_bits(rd_u32(b, o));
    let unused = |b: &[u8], o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    match rd_i32(buf, 0) {
        1 if buf.len() >= 24 => {
            if ld(w, e).flags & lflags::IGNORE_MOVE != 0
                || !(validate_move(buf) || w.vars.validation_strict == 0)
            {
                return 0;
            }
            let ent_flags = w.ent(e).flags;
            if !unused(buf, 4) {
                let ijump = rd_i32(buf, 16);
                let dir = Vec3::new(f(buf, 4), f(buf, 8), f(buf, 12));
                let l = lm(w, e);
                if ijump != 3 {
                    l.use_raw_vel = false;
                }
                l.flags ^= (l.flags ^ (2 * ijump as u32)) & 2;
                if ijump == 0 || ijump == 3 {
                    l.vel_requested = dir;
                    if ijump == 3 {
                        l.use_raw_vel = true;
                    }
                } else if ijump == 2 {
                    l.vel.x = dir.x + l.vel.x;
                    let y = dir.y + l.vel.y;
                    l.vel.z = dir.z + l.vel.z;
                    l.vel.y = y;
                    l.use_raw_vel = false;
                    if dir.z > 1.0 {
                        l.flags |= lflags::JUMP;
                        l.time_flying = 0.0;
                    }
                } else {
                    l.vel = dir;
                    l.flags |= lflags::JUMP;
                    l.use_raw_vel = false;
                    l.time_flying = 0.0;
                }
                if ent_flags & 4 != 0 {
                    l.vel_requested = quantize_vel_requested(l.vel_requested);
                    l.vel = quantize_vel(l.vel);
                }
                l.dt_cap = f(buf, 20);
            }
            1
        }
        2 if buf.len() >= 80 => {
            if !(validate_impulse(buf) || w.vars.validation_strict == 0) {
                return 0;
            }
            let mut j = Vec3::new(f(buf, 4), f(buf, 8), f(buf, 12));
            if rd_i32(buf, 76) == 2 {
                j = Vec3::new(j.x * 0.300_000_01, j.y * 0.300_000_01, j.z * 0.300_000_01);
            }
            if !unused(buf, 28) {
                let m = ld(w, e).mass;
                j = Vec3::new(
                    j.x + f(buf, 28) * m,
                    j.y + f(buf, 32) * m,
                    j.z + f(buf, 36) * m,
                );
            }
            let ent_flags = w.ent(e).flags;
            if rd_i32(buf, 72) != 0 {
                let mi = ld(w, e).mass_inv;
                let mut mv = [0u8; 24];
                wr_i32(&mut mv, 0, 1);
                wr_f32(&mut mv, 4, mi * j.x);
                wr_f32(&mut mv, 8, j.y * mi);
                wr_f32(&mut mv, 12, mi * j.z);
                wr_i32(&mut mv, 16, 2);
                wr_f32(&mut mv, 20, 0.0);
                action(w, e, &mut mv, 1);
            } else {
                let l = lm(w, e);
                let mi = l.mass_inv;
                l.vel.x = j.x * mi + l.vel.x;
                let y = j.y * mi + l.vel.y;
                l.vel.z = mi * j.z + l.vel.z;
                l.vel.y = y;
                if ent_flags & 4 != 0 {
                    l.vel = quantize_vel(l.vel);
                }
                if j.z * l.mass_inv > 1.0 {
                    l.flags |= lflags::FLYING;
                    l.time_flying = 0.0;
                }
            }
            let l = lm(w, e);
            if l.inertia == 0.0 {
                l.impulse_recover = l.impulse_recover_time;
            }
            1
        }
        4 if buf.len() >= 8 => {
            let l = lm(w, e);
            l.vel = Vec3::ZERO;
            l.vel_requested = Vec3::ZERO;
            l.flags |= lflags::FLYING;
            l.eye_offset = 0.0;
            l.eye_vel = 0.0;
            l.eye_timer = 1.0;
            1
        }
        10 if buf.len() >= 40 => {
            if !unused(buf, 12) {
                let v = Vec3::new(f(buf, 12), f(buf, 16), f(buf, 20));
                let l = lm(w, e);
                l.vel = v;
                l.vel_requested = v;
                l.flags |= lflags::JUMP;
            }
            1
        }
        _ => super::interface::action::class_action(w, e, buf, 1),
    }
}

// ---------------------------------------------------------------------------------------------
// small slots

/// Slot 26 `StartStep(dt)` (0x35024DC0): a pending dt cap (`+512`) limits dt once; the step
/// accounting restarts and the contact count is cleared.
pub fn start_step(w: &mut World, e: EntIdx, dt: f32) {
    let l = lm(w, e);
    let mut dt = dt;
    let cap = l.dt_cap;
    if cap > 0.0 {
        if dt > cap {
            dt = cap;
        }
        l.dt_cap = 0.0;
    }
    l.step_full = dt;
    l.step_done = 0.0;
    l.n_contacts = 0;
}

/// Slot 37 `GetMaxTimeStep(dt)` (0x35025390): the remaining step time if it is at least
/// 0.001 and below `dt`, else `dt`.
#[must_use]
pub fn get_max_time_step(w: &World, e: EntIdx, dt: f32) -> f32 {
    let l = ld(w, e);
    let r = l.step_full - l.step_done;
    if r < 0.001 || r >= dt {
        return dt;
    }
    r
}

/// Slot 55 `Awake(bAwake)` (0x35024F90): drops a dead ground collider; the awake bit; asleep
/// zeroes vel, velRequested and gravity (**bug kept**: the binary clears `+472`, the gravity,
/// together with the velocities).
pub fn awake(w: &mut World, e: EntIdx, b: i32, _src: i32) -> i32 {
    let g = ld(w, e).ground;
    if !g.is_none() {
        let ge = w.ent(g);
        if ge.id_bits & 0xFF00_0000 == 0x0700_0000 || (ge.flags as i32) < 0 {
            release_ground(w, e);
        }
    }
    let l = lm(w, e);
    l.flags ^= (l.flags ^ ((b as u32) << 7)) & 0x80;
    if l.flags & 0x80 == 0 {
        l.vel = Vec3::ZERO;
        l.vel_requested = Vec3::ZERO;
        l.gravity = Vec3::ZERO;
    }
    1
}

/// Slot 54 (0x35037CD0): release the ground collider, then the base hook.
pub fn detach_hook(w: &mut World, e: EntIdx, mode: i32) {
    release_ground(w, e);
    super::class_detach_hook(w, e, mode);
}

/// Slot 11 `AddGeometry` (0x35024D30): clears part flags `0xA000` on part 0 before the base
/// AddGeometry and sets `0x400000` on the new last part.
pub fn add_geometry(
    w: &mut World,
    e: EntIdx,
    g: &Arc<PhysGeometry>,
    gp: &GeomParams,
    id: i32,
) -> i32 {
    if let Some(p) = w.ent_mut(e).parts.get_mut(0) {
        p.flags &= 0xFFFF_5FFF;
    }
    let r = super::base::geometry::base_add_geometry(w, e, g, gp, id);
    if let Some(p) = w.ent_mut(e).parts.last_mut() {
        p.flags |= 0x40_0000;
    }
    r
}

/// Slot 12 `RemoveGeometry(id)` (0x35024D80): the base removal, then part 0 gets flag
/// `0x8000` when one part is left.
pub fn remove_geometry(w: &mut World, e: EntIdx, id: i32) -> i32 {
    let ent = w.ent_mut(e);
    let r = match ent.parts.iter().position(|p| p.id == id) {
        Some(i) => {
            ent.parts.remove(i);
            1
        }
        None => 0,
    };
    let ent = w.ent_mut(e);
    if ent.parts.len() == 1 {
        ent.parts[0].flags |= 0x8000;
    }
    r
}

/// Slot 75 `ComputeBBox` (0x35026BC0): the base box, extended to contain the pivot point
/// `pos − q·(0, 0, heightPivot)` and, with a head sphere, the head centre
/// `pos + q·(0, 0, r + heightHead − heightPivot)`.
pub fn compute_bbox(
    w: &mut World,
    e: EntIdx,
    flags: u32,
    ipart: i32,
    coords: Option<Coords>,
) -> [Vec3; 2] {
    let mut b = super::base::bbox::base_compute_bbox(w, e, flags, ipart, coords);
    let c = coords.unwrap_or_else(|| w.ent(e).new_coords());
    let l = ld(w, e);
    let r = c.q.rotate(Vec3::new(0.0, 0.0, l.height_pivot));
    let p = Vec3::new(c.pos.x - r.x, c.pos.y - r.y, c.pos.z - r.z);
    let mn = |a: f32, v: f32| ((a + v) - (a - v).abs()) * 0.5;
    let mnr = |a: f32, v: f32| ((v + a) - (a - v).abs()) * 0.5;
    let mx = |a: f32, v: f32| ((a - v).abs() + (v + a)) * 0.5;
    b[0].x = mn(b[0].x, p.x);
    b[0].y = mnr(b[0].y, p.y);
    b[0].z = mnr(b[0].z, p.z);
    let y1 = mx(b[1].y, p.y);
    let z1 = ((b[1].z - p.z).abs() + (p.z + b[1].z)) * 0.5;
    b[1].x = ((b[1].x - p.x).abs() + (b[1].x + p.x)) * 0.5;
    b[1].y = y1;
    b[1].z = z1;
    if l.head_r > 0.0 {
        let hz = (l.head_r + l.height_head) - l.height_pivot;
        let r = c.q.rotate(Vec3::new(0.0, 0.0, hz));
        let h = Vec3::new(r.x + c.pos.x, r.y + c.pos.y, r.z + c.pos.z);
        b[0].x = ((b[0].x + h.x) - (b[0].x - h.x).abs()) * 0.5;
        b[0].y = mnr(b[0].y, h.y);
        b[0].z = mnr(b[0].z, h.z);
        let y1 = mx(b[1].y, h.y);
        let z1 = ((b[1].z - h.z).abs() + (h.z + b[1].z)) * 0.5;
        b[1].x = ((b[1].x - h.x).abs() + (b[1].x + h.x)) * 0.5;
        b[1].y = y1;
        b[1].z = z1;
    }
    b
}

/// The new bbox for a candidate position (`sub_35026EB0`): ComputeBBox at `{pos, q}` with the
/// part's coords redirected to a copy (flag 2 only for a single-part entity).
pub fn bbox_at(w: &mut World, e: EntIdx, pos: Vec3) -> [Vec3; 2] {
    let q = w.ent(e).q;
    let n = w.ent(e).parts.len() as i32;
    let flags = (((n - 2) >> 31) & 2) as u32;
    // part 0's new-coords pointer (+120) aims at a stack copy of the part: a per-part box
    // written with flag 2 lands in the copy, which commit_pos copies back
    if let Some(p) = w.ent_mut(e).parts.get_mut(0) {
        p.bbox_tmp = Some(p.bbox);
    }
    compute_bbox(w, e, flags, -1, Some(Coords { pos, q }))
}

/// `sub_350253C0`: commit a position computed by [`bbox_at`]: `pos`, and for a single part
/// its box from the stack copy and the entity bbox; with more parts the coords pointers are
/// reset and slot 75 recomputes the bbox with flag 2.
pub fn commit_pos(w: &mut World, e: EntIdx, pos: Vec3, bbox: [Vec3; 2]) {
    let ent = w.ent_mut(e);
    ent.pos = pos;
    if ent.parts.len() == 1 {
        let p = &mut ent.parts[0];
        if let Some(b) = p.bbox_tmp.take() {
            p.bbox = b;
        }
        ent.bbox = bbox;
    } else {
        for p in &mut ent.parts {
            p.bbox_tmp = None;
        }
        let b = compute_bbox(w, e, 2, -1, None);
        w.ent_mut(e).bbox = b;
    }
}

/// Slot 16 (0x3502C780): re-attach the ground collider while preserving pos, vel, q, the
/// flying bit, timeFlying, the lock timer, the ground normal and the ground-frame position;
/// counts world-frame changes (`+932`) when the world's net flag (`+328`) is set.
pub fn reattach_ground(w: &mut World, e: EntIdx) -> i32 {
    let g = ld(w, e).ground;
    release_ground(w, e);
    let frame = w.itime;
    if w.vars.env_byte_290 != 0 && frame != ld(w, e).last_frame {
        let l = lm(w, e);
        l.frame_changes += 1;
        l.last_frame = frame;
    }
    if !g.is_none() {
        ground::set_ground(w, e, g, 0);
    }
    1
}

/// Slot 24 `SyncFromNet(state)` (0x3502AA60), struct id 4: `+4` pos, `+16` vel, `+28`
/// velRequested, byte `+68` relative to the ground part, `+69` drop the ground collider,
/// `+70` snap, `+76` / `+80` ground part index / id. The position is corrected towards the
/// target by at most `c = clamp(min(|vel|·0.1, size.x·0.5), ≥ size.x·0.1)` per call (in the
/// ground-part frame in relative mode) and applied through `pe_params_pos` (flags `0x31`).
pub fn sync_from_net(w: &mut World, e: EntIdx, b: &mut [u8]) -> i32 {
    if b.len() < 84 || rd_i32(b, 0) != 4 {
        return 0;
    }
    let f = |b: &[u8], o: usize| f32::from_bits(rd_u32(b, o));
    let unused = |b: &[u8], o: usize| rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000;
    let v6 = if unused(b, 16) {
        0.0
    } else {
        ((f(b, 16) * f(b, 16)) + f(b, 20) * f(b, 20) + f(b, 24) * f(b, 24)).sqrt()
    };
    let v7 = ld(w, e).size.x;
    let a = v6 * 0.1;
    let c = v7 * 0.5;
    let v8 = ((a + c) - (a - c).abs()) * 0.5;
    let lo = v7 * 0.1;
    let v9 = ((v8 - lo).abs() + (v8 + lo)) * 0.5;
    let target = Vec3::new(f(b, 4), f(b, 8), f(b, 12));
    let correct = |cur: Vec3, t: Vec3| -> Vec3 {
        let d = ((cur.x - t.x) * (cur.x - t.x) + (cur.y - t.y) * (cur.y - t.y))
            + (cur.z - t.z) * (cur.z - t.z);
        let dist = d.sqrt();
        if dist <= v9 {
            let r = 1.0 / v9;
            Vec3::new(
                ((r * ((t.x - cur.x) * dist)) * 0.033) + cur.x,
                ((((t.y - cur.y) * dist) * r) * 0.033) + cur.y,
                ((((t.z - cur.z) * dist) * r) * 0.033) + cur.z,
            )
        } else {
            let (dx, dy, dz) = (cur.x - t.x, cur.y - t.y, cur.z - t.z);
            let r = 1.0 / ((((dx * dx) + dy * dy) + dz * dz) + f32::from_bits(0x0080_0000)).sqrt();
            Vec3::new(
                ((dx * r) * v9) + t.x,
                ((dy * r) * v9) + t.y,
                ((dz * r) * v9) + t.z,
            )
        }
    };
    let new_pos;
    if b[68] != 0 {
        let g = ld(w, e).ground;
        if g.is_none() {
            return 0;
        }
        let local = if b[70] != 0 {
            target
        } else {
            correct(ld(w, e).ground_local, target)
        };
        {
            let l = lm(w, e);
            l.ground_local = local;
            l.ground_anchor = local;
            l.ground_dz = 0.0;
        }
        let nparts = w.ent(g).parts.len() as i32;
        let ip = rd_i32(b, 76);
        let ipart = if ip != 0 && ip < nparts {
            let l = lm(w, e);
            l.ground_ipart = ip;
            l.ground_partid = rd_i32(b, 80);
            ip
        } else {
            ld(w, e).ground_ipart
        };
        let ge = w.ent(g);
        let part = &ge.parts[ipart as usize];
        let r = ge.q.rotate(part.pos);
        let base = Vec3::new(r.x + ge.pos.x, r.y + ge.pos.y, r.z + ge.pos.z);
        let qp = ge.q.product(part.q);
        let sc = part.scale;
        let lr = qp.rotate(ld(w, e).ground_local);
        let pv = w
            .ent(e)
            .q
            .rotate(Vec3::new(0.0, 0.0, ld(w, e).height_pivot));
        new_pos = Vec3::new(
            (lr.x * sc + base.x) + pv.x,
            (lr.y * sc + base.y) + pv.y,
            ((lr.z * sc) + base.z) + pv.z,
        );
        wr_v3(b, 4, new_pos);
    } else {
        if b[69] != 0 {
            release_ground(w, e);
        }
        new_pos = if b[70] != 0 {
            target
        } else {
            correct(w.ent(e).pos, target)
        };
    }
    // pe_params_pos {pos, flags 0x31 (bRecalcBounds)}, bThreadSafe -1
    let mut pp = [0u8; 72];
    for o in (4..40).step_by(4) {
        wr_u32(&mut pp, o, crate::sentinel::UNUSED_F32_BITS);
    }
    wr_i32(&mut pp, 0, 0);
    wr_v3(&mut pp, 4, new_pos);
    wr_i32(&mut pp, 56, UI);
    wr_i32(&mut pp, 60, 49);
    set_params(w, e, &mut pp, -1);
    if !unused(b, 16) && !unused(b, 20) {
        let l = lm(w, e);
        l.vel.x = f(b, 16);
        l.vel.y = f(b, 20);
        if !unused(b, 28) {
            l.vel_requested = Vec3::new(f(b, 28), f(b, 32), f(b, 36));
        }
        if !unused(b, 24) {
            l.vel.z = f(b, 24);
            l.vel_requested.z = f(b, 24);
        }
    }
    1
}

/// Entity-level helper for the world's awake-list job: `sub_3502B3F0` with dt = `+252`.
pub fn follow_ground(w: &mut World, e: EntIdx, dt: f32) {
    ground::follow_ground(w, e, dt);
}

/// The modelled words of a living entity (`E+off`, 448 ≤ off < 0x468), for `peek`
/// comparisons with the oracle; `None` for pointers and unmodelled words.
#[must_use]
pub fn member_word(l: &LivingData, off: usize) -> Option<u32> {
    let f = |x: f32| Some(x.to_bits());
    let v = |x: Vec3, o: usize| match o {
        0 => Some(x.x.to_bits()),
        4 => Some(x.y.to_bits()),
        _ => Some(x.z.to_bits()),
    };
    match off {
        448..=456 => v(l.vel, off - 448),
        460..=468 => v(l.vel_requested, off - 460),
        472..=480 => v(l.gravity, off - 472),
        484..=492 => v(l.ground_normal, off - 484),
        496..=504 => v(l.disp_vel, off - 496),
        508 => Some(l.ground_mat_id as u32),
        512 => f(l.dt_cap),
        516 => f(l.inertia),
        520 => f(l.inertia_accel),
        524 => f(l.air_control),
        528 => f(l.air_resistance),
        532 => f(l.height_collider),
        536 => f(l.height_eye),
        540 => f(l.height_pivot),
        544..=552 => v(l.size, off - 544),
        556 => f(l.eye_offset),
        560 => f(l.eye_vel),
        564 => f(l.eye_acc),
        568 => f(l.eye_timer),
        572 => f(l.ground_height),
        576 => f(l.nod_speed),
        580 => f(l.mass),
        584 => f(l.mass_inv),
        588 => Some(l.surface_idx as u32),
        592..=600 => Some(l.ground_surface[(off - 592) / 4] as u32),
        604 => f(l.time_flying),
        608 => f(l.impulse_recover),
        612 => f(l.cos_slide),
        616 => f(l.cos_climb),
        620 => f(l.cos_jump),
        624 => f(l.cos_fall),
        628 => f(l.slide_deg),
        632 => f(l.climb_deg),
        636 => f(l.jump_deg),
        640 => f(l.fall_deg),
        644 => f(l.max_ground_vel),
        648 => f(l.impulse_recover_time),
        664 => f(l.height_head),
        732 => f(l.head_r),
        912 => f(l.lock_timer),
        916 => f(l.eye_change_time),
        920 => f(l.push_time),
        924 => f(l.step_full),
        928 => f(l.step_done),
        932 => Some(l.frame_changes as u32),
        936 => Some(l.last_frame as u32),
        940 => Some(l.coll_types as u32),
        952 => Some(l.flags),
        956 => Some(l.coll_count as u32),
        968 => Some(l.ground_ipart as u32),
        972 => Some(l.ground_partid as u32),
        976..=984 => v(l.ground_local, off - 976),
        988..=996 => v(l.ground_anchor, off - 988),
        1000 => f(l.ground_dz),
        1004..=1012 => v(l.ground_vel, off - 1004),
        1016..=1024 => v(l.ground_w, off - 1016),
        1028..=1036 => v(l.net_err, off - 1028),
        1040..=1048 => v(l.net_target, off - 1040),
        1052..=1060 => v(l.dvel, off - 1052),
        1064..=1076 => f(l.dq.to_array()[(off - 1064) / 4]),
        1080 => f(l.last_dt),
        1096 => Some(l.n_contacts as u32),
        _ => None,
    }
}
