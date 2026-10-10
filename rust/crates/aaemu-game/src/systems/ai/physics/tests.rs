//! Shared test doubles and the tick-level flow tests of the ship physics.

use std::cell::RefCell;

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
}

impl FakeWorld {
    pub fn new(env: FakeEnv) -> Self {
        Self {
            env,
            view: ShipGameView::default(),
            doodads: Vec::new(),
            lifetime_calls: RefCell::new(Vec::new()),
            lines: Vec::new(),
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
        false
    }
    fn terrain(&self) -> Option<&dyn TerrainSamples> {
        None
    }
    fn bai_cell(&self, _cell_x: i32, _cell_y: i32) -> Option<[[Option<BaiRef>; 4]; 4]> {
        None
    }
}

pub fn cfg() -> PhysConfig {
    PhysConfig {
        wind: WindModel::Official,
        realistic_sea_weather: false,
        geo_data_mode: false,
        game_time: 0.0,
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
}
