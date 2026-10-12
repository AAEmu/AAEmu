//! Actual1.2 model1360 / ShipModel19, rather than the synthetic hull used by generic flows.
//! These diagnostics keep production physics unchanged until contacts vs quaternion mapping
//! distinguish the severe live Longliner tilt.

use super::*;
use crate::models::movement::MoveKind;
use crate::models::transform::{PositionAndRotation, Quaternion};
use crate::systems::ai::data::ShipModel;

fn longliner_model() -> ShipModel {
    // Read-only r208022 compact: models1360->ship_models19, slaves110. Pink1384/21
    // has the same physics values. fishboat.xml contains no buoy* brushes.
    ShipModel {
        id: 19,
        velocity: 13.0,
        mass: 12000.0,
        mass_center_x: 0.0,
        mass_center_y: 0.0,
        mass_center_z: -3.3,
        mass_box_size_x: 5.0,
        mass_box_size_y: 11.0,
        mass_box_size_z: 10.0,
        water_density: 1.0,
        water_damping: 0.0,
        water_resistance: 1.0,
        steer_vel: 0.8,
        accel: 1.0,
        reverse_accel: 0.5,
        reverse_velocity: 5.0,
        turn_accel: 0.75,
        tube_length: 0.0,
        tube_radius: 2.0,
        tube_offset_z: 2.2,
        keel_length: 0.0,
        keel_height: 0.1,
        keel_offset_z: 0.0,
    }
}

fn recorded_longliner_bounds() -> aaemu_physics::geometry::primitives::BoxPrim {
    // Actual cached DEFAULT proxy union from originalr208022 assets, recorded in
    // sport-longliner-real-bounds-final.log. Test fixture only; production loads assets.
    aaemu_physics::geometry::primitives::BoxPrim::aligned(
        aaemu_physics::math::Vec3::new(0.0, 1.9358959, 3.5772932),
        aaemu_physics::math::Vec3::new(4.577419, 14.026104, 5.2896814),
    )
}

fn wire_up(m: &crate::models::movement::ShipMove) -> [f32; 3] {
    let (x, y, z) = (
        f32::from(m.rotation_x) * 0.000_030_52,
        f32::from(m.rotation_y) * 0.000_030_52,
        f32::from(m.rotation_z) * 0.000_030_52,
    );
    Quaternion {
        x,
        y,
        z,
        w: (1.0 - x * x - y * y - z * z).max(0.0).sqrt(),
    }
    .rotate([0.0, 0.0, 1.0])
}

fn tilt(up_z: f32) -> f32 {
    up_z.clamp(-1.0, 1.0).acos()
}

#[derive(Debug)]
struct Observation {
    floor: f32,
    yaw: f32,
    tail_engine_tilt: f32,
    tail_corrected_tilt: f32,
    tail_wire_tilt: f32,
    min_z: f32,
    max_z: f32,
    last_z: f32,
    contact_events: u64,
    ground_latched_ticks: usize,
}

fn simulate_longliner(
    floor: f32,
    yaw: f32,
    model: ShipModel,
    bounds: Option<aaemu_physics::geometry::primitives::BoxPrim>,
    initial_tilt: f32,
) -> Observation {
    let w = cry_world(113.7, floor);
    let wp = WorldPhysics::new(0);
    let mut specification = spec(1, 1000.0, 1000.0, 113.7, yaw);
    specification.model_id = 1360;
    specification.kind = super::super::SlaveKind::FISHBOAT;
    let mut ship = PhysShip::new(specification, model, ShipGameView::default());
    ship.collision_bounds = bounds;
    ship.body.orientation =
        crate::systems::ai::numerics::Quat::from_yaw_pitch_roll(yaw, initial_tilt, 0.0);
    wp.enqueue(Pending::Add(Box::new(ship)));
    let mut result = Observation {
        floor,
        yaw,
        tail_engine_tilt: 0.0,
        tail_corrected_tilt: 0.0,
        tail_wire_tilt: 0.0,
        min_z: f32::INFINITY,
        max_z: f32::NEG_INFINITY,
        last_z: 0.0,
        contact_events: 0,
        ground_latched_ticks: 0,
    };
    for i in 0..7500 {
        let out = wp.tick(&w, 40, 10_000 + i * 40, cry_cfg());
        assert_eq!(out.movements.len(), 1);
        let position = out.movements[0].position;
        assert!(position.x.is_finite() && position.y.is_finite() && position.z.is_finite());
        let (physical, entity, events, grounded) = wp.inspect(|st| {
            let ship = &st.ships[0];
            let q = super::super::cry::quat_to_phys(ship.body.orientation);
            (
                q.rotate(aaemu_physics::math::Vec3::new(0.0, 0.0, 1.0)),
                st.cry.ents[&1],
                st.cry.events_seen,
                ship.ground_contact_latched,
            )
        });
        result.contact_events = events;
        result.ground_latched_ticks += usize::from(grounded);
        if i >= 750 {
            // ignore30s settling; observe the next270s
            let engine = w.cry.as_ref().unwrap().lock().world.body_state(entity);
            let engine_up = engine
                .q
                .rotate(aaemu_physics::math::Vec3::new(0.0, 0.0, 1.0));
            let MoveKind::Ship(m) = &out.movements[0].move_type.kind else {
                panic!("ship movement")
            };
            result.tail_engine_tilt = result.tail_engine_tilt.max(tilt(engine_up.z));
            result.tail_corrected_tilt = result.tail_corrected_tilt.max(tilt(physical.z));
            result.tail_wire_tilt = result.tail_wire_tilt.max(tilt(wire_up(m)[2]));
            result.min_z = result.min_z.min(position.z);
            result.max_z = result.max_z.max(position.z);
        }
        result.last_z = position.z;
    }
    println!(
        "Longliner1360 after300s authored={} initial_tilt={initial_tilt} {result:?}",
        bounds.is_some()
    );
    result
}

fn assert_idle_stability(floor: f32, model: ShipModel) {
    // Run both headings before assertions so a red regression retains comparable evidence.
    let observations = [0.0, -1.47405].map(|yaw| simulate_longliner(floor, yaw, model, None, 0.0));
    for r in observations {
        assert_eq!(r.floor, floor);
        assert!(r.yaw.is_finite());
        assert!(r.last_z.is_finite());
        assert!(r.max_z - r.min_z < 2.0, "large persistent heave: {r:?}");
        assert!(
            r.tail_engine_tilt < 20.0f32.to_radians(),
            "idle hull did not settle upright: {r:?}"
        );
        assert!(
            r.tail_wire_tilt < 25.0f32.to_radians(),
            "idle visual hull remained severely tilted: {r:?}"
        );
    }
}

#[test]
fn longliner_1360_idle_shallow_300_seconds() {
    assert_idle_stability(103.975, longliner_model());
}

#[test]
fn longliner_1360_idle_deep_300_seconds() {
    assert_idle_stability(80.0, longliner_model());
}

#[test]
fn longliner_physical_and_wire_up_axes_agree_without_visual_bank_or_wave() {
    let env = FakeEnv::flat(113.7, 80.0);
    for (yaw, pitch, roll) in [
        (0.0, 0.0, 0.0),
        (-1.47405, 0.0, 0.0),
        (0.7, 0.2, -0.1),
        (-1.47405, 0.4, 0.3),
        (4.0, 0.2, -0.1), // omittedW requires canonical quaternion sign
    ] {
        let mut ship = PhysShip::new(
            spec(1, 1000.0, 1000.0, 113.7, yaw),
            longliner_model(),
            ShipGameView::default(),
        );
        ship.body.orientation =
            crate::systems::ai::numerics::Quat::from_yaw_pitch_roll(yaw, pitch, roll);
        ship.cached_water_surface = ship.body.position.y; // depth0 turns visual waves off
        ship.cached_floor_level = 80.0;
        let physical = super::super::cry::quat_to_phys(ship.body.orientation)
            .rotate(aaemu_physics::math::Vec3::new(0.0, 0.0, 1.0));
        let update =
            super::super::replication::send_updated_movement_data(&mut ship, &env, 0.04, 10_000);
        let MoveKind::Ship(m) = update.move_type.kind else {
            panic!("ship movement")
        };
        let wire = wire_up(&m);
        let world = PositionAndRotation {
            rot_x: update.rotation.0,
            rot_y: update.rotation.1,
            rot_z: update.rotation.2,
            ..Default::default()
        }
        .to_quaternion()
        .rotate([0.0, 0.0, 1.0]);
        let dot = physical.x * wire[0] + physical.y * wire[1] + physical.z * wire[2];
        println!("Longliner quaternion yaw/pitch/roll={yaw}/{pitch}/{roll}: physical={physical:?} wire={wire:?} world={world:?} dot={dot}");
        assert!(
            dot > 0.9999,
            "physical and transmitted up axes differ: {dot}"
        );
        for (a, b) in wire.into_iter().zip(world) {
            assert!((a - b).abs() < 0.0003);
        }
    }
}

#[test]
fn longliner_initial_tilt_recovers_shallow_and_deep_300_seconds() {
    let results: Vec<_> = [103.975, 80.0]
        .into_iter()
        .flat_map(|floor| {
            [0.0, -1.47405].map(|yaw| {
                simulate_longliner(
                    floor,
                    yaw,
                    longliner_model(),
                    Some(recorded_longliner_bounds()),
                    0.947846,
                )
            })
        })
        .collect();
    for result in results {
        assert!(
            result.tail_engine_tilt < 20.0f32.to_radians(),
            "physical initial tilt persists: {result:?}"
        );
        assert!(
            result.tail_wire_tilt < 25.0f32.to_radians(),
            "visual initial tilt persists: {result:?}"
        );
    }
}

#[test]
#[ignore]
fn real_longliner_authored_bounds_stability_shallow_deep_and_initial_tilt() {
    use crate::data::client_files::ClientFileManager;
    use crate::systems::ai::collision::{models::ModelCache, Files};
    let (Ok(db), Ok(pak)) = (
        std::env::var("AAEMU_COMPACT_DB"),
        std::env::var("AAEMU_GAME_PAK"),
    ) else {
        return;
    };
    let connection =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = crate::systems::ai::data::AiData::load_from(&connection).unwrap();
    let client = std::sync::Arc::new(ClientFileManager::from_sources(
        &[pak],
        std::path::Path::new("."),
    ));
    let files = Files::Client(client.clone());
    let cache = ModelCache::new(client);
    let reference = &data.ship_model_refs[&1360];
    let bounds = super::super::cry::model_collision_bounds(&files, &cache, reference)
        .expect("authored fishboat hull bounds");
    println!("Longliner1360 actual cached collision bounds: {bounds:?}");
    let recorded = recorded_longliner_bounds();
    assert_eq!(bounds.oriented, recorded.oriented);
    assert_eq!(bounds.basis, recorded.basis);
    for (loaded, expected) in [
        (bounds.center.x, recorded.center.x),
        (bounds.center.y, recorded.center.y),
        (bounds.center.z, recorded.center.z),
        (bounds.size.x, recorded.size.x),
        (bounds.size.y, recorded.size.y),
        (bounds.size.z, recorded.size.z),
    ] {
        assert!(
            (loaded - expected).abs() < 0.001,
            "cached bounds differ from recorded fixture: {loaded} != {expected}"
        );
    }
    assert!(
        bounds.center.z > 0.0,
        "mass box must not stand in for authored model bounds: {bounds:?}"
    );
    assert!(bounds.center.z - bounds.size.z > -5.0);
    assert!(super::super::cry::prefab_buoys(&files, &cache, reference).is_empty());
    let mut results = Vec::new();
    for floor in [103.975, 80.0] {
        for yaw in [0.0, -1.47405] {
            for initial_tilt in [0.0, 0.947846] {
                results.push(simulate_longliner(
                    floor,
                    yaw,
                    data.ship_models[&1360],
                    Some(bounds),
                    initial_tilt,
                ));
            }
        }
    }
    for result in results {
        assert!(
            result.max_z - result.min_z < 2.0,
            "persistent authored hull heave: {result:?}"
        );
        assert!(
            result.tail_engine_tilt < 20.0f32.to_radians(),
            "authored hull did not settle upright: {result:?}"
        );
        assert!(
            result.tail_wire_tilt < 25.0f32.to_radians(),
            "authored hull visual tilt: {result:?}"
        );
    }
}

#[test]
#[ignore]
fn real_compact_longliner_1360_matches_hydrostatic_fixture() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let connection =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = crate::systems::ai::data::AiData::load_from(&connection).unwrap();
    assert_eq!(data.ship_models[&1360], longliner_model());
    assert_eq!(
        data.ship_model_refs[&1360].to_ascii_lowercase(),
        "prefab://prefabs/fishboat.xml/fishboat.fishboat_body"
    );
}
