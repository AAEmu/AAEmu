//! **P4a — `CRigidEntity`** (type 2, 0x4B0 bytes; vtable 0x351F14C8; ctor 0x350ACBC0).
//! `dynamics.md` §1–§2, §4; `entities.md` §3.
//!
//! The rigid payload ([`RigidData`]) holds the members at `E+448 … E+1196`; the embedded
//! `RigidBody` (`E+744`) lives in the world's body arena ([`crate::solver::BodyArena`]) and is
//! referenced by [`RigidData::body`]; the contact list (`E+536`) is a sentinel record in the
//! world's contact pool ([`crate::solver::ContactPool`]).

pub mod action;
pub mod awake;
pub mod buoyancy;
pub mod colliders;
pub mod contacts;
pub mod mass;
pub mod patch;
pub mod register;
pub mod step;
pub mod update;
pub mod velocity;

use crate::math::{Quat, Vec3};
use crate::solver::{BodyId, ContactId};
use crate::world::EntIdx;

/// `CRigidEntity` members (offsets `E+x`).
#[derive(Clone, Debug)]
pub struct RigidData {
    /// `+448` stepped (new) position.
    pub pos_new: Vec3,
    /// `+460` stepped orientation.
    pub q_new: Quat,
    /// `+476` / `+488` new bbox.
    pub bbox_new: [Vec3; 2],
    /// `+500` part-count threshold for per-part bboxes (1 000 000).
    pub part_bbox_threshold: i32,
    /// `+504` flags2: 0x1 swept CCD active, bits 1–8 saved nColliders, 0x200/0x400 solver
    /// state, 0x800 post-step zeroing, 0x1000 stepped back, 0x1E000 counters, 0x20000 CCD
    /// enabled, 0x3FC0000 "need GetPotentialColliders", 0x4000000 floats, 0x8000000
    /// disablePreCG, 0x10000000 flags-0x100 latch.
    pub flags2: u32,
    /// `+508` byte 0 awake; bits 8–12 sleep-frame counter; 13–16 sub-step counter; 17–21
    /// nLoggedColl; 22–26 maxLoggedColl.
    pub awake_bits: u32,
    /// `+512` low 10 bits.
    pub bits512: u32,
    /// `+520` per-collider first contact.
    pub collider_contacts: Vec<ContactId>,
    /// `+528` per-collider constraint masks.
    pub collider_masks: Vec<u64>,
    /// `+536` the contact list sentinel; `+552` count.
    pub contacts_head: ContactId,
    pub n_contacts: i32,
    /// `+576` constraints capacity, `+584` count.
    pub n_constraints: i32,
    /// `+580` awake-history shift register.
    pub awake_history: u32,
    /// `+588` / `+592` CCD: velocity along the thinnest axis / thickness along it.
    pub ccd_vel: f32,
    pub ccd_size: f32,
    /// `+596` / `+600` timeStepFull / timeStepPerformed.
    pub time_step_full: f32,
    pub time_step_performed: f32,
    /// `+604` last dt.
    pub last_dt: f32,
    /// `+608` minAwakeTime / sleep-prevention timer.
    pub min_awake_time: f32,
    /// `+612` forced dt for the next step.
    pub forced_dt: f32,
    /// `+616` gravity in contact, `+628` gravity in free fall.
    pub gravity: Vec3,
    pub gravity_freefall: Vec3,
    /// `+640` constant angular acceleration.
    pub ang_accel: Vec3,
    /// `+652` Emin, `+656` maxTimeStep.
    pub min_energy: f32,
    pub max_time_step: f32,
    /// `+660` / `+672` sleep accumulators.
    pub v_accum: Vec3,
    pub w_accum: Vec3,
    /// `+684` damping, `+688` dampingFreefall, `+692` water damping (per step).
    pub damping: f32,
    pub damping_freefall: f32,
    pub water_damping_eff: f32,
    /// `+696` maxw.
    pub max_w: f32,
    /// `+700` minFriction (0.1; 3.0 after a CCD unprojection).
    pub min_friction: f32,
    /// `+704` maxFriction.
    pub max_friction: f32,
    /// `+708` / `+720` v / w saved at sleep.
    pub v_sleep: Vec3,
    pub w_sleep: Vec3,
    /// `+736` ground contact for velocity transfer.
    pub ground_contact: ContactId,
    /// `+744` the body.
    pub body: BodyId,
    /// `+956` / `+968` deferred P / L.
    pub p_deferred: Vec3,
    pub l_deferred: Vec3,
    /// `+980` prev com, `+992` prev v, `+1004` prev w, `+1016` prev body q.
    pub prev_pos: Vec3,
    pub prev_v: Vec3,
    pub prev_w: Vec3,
    pub prev_q: Quat,
    /// `+1032` cached energy, `+1036` energy clamp.
    pub e_cached: f32,
    pub e_clamp: f32,
    /// `+1040`, `+1044`.
    pub unk1040: f32,
    pub unk1044: u32,
    /// `+1048` external force, `+1060` external torque.
    pub f_ext: Vec3,
    pub t_ext: Vec3,
    /// `+1072` pending position correction.
    pub pos_correction: Vec3,
    /// `+1096` stamp, `+1100` (1e10), `+1104` last step with external contact.
    pub stamp1096: i32,
    pub f1100: f32,
    pub last_ext_contact_step: i32,
    /// `+1120` waterDamping, `+1124` kwaterDensity, `+1128` kwaterResistance, `+1132` Emin in
    /// water, `+1136` submerged fraction, `+1140` kAirResistance.
    pub water_damping: f32,
    pub k_water_density: f32,
    pub k_water_resistance: f32,
    pub min_energy_water: f32,
    pub submerged_fraction: f32,
    pub k_air_resistance: f32,
    /// `+1144` next constraint id.
    pub next_constraint_id: i32,
    /// `+1156`, `+1168`.
    pub unk1156: i32,
    pub unk1168: i32,
    /// `+1176` pending net/queued impulse (slot 24), `+1184` flag.
    pub queued_impulse: Option<crate::params::ActionImpulse>,
    /// `+1192` water-level offset.
    pub water_level_offset: f32,
    /// `+1088` per-step logged collision slots (indices into the world's logged queue).
    pub coll_log: Vec<usize>,
    /// **P5**: the `CWheeledVehicleEntity` members (`E+1200 …`), `Some` for type 3.
    pub wheeled: Option<Box<super::wheeled::WheeledData>>,
}

impl RigidData {
    /// The members as the ctor 0x350ACBC0 leaves them. `gravity` is the world gravity
    /// (vars `+284`), or `(0, 0, -9.81)` without a world.
    #[must_use]
    pub fn new(gravity: Vec3, body: BodyId, contacts_head: ContactId) -> Self {
        Self {
            pos_new: Vec3::ZERO,
            q_new: Quat::IDENTITY,
            bbox_new: [Vec3::ZERO; 2],
            part_bbox_threshold: 1_000_000,
            // `&= 0xFBFE0000; |= 0x20000`, later `&= 0xFC03FFFF`, `&= 0xE7FFFFFF`.
            flags2: 0x20000,
            // byte 1 (awake), `&= 0xFFFFE0FF`, `&= 0x1FFFF`, `&= 0xFFFE1FFF`.
            awake_bits: 1,
            bits512: 0,
            collider_contacts: Vec::new(),
            collider_masks: Vec::new(),
            contacts_head,
            n_contacts: 0,
            n_constraints: 0,
            awake_history: 0,
            ccd_vel: 0.0,
            ccd_size: 0.0,
            time_step_full: 0.01,
            time_step_performed: 0.0,
            last_dt: 0.0,
            min_awake_time: 0.0,
            forced_dt: 0.0,
            gravity,
            gravity_freefall: gravity,
            ang_accel: Vec3::ZERO,
            min_energy: f32::from_bits(1_000_378_414), // 0.0049
            max_time_step: f32::from_bits(1_017_370_378), // 0.02
            v_accum: Vec3::ZERO,
            w_accum: Vec3::ZERO,
            damping: 0.0,
            damping_freefall: 0.1,
            water_damping_eff: 0.0,
            max_w: 20.0,
            min_friction: 0.1,
            max_friction: 100.0,
            v_sleep: Vec3::ZERO,
            w_sleep: Vec3::ZERO,
            ground_contact: ContactId::NONE,
            body,
            p_deferred: Vec3::ZERO,
            l_deferred: Vec3::ZERO,
            prev_pos: Vec3::ZERO,
            prev_v: Vec3::ZERO,
            prev_w: Vec3::ZERO,
            prev_q: Quat::IDENTITY,
            e_cached: 0.0,
            e_clamp: 0.0,
            unk1040: 0.0,
            unk1044: 0,
            f_ext: Vec3::ZERO,
            t_ext: Vec3::ZERO,
            pos_correction: Vec3::ZERO,
            stamp1096: -2,
            f1100: 1e10,
            last_ext_contact_step: -2,
            water_damping: 0.0,
            k_water_density: f32::from_bits(981_668_463), // 0.001
            k_water_resistance: f32::from_bits(981_668_463),
            min_energy_water: f32::from_bits(953_267_991), // 1e-4
            submerged_fraction: 0.0,
            k_air_resistance: f32::from_bits(1_028_443_341), // 0.05
            next_constraint_id: 1,
            unk1156: 0,
            unk1168: 0,
            queued_impulse: None,
            water_level_offset: 0.0,
            coll_log: Vec::new(),
            wheeled: None,
        }
    }

    /// Awake byte (`+508` byte 0).
    #[inline]
    #[must_use]
    pub fn awake(&self) -> bool {
        self.awake_bits & 0xFF != 0
    }

    /// Set the awake byte.
    #[inline]
    pub fn set_awake(&mut self, a: bool) {
        self.awake_bits = (self.awake_bits & !0xFF) | u32::from(a);
    }
}

/// Free helper for callers holding an [`EntIdx`].
#[inline]
#[must_use]
pub fn is_rigid(w: &crate::world::World, e: EntIdx) -> bool {
    w.ent(e).rigid().is_some()
}
