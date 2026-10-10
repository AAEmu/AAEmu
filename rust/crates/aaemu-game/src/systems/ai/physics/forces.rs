//! The `PreStep` force generators (`Physics/Forces`, `Weather/StormShipLogic`): `Buoyancy`,
//! `WhirlpoolShipPull`, `StormShipLogic` and `OneShotVelocityKick`, in their subscription order.

use std::collections::{HashMap, HashSet};

use crate::systems::ai::numerics::Vec3Ext;
use crate::systems::skills::controllers::Vec3;

use super::contacts::HullDamage;
use super::ship::PhysShip;
use super::PhysEnv;

/// `Buoyancy.BaseWaterDensity`.
pub const BASE_WATER_DENSITY: f32 = 1.025;
/// `Buoyancy.ShipWaterDensityMul`.
pub const SHIP_WATER_DENSITY_MUL: f32 = 3.0;

/// `Buoyancy.PreStep` for one ship (`FluidBox.Max.Y` = the template ocean level; the custom
/// fluid area is `WorldInstance.IsWater` at the hull centre).
pub fn buoyancy(ship: &mut PhysShip, env: &dyn PhysEnv, now_ms: i64) {
    let body_active = ship.body.active;
    if !body_active {
        return;
    }
    if ship.model.mass <= 0.0 {
        return;
    }
    ship.body.affected_by_gravity = !ship.summoning(now_ms);
    if !ship.body.affected_by_gravity {
        return;
    }
    let mut level = env.ocean_level();
    let c = ship.body.position;
    // CustomWater(ref JVector area): IsWater(new Vector3(area.X, area.Z, area.Y))
    if env.is_water(Vec3::new(c.x, c.z, c.y)).is_some() {
        level = ship.cached_water_surface;
    }
    let depth = level - c.y;
    if depth <= 0.0 {
        return;
    }
    apply_drag(ship, ship.model.mass_box_size_x, ship.model.mass_box_size_z);
    let submerged = 0f32.max(level - c.y);
    if submerged > 0.0 {
        let mass = ship.body.mass();
        ship.body.add_force(Vec3::new(
            0.0,
            submerged * mass * BASE_WATER_DENSITY * SHIP_WATER_DENSITY_MUL * 9.81,
            0.0,
        ));
        let v = ship.body.velocity;
        ship.body.add_force(Vec3::new(
            -v.x * BASE_WATER_DENSITY,
            -v.y * BASE_WATER_DENSITY,
            -v.z * BASE_WATER_DENSITY,
        ));
    }
}

/// `Buoyancy.ApplyDrag(body, hullWidth, hullLength, hullHeight)`.
fn apply_drag(ship: &mut PhysShip, hull_width: f32, hull_height: f32) {
    let v = ship.body.velocity;
    let speed = v.length();
    if speed < 0.1 {
        return;
    }
    const DRAG_COEFFICIENT: f32 = 0.8;
    let area = hull_width * hull_height;
    let drag = 0.5 * BASE_WATER_DENSITY * DRAG_COEFFICIENT * area * speed * speed;
    ship.body.add_force(-v.normalized() * drag);
}

/// `WhirlpoolShipPull` constants.
pub mod whirlpool {
    pub const BUFF_ID: u32 = 1918;
    pub const DOODAD_TEMPLATE_ID: u32 = 3086;
    pub const PULL_SPEED_METERS_PER_SEC: f32 = 3.0;
    pub const PULL_AT_FULL_THROTTLE_MUL_MIN: f32 = 0.5;
    pub const MIN_DISTANCE_METERS: f32 = 0.35;
}

/// `WhirlpoolShipPull` state (hull damage accumulators by slave obj id).
#[derive(Debug, Default)]
pub struct Whirlpool {
    acc: HashMap<u32, f32>,
}

impl Whirlpool {
    /// `PreStep`: ships with the whirlpool buff drift toward the nearest visible whirlpool doodad
    /// (`whirlpools`: their XY) and take 1% hull damage per second. `any_affected` is the
    /// "any slave of the world has the buff" early-out.
    pub fn pre_step(
        &mut self,
        ships: &mut [&mut PhysShip],
        whirlpools: &[(f32, f32)],
        any_affected: bool,
        time_step: f32,
        damage: &mut Vec<HullDamage>,
    ) {
        if time_step <= 0.0 || !any_affected {
            return;
        }
        let dt = time_step.clamp(0.0, 0.5);
        if whirlpools.is_empty() {
            return;
        }
        let min_dist_sq = whirlpool::MIN_DISTANCE_METERS * whirlpool::MIN_DISTANCE_METERS;
        let mut affected = HashSet::new();
        for s in ships.iter_mut() {
            if !s.body.active || !s.view.whirlpool_buff {
                continue;
            }
            if s.ground_contact_latched || s.cached_floor_level > s.cached_water_surface {
                continue;
            }
            affected.insert(s.spec.obj_id);
            let (px, py) = (s.pos.x, s.pos.y);
            let mut best = f32::MAX;
            let mut center = (0f32, 0f32);
            for c in whirlpools {
                let d = (px - c.0) * (px - c.0) + (py - c.1) * (py - c.1);
                if d < best {
                    best = d;
                    center = *c;
                }
            }
            if best <= min_dist_sq {
                continue;
            }
            let (tx, ty) = (center.0 - px, center.1 - py);
            let l = (tx * tx + ty * ty).sqrt();
            let (dx, dy) = (tx / l, ty / l);
            let throttle01 = (f32::from(s.throttle).abs() / 127.0).clamp(0.0, 1.0);
            let pull_mul = 1.0 + (whirlpool::PULL_AT_FULL_THROTTLE_MUL_MIN - 1.0) * throttle01;
            let speed = whirlpool::PULL_SPEED_METERS_PER_SEC * pull_mul;
            if speed <= 0.0 {
                continue;
            }
            s.body.position += Vec3::new(dx * speed * dt, 0.0, dy * speed * dt);
            let acc = self.acc.entry(s.spec.obj_id).or_insert(0.0);
            *acc += dt;
            while *acc >= 1.0 {
                *acc -= 1.0;
                damage.push(HullDamage {
                    target: s.spec.obj_id,
                    attacker: s.spec.obj_id,
                    percent: 1,
                });
            }
        }
        self.acc.retain(|k, _| affected.contains(k));
    }
}

/// `StormShipLogic` (only with `SeaWeatherModel = Realistic`): the lamps of a ship entering a
/// storm cloud are lit once.
#[derive(Debug, Default)]
pub struct Storm {
    lamps_ignited: HashSet<u32>,
}

/// `StormShipLogic.StormBuffId`.
pub const STORM_BUFF_ID: u32 = 1917;

impl Storm {
    /// `PreStep`: returns the ships whose lamps must be ignited now. `still_in_storm` = the obj ids
    /// of the world's slaves that have the storm buff (the cleanup pass).
    pub fn pre_step(&mut self, ships: &[&mut PhysShip], still_in_storm: &HashSet<u32>) -> Vec<u32> {
        self.lamps_ignited.retain(|id| still_in_storm.contains(id));
        let mut out = Vec::new();
        for s in ships {
            if !s.body.active || !s.view.storm_buff {
                continue;
            }
            if self.lamps_ignited.insert(s.spec.obj_id) {
                out.push(s.spec.obj_id);
            }
        }
        out
    }
}

/// `StormShipLogic.ResolveClientTimeOfDayHours`: the hours forced on a character in a storm.
pub const STORM_CLIENT_TIME_OF_DAY_HOURS: f32 = 2.0;

#[cfg(test)]
mod tests {
    use super::super::ship::tests::{model, spec};
    use super::super::ship::{PhysShip, ShipGameView};
    use super::super::tests::FakeEnv;
    use super::*;

    #[test]
    fn buoyancy_pushes_a_submerged_hull_up() {
        let env = FakeEnv::flat(100.0, 50.0);
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        buoyancy(&mut s, &env, 1);
        let expected = 1.0 * 20000.0 * 1.025 * 3.0 * 9.81;
        assert!(
            (s.body.force.y - expected).abs() < 1.0,
            "{}",
            s.body.force.y
        );
        // above the surface: nothing
        let mut t = PhysShip::new(
            spec(2, 0.0, 0.0, 101.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        t.cached_water_surface = 100.0;
        buoyancy(&mut t, &env, 1);
        assert_eq!(t.body.force, Vec3::new(0.0, 0.0, 0.0));
        // still summoning: no gravity
        let mut u = PhysShip::new(
            spec(3, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        u.spec.portal_time = 10.0;
        buoyancy(&mut u, &env, 1);
        assert!(!u.body.affected_by_gravity);
    }

    #[test]
    fn whirlpool_drags_and_damages_buffed_ships() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.cached_water_surface = 100.0;
        s.cached_floor_level = 50.0;
        s.view.whirlpool_buff = true;
        let mut w = Whirlpool::default();
        let mut dmg = Vec::new();
        for _ in 0..30 {
            w.pre_step(&mut [&mut s], &[(100.0, 0.0)], true, 0.04, &mut dmg);
        }
        assert!((s.body.position.x - 30.0 * 0.04 * 3.0).abs() < 1e-3);
        assert_eq!(dmg.len(), 1);
        assert_eq!(dmg[0].percent, 1);
    }

    #[test]
    fn storm_lamps_are_lit_once_per_storm() {
        let mut s = PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        );
        s.view.storm_buff = true;
        let mut storm = Storm::default();
        let in_storm: HashSet<u32> = [1].into_iter().collect();
        assert_eq!(storm.pre_step(&[&mut s], &in_storm), vec![1]);
        assert!(storm.pre_step(&[&mut s], &in_storm).is_empty());
        s.view.storm_buff = false;
        assert!(storm.pre_step(&[&mut s], &HashSet::new()).is_empty());
        s.view.storm_buff = true;
        assert_eq!(storm.pre_step(&[&mut s], &in_storm), vec![1]);
    }
}
