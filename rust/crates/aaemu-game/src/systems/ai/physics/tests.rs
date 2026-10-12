//! Shared test doubles and the tick-level flow tests of the ship physics.

use std::cell::{Cell, RefCell};

use crate::systems::ai::geodata::manager::BaiRef;
use crate::systems::skills::controllers::Vec3;

use super::contacts::DoodadObstacle;
use super::harpoon::HarpoonLine;
use super::jitter::TerrainSamples;
use super::manager::{Pending, PhysConfig, TickWorld, WorldPhysics};
use super::ship::tests::{model, spec};
use super::ship::{PhysShip, ShipGameView};
use super::{PhysEnv, WindModel};

/// Heights from a closure, the ocean everywhere below `ocean`.
pub struct FakeEnv {
    pub ocean: f32,
    pub height: Box<dyn Fn(f32, f32) -> f32>,
}

impl FakeEnv {
    pub fn flat(ocean: f32, floor: f32) -> Self {
        Self {
            ocean,
            height: Box::new(move |_, _| floor),
        }
    }
}

impl PhysEnv for FakeEnv {
    fn height(&self, x: f32, y: f32) -> f32 {
        (self.height)(x, y)
    }

    fn water_surface(&self, _p: Vec3) -> (f32, Vec3) {
        (self.ocean, Vec3::new(0.0, 0.0, 0.0))
    }

    fn is_water(&self, p: Vec3) -> Option<Vec3> {
        (p.z <= self.ocean).then_some(Vec3::new(0.0, 0.0, 0.0))
    }

    fn ocean_level(&self) -> f32 {
        self.ocean
    }
}

/// A [`TickWorld`] with fixed views, doodads and a recorded harpoon lifetime call list.
pub struct FakeWorld {
    pub env: FakeEnv,
    pub view: ShipGameView,
    pub doodads: Vec<DoodadObstacle>,
    pub lifetime_calls: RefCell<Vec<u32>>,
    pub lines: Vec<(u32, HarpoonLine)>,
    /// The collision world for CryPhysics hulls.
    pub cry: Option<std::sync::Arc<crate::systems::ai::collision::world::PhysWorld>>,
    pub environment_queries: Cell<usize>,
}

impl FakeWorld {
    pub fn new(env: FakeEnv) -> Self {
        Self {
            env,
            view: ShipGameView::default(),
            doodads: Vec::new(),
            lifetime_calls: RefCell::new(Vec::new()),
            lines: Vec::new(),
            cry: None,
            environment_queries: Cell::new(0),
        }
    }
}

impl PhysEnv for FakeWorld {
    fn height(&self, x: f32, y: f32) -> f32 {
        self.env.height(x, y)
    }
    fn water_surface(&self, p: Vec3) -> (f32, Vec3) {
        self.env.water_surface(p)
    }
    fn is_water(&self, p: Vec3) -> Option<Vec3> {
        self.env.is_water(p)
    }
    fn ocean_level(&self) -> f32 {
        self.env.ocean
    }
}

impl TickWorld for FakeWorld {
    fn ship_view(&self, _obj_id: u32) -> Option<ShipGameView> {
        Some(self.view)
    }
    fn harpoon_lines(&self, obj_id: u32) -> Vec<HarpoonLine> {
        self.lines
            .iter()
            .filter(|(id, _)| *id == obj_id)
            .map(|(_, l)| *l)
            .collect()
    }
    fn harpoon_recoil(&self, _obj_id: u32, _dt: f32) -> (f32, f32) {
        (0.0, 0.0)
    }
    fn harpoon_lifetime(&self, obj_id: u32) {
        self.lifetime_calls.borrow_mut().push(obj_id);
    }
    fn doodads_around(&self, _obj_id: u32, _radius: f32) -> Vec<DoodadObstacle> {
        self.doodads.clone()
    }
    fn whirlpools(&self) -> Vec<(f32, f32)> {
        Vec::new()
    }
    fn any_whirlpool_buff(&self) -> bool {
        self.environment_queries
            .set(self.environment_queries.get() + 1);
        false
    }
    fn terrain(&self) -> Option<&dyn TerrainSamples> {
        None
    }
    fn bai_cell(&self, _cell_x: i32, _cell_y: i32) -> Option<[[Option<BaiRef>; 4]; 4]> {
        None
    }
    fn cry_world(&self) -> Option<std::sync::Arc<crate::systems::ai::collision::world::PhysWorld>> {
        self.environment_queries
            .set(self.environment_queries.get() + 1);
        self.cry.clone()
    }
}

pub fn cfg() -> PhysConfig {
    PhysConfig {
        wind: WindModel::Official,
        realistic_sea_weather: false,
        geo_data_mode: false,
        game_time: 0.0,
        cryphysics: false,
    }
}

fn add(wp: &WorldPhysics, obj_id: u32, x: f32, y: f32, z: f32, yaw: f32) {
    let ship = PhysShip::new(spec(obj_id, x, y, z, yaw), model(), ShipGameView::default());
    wp.enqueue(Pending::Add(Box::new(ship)));
}

#[test]
fn a_driven_ship_floats_sails_and_broadcasts_its_movement() {
    let w = FakeWorld::new(FakeEnv::flat(100.0, 40.0));
    let wp = WorldPhysics::new(0);
    add(&wp, 1, 1000.0, 1000.0, 99.0, 0.0);
    wp.enqueue(Pending::Driver {
        obj_id: 1,
        has_driver: true,
    });
    wp.enqueue(Pending::Request {
        obj_id: 1,
        throttle: 127,
        steering: 0,
    });
    let mut now = 10_000;
    let mut last = None;
    for _ in 0..250 {
        now += 40;
        let out = wp.tick(&w, 40, now, cfg());
        assert_eq!(out.movements.len(), 1);
        last = Some(out.movements[0].clone());
    }
    let snap = wp.snapshot(1).expect("snapshot");
    // floats around the ocean surface (buoyancy vs gravity, vertical damping)
    assert!(
        (snap.position.z - 100.0).abs() < 2.0,
        "z {}",
        snap.position.z
    );
    // moved north (yaw 0: bow along game +Y)
    assert!(snap.position.y > 1010.0, "y {}", snap.position.y);
    assert!((snap.position.x - 1000.0).abs() < 1.0);
    assert!(snap.speed > 5.0 && snap.throttle > 100);
    let m = last.expect("movement");
    let crate::models::movement::MoveKind::Ship(sm) = m.move_type.kind else {
        panic!("ship move expected")
    };
    // hull velocity = Speed * MoveSpeedMul / 4 along the bow (C#), * 2048 on the wire
    assert!(sm.vel_y > 2900, "replicated velocity {}", sm.vel_y);
    assert_eq!(m.move_type.time, now as u32); // spawn time 0
                                              // the driver leaves: helm reset, the hull coasts down
    wp.enqueue(Pending::Driver {
        obj_id: 1,
        has_driver: false,
    });
    for _ in 0..25 {
        now += 40;
        wp.tick(&w, 40, now, cfg());
    }
    let snap2 = wp.snapshot(1).expect("snapshot");
    assert_eq!((snap2.throttle, snap2.throttle_request), (0, 0));
    assert!(snap2.speed < snap.speed);
    assert!(w.lifetime_calls.borrow().len() >= 275);
    // removal
    wp.enqueue(Pending::Remove(1));
    let out = wp.tick(&w, 40, now + 40, cfg());
    assert!(out.movements.is_empty());
    assert!(wp.snapshot(1).is_none());
}

#[test]
fn summoning_ships_wait_for_the_portal_time() {
    let w = FakeWorld::new(FakeEnv::flat(100.0, 40.0));
    let wp = WorldPhysics::new(0);
    let mut s = spec(1, 0.0, 0.0, 99.0, 0.0);
    s.spawn_time_ms = 1_000;
    s.portal_time = 2.0;
    wp.enqueue(Pending::Add(Box::new(PhysShip::new(
        s,
        model(),
        ShipGameView::default(),
    ))));
    let out = wp.tick(&w, 40, 2_000, cfg());
    assert!(out.movements.is_empty());
    wp.inspect(|st| assert!(!st.ships[0].body.affected_by_gravity));
    let out = wp.tick(&w, 40, 3_100, cfg());
    assert_eq!(out.movements.len(), 1);
}

#[test]
fn two_ships_ramming_separate_and_take_hull_damage() {
    let w = FakeWorld::new(FakeEnv::flat(100.0, 40.0));
    let wp = WorldPhysics::new(0);
    // b sits 8 m ahead of a (bow along +Y); a rams it at full throttle
    add(&wp, 1, 0.0, 0.0, 99.5, 0.0);
    add(&wp, 2, 0.0, 16.0, 99.5, 0.0);
    wp.enqueue(Pending::Driver {
        obj_id: 1,
        has_driver: true,
    });
    wp.enqueue(Pending::Request {
        obj_id: 1,
        throttle: 127,
        steering: 0,
    });
    let mut damage = Vec::new();
    let mut now = 0;
    for _ in 0..150 {
        now += 40;
        damage.extend(wp.tick(&w, 40, now, cfg()).damage);
    }
    assert!(!damage.is_empty(), "the hulls collided");
    assert!(damage.iter().any(|d| d.target == 2 && d.attacker == 1));
    let a = wp.snapshot(1).unwrap();
    let b = wp.snapshot(2).unwrap();
    // never deeply inside each other (14 m hulls, centres stay ~ a hull length apart)
    assert!(
        b.position.y - a.position.y > 12.0,
        "{} {}",
        a.position.y,
        b.position.y
    );
}

/// A FakeWorld whose hulls run on the CryPhysics port: terrain plane at `floor`, ocean at
/// `ocean`, 2 × 2 cells.
fn cry_world(ocean: f32, floor: f32) -> FakeWorld {
    let mut w = FakeWorld::new(FakeEnv::flat(ocean, floor));
    w.cry = Some(std::sync::Arc::new(
        crate::systems::ai::collision::world::PhysWorld::new(
            0,
            0,
            "test",
            2,
            2,
            ocean,
            Some(std::sync::Arc::new(
                crate::systems::ai::collision::terrain::tests::Plane {
                    a: floor,
                    b: 0.0,
                    c: 0.0,
                    n: 1024,
                },
            )),
        ),
    ));
    w
}

fn cry_cfg() -> PhysConfig {
    PhysConfig {
        cryphysics: true,
        ..cfg()
    }
}

#[test]
fn cryphysics_ship_floats_and_sails() {
    let w = cry_world(100.0, 40.0);
    let wp = WorldPhysics::new(0);
    add(&wp, 1, 1000.0, 1000.0, 99.0, 0.0);
    wp.enqueue(Pending::Driver {
        obj_id: 1,
        has_driver: true,
    });
    wp.enqueue(Pending::Request {
        obj_id: 1,
        throttle: 127,
        steering: 0,
    });
    let mut now = 10_000;
    for _ in 0..250 {
        now += 40;
        let out = wp.tick(&w, 40, now, cry_cfg());
        assert_eq!(out.movements.len(), 1);
    }
    let snap = wp.snapshot(1).expect("snapshot");
    assert!(
        (snap.position.z - 100.0).abs() < 2.0,
        "floats at the ocean: z {}",
        snap.position.z
    );
    assert!(snap.position.y > 1010.0, "y {}", snap.position.y);
    assert!(
        (snap.position.x - 1000.0).abs() < 1.0,
        "x {}",
        snap.position.x
    );
    let cry = w.cry.as_ref().unwrap();
    assert_eq!(wp.inspect(|st| st.cry.ents.len()), 1);
    // removal destroys the hull entity
    wp.enqueue(Pending::Remove(1));
    wp.tick(&w, 40, now + 40, cry_cfg());
    assert_eq!(wp.inspect(|st| st.cry.ents.len()), 0);
    assert!(cry.lock().owners.values().all(|owner| !matches!(
        owner,
        crate::systems::ai::collision::world::Owner::Object {
            kind: crate::systems::ai::collision::world::ObjKind::Ship,
            ..
        }
    )));
    let queries = w.environment_queries.get();
    for _ in 0..100 {
        let out = wp.tick(&w, 40, now + 80, cry_cfg());
        assert!(out.movements.is_empty());
        assert!(out.damage.is_empty());
    }
    assert_eq!(
        w.environment_queries.get(),
        queries,
        "an empty world skips simulation queries after the final hull is cleaned up"
    );
}

/// Settle and drive the actual 1.2 clipper's buoyancy parts in deep water at two headings.
#[test]
#[ignore]
fn real_clipper_prefab_buoyancy_draft() {
    use crate::data::client_files::ClientFileManager;
    use crate::systems::ai::collision::{models::ModelCache, Files};
    let (Ok(db), Ok(pak)) = (
        std::env::var("AAEMU_COMPACT_DB"),
        std::env::var("AAEMU_GAME_PAK"),
    ) else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let data = crate::systems::ai::data::AiData::load_from(&conn).unwrap();
    let client = std::sync::Arc::new(ClientFileManager::from_sources(
        &[pak],
        std::path::Path::new("."),
    ));
    let files = Files::Client(client.clone());
    let models = ModelCache::new(client);
    let buoys = super::cry::prefab_buoys(&files, &models, &data.ship_model_refs[&128]);
    assert_eq!(buoys.len(), 2, "the clipper's two authored buoys must load");
    assert!((buoys[0].pos.z - 0.1499939).abs() < 1e-5);
    assert!((buoys[1].pos.z - 0.1000061).abs() < 1e-5);
    assert!((buoys[0].scale - 2.9766002).abs() < 1e-5);
    let rowboat = super::cry::prefab_buoys(&files, &models, &data.ship_model_refs[&129]);
    assert!(
        rowboat.is_empty(),
        "the rowboat keeps its explicit tube geometry"
    );
    let simulate = |parts, yaw| {
        let mut w = cry_world(100.0, 40.0);
        w.view.move_speed_mul = 4.0;
        let wp = WorldPhysics::new(0);
        let mut spec = super::ship::tests::spec(1, 1000.0, 1000.0, 100.0, yaw);
        spec.kind = super::SlaveKind::SPEEDBOAT;
        let mut ship =
            super::ship::PhysShip::new(spec, data.ship_models[&128], ShipGameView::default());
        ship.buoys = parts;
        wp.enqueue(Pending::Add(Box::new(ship)));
        let mut height = Vec::new();
        for i in 0..1500 {
            wp.tick(&w, 40, 10_000 + i * 40, cry_cfg());
            let snap = wp.snapshot(1).unwrap();
            assert!(snap.position.z.is_finite());
            if i >= 1250 {
                height.push(snap.position.z);
            }
        }
        let mean = height.iter().sum::<f32>() / height.len() as f32;
        let min = height.iter().copied().fold(f32::INFINITY, f32::min);
        let max = height.iter().copied().fold(f32::NEG_INFINITY, f32::max);
        wp.enqueue(Pending::Driver {
            obj_id: 1,
            has_driver: true,
        });
        let mut moving_max_z = f32::NEG_INFINITY;
        let mut moving_max_tilt = 0.0f32;
        let mut peak_speed = 0.0f32;
        for i in 0..400 {
            let (throttle, steering) = if i < 100 {
                (127, 0)
            } else if i < 175 {
                (127, 127)
            } else {
                (-127, 0)
            };
            wp.enqueue(Pending::Request {
                obj_id: 1,
                throttle,
                steering,
            });
            wp.tick(&w, 40, 70_000 + i * 40, cry_cfg());
            let snap = wp.snapshot(1).unwrap();
            moving_max_z = moving_max_z.max(snap.position.z);
            peak_speed = peak_speed.max(snap.speed.abs());
            let tilt = wp.inspect(|st| {
                let q = super::cry::quat_to_phys(st.ships[0].body.orientation);
                let up = q.rotate(aaemu_physics::math::Vec3::new(0.0, 0.0, 1.0));
                up.z.clamp(-1.0, 1.0).acos()
            });
            moving_max_tilt = moving_max_tilt.max(tilt);
        }
        (mean, min, max, moving_max_z, moving_max_tilt, peak_speed)
    };
    for yaw in [0.0, 2.26423] {
        let baseline = simulate(Vec::new(), yaw);
        let authored = simulate(buoys.clone(), yaw);
        println!(
            "clipper root z, ocean 100, yaw {yaw}: fallback {baseline:?}, authored {authored:?}"
        );
        assert!(
            (99.75..99.9).contains(&authored.0),
            "authored buoy waterline: {authored:?}"
        );
        assert!(
            authored.2 - authored.1 < 0.01,
            "settled draft remains stable"
        );
        assert!(authored.5 > 1.0, "the helm actually drives the ship");
    }
}

#[test]
fn cryphysics_ships_push_each_other_and_report_contacts() {
    let w = cry_world(100.0, 40.0);
    let wp = WorldPhysics::new(0);
    // b sits a hull length ahead of a; a pushes it at full throttle (~1.5 m/s with this model)
    add(&wp, 1, 500.0, 500.0, 99.5, 0.0);
    add(&wp, 2, 500.0, 516.0, 99.5, 0.0);
    wp.enqueue(Pending::Driver {
        obj_id: 1,
        has_driver: true,
    });
    wp.enqueue(Pending::Request {
        obj_id: 1,
        throttle: 127,
        steering: 0,
    });
    let mut damage = Vec::new();
    let mut now = 0;
    for _ in 0..600 {
        now += 40;
        damage.extend(wp.tick(&w, 40, now, cry_cfg()).damage);
    }
    assert!(wp.inspect(|st| st.cry.events_seen) > 0, "collision events");
    let a = wp.snapshot(1).unwrap();
    let b = wp.snapshot(2).unwrap();
    assert!(b.position.y > 520.0, "b was pushed: {}", b.position.y);
    assert!(
        b.position.y - a.position.y > 12.0,
        "{} {}",
        a.position.y,
        b.position.y
    );
    // a push below the ZWUnitCollision minimum closing speed deals no damage
    assert!(damage.is_empty(), "{damage:?}");
}

#[test]
fn unit_collisions_keep_the_pair_maximum_for_500_ms() {
    use super::cry::UnitCollisions;
    let ships = [
        PhysShip::new(
            spec(1, 0.0, 0.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        ),
        PhysShip::new(
            spec(2, 0.0, 14.0, 99.0, 0.0),
            model(),
            ShipGameView::default(),
        ),
    ];
    let p = aaemu_physics::math::Vec3::ZERO;
    let mut u = UnitCollisions::default();
    u.record(Some(1), Some(2), 1.5, p, 0); // below the minimum: dropped
    u.record(Some(2), Some(1), 4.0, p, 0);
    u.record(Some(1), Some(2), 6.0, p, 100);
    u.record(Some(1), None, 30.0, p, 100); // vs terrain: clamped to the maximum
    let d = u.flush(&ships, 600);
    let pct = |t, a| {
        d.iter()
            .find(|h| h.target == t && h.attacker == a)
            .map(|h| h.percent)
    };
    let scaled6 = super::contacts::speed_scaled_hull_damage_percent(6.0);
    // a's bow points at b (nose cone): minimum; b is hit at 6 m/s
    assert_eq!(pct(1, 2), Some(1));
    assert_eq!(pct(2, 1), Some(scaled6));
    assert_eq!(pct(1, 1), Some(10));
    // within 500 ms of the flush nothing is reported
    u.record(Some(1), Some(2), 8.0, p, 700);
    assert!(u.flush(&ships, 800).is_empty());
    assert_eq!(u.flush(&ships, 1100).len(), 2);
}

#[test]
fn cryphysics_ship_runs_aground_on_the_terrain() {
    // the sea floor rises: 0.5 m deep water everywhere → the hull rests on the terrain
    let w = cry_world(100.0, 99.5);
    let wp = WorldPhysics::new(0);
    add(&wp, 1, 500.0, 500.0, 102.0, 0.0);
    let mut now = 0;
    for _ in 0..150 {
        now += 40;
        wp.tick(&w, 40, now, cry_cfg());
    }
    let s = wp.snapshot(1).unwrap();
    // the bottom box rests on z = 99.5 (hull box half height 1.5 + bottom box 0.3)
    assert!(s.position.z > 99.5 + 1.0, "z {}", s.position.z);
}

#[test]
fn cryphysics_ship_floats_on_a_lake_above_the_ocean() {
    use crate::systems::ai::physics::water::{WaterBodyArea, WaterBodyAreaType};
    // terrain at 140, ocean at 100, a 200 × 200 m lake at 150 (10 m deep)
    let w = cry_world(100.0, 140.0);
    let mut lake = WaterBodyArea::new("lake", WaterBodyAreaType::Polygon);
    lake.depth = 10.0;
    lake.points = vec![
        Vec3::new(400.0, 400.0, 150.0),
        Vec3::new(600.0, 400.0, 150.0),
        Vec3::new(600.0, 600.0, 150.0),
        Vec3::new(400.0, 600.0, 150.0),
    ];
    lake.update_bounds();
    {
        let mut st = w.cry.as_ref().unwrap().lock();
        assert_eq!(
            crate::systems::ai::collision::water_areas::sync_areas(&mut st, &[lake.clone()]),
            1
        );
        // idempotent
        assert_eq!(
            crate::systems::ai::collision::water_areas::sync_areas(&mut st, &[lake]),
            0
        );
    }
    let wp = WorldPhysics::new(0);
    add(&wp, 1, 500.0, 500.0, 150.0, 0.0);
    let mut now = 0;
    for _ in 0..200 {
        now += 40;
        wp.tick(&w, 40, now, cry_cfg());
    }
    let s = wp.snapshot(1).unwrap();
    assert!(
        (s.position.z - 150.0).abs() < 2.0,
        "floats on the lake: z {}",
        s.position.z
    );
}

mod server_flow {
    use std::sync::Arc;

    use crate::data::GameData;
    use crate::models::slave::Slave;
    use crate::opcodes::sc;
    use crate::server::GameServer;
    use crate::systems::ai::physics::{self, ship::ShipSpec, SlaveKind};
    use crate::systems::skills::controllers::Vec3;
    use crate::test_support::*;
    use crate::world::{Entity, SlaveRef, WorldObject};

    const SHIP_MODEL: u32 = 31;

    fn server() -> Arc<GameServer> {
        let mut data = GameData::empty();
        data.systems
            .ai
            .ai
            .ship_models
            .insert(SHIP_MODEL, super::model());
        let server = Arc::new(test_server_with(data));
        server.set_self();
        crate::systems::skills::bind(&server);
        server.systems.ai.physics.set_manual_ticks(true);
        server
    }

    fn spawn_slave(server: &Arc<GameServer>, obj_id: u32, x: f32, y: f32) -> SlaveRef {
        let mut s = Slave::new(obj_id + 5000, 77);
        s.unit.hp = 10_000;
        s.unit.max_hp = 10_000;
        s.unit.scale = 1.0;
        s.unit.transform.set_xyz(x, y, 99.0);
        s.unit.transform.zone_id = TEST_ZONE;
        let slave = Entity::new(obj_id, s);
        let world = server.world.world(0).expect("world");
        world.spawn(&slave, &server.view_ctx());
        let spec = ShipSpec {
            obj_id,
            slave_id: obj_id + 5000,
            model_id: SHIP_MODEL,
            kind: SlaveKind::BOAT,
            scale: 1.0,
            spawn_time_ms: 0,
            portal_time: 0.0,
            position: Vec3::new(x, y, 99.0),
            yaw: 0.0,
        };
        assert!(physics::add_ship(server, 0, spec));
        slave
    }

    #[tokio::test]
    async fn ships_move_their_slave_broadcast_and_damage_each_other() {
        let server = server();
        let mut client = TestClient::new(&server, 1, 1);
        let _p = spawn_player(
            &server,
            &client,
            test_character(1, "Sailor", 500.0, 480.0),
            0x100,
        );
        let a = spawn_slave(&server, 0x300, 500.0, 500.0);
        let _b = spawn_slave(&server, 0x301, 500.0, 515.0);
        client.drain();
        physics::set_ship_driver(&server, 0x300, true);
        physics::set_ship_request(&server, 0x300, 127, 0);
        let wp = server.systems.ai.physics.world(0).expect("physics world");
        for _ in 0..100 {
            physics::run_world_tick(&server, &wp, 40);
        }
        let ops: Vec<u16> = client.drain().into_iter().map(|f| f.0 & 0x7FFF).collect();
        assert!(ops.contains(&sc::SCOneUnitMovementPacket), "{ops:?}");
        assert!(
            ops.contains(&sc::SCEnvDamagePacket),
            "hull collision damage"
        );
        let t = a.data().unit.transform;
        assert!(t.y > 501.0, "the slave moved: {}", t.y);
        let hp = a.data().unit.hp;
        assert!(hp < 10_000, "rammed ship took damage: {hp}");
        let snap = physics::ship_state(&server, 0x300).expect("state");
        assert!(snap.throttle > 0);
        // siege AoE vs the hull box
        assert!(physics::siege_point_hits_ship(
            &server,
            0x300,
            snap.mass_box_center.0 + 3.0,
            snap.mass_box_center.1,
            1.5
        ));
        assert!(!physics::siege_point_hits_ship(
            &server,
            0x300,
            snap.mass_box_center.0 + 10.0,
            snap.mass_box_center.1,
            1.5
        ));
        // removal
        physics::remove_ship(&server, 0x300);
        physics::run_world_tick(&server, &wp, 40);
        assert!(physics::ship_state(&server, 0x300).is_none());
        let _ = WorldObject::from(a);
    }

    #[tokio::test]
    async fn cryphysics_ships_through_the_server_collision_world() {
        let server = server();
        server
            .systems
            .ai
            .collision
            .test_files
            .set(Arc::new(std::collections::HashMap::new()))
            .unwrap();
        server
            .systems
            .ai
            .collision
            .test_terrain
            .set(Arc::new(
                crate::systems::ai::collision::terrain::tests::Plane {
                    a: 40.0,
                    b: 0.0,
                    c: 0.0,
                    n: 1024,
                },
            ))
            .unwrap_or_else(|_| panic!("terrain"));
        let mut client = TestClient::new(&server, 1, 1);
        let _p = spawn_player(
            &server,
            &client,
            test_character(1, "Sailor", 500.0, 480.0),
            0x100,
        );
        let a = spawn_slave(&server, 0x300, 500.0, 500.0);
        client.drain();
        physics::set_ship_driver(&server, 0x300, true);
        physics::set_ship_request(&server, 0x300, 127, 0);
        let wp = server.systems.ai.physics.world(0).expect("physics world");
        for _ in 0..100 {
            physics::run_world_tick(&server, &wp, 40);
        }
        // the hull lives in the collision world
        assert_eq!(wp.inspect(|st| st.cry.ents.len()), 1);
        let ops: Vec<u16> = client.drain().into_iter().map(|f| f.0 & 0x7FFF).collect();
        assert!(ops.contains(&sc::SCOneUnitMovementPacket), "{ops:?}");
        let t = a.data().unit.transform;
        assert!(t.y > 501.0, "the slave moved: {}", t.y);
        // the synthetic world's ocean level
        let ocean = server.world.world(0).unwrap().template.ocean_level;
        assert!((t.z - ocean).abs() < 2.0, "floats: {} vs {ocean}", t.z);
    }
}
