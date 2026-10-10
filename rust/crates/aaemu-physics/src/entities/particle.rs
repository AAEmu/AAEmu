//! **P5 — `CParticleEntity`** (type 5, 0x2A8 bytes; vtable 0x351EEE88; ctor 0x35039D40):
//! a point mass with a radius and a thickness, simulated as an "independent" entity
//! (simclass 4, stepped by the world's job 4). `entities.md` §5.
//!
//! | slot | function | address | port |
//! |---|---|---|---|
//! | ctor | | 0x35039D40 | [`construct`] |
//! | 7 | SetParams (`pe_params_particle`, id 3) | 0x3503B800 | [`set_params`] |
//! | — | params validator | 0x350409C0 | [`validate_params`] |
//! | — | "blank" `pe_params_particle` | 0x350408F0 | [`blank_params`] |
//! | 8 | GetParams | 0x350398D0 | [`get_params`] |
//! | 9 | GetStatus (ids 8, 9) | 0x3503A310 | [`get_status`] |
//! | 10 | Action (ids 2, 4, 10) | 0x3503A750 | [`action`] |
//! | 25 | DoStep(dt, iCaller) | 0x3503C210 | [`step`] (module `step`) |
//! | 26 | StartStep | 0x35039850 | [`start_step`] |
//! | 34 | Step → DoStep(dt, TLS index) | 0x350407E0 | [`step`] |
//! | 37 | GetMaxTimeStep | 0x35039CE0 | [`get_max_time_step`] |
//! | 54 | release the collider to ignore | 0x350406D0 | [`detach_hook`] |
//! | 55 | Awake | 0x35039B60 | [`awake`] |
//! | 56 | IsAwake | 0x35039BB0 | [`is_awake`] |
//! | 57 | RayTrace (box of the particle vs the ray) | 0x3503AB10 | [`ray_trace`] |
//! | 66 | GetMass | 0x350406C0 | [`get_mass`] |
//! | 75 | ComputeBBox (pos ± r) | 0x35040F10 | [`compute_bbox`] |
//! | — | QuatRotationArc | 0x35040F80 | [`crate::geometry::shapes::quat_rotation_arc`] (P1a) |
//!
//! Not ported: the snapshot slots 15–18 (serialisation), DrawHelperInformation (73), the
//! memory statistics (29), the destructor's collider release (the port never destroys
//! entity objects while a particle refers to them) and the water-splash handler
//! `sub_350FF0E0` (`world+20848`, a client object the zone server never installs).
//!
//! **Entity pointers.** `pe_params_particle::pColliderToIgnore` (+144) is an entity pointer.
//! The port encodes entities in byte images as [`ent_ptr`] (`ENT_PTR_TAG | index`); null and
//! `-1` keep their meaning. Any other pointer value makes SetParams(3) return 0 (not ported).
//! AddRef / Release of the referenced entity are the `+88` counter ([`Entity::pod_refs`]).

mod raytrace;
mod step;

pub use raytrace::ray_trace;

use super::base::{Coords, Entity, EntityKind};
use super::interface::{
    action_gate, params_gate, rd_i32, rd_u32, rd_u64, wr_f32, wr_i32, wr_u32, wr_u64, wr_v3, Gate,
};
use crate::geometry::shapes::orthogonal;
use crate::math::{Mat33, Quat, Vec3};
use crate::world::queries::RayHit;
use crate::world::{EntIdx, World, MAX_CALLERS};

/// Tag of the port's entity "pointers" in byte images (see the module docs).
pub const ENT_PTR_TAG: u64 = 0x7E57_0000_0000_0000;

/// The byte-image value standing for a pointer to entity `e` (`0` for [`EntIdx::NONE`]).
#[must_use]
pub fn ent_ptr(e: EntIdx) -> u64 {
    if e.is_none() {
        0
    } else {
        ENT_PTR_TAG | u64::from(e.0)
    }
}

/// Decode a byte-image entity pointer: `Some(NONE)` for null, `Some(e)` for [`ent_ptr`]
/// values, `None` for anything else (including `-1`).
#[must_use]
pub fn ptr_ent(p: u64) -> Option<EntIdx> {
    if p == 0 {
        Some(EntIdx::NONE)
    } else if p & 0xFFFF_0000_0000_0000 == ENT_PTR_TAG {
        Some(EntIdx((p & 0xFFFF_FFFF) as u32))
    } else {
        None
    }
}

/// `E+668` bit masks.
pub mod bits {
    /// Bits 0–7: the area-check period; bits 8–15: its countdown.
    pub const AREA_PERIOD: u32 = 0xFF;
    /// Bit 16: collided since the last GetStatus(9).
    pub const COLLIDED: u32 = 0x1_0000;
    /// Bits 17–19: bounce counter (set to 3 by a hit against the acceleration, −1 per step).
    pub const BOUNCES: u32 = 0xE_0000;
    /// Bits 20–21: state (0 asleep, 1 moving, 2 resting / sliding on a static surface).
    pub const STATE: u32 = 0x30_0000;
    /// Bits 22–25: the particle's pierceability (RWI flags).
    pub const PIERCE: u32 = 0x3C0_0000;
    /// Bit 26: contact (sliding) mode.
    pub const CONTACT: u32 = 0x400_0000;
}

/// Per-thread particle state (statics of the binary), kept in the world.
#[derive(Clone, Debug)]
pub struct ParticleTls {
    /// `0x3520B690 + 640·tid`: the RWI hit buffers of DoStep (8 hits per thread, zero
    /// initialised once on first use; their stale contents persist between steps).
    pub hits: [[RayHit; 8]; MAX_CALLERS],
    /// `0x3520B3A0 + 8·i`: the particle being stepped (set by DoStep for its `iCaller`,
    /// read by RayTrace through the TLS index).
    pub cur: [EntIdx; MAX_CALLERS],
}

impl Default for ParticleTls {
    fn default() -> Self {
        let z = RayHit {
            dist: 0.0,
            collider: EntIdx::NONE,
            ipart: 0,
            partid: 0,
            surface_idx: 0,
            idmat_org: 0,
            foreign_idx: 0,
            inode: 0,
            pt: Vec3::ZERO,
            n: Vec3::ZERO,
            terrain: 0,
            iprim: 0,
            next: 0,
        };
        Self {
            hits: [[z; 8]; MAX_CALLERS],
            cur: [EntIdx::NONE; MAX_CALLERS],
        }
    }
}

/// `CParticleEntity` members (`E+448 …`; `entities.md` §5.3.1).
#[derive(Clone, Debug, PartialEq)]
pub struct ParticleData {
    /// `+448` mass.
    pub mass: f32,
    /// `+452` radius (size / 2).
    pub size: f32,
    /// `+456` 1 / radius.
    pub size_inv: f32,
    /// `+460` half thickness.
    pub thickness: f32,
    /// `+464` air resistance.
    pub k_air_resistance: f32,
    /// `+468` water resistance.
    pub k_water_resistance: f32,
    /// `+472` thrust acceleration (along the heading).
    pub acc_thrust: f32,
    /// `+476` lift coefficient (`accLift / |v|` at SetParams).
    pub k_lift: f32,
    /// `+480` gravity.
    pub gravity: Vec3,
    /// `+492` the gravity set through SetParams (restored when an area's gravity equals the
    /// world's).
    pub gravity0: Vec3,
    /// `+504` gravity under water.
    pub water_gravity: Vec3,
    /// `+516` roll axis (particle frame).
    pub roll_axis: Vec3,
    /// `+528` normal (particle frame).
    pub normal: Vec3,
    /// `+540` heading (unit velocity direction).
    pub heading: Vec3,
    /// `+552` velocity.
    pub vel: Vec3,
    /// `+564` angular velocity.
    pub w: Vec3,
    /// `+576` spin orientation.
    pub q0: Quat,
    /// `+592` minimal speed for bounces and impulses.
    pub min_bounce_vel: f32,
    /// `+596` sleep speed.
    pub min_vel: f32,
    /// `+600` surface index.
    pub surface_idx: i32,
    /// `+608` collider to ignore (`NONE` = null).
    pub collider_to_ignore: EntIdx,
    /// `+616` entity types collided with.
    pub coll_types: u32,
    /// `+620` contact normal.
    pub contact_normal: Vec3,
    /// `+632` (forced to 1 by the first step; never read).
    pub unk632: f32,
    /// `+636` water depth (< 0 = submerged).
    pub depth: f32,
    /// `+640` medium flow velocity.
    pub water_flow: Vec3,
    /// `+652` time stepped of the current world step.
    pub time_performed: f32,
    /// `+656` the world step (StartStep).
    pub time_full: f32,
    /// `+660` time in state 1.
    pub time_sliding: f32,
    /// `+664` time spent asleep.
    pub time_asleep: f32,
    /// `+668` state bits ([`bits`]).
    pub bits: u32,
}

impl Default for ParticleData {
    fn default() -> Self {
        Self {
            mass: 0.0,
            size: 0.0,
            size_inv: 0.0,
            thickness: 0.0,
            k_air_resistance: 0.0,
            k_water_resistance: 0.0,
            acc_thrust: 0.0,
            k_lift: 0.0,
            gravity: Vec3::ZERO,
            gravity0: Vec3::ZERO,
            water_gravity: Vec3::ZERO,
            roll_axis: Vec3::ZERO,
            normal: Vec3::ZERO,
            heading: Vec3::ZERO,
            vel: Vec3::ZERO,
            w: Vec3::ZERO,
            q0: Quat::IDENTITY,
            min_bounce_vel: 0.0,
            min_vel: 0.0,
            surface_idx: 0,
            collider_to_ignore: EntIdx::NONE,
            coll_types: 0,
            contact_normal: Vec3::ZERO,
            unk632: 0.0,
            depth: 0.0,
            water_flow: Vec3::ZERO,
            time_performed: 0.0,
            time_full: 0.0,
            time_sliding: 0.0,
            time_asleep: 0.0,
            bits: 0,
        }
    }
}

impl ParticleData {
    /// The state bits 20–21.
    #[inline]
    #[must_use]
    pub fn state(&self) -> u32 {
        (self.bits >> 20) & 3
    }
}

#[inline]
fn pd(w: &World, e: EntIdx) -> &ParticleData {
    w.ent(e).particle().expect("particle")
}

#[inline]
fn pdm(w: &mut World, e: EntIdx) -> &mut ParticleData {
    w.ent_mut(e).particle_mut().expect("particle")
}

/// `x & 0xFFA00000 == 0xFFA00000`: the "unused" test of float fields.
#[inline]
fn unused_bits(b: &[u8], o: usize) -> bool {
    rd_u32(b, o) & 0xFFA0_0000 == 0xFFA0_0000
}

#[inline]
fn rd_f(b: &[u8], o: usize) -> f32 {
    f32::from_bits(rd_u32(b, o))
}

#[inline]
fn rd_v(b: &[u8], o: usize) -> Vec3 {
    Vec3::new(rd_f(b, o), rd_f(b, o + 4), rd_f(b, o + 8))
}

/// `sub_350210A0`: `v · (1/sqrt(|v|²))`, `(0, 0, 1)` for `|v|² <= 1e-20`.
#[inline]
pub(crate) fn normalized_210a0(v: Vec3) -> Vec3 {
    let l2 = ((v.x * v.x) + (v.y * v.y)) + (v.z * v.z);
    if l2 <= f32::from_bits(0x1E3C_E508) {
        Vec3::new(0.0, 0.0, 1.0)
    } else {
        let inv = 1.0 / l2.sqrt();
        Vec3::new(v.x * inv, inv * v.y, inv * v.z)
    }
}

/// `sub_35021020`: normalise in place (`(0, 0, 1)` for `|v|² <= 1e-20`).
#[inline]
pub(crate) fn normalized_21020(v: Vec3) -> Vec3 {
    normalized_210a0(v)
}

/// `sub_35020600`: quaternion normalisation, `|q|² = ((x² + w²) + y²) + z²`.
#[inline]
pub(crate) fn quat_normalized_20600(q: Quat) -> Quat {
    let inv = 1.0 / q.len2_xwyz().sqrt();
    Quat::new(q.x * inv, q.y * inv, q.z * inv, q.w * inv)
}

/// The orientation basis of a particle: column 0 = `a` (normalised), column 1 = the
/// heading `h`, column 2 = `a × h` (row-major `[a.x, h.x, ·; a.y, h.y, ·; a.z, h.z, ·]`),
/// as SetParams and DoStep build it before `sub_35023EB0`.
fn heading_basis(a: Vec3, h: Vec3) -> Quat {
    let m = Mat33::from_array([
        a.x,
        h.x,
        (h.z * a.y) - (h.y * a.z),
        a.y,
        h.y,
        (h.x * a.z) - (h.z * a.x),
        a.z,
        h.z,
        (h.y * a.x) - (h.x * a.y),
    ]);
    Quat::from_mat33(&m)
}

// ---------------------------------------------------------------------------------------------
// construction

/// Constructor 0x35039D40 (on top of the base ctor): the defaults of `entities.md` §5.3.1,
/// flags 0 (not the base's 0x8640), simclass 4, hidden from the grid (cells -3), gravity =
/// the world gravity, water gravity = 0.8 · gravity, state 2, pierceability 15, area-check
/// period 6.
pub fn construct(w: &World, ent: &mut Entity) {
    let g = w.gravity();
    let k08 = f32::from_bits(0x3F4C_CCCD);
    let p = ParticleData {
        mass: f32::from_bits(0x3E4C_CCCD),
        size: f32::from_bits(0x3D4C_CCCD),
        size_inv: 20.0,
        thickness: f32::from_bits(0x3D4C_CCCD),
        k_air_resistance: 0.0,
        k_water_resistance: 0.5,
        acc_thrust: 0.0,
        k_lift: 0.0,
        gravity: g,
        gravity0: g,
        water_gravity: Vec3::new(g.x * k08, g.y * k08, g.z * k08),
        roll_axis: Vec3::new(0.0, 0.0, 1.0),
        normal: Vec3::new(0.0, 0.0, 1.0),
        heading: Vec3::new(1.0, 0.0, 0.0),
        vel: Vec3::ZERO,
        w: Vec3::ZERO,
        q0: Quat::IDENTITY,
        min_bounce_vel: 1.5,
        min_vel: f32::from_bits(0x3CA3_D70A),
        surface_idx: 0,
        collider_to_ignore: EntIdx::NONE,
        coll_types: 0x11F,
        contact_normal: Vec3::new(0.0, 0.0, 1.0),
        unk632: 0.0,
        depth: 0.0,
        water_flow: Vec3::ZERO,
        time_performed: 0.0,
        time_full: 0.0,
        time_sliding: 0.0,
        time_asleep: 0.0,
        // `&= ~0x4000000; |= 0x3C00000; & 0xFFC0FFFF | 0x200000; (word) = 6` (bits 27–31 are
        // uninitialised heap memory in the binary and never read)
        bits: 0x03E0_0006,
    };
    ent.kind = EntityKind::Particle(Box::new(p));
    ent.flags = 0;
    ent.set_sim_class(4);
    ent.grid_cells = [-3, -3, -3, -3];
}

// ---------------------------------------------------------------------------------------------
// params

/// `sub_350408F0`: a `pe_params_particle` with every field "unused" (what SetParams re-applies
/// after a base `pe_params_flags` call, so that the flag side effects run).
#[must_use]
pub fn blank_params() -> Vec<u8> {
    let mut b = vec![0u8; 168];
    wr_i32(&mut b, 0, 3);
    for o in [
        8, 12, 16, 56, 36, 40, 32, 20, 48, 44, 68, 80, 92, 128, 132, 104, 136,
    ] {
        wr_u32(&mut b, o, 0xFFBF_FFFF);
    }
    for o in [52, 4, 152, 160, 156] {
        wr_u32(&mut b, o, 0x8000_0000);
    }
    wr_u64(&mut b, 144, u64::MAX);
    b
}

/// The params validator `sub_350409C0` ("%s: (%.50s @ %.1f,%.1f,%.1f) Validation Error: %s is
/// invalid"): `mass`, `size`, `thickness` (squares in `[0, 1e30)`), `heading` and `normal`
/// (`|v|⁴` in `[0, 1e30)` and `||v|² − 1| < 0.01`), `q0` (same with the quaternion). Only
/// fields that are set are checked. The log lines are not reproduced.
#[must_use]
pub fn validate_params(b: &[u8]) -> bool {
    let mut ok = true;
    let lim = f32::from_bits(0x7149_F2CA); // 1e30
    let eps = f32::from_bits(0x3C23_D70A); // 0.01
    for o in [8usize, 12, 16] {
        if !unused_bits(b, o) {
            let x = rd_f(b, o);
            let s = x * x;
            if !(s >= 0.0 && s < lim) {
                ok = false;
            }
        }
    }
    for o in [20usize, 92] {
        if !unused_bits(b, o) {
            let x = rd_f(b, o);
            let y2 = rd_f(b, o + 4) * rd_f(b, o + 4);
            let z2 = rd_f(b, o + 8) * rd_f(b, o + 8);
            let l = ((x * x) + y2) + z2;
            let s = l * l;
            if !(s >= 0.0) || s >= lim || (l - 1.0).abs() >= eps {
                ok = false;
            }
        }
    }
    if !unused_bits(b, 128) {
        let x2 = rd_f(b, 116) * rd_f(b, 116);
        let z2 = rd_f(b, 124) * rd_f(b, 124);
        let w2 = rd_f(b, 128) * rd_f(b, 128);
        let y2 = rd_f(b, 120) * rd_f(b, 120);
        let l = ((y2 + x2) + z2) + w2;
        let s = l * l;
        if !(s >= 0.0 && s < lim && (l - 1.0).abs() < eps) {
            ok = false;
        }
    }
    ok
}

/// Slot 7 `SetParams` 0x3503B800: lock-or-queue; the base SetParams (`bThreadSafe = 1`)
/// first — when it handled the id the particle only re-applies [`blank_params`] after a
/// `pe_params_flags` (id 15); otherwise id 3 `pe_params_particle` (validated, see
/// [`validate_params`]; applied when valid or while `vars+312` is 0).
///
/// Id 3 in field order: mass, size (`r = size/2`, `1/r`, thickness = r — "Invalid Particle
/// size!!" is logged for 0), thickness, air/water resistance, thrust, lift (`accLift/|v|`,
/// needs the velocity field), heading, velocity (`v = heading·v`, a non-zero velocity
/// sets state 1), spin, gravity (also `gravity0`), water gravity, normal, roll axis, q0
/// (tested on `w`), bounce / sleep speeds, surface, flags (0x400 = traceable: grid
/// registration through RepositionEntity when `|pos|² > 0`, else removal), collider to
/// ignore (Release old, AddRef new), pierceability, collision types, area-check period
/// (written as a 16-bit word: the countdown restarts at 0). Then, unless flag 2, the
/// orientation (`q0` with flag 8, else `basis(normalize(gravity), heading) ⊗ q0`) and the
/// bbox `pos ± r`.
pub fn set_params(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    if buf.len() < 4 {
        return 0;
    }
    if let Gate::Skip { .. } = params_gate(w, e, buf, ts) {
        return 1;
    }
    let ty = rd_i32(buf, 0);
    let r = super::interface::set::class_set_params(w, e, buf, 1);
    if r != 0 {
        if ty == 15 {
            let mut b = blank_params();
            set_params(w, e, &mut b, 1);
        }
        return r;
    }
    if ty != 3 || buf.len() < 164 {
        return 0;
    }
    if !validate_params(buf) && w.vars.validation_strict != 0 {
        return 0;
    }
    let pcol = rd_u64(buf, 144);
    let new_ignore = if pcol == u64::MAX {
        None
    } else {
        match ptr_ent(pcol) {
            Some(x) if x.is_none() || w.ents.get(x.0 as usize).is_some_and(Option::is_some) => {
                Some(x)
            }
            _ => return 0, // a real pointer: not ported
        }
    };
    {
        let p = pdm(w, e);
        if !unused_bits(buf, 8) {
            p.mass = rd_f(buf, 8);
        }
        if !unused_bits(buf, 12) {
            // size == 0: X2Log(2, "Invalid Particle size!!")
            let v7 = rd_f(buf, 12) * 0.5;
            p.size = v7;
            p.size_inv = 1.0 / v7;
            p.thickness = rd_f(buf, 12) * 0.5;
        }
        if !unused_bits(buf, 16) {
            p.thickness = rd_f(buf, 16) * 0.5;
        }
        if !unused_bits(buf, 36) {
            p.k_air_resistance = rd_f(buf, 36);
        }
        if !unused_bits(buf, 40) {
            p.k_water_resistance = rd_f(buf, 40);
        }
        if !unused_bits(buf, 44) {
            p.acc_thrust = rd_f(buf, 44);
        }
        if !unused_bits(buf, 48) && !unused_bits(buf, 32) {
            let v8 = rd_f(buf, 32);
            p.k_lift = if v8 == 0.0 {
                0.0
            } else {
                rd_f(buf, 48) / v8.abs()
            };
        }
        if !unused_bits(buf, 20) {
            p.heading = rd_v(buf, 20);
        }
        if !unused_bits(buf, 32) {
            let v10 = rd_f(buf, 32);
            let h = p.heading;
            p.vel = Vec3::new(h.x * v10, h.y * v10, h.z * v10);
            let (a, b, c) = (h.x * v10, h.y * v10, h.z * v10);
            if ((a * a) + (b * b)) + (c * c) > 0.0 {
                p.bits &= !0x20_0000;
                p.bits |= 0x10_0000;
            }
        }
        if !unused_bits(buf, 56) {
            p.w = rd_v(buf, 56);
        }
        if !unused_bits(buf, 68) {
            p.gravity = rd_v(buf, 68);
            p.gravity0 = p.gravity;
        }
        if !unused_bits(buf, 80) {
            p.water_gravity = rd_v(buf, 80);
        }
        if !unused_bits(buf, 92) {
            p.normal = rd_v(buf, 92);
        }
        if !unused_bits(buf, 104) {
            p.roll_axis = rd_v(buf, 104);
        }
        if !unused_bits(buf, 128) {
            p.q0 = Quat::new(
                rd_f(buf, 116),
                rd_f(buf, 120),
                rd_f(buf, 124),
                rd_f(buf, 128),
            );
        }
        if !unused_bits(buf, 132) {
            p.min_bounce_vel = rd_f(buf, 132);
        }
        if !unused_bits(buf, 136) {
            p.min_vel = rd_f(buf, 136);
        }
        let v13 = rd_i32(buf, 52);
        if v13 != i32::MIN {
            p.surface_idx = v13;
        }
    }
    let v14 = rd_i32(buf, 4);
    if v14 != i32::MIN {
        w.ent_mut(e).flags = v14 as u32;
    }
    if w.ent(e).flags & 0x400 != 0 {
        if w.ent(e).grid_cells[0] == -3 {
            w.ent_mut(e).grid_cells = [-2, -2, -2, -2];
        }
        let pos = w.ent(e).pos;
        if ((pos.x * pos.x) + (pos.y * pos.y)) + (pos.z * pos.z) > 0.0 {
            // the returned grid-lock delta only releases the world's grid lock
            w.reposition_entity(e, 1, None);
        }
    } else if w.ent(e).grid_cells[0] != -3 {
        w.grid_detach(e);
        w.ent_mut(e).grid_cells = [-3, -3, -3, -3];
    }
    if let Some(n) = new_ignore {
        let old = pd(w, e).collider_to_ignore;
        if !old.is_none() {
            // Release (slot 3)
            if let Some(Some(o)) = w.ents.get_mut(old.0 as usize) {
                o.pod_refs -= 1;
            }
        }
        pdm(w, e).collider_to_ignore = n;
        if !n.is_none() {
            // AddRef (slot 2)
            w.ent_mut(n).pod_refs += 1;
        }
    }
    {
        let p = pdm(w, e);
        let v17 = rd_i32(buf, 152);
        if v17 != i32::MIN {
            p.bits &= 0xFC3F_FFFF;
            p.bits |= ((v17 & 0xF) as u32) << 22;
        }
        let v18 = rd_i32(buf, 156);
        if v18 != i32::MIN {
            p.coll_types = v18 as u32;
        }
        if rd_i32(buf, 160) != i32::MIN {
            p.bits = (p.bits & 0xFFFF_0000) | u32::from(buf[160]);
        }
    }
    let flags = w.ent(e).flags;
    if flags & 2 == 0 {
        let p = pd(w, e);
        let q = if flags & 8 != 0 {
            p.q0
        } else {
            let gn = normalized_210a0(p.gravity);
            let h = p.heading;
            let mut a = Vec3::new(
                (gn.y * h.z) - (gn.z * h.y),
                (gn.z * h.x) - (gn.x * h.z),
                (gn.x * h.y) - (gn.y * h.x),
            );
            if ((a.x * a.x) + (a.y * a.y)) + (a.z * a.z) < f32::from_bits(0x3C23_D70A) {
                a = orthogonal(h);
            }
            let a = normalized_21020(a);
            heading_basis(a, h).product(p.q0)
        };
        w.ent_mut(e).q = q;
    }
    let r = pd(w, e).size;
    let ent = w.ent_mut(e);
    let pos = ent.pos;
    ent.bbox = [
        Vec3::new(pos.x - r, pos.y - r, pos.z - r),
        Vec3::new(r + pos.x, r + pos.y, r + pos.z),
    ];
    1
}

/// Slot 8 `GetParams` 0x350398D0: the base first; id 3 writes back `pe_params_particle`
/// (size and thickness doubled, the velocity as `|v|`, lift as `k·|v|`; the water
/// resistance, water gravity and area-check period are not reported).
pub fn get_params(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    if super::interface::get::class_get_params(w, e, buf) != 0 {
        return 1;
    }
    if buf.len() < 160 || rd_i32(buf, 0) != 3 {
        return 0;
    }
    let flags = w.ent(e).flags;
    let p = pd(w, e).clone();
    wr_f32(buf, 8, p.mass);
    wr_f32(buf, 12, p.size * 2.0);
    wr_f32(buf, 16, p.thickness * 2.0);
    wr_v3(buf, 20, p.heading);
    let v = p.vel;
    let speed = (((v.x * v.x) + (v.y * v.y)) + (v.z * v.z)).sqrt();
    wr_f32(buf, 32, speed);
    wr_v3(buf, 56, p.w);
    wr_v3(buf, 68, p.gravity);
    wr_v3(buf, 92, p.normal);
    wr_v3(buf, 104, p.roll_axis);
    wr_f32(buf, 36, p.k_air_resistance);
    wr_f32(buf, 44, p.acc_thrust);
    wr_f32(buf, 48, p.k_lift * speed);
    for (i, x) in p.q0.to_array().into_iter().enumerate() {
        wr_f32(buf, 116 + 4 * i, x);
    }
    wr_i32(buf, 52, p.surface_idx);
    wr_u32(buf, 4, flags);
    wr_u64(buf, 144, ent_ptr(p.collider_to_ignore));
    wr_u32(buf, 152, (p.bits >> 22) & 0xF);
    wr_u32(buf, 156, p.coll_types);
    wr_f32(buf, 132, p.min_bounce_vel);
    wr_f32(buf, 136, p.min_vel);
    1
}

/// Slot 9 `GetStatus` 0x3503A310: the base first (any handled id returns 1); id 9 returns
/// and clears the "collided" bit; id 8 (`pe_status_dynamics`) reports `v`, `w`, the
/// acceleration `g + heading·thrust − v·k + lift·|v|` (water gravity / resistance while
/// submerged; the lift direction `h × (h × g)` from the air gravity), the centre of mass
/// (= pos) and the mass.
pub fn get_status(w: &mut World, e: EntIdx, buf: &mut [u8]) -> i32 {
    if super::interface::status::class_get_status(w, e, buf) != 0 {
        return 1;
    }
    match rd_i32(buf, 0) {
        9 => {
            let p = pdm(w, e);
            let v5 = (p.bits >> 16) & 1;
            if p.bits & 0x1_0000 != 0 {
                p.bits ^= (p.bits ^ (((p.bits >> 16).wrapping_sub(1)) << 16)) & 0x1_0000;
                v5 as i32
            } else {
                0
            }
        }
        8 if buf.len() >= 116 => {
            let pos = w.ent(e).pos;
            let p = pd(w, e);
            let (g, k) = if p.depth >= 0.0 {
                (p.gravity, p.k_air_resistance)
            } else {
                (p.water_gravity, p.k_water_resistance)
            };
            let (v, h, ga) = (p.vel, p.heading, p.gravity);
            let v10 = h.y;
            let v11 = h.z;
            let v12 = ga.y * v11;
            let v13 = ga.x;
            let v14 = v11 * v13;
            let v15 = (((v.y * v.y) + (v.x * v.x)) + (v.z * v.z)).sqrt();
            let v16 = ga.z * v10;
            let v17 = v10 * v13;
            let v18 = h.y;
            let v19 = v16 - v12;
            let v20 = v18 * v19;
            let v21 = v14 - (h.x * ga.z);
            let v22 = (h.x * ga.y) - v17;
            let v23 = (v18 * v22) - (h.z * v21);
            let v24 = h.x;
            let v25 = (v24 * v21) - v20;
            let v26 = (h.z * v19) - (v24 * v22);
            let v27 = ((v26 * v26) + (v23 * v23)) + (v25 * v25);
            let (v29, v30, v31) = if v27 <= f32::from_bits(0x1E3C_E508) {
                (0.0, 0.0, 1.0)
            } else {
                let v28 = 1.0 / v27.sqrt();
                (v23 * v28, v26 * v28, v25 * v28)
            };
            let v32 = p.k_lift;
            let (v33, v34, v35) = (v29 * v32, v30 * v32, v31 * v32);
            let v36 = p.acc_thrust;
            let v37 = v.y * k;
            let v38 = v.z * k;
            let a = Vec3::new(
                (((h.x * v36) + g.x) - (v.x * k)) + (v33 * v15),
                (((h.y * v36) + g.y) - v37) + (v34 * v15),
                (((h.z * v36) + g.z) - v38) + (v35 * v15),
            );
            let (pw, pm) = (p.w, p.mass);
            wr_v3(buf, 12, v);
            wr_v3(buf, 36, a);
            wr_v3(buf, 24, pw);
            wr_v3(buf, 60, pos);
            wr_f32(buf, 112, pm);
            1
        }
        _ => 0,
    }
}

/// Slot 10 `Action` 0x3503A750: lock-or-queue (a skipped / queued call returns 1); id 2
/// impulse (`v += P/m` for `m > 0`; unless flag 2 and when `m·r > 0`, `w += L / (0.4·m·r)`
/// with `L` = the angular impulse or `(point − pos) × P`); id 10 set velocity (`v`, heading
/// = normalised `v`; `w`); id 4 reset (`v = w = 0`); other ids go to the base Action.
pub fn action(w: &mut World, e: EntIdx, buf: &mut [u8], ts: i32) -> i32 {
    if buf.len() < 4 {
        return 0;
    }
    if let Gate::Skip { .. } = action_gate(w, e, buf, ts) {
        return 1;
    }
    match rd_i32(buf, 0) {
        2 if buf.len() >= 64 => {
            let pos = w.ent(e).pos;
            let flags = w.ent(e).flags;
            let imp = rd_v(buf, 4);
            let (v7, v8, v6);
            if unused_bits(buf, 16) {
                if unused_bits(buf, 52) {
                    (v7, v8, v6) = (0.0, 0.0, 0.0);
                } else {
                    let (v9, v10, v11) = (imp.z, imp.y, imp.x);
                    let v12 = rd_f(buf, 52) - pos.x;
                    let v13 = rd_f(buf, 56) - pos.y;
                    let v14 = rd_f(buf, 60) - pos.z;
                    let v15 = v10 * v14;
                    v6 = (v10 * v12) - (v11 * v13);
                    v7 = (v9 * v13) - v15;
                    v8 = (v11 * v14) - (v9 * v12);
                }
            } else {
                (v7, v8, v6) = (rd_f(buf, 16), rd_f(buf, 20), rd_f(buf, 24));
            }
            let p = pdm(w, e);
            let v16 = p.mass;
            if v16 > 0.0 {
                let v17 = (imp.y * (1.0 / v16)) + p.vel.y;
                let v18 = (imp.z * (1.0 / v16)) + p.vel.z;
                p.vel.x = (imp.x * (1.0 / v16)) + p.vel.x;
                p.vel.y = v17;
                p.vel.z = v18;
            }
            let v19 = p.mass;
            if (p.size * v19) > 0.0 && flags & 2 == 0 {
                let v20 = 1.0 / ((v19 * f32::from_bits(0x3ECC_CCCD)) * p.size);
                let v21 = (v6 * v20) + p.w.z;
                let v22 = (v20 * v7) + p.w.x;
                p.w.y = (v8 * v20) + p.w.y;
                p.w.z = v21;
                p.w.x = v22;
            }
            1
        }
        10 if buf.len() >= 36 => {
            let p = pdm(w, e);
            if !unused_bits(buf, 12) {
                p.vel = rd_v(buf, 12);
                p.heading = normalized_210a0(p.vel);
            }
            if !unused_bits(buf, 24) {
                p.w = rd_v(buf, 24);
            }
            1
        }
        4 => {
            let p = pdm(w, e);
            p.vel = Vec3::ZERO;
            p.w = Vec3::ZERO;
            1
        }
        _ => super::interface::action::class_action(w, e, buf, 1),
    }
}

/// Slot 75 `ComputeBBox` 0x35040F10: `pos ± r` (the entity position, whatever the coords).
pub fn compute_bbox(
    w: &mut World,
    e: EntIdx,
    _flags: u32,
    _ipart: i32,
    _c: Option<Coords>,
) -> [Vec3; 2] {
    let ent = w.ent(e);
    let r = pd(w, e).size;
    let p = ent.pos;
    [
        Vec3::new(p.x - r, p.y - r, p.z - r),
        Vec3::new(r + p.x, r + p.y, r + p.z),
    ]
}

/// Slot 55 `Awake` 0x35039B60: `bAwake` → state 1; else state 0 and `v = 0`. Returns the
/// simclass.
pub fn awake(w: &mut World, e: EntIdx, b: i32, _src: i32) -> i32 {
    let sc = w.ent(e).sim_class();
    let p = pdm(w, e);
    if b != 0 {
        p.bits &= !0x20_0000;
        p.bits |= 0x10_0000;
    } else {
        p.bits &= 0xFFCF_FFFF;
        p.vel = Vec3::ZERO;
    }
    sc
}

/// Slot 56 `IsAwake` 0x35039BB0: asleep when `pos.z <= -1000`, in state 0, or (state ≠ 1,
/// `|v| <= minVel` and (a pending bounce, contact mode or no gravity — the water gravity
/// while submerged)).
#[must_use]
pub fn is_awake(w: &World, e: EntIdx) -> i32 {
    let ent = w.ent(e);
    let p = pd(w, e);
    let g = if p.depth >= 0.0 {
        p.gravity
    } else {
        p.water_gravity
    };
    let v = p.vel;
    let v4 = p.bits;
    let st = (v4 >> 20) & 3;
    let asleep = ent.pos.z <= -1000.0
        || st == 0
        || (st != 1
            && ((v.x * v.x) + (v.y * v.y)) + (v.z * v.z) <= (p.min_vel * p.min_vel)
            && ((v4 & 0xE_0000) != 0
                || (v4 & 0x400_0000) != 0
                || ((g.y * g.y) + (g.x * g.x)) + (g.z * g.z) <= 0.0));
    i32::from(!asleep)
}

/// Slot 26 `StartStep` 0x35039850: `full = dt`, `performed = 0`.
pub fn start_step(w: &mut World, e: EntIdx, dt: f32) {
    let p = pdm(w, e);
    p.time_full = dt;
    p.time_performed = 0.0;
}

/// Slot 37 `GetMaxTimeStep` 0x35039CE0: the rest of the world step (`full − performed`)
/// when less than `dt` and more than 0.001 remains, else `dt`.
pub fn get_max_time_step(w: &mut World, e: EntIdx, dt: f32) -> f32 {
    let p = pd(w, e);
    let full = p.time_full;
    let done = p.time_performed;
    if done > full - f32::from_bits(0x3A83_126F) {
        return dt;
    }
    let rest = full - done;
    if rest < dt || rest.is_nan() {
        rest
    } else {
        dt
    }
}

/// Slot 34 `Step` 0x350407E0: `DoStep(dt, iCaller)` with the calling thread's index.
pub fn step(w: &mut World, e: EntIdx, dt: f32) -> i32 {
    let caller = w.tls_thread;
    step::do_step(w, e, dt, caller)
}

/// Slot 66 `GetMass` 0x350406C0.
#[must_use]
pub fn get_mass(w: &World, e: EntIdx) -> f32 {
    pd(w, e).mass
}

/// Slot 54 0x350406D0: release the collider to ignore (the base detach hook is not called).
pub fn detach_hook(w: &mut World, e: EntIdx, _mode: i32) {
    let old = pd(w, e).collider_to_ignore;
    if !old.is_none() {
        if let Some(Some(o)) = w.ents.get_mut(old.0 as usize) {
            o.pod_refs -= 1;
        }
    }
    pdm(w, e).collider_to_ignore = EntIdx::NONE;
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn blank_params_image() {
        let b = blank_params();
        assert_eq!(rd_i32(&b, 0), 3);
        assert!(unused_bits(&b, 8) && unused_bits(&b, 128) && !unused_bits(&b, 116));
        assert_eq!(rd_u64(&b, 144), u64::MAX);
        assert!(validate_params(&b));
    }

    #[test]
    fn validator() {
        let mut b = blank_params();
        wr_f32(&mut b, 20, 1.0);
        wr_f32(&mut b, 24, 0.0);
        wr_f32(&mut b, 28, 0.0);
        assert!(validate_params(&b));
        wr_f32(&mut b, 24, 0.5);
        assert!(!validate_params(&b));
        let mut b = blank_params();
        wr_f32(&mut b, 8, -1e20);
        assert!(!validate_params(&b));
    }

    #[test]
    fn ptr_codec() {
        assert_eq!(ptr_ent(ent_ptr(EntIdx(7))), Some(EntIdx(7)));
        assert_eq!(ptr_ent(0), Some(EntIdx::NONE));
        assert_eq!(ptr_ent(0x1234), None);
    }
}
