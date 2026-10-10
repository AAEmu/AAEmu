//! **P5 — `CWheeledVehicleEntity`** (type 3, 0x2BF8 bytes; vtable 0x351F2388; ctor
//! 0x35104900): a [`CRigidEntity`](super::rigid) whose last parts are wheels.
//! `dynamics.md` §5; `entities.md` §4 (vtable), §5.1 (params / status / action), §5.2 (wheel
//! record), §5.3 (step hooks).
//!
//! In ArcheAge carts and siege vehicles are wheeled vehicles. The class does not override the
//! rigid `Step` (slot 34); it hooks into it:
//!
//! | slot | address | port | when |
//! |---|---|---|---|
//! | 78 `CheckAdditionalGeometry` | 0x3510C940 | [`detect::check_additional_geometry`] | rigid Step, after the integration (and in the asleep branch) |
//! | 79 | 0x351074A0 | [`drive::step_drive`] (engine 0x351057C0 [`drive::engine`]) | rigid Step, after the momenta are final |
//! | 40 `RegisterContacts` | 0x35105E50 | [`register_contacts`] | before the solver |
//! | 41 `Update` | 0x351050A0 | [`update`] | after the solver |
//! | 59 `OnContact` | 0x35104760 | [`on_contact`] | post-solve, contacts on parts with flag 0x400000 |
//! | 37 / 43 | 0x35104790 / 0x35104FA0 | [`get_max_time_step`] / [`get_damping`] | island setup / damping |
//! | 48 / 50 / 51 / 54 | 0x35104840 / 0x351047F0 / 0x35104FF0 / 0x35104700 | collider bookkeeping | |
//! | 7 / 8 / 9 / 10 | 0x35106740 / 0x35104B50 / 0x3510A090 / 0x351094A0 | [`params`] | |
//! | 11 / 12 | 0x3510BBB0 / 0x351072F0 | [`geometry`] | |
//!
//! Not ported: the snapshot slots 15–18 (`TSerialize` / bit-stream state, no serializer in the
//! port), slot 29 (memory statistics), slot 73 (debug drawing), and slot 83 (an expanded
//! `ComputeBBox` 0x35104F10 that no code in the DLL calls — no `call [reg+0x298]` exists).
//!
//! **Wheels and parts.** Wheel `i` is part `n_hull + i` (`+11156` = number of hull parts).
//! Its new-coords pointer (`part+120`) points at the wheel record (`+0` pos, `+12` q, `+28`
//! scale, `+32` box), modelled by [`crate::entities::base::Part::coords_wheel`]: ComputeBBox
//! reads the wheel pose from the record and stores the wheel box there, `UpdatePosition`
//! copies the record pose back into the part.
//!
//! **The inline solver contact** (`wheel+336`, 176 bytes) is a dedicated record of the world
//! contact pool ([`Wheel::contact`], allocated by the constructor and never freed).

pub mod contacts;
pub mod detect;
pub mod drive;
pub mod geometry;
pub mod params;

use super::base::Entity;
use crate::geometry::PhysGeometry;
use crate::math::{Quat, Vec3};
use crate::solver::{BodyId, ContactId};
use crate::world::{EntIdx, World};
use std::sync::Arc;

pub use contacts::register_contacts;
pub use geometry::add_geometry;
pub use params::{action, get_params, get_status, set_params};

/// Maximum number of wheels (`18` records of 544 bytes at `E+1200`).
pub const MAX_WHEELS: usize = 18;

/// Wheel flag bits (`wheel+180`).
pub mod wflags {
    /// Driving wheel.
    pub const DRIVING: u32 = 0x1;
    /// Can brake (handbrake).
    pub const CAN_BRAKE: u32 = 0x2;
    /// Blocked (no rotation).
    pub const BLOCKED: u32 = 0x4;
    /// Ray-cast suspension.
    pub const RAYCAST: u32 = 0x8;
    /// Slipping.
    pub const SLIP: u32 = 0x10;
    /// Rolls freely / lost grip this step (temp).
    pub const TEMP: u32 = 0x20;
    /// In contact.
    pub const CONTACT: u32 = 0x40;
}

/// One wheel record (`E + 1200 + 544·i`). Offsets are wheel-relative.
#[derive(Clone, Debug)]
pub struct Wheel {
    /// `+0` position in the entity frame (the part's new coords; includes the suspension).
    pub pos: Vec3,
    /// `+12` orientation in the entity frame (initial orientation ⊗ steering ⊗ rotation).
    pub q: Quat,
    /// `+28` scale.
    pub scale: f32,
    /// `+32` / `+44` world box (ComputeBBox with flag 2).
    pub bbox: [Vec3; 2],
    /// `+56` drive-torque scale (1).
    pub drive_scale: f32,
    /// `+60` axle index (< 0: not on an axle).
    pub axle: i32,
    /// `+64` suspension pivot (entity frame).
    pub pivot: Vec3,
    /// `+76` maximum suspension length.
    pub len_max: f32,
    /// `+80` (geomparams `+148`; the minimal compression term of the spring force).
    pub len_min_off: f32,
    /// `+84` spring stiffness.
    pub k: f32,
    /// `+88` damping.
    pub c: f32,
    /// `+92` damping as given (geomparams `+156`).
    pub c0: f32,
    /// `+96` rest length.
    pub len_rest: f32,
    /// `+100` effective mass at the pivot.
    pub mass_share: f32,
    /// `+104` initial part orientation.
    pub q0: Quat,
    /// `+120` initial part position.
    pub pos0: Vec3,
    /// `+132` initial wheel centre (`pos0 + q0·origin`).
    pub center0: Vec3,
    /// `+144` `1 / (density·Iz)` (phys_geometry `+16`).
    pub inv_inertia: f32,
    /// `+148` / `+152` min / max friction.
    pub min_friction: f32,
    pub max_friction: f32,
    /// `+156` (geomparams `+168`; scales the lateral friction impulse limit).
    pub f156: f32,
    /// `+160` slip-angle threshold (geomparams `+180`, ≥ 0.001; default 0.1745).
    pub slip_angle: f32,
    /// `+164` (geomparams `+176`; low-speed friction boost).
    pub f164: f32,
    /// `+168` (geomparams `+172`; load-dependent friction factor).
    pub f168: f32,
    /// `+172` / `+176` the part's original flags / collider flags.
    pub part_flags: u32,
    pub part_flags_collider: u32,
    /// `+180` [`wflags`].
    pub flags: u32,
    /// `+184` stabilizer partner (the other wheel on the same axle, -1 none).
    pub partner: i32,
    /// `+188` radius, `+192` 1/radius, `+196` half width.
    pub r: f32,
    pub r_inv: f32,
    pub width: f32,
    /// `+200` current suspension length.
    pub len: f32,
    /// `+204` steering angle, `+208` rotation angle, `+212` angular velocity.
    pub steer: f32,
    pub angle: f32,
    pub w: f32,
    /// `+216`.
    pub f216: f32,
    /// `+220` brake / axle-friction torque, `+224` its impulse (`·dt`), `+228` ω before the
    /// step.
    pub brake_torque: f32,
    pub brake_impulse: f32,
    pub w_prev: f32,
    /// `+232`.
    pub f232: f32,
    /// `+240` the collision event logged by RegisterContacts (its normal impulse gets the
    /// solver's impulse in Update).
    pub event: Option<usize>,
    /// `+248` contact normal, `+260` contact point.
    pub n: Vec3,
    pub pt: Vec3,
    /// `+272` / `+276` own / ground surface (material) ids.
    pub surf: [i32; 2],
    /// `+280` slip velocity.
    pub vslip: Vec3,
    /// `+292` pivot relative to the centre of mass (world).
    pub rpivot: Vec3,
    /// `+304` velocity of the pivot along the suspension axis.
    pub v_up: f32,
    /// `+308` suspension impulse.
    pub n_impulse: f32,
    /// `+312` the ground body (`None` = null).
    pub contact_body: Option<BodyId>,
    /// `+320` / `+328` the ground entity and part.
    pub contact_ent: EntIdx,
    pub contact_part: i32,
    /// `+336` the inline solver contact.
    pub contact: ContactId,
    /// `+512` lateral friction impulse vector (`pn·axis`), `+524` drive impulse vector,
    /// `+536` slip ratio, `+540` grip factor.
    pub f512: Vec3,
    pub f524: Vec3,
    pub f536: f32,
    pub f540: f32,
}

impl Wheel {
    /// `sub_351106E0`: the record as the entity constructor leaves it (the fields it does not
    /// touch are heap garbage in the binary; the port zeroes them).
    #[must_use]
    pub fn new(contact: ContactId) -> Self {
        Self {
            pos: Vec3::ZERO,
            q: Quat::new(0.0, 0.0, 0.0, 0.0),
            scale: 0.0,
            bbox: [Vec3::ZERO; 2],
            drive_scale: 0.0,
            axle: 0,
            pivot: Vec3::ZERO,
            len_max: 0.0,
            len_min_off: 0.0,
            k: 0.0,
            c: 0.0,
            c0: 0.0,
            len_rest: 0.0,
            mass_share: 0.0,
            q0: Quat::new(0.0, 0.0, 0.0, 0.0),
            pos0: Vec3::ZERO,
            center0: Vec3::ZERO,
            inv_inertia: 0.0,
            min_friction: 0.0,
            max_friction: 0.0,
            f156: 0.0,
            slip_angle: 0.0,
            f164: 0.0,
            f168: 0.0,
            part_flags: 0,
            part_flags_collider: 0,
            flags: 0,
            partner: 0,
            r: 0.0,
            r_inv: 0.0,
            width: 0.0,
            len: 0.0,
            steer: 0.0,
            angle: 0.0,
            w: 0.0,
            f216: 0.0,
            brake_torque: 0.0,
            brake_impulse: 0.0,
            w_prev: 0.0,
            f232: 0.0,
            event: None,
            n: Vec3::ZERO,
            pt: Vec3::ZERO,
            surf: [0; 2],
            vslip: Vec3::ZERO,
            rpivot: Vec3::ZERO,
            v_up: 0.0,
            n_impulse: 0.0,
            contact_body: None,
            contact_ent: EntIdx::NONE,
            contact_part: 0,
            contact,
            f512: Vec3::ZERO,
            f524: Vec3::ZERO,
            f536: 0.0,
            f540: 0.0,
        }
    }
}

/// `CWheeledVehicleEntity` members (`E+1200 … E+0x2BF8`), held by
/// [`super::rigid::RigidData::wheeled`].
#[derive(Clone, Debug)]
pub struct WheeledData {
    /// `+1200` the wheel records (always [`MAX_WHEELS`]; the first `nParts − n_hull` are used).
    pub wheels: Vec<Wheel>,
    /// `+10992` engine power, `+10996` max speed (0 = none), `+11000` max steering angle,
    /// `+11004` engine max ω, `+11008` stall ω, `+11012` idle ω, `+11016` shift-up ω,
    /// `+11020` shift-down ω, `+11024` gear-direction-switch ω, `+11028` start ω.
    pub engine_power: f32,
    pub max_speed: f32,
    pub max_steer: f32,
    pub engine_max_w: f32,
    pub engine_min_w: f32,
    pub engine_idle_w: f32,
    pub engine_shift_up_w: f32,
    pub engine_shift_down_w: f32,
    pub gear_dir_switch_w: f32,
    pub engine_start_w: f32,
    /// `+11032` axle friction, `+11036` brake torque, `+11040` clutch speed, `+11044` /
    /// `+11048` min / max braking friction, `+11052` dynamic-friction ratio, `+11056` slip
    /// threshold, `+11060` stabilizer.
    pub axle_friction: f32,
    pub brake_torque: f32,
    pub clutch_speed: f32,
    pub min_brake_friction: f32,
    pub max_brake_friction: f32,
    pub k_dyn_friction: f32,
    pub slip_threshold: f32,
    pub k_stabilizer: f32,
    /// `+11064` pedal, `+11068` steering, `+11072` clutch, `+11076` engine ω.
    pub pedal: f32,
    pub steer: f32,
    pub clutch: f32,
    pub w_engine: f32,
    /// `+11080` gear ratios (index 0 reverse, 1 neutral, ≥ 2 forward; 12 slots).
    pub gears: [f32; 12],
    /// `+11128` number of gears, `+11132` current gear, `+11136` / `+11140` max / min gear.
    pub n_gears: i32,
    pub cur_gear: i32,
    pub max_gear: i32,
    pub min_gear: i32,
    /// `+11144` tank-steering factor (`2 / turn width`; 0 = car steering).
    pub k_steer_tank: f32,
    /// `+11148` handbrake, `+11152` footbrake.
    pub handbrake: i32,
    pub footbrake: i32,
    /// `+11156` number of hull parts (first wheel part index).
    pub n_hull: i32,
    /// `+11160` integration type.
    pub int_type: i32,
    /// `+11164` / `+11168` min energy with a recent hull contact / on wheels.
    pub min_energy_hull: f32,
    pub min_energy: f32,
    /// `+11172` / `+11176` max time step on wheels / with a recent hull contact.
    pub max_time_step: f32,
    pub max_time_step_hull: f32,
    /// `+11180` damping without a recent hull contact.
    pub damping: f32,
    /// `+11184 … +11204` (cleared by the ctor and slot 79).
    pub unk11184: [f32; 6],
    /// `+11208` time since the last hull contact, `+11212` hull contacts this step,
    /// `+11216` recent hull contact (`+11208 < 0.5`).
    pub time_no_hull_contact: f32,
    pub n_hull_contacts: i32,
    pub hull_contact: i32,
    /// `+11224` drive-direction angle for tank steering, `+11232` min `|up·n|` of a wheel
    /// contact (cos 30°), `+11236` keep-contact flag, `+11240` engine braking.
    pub angle_tank: f32,
    pub min_contact_cos: f32,
    pub keep_contact: i32,
    pub engine_braking: f32,
    /// `+11228` drive torque.
    pub drive_torque: f32,
    /// `+11244` reverse-steering flag.
    pub rear_steer: i32,
    /// `+11248` / `+11252` lateral / longitudinal load (debug display).
    pub load_lat: f32,
    pub load_long: f32,
}

impl WheeledData {
    /// Number of wheels of the entity.
    #[inline]
    #[must_use]
    pub fn n_wheels(ent: &Entity) -> i32 {
        ent.parts.len() as i32 - ent.wheeled().map_or(0, |w| w.n_hull)
    }
}

/// Constructor 0x35104900 (after the rigid ctor): the wheel records (`sub_351106E0`) with their
/// contact records, the car defaults, entity flags `|= 0x40000` (`&= ~0x40`) and rigid
/// `flags2 |= 0x8000001`.
pub fn construct(w: &mut World, ent: &mut Entity) {
    let wheels = (0..MAX_WHEELS).map(|_| Wheel::new(w.contacts.new_sentinel())).collect();
    let Some(r) = ent.rigid_mut() else {
        return;
    };
    let max_w = f32::from_bits(1_142_292_480); // 600
    let d = WheeledData {
        wheels,
        engine_power: f32::from_bits(1_176_256_512), // 10000
        max_speed: 0.0,
        max_steer: f32::from_bits(1_061_752_795), // π/4
        engine_max_w: max_w,
        engine_min_w: max_w * 0.050_000_001,
        engine_idle_w: max_w * 0.1,
        engine_shift_up_w: max_w * 0.5,
        engine_shift_down_w: max_w * 0.2,
        gear_dir_switch_w: 1.0,
        engine_start_w: f32::from_bits(1_109_393_408), // 40
        axle_friction: 0.0,
        brake_torque: f32::from_bits(1_165_623_296), // 4000
        clutch_speed: 1.0,
        min_brake_friction: 0.0,
        max_brake_friction: 1.0,
        k_dyn_friction: 1.0,
        slip_threshold: f32::from_bits(1_045_220_557), // 0.2
        k_stabilizer: 0.0,
        pedal: 0.0,
        steer: 0.0,
        clutch: 0.0,
        w_engine: max_w * 0.1,
        gears: {
            // +11080..+11088 = {-1, 1, 1}; the rest of the array is not initialised
            let mut g = [0.0; 12];
            g[0] = -1.0;
            g[1] = 1.0;
            g[2] = 1.0;
            g
        },
        n_gears: 3,
        cur_gear: 1,
        max_gear: 2,
        min_gear: 0,
        k_steer_tank: 0.0,
        handbrake: 1,
        footbrake: 1,
        n_hull: 0,
        int_type: 1,
        min_energy_hull: r.min_energy,
        min_energy: f32::from_bits(992_204_555), // 0.0025
        max_time_step: f32::from_bits(1_017_370_378), // 0.02
        max_time_step_hull: r.max_time_step,
        damping: f32::from_bits(1_008_981_770), // 0.01
        unk11184: [0.0; 6],
        time_no_hull_contact: 10.0,
        n_hull_contacts: 0,
        hull_contact: 0,
        angle_tank: 0.0,
        min_contact_cos: f32::from_bits(1_063_105_069), // 0.866
        keep_contact: 0,
        engine_braking: 1.0,
        drive_torque: 0.0,
        rear_steer: 0,
        load_lat: 0.0,
        load_long: 0.0,
    };
    r.flags2 |= 1;
    r.flags2 |= 0x800_0000;
    r.wheeled = Some(Box::new(d));
    ent.flags &= !0x40;
    ent.flags |= 0x4_0000;
}

#[inline]
pub(crate) fn wd(w: &World, e: EntIdx) -> &WheeledData {
    w.ent(e).wheeled().expect("wheeled")
}

#[inline]
pub(crate) fn wm(w: &mut World, e: EntIdx) -> &mut WheeledData {
    w.ent_mut(e).wheeled_mut().expect("wheeled")
}

/// Number of wheels (`nParts − +11156`).
#[inline]
#[must_use]
pub fn n_wheels(w: &World, e: EntIdx) -> i32 {
    WheeledData::n_wheels(w.ent(e))
}

/// Slot 37 `GetMaxTimeStep` 0x35104790: rigid `maxTimeStep` (`+656`) = (recent hull contact
/// ? `+11176` : `+11172`) × (0.75 with entity flag 0x10000), then the rigid slot.
pub fn get_max_time_step(w: &mut World, e: EntIdx, dt: f32) -> f32 {
    let d = wd(w, e);
    let v2 = if d.hull_contact != 0 {
        d.max_time_step_hull
    } else {
        d.max_time_step
    };
    let f = (((w.ent(e).flags & 0x1_0000) as i32).wrapping_neg() >> 31) as f32;
    let r = w.ent_mut(e).rigid_mut().expect("rigid");
    r.max_time_step = (1.0 - f * 0.25) * v2;
    super::rigid::update::get_max_time_step(w, e, dt)
}

/// Slot 41 `Update(dt, damping)` 0x351050A0.
///
/// The hull-contact timer (`+11208`, reset by a hull contact counted in `+11212`),
/// `+11216 = timer < 0.5`, Emin (`+652`) = `+11164` with a recent hull contact or no wheel on
/// the ground, else `+11168`; the rigid Update (its result is **discarded**); every wheel's ω
/// is multiplied by the awake byte and the logged collision event of the wheel (if any) gets
/// the contact's solver impulse. Returns `(timeStepPerformed > timeStepFull − 0.001) |
/// !awake | (flag 0x10000 && pedal == 0 ? -1 : 0)` (the last term is `0xFFFFFFFF`).
pub fn update(w: &mut World, e: EntIdx, dt: f32, damping: f32) -> u32 {
    {
        let d = wm(w, e);
        if d.n_hull_contacts != 0 {
            d.time_no_hull_contact = 0.0;
        } else {
            d.time_no_hull_contact = dt + d.time_no_hull_contact;
        }
    }
    let nw = n_wheels(w, e);
    let mut v4 = 0u32;
    for i in 0..nw.max(0) as usize {
        v4 |= u32::from(wd(w, e).wheels[i].flags & wflags::CONTACT != 0);
    }
    {
        let r_emin;
        let d = wm(w, e);
        let v9 = (d.time_no_hull_contact - 0.5).to_bits() >> 31;
        d.hull_contact = v9 as i32;
        r_emin = if (v9 | (v4 ^ 1)) != 0 {
            d.min_energy_hull
        } else {
            d.min_energy
        };
        w.ent_mut(e).rigid_mut().expect("rigid").min_energy = r_emin;
    }
    let _ = super::rigid::update::update(w, e, dt, damping);
    let awake = f32::from((w.ent(e).rigid().expect("rigid").awake_bits & 0xFF) as u8);
    for i in 0..n_wheels(w, e).max(0) as usize {
        let (ev, c) = {
            let wh = &mut wm(w, e).wheels[i];
            wh.w = awake * wh.w;
            (wh.event.take(), wh.contact)
        };
        if let Some(idx) = ev {
            let pn = w.contacts.get(c).pn;
            if let Some(crate::world::events::Event::Collision { normimpulse, .. }) =
                w.events.logged.get_mut(idx)
            {
                *normimpulse = pn + *normimpulse;
            }
        }
    }
    let ent = w.ent(e);
    let r = ent.rigid().expect("rigid");
    let v15 = ((!ent.flags & 0x1_0000) as f32) + wd(w, e).pedal;
    let a = (((r.time_step_full - r.time_step_performed) - 0.001).to_bits()) >> 31;
    let b = u32::from(r.awake_bits as u8) ^ 1;
    let abs = v15.to_bits() & 0x7FFF_FFFF;
    let c = ((abs ^ abs.wrapping_sub(1)) >> 31).wrapping_neg();
    a | b | c
}

/// Slot 43 `GetDamping(dt)` 0x35104FA0: with a recent hull contact the rigid slot, otherwise
/// `max(0, 1 − dt·+11180)` (`(|−x| + x)·0.5`).
#[must_use]
pub fn get_damping(w: &World, e: EntIdx, dt: f32) -> f32 {
    let d = wd(w, e);
    if d.hull_contact != 0 {
        return super::rigid::update::get_damping(w, e, dt);
    }
    let x = 1.0 - dt * d.damping;
    ((-x).abs() + x) * 0.5
}

/// Slot 48 `RemoveCollider` 0x35104840: wheels in contact with `c` lose the contact (flag
/// 0x40, entity pointer), then the rigid slot.
pub fn remove_collider(w: &mut World, e: EntIdx, c: EntIdx, always: bool) -> i32 {
    let nw = n_wheels(w, e);
    for i in 0..nw.max(0) as usize {
        let wh = &mut wm(w, e).wheels[i];
        if wh.contact_ent == c {
            wh.flags &= !wflags::CONTACT;
            wh.contact_ent = EntIdx::NONE;
        }
    }
    super::class_remove_collider(w, e, c, always)
}

/// Slot 50 `HasContactsWith` 0x351047F0: a wheel standing on `other`, else the rigid slot.
#[must_use]
pub fn has_contacts_with(w: &World, e: EntIdx, other: EntIdx) -> bool {
    let nw = n_wheels(w, e);
    let d = wd(w, e);
    (0..nw.max(0) as usize).any(|i| d.wheels[i].contact_ent == other)
        || super::class_has_contacts_with(w, e, other)
}

/// Slot 51 `HasPartContactsWith(pent, ipart, bGreater)` 0x35104FF0: a wheel standing on
/// part `ipart` of `other` (or on a part with a higher index when `greater`), else the rigid
/// slot 0x350AAF80 ([`super::class_has_part_contacts_with`]).
#[must_use]
pub fn has_part_contacts_with(w: &World, e: EntIdx, other: EntIdx, ipart: i32, greater: i32) -> bool {
    let nw = n_wheels(w, e);
    let d = wd(w, e);
    for i in 0..nw.max(0) as usize {
        let wh = &d.wheels[i];
        if wh.contact_ent != other {
            continue;
        }
        let diff = ipart.wrapping_sub(wh.contact_part);
        let eq = (((diff ^ diff.wrapping_sub(1)) >> 31) as u32).wrapping_neg();
        let gt = greater.wrapping_neg() as u32 & ((diff as u32) >> 31);
        if (eq | gt) != 0 {
            return true;
        }
    }
    super::class_has_part_contacts_with(w, e, other, ipart, greater)
}

/// Slot 54 0x35104700: the rigid slot, then every wheel loses its contact.
pub fn detach_hook(w: &mut World, e: EntIdx, mode: i32) {
    super::class_detach_hook(w, e, mode);
    let nw = n_wheels(w, e);
    for i in 0..nw.max(0) as usize {
        let wh = &mut wm(w, e).wheels[i];
        wh.flags &= !wflags::CONTACT;
        wh.contact_ent = EntIdx::NONE;
    }
}

/// Slot 59 `OnContact(contact, iside)` 0x35104760 (called by the post-solve for contacts on
/// parts with flag 0x400000): a contact whose other side is not the vehicle itself counts as a
/// hull contact (`+11212`).
pub fn on_contact(w: &mut World, e: EntIdx, c: ContactId, side: i32) {
    if side < 2 {
        let other = w.contacts.get(c).pent[(side ^ 1) as usize];
        if other != e {
            wm(w, e).n_hull_contacts += 1;
        }
    }
}

/// The modelled words of the wheeled members (`E+1200 …`) for `peek` comparisons; `None` for
/// pointers and unmodelled bytes.
#[must_use]
pub fn peek_word(w: &World, e: EntIdx, off: usize) -> Option<u32> {
    let d = w.ent(e).wheeled()?;
    let f = |x: f32| Some(x.to_bits());
    let v = |x: Vec3, o: usize| match o {
        0 => Some(x.x.to_bits()),
        4 => Some(x.y.to_bits()),
        8 => Some(x.z.to_bits()),
        _ => None,
    };
    let q = |x: Quat, o: usize| f(x.to_array()[o / 4]);
    if (1200..1200 + 544 * MAX_WHEELS).contains(&off) {
        let i = (off - 1200) / 544;
        let o = (off - 1200) % 544;
        let wh = &d.wheels[i];
        if (336..512).contains(&o) {
            return contact_word(w.contacts.get(wh.contact), o - 336);
        }
        return match o {
            0..=8 => v(wh.pos, o),
            12..=24 => q(wh.q, o - 12),
            28 => f(wh.scale),
            32..=52 => v(wh.bbox[(o - 32) / 12], (o - 32) % 12),
            56 => f(wh.drive_scale),
            60 => Some(wh.axle as u32),
            64..=72 => v(wh.pivot, o - 64),
            76 => f(wh.len_max),
            80 => f(wh.len_min_off),
            84 => f(wh.k),
            88 => f(wh.c),
            92 => f(wh.c0),
            96 => f(wh.len_rest),
            100 => f(wh.mass_share),
            104..=116 => q(wh.q0, o - 104),
            120..=128 => v(wh.pos0, o - 120),
            132..=140 => v(wh.center0, o - 132),
            144 => f(wh.inv_inertia),
            148 => f(wh.min_friction),
            152 => f(wh.max_friction),
            156 => f(wh.f156),
            160 => f(wh.slip_angle),
            164 => f(wh.f164),
            168 => f(wh.f168),
            172 => Some(wh.part_flags),
            176 => Some(wh.part_flags_collider),
            180 => Some(wh.flags),
            184 => Some(wh.partner as u32),
            188 => f(wh.r),
            192 => f(wh.r_inv),
            196 => f(wh.width),
            200 => f(wh.len),
            204 => f(wh.steer),
            208 => f(wh.angle),
            212 => f(wh.w),
            216 => f(wh.f216),
            220 => f(wh.brake_torque),
            224 => f(wh.brake_impulse),
            228 => f(wh.w_prev),
            232 => f(wh.f232),
            248..=256 => v(wh.n, o - 248),
            260..=268 => v(wh.pt, o - 260),
            272 => Some(wh.surf[0] as u32),
            276 => Some(wh.surf[1] as u32),
            280..=288 => v(wh.vslip, o - 280),
            292..=300 => v(wh.rpivot, o - 292),
            304 => f(wh.v_up),
            308 => f(wh.n_impulse),
            328 => Some(wh.contact_part as u32),
            512..=520 => v(wh.f512, o - 512),
            524..=532 => v(wh.f524, o - 524),
            536 => f(wh.f536),
            540 => f(wh.f540),
            _ => None,
        };
    }
    match off {
        10992 => f(d.engine_power),
        10996 => f(d.max_speed),
        11000 => f(d.max_steer),
        11004 => f(d.engine_max_w),
        11008 => f(d.engine_min_w),
        11012 => f(d.engine_idle_w),
        11016 => f(d.engine_shift_up_w),
        11020 => f(d.engine_shift_down_w),
        11024 => f(d.gear_dir_switch_w),
        11028 => f(d.engine_start_w),
        11032 => f(d.axle_friction),
        11036 => f(d.brake_torque),
        11040 => f(d.clutch_speed),
        11044 => f(d.min_brake_friction),
        11048 => f(d.max_brake_friction),
        11052 => f(d.k_dyn_friction),
        11056 => f(d.slip_threshold),
        11060 => f(d.k_stabilizer),
        11064 => f(d.pedal),
        11068 => f(d.steer),
        11072 => f(d.clutch),
        11076 => f(d.w_engine),
        11080..=11124 => f(d.gears[(off - 11080) / 4]),
        11128 => Some(d.n_gears as u32),
        11132 => Some(d.cur_gear as u32),
        11136 => Some(d.max_gear as u32),
        11140 => Some(d.min_gear as u32),
        11144 => f(d.k_steer_tank),
        11148 => Some(d.handbrake as u32),
        11152 => Some(d.footbrake as u32),
        11156 => Some(d.n_hull as u32),
        11160 => Some(d.int_type as u32),
        11164 => f(d.min_energy_hull),
        11168 => f(d.min_energy),
        11172 => f(d.max_time_step),
        11176 => f(d.max_time_step_hull),
        11180 => f(d.damping),
        11184..=11204 => f(d.unk11184[(off - 11184) / 4]),
        11208 => f(d.time_no_hull_contact),
        11212 => Some(d.n_hull_contacts as u32),
        11216 => Some(d.hull_contact as u32),
        11224 => f(d.angle_tank),
        11228 => f(d.drive_torque),
        11232 => f(d.min_contact_cos),
        11236 => Some(d.keep_contact as u32),
        11240 => f(d.engine_braking),
        11244 => Some(d.rear_steer as u32),
        11248 => f(d.load_lat),
        11252 => f(d.load_long),
        _ => None,
    }
}

/// The non-pointer words of a contact record (`entity_contact` offsets).
fn contact_word(c: &crate::solver::contact::Contact, o: usize) -> Option<u32> {
    let v = |x: Vec3, k: usize| Some([x.x, x.y, x.z][k / 4].to_bits());
    match o {
        24..=32 => v(c.pt[0], o - 24),
        36..=44 => v(c.pt[1], o - 36),
        48..=56 => v(c.n, o - 48),
        80 => Some(c.ipart[0] as u32),
        84 => Some(c.ipart[1] as u32),
        112..=120 => v(c.nloc, o - 112),
        124 => Some(c.friction.to_bits()),
        128 => Some(c.flags),
        132 => Some(c.vn.to_bits()),
        136..=144 => v(c.vreq, o - 136),
        148 => Some(c.pn.to_bits()),
        152 => Some(c.penetration.to_bits()),
        156 => Some(c.id_bits),
        160 => Some(c.idx_bits),
        164 => Some(c.group_n as u32),
        _ => None,
    }
}

/// Entity slot 11 for the raw-image entry ([`crate::entities::base::geometry::add_geometry_raw`]).
pub fn add_geometry_raw(w: &mut World, e: EntIdx, g: &Arc<PhysGeometry>, buf: &[u8], id: i32) -> i32 {
    geometry::add_geometry(w, e, g, buf, id)
}
