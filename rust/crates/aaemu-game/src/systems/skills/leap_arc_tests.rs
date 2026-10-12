//! Verified fixed-target Leap subset; foreign unit targets deliberately keep the legacy path.

use std::collections::HashMap;

use aaemu_commons::PacketStream;
use aaemu_physics::geometry::primitives::{BoxPrim, Primitive};
use aaemu_physics::math::{Quat, Vec3 as PhysicsVec3};

use super::*;
use crate::models::transform::PositionAndRotation;
use crate::models::unit::{Bonus, BonusTemplate, UnitAttribute, UnitModifierType};
use crate::systems::ai::collision::models::{ModelPart, ModelPhys};
use crate::systems::ai::collision::terrain::TerrainSource;
use crate::systems::ai::collision::world::{CellContent, StaticInstance};
use crate::systems::skills::geometry::PosUnit;

fn fish_jump() -> SkillControllerTemplate {
    // Real1.2 controller7040, skill21289/plot780, including flags (not an angle-only fixture).
    SkillControllerTemplate {
        id: 7040,
        kind_id: 2,
        value: [80, 4, 1000, 4000, 0, 0, 0, 1, 1, 1, 0, 1, 0, 0, 0],
        ..SkillControllerTemplate::default()
    }
}

fn near(a: f32, b: f32) {
    assert!((a - b).abs() < 0.001, "{a} != {b}");
}

#[test]
fn fish_7040_ballistics_rise_fall_and_land_with_native_initial_math() {
    let start = Vec3::new(0.0, 0.0, 100.0);
    let mut arc = LeapArc::new(
        &fish_jump(),
        start,
        start,
        Some(Vec3::new(0.0, 1.0, 0.0)),
        1.0,
    )
    .unwrap();
    near(arc.horizontal_speed, 4.0);
    near(arc.vertical_speed, 22.685127);
    near(arc.vertical_acceleration, -45.370255);
    let (peak, up, ended) = arc.advance(start, 0.5).unwrap();
    near(peak.y, 2.0);
    near(peak.z, 105.67128);
    near(up.z, 0.0);
    assert!(!ended);
    let (land, down, ended) = arc.advance(peak, 0.5).unwrap();
    near(land.y, 4.0);
    near(land.z, 100.0);
    assert!(down.z < -22.6 && ended);
    assert!(arc.advance(land, 0.1).is_none());
}

#[test]
fn position_offset_is_3d_and_direction_flag_does_not_run_unit_target_gate() {
    let mut t = fish_jump();
    t.value[0] = 0;
    t.value[1] = 0;
    t.value[3] = -5000;
    t.value[6] = 2;
    t.value[11] = 0;
    let arc = LeapArc::new(&t, Vec3::default(), Vec3::new(6.0, 0.0, 8.0), None, 1.0).unwrap();
    near(arc.end_position.x, 3.0);
    near(arc.end_position.z, 4.0);
    near(arc.horizontal_speed, 3.0);
    near(arc.vertical_speed, 4.0);
    near(arc.vertical_acceleration, 0.0);
    // Native position Init does not apply the foreign-unit horizontal direction gate.
    assert_ne!(arc.end_position, Vec3::default());
}

#[test]
fn speed_cap_modifier_and_zero_xy_boundaries() {
    let mut t = fish_jump();
    let start = Vec3::default();
    let arc = LeapArc::new(&t, start, start, Some(Vec3::new(0.0, 1.0, 0.0)), 2.0).unwrap();
    near(arc.duration, 0.5);
    near(arc.horizontal_speed, 8.0);
    t.value[3] = 0;
    t.value[1] = 60;
    t.value[11] = 0;
    let arc = LeapArc::new(&t, start, Vec3::new(100.0, 0.0, 100.0), None, 1.0).unwrap();
    near(arc.horizontal_speed, 60.0); // min-speed is not clipped by dynamic50cap
    t.value[1] = 0;
    let arc = LeapArc::new(&t, start, Vec3::new(100.0, 0.0, 100.0), None, 1.0).unwrap();
    near(arc.horizontal_speed, 50.0);
    near(
        arc.vertical_speed / arc.horizontal_speed,
        deg_to_rad_f32(80.0).tan(),
    );
    // Actual7071: angle80/speed7/duration1000/self offset0 does not imply an in-place arc.
    t.value[1] = 7;
    assert!(LeapArc::new(&t, start, start, Some(Vec3::new(0.0, 1.0, 0.0)), 1.0).is_none());
    assert!(LeapArc::new(&t, start, Vec3::new(0.0, 0.0, 20.0), None, 1.0).is_none());
    t.value[2] = 0;
    assert!(LeapArc::new(&t, start, Vec3::new(1.0, 0.0, 0.0), None, 1.0).is_none());
}

#[tokio::test]
async fn self_target_uses_quaternion_forward_and_animation_modifier_not_movement_slow() {
    let (server, mut client, _player, npc) = super::tests::setup();
    npc.data().unit.transform.rot_z = std::f32::consts::FRAC_PI_2;
    for (index, attribute, value) in [
        (1, UnitAttribute::AttackAnimSpeedMul, 1000),
        (2, UnitAttribute::MoveSpeedMul, -1000),
    ] {
        npc.data().unit.bonuses.add_bonus(
            index,
            Bonus {
                template: BonusTemplate {
                    attribute,
                    modifier_type: UnitModifierType::Value,
                    value,
                    linear_level_bonus: 0,
                },
                value,
            },
        );
    }
    let owner = WorldObject::from(&npc);
    let sc = create(
        &server,
        &fish_jump(),
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    let Behavior::BallisticLeap(arc) = sc.behavior() else {
        panic!("self leap must use arc")
    };
    near(arc.end_position.x, 101.0);
    near(arc.end_position.y, 100.0);
    near(arc.duration, 0.5);
    near(arc.horizontal_speed, 8.0);
    sc.lock().state = ScState::Running;
    owner.with_unit(|u| u.unit_mut().skill_state.active_skill_controller = Some(sc.clone()));
    client.drain();
    sc.tick(&server, Duration::from_millis(125));
    let tr = npc.data().unit.transform;
    near(tr.x, 104.0);
    assert!(tr.z > 104.0); // a short vertical step survives generic ground snap
    let frame = client
        .drain()
        .into_iter()
        .find(|(op, _)| *op == crate::opcodes::sc::SCOneUnitMovementPacket)
        .unwrap()
        .1;
    let mut s = PacketStream::from_bytes(frame);
    assert_eq!(s.read_bc().unwrap(), npc.obj_id);
    let type_byte = s.read_u8().unwrap();
    let mt = MoveType::read(type_byte, &mut s).unwrap();
    let MoveKind::Unit(um) = mt.kind else {
        panic!("unit movement")
    };
    assert_eq!(um.vel_x, -8000);
    assert!(um.vel_z > 22000);
    near(um.z, tr.z);
    sc.end(&server, true);
}

#[tokio::test]
async fn foreign_unit_unknown_landing_flag_and_parented_owner_keep_linear_behavior() {
    let (server, _client, player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    let t = fish_jump();
    let foreign = Target::Obj(WorldObject::from(&player));
    let sc = create(&server, &t, &owner, &foreign, 0.0, 0.0, 0.0, 0.0).unwrap();
    assert!(matches!(sc.behavior(), Behavior::Leap(_)));
    let mut t = t;
    t.value[12] = 1;
    let sc = create(
        &server,
        &t,
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    assert!(matches!(sc.behavior(), Behavior::Leap(_)));
    t.value[12] = 0;
    npc.data().unit.transform.parent_obj_id = player.obj_id;
    let sc = create(
        &server,
        &t,
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    assert!(matches!(sc.behavior(), Behavior::Leap(_)));
}

#[tokio::test]
async fn deterministic_tick_lands_stops_and_clears_active_controller() {
    let (server, mut client, _player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    let sc = create(
        &server,
        &fish_jump(),
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    sc.lock().state = ScState::Running;
    owner.with_unit(|u| u.unit_mut().skill_state.active_skill_controller = Some(sc.clone()));
    client.drain();
    for _ in 0..8 {
        sc.tick(&server, Duration::from_millis(125));
    }
    assert_eq!(sc.state(), ScState::Ended);
    assert!(active(&owner).is_none());
    let tr = npc.data().unit.transform;
    near(tr.y, 104.0);
    near(tr.z, 100.0);
    let moves = client.drain();
    assert_eq!(moves.len(), 9); // eight arc steps plus authoritative stop, no async timer
    let mut s = PacketStream::from_bytes(moves.last().unwrap().1.clone());
    s.read_bc().unwrap();
    let type_byte = s.read_u8().unwrap();
    let mt = MoveType::read(type_byte, &mut s).unwrap();
    assert!(mt.flags.contains(MoveTypeFlags::STOPPING));
    assert_eq!(mt.velocity(), (0, 0, 0));
}

#[tokio::test]
async fn fixed_position_target_keeps_native_elevation_and_emits_vertical_velocity() {
    let (server, mut client, _player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    let target = Target::Pos(PosUnit {
        instance_id: 0,
        zone_id: 0,
        pos: PositionAndRotation {
            x: 110.0,
            y: 100.0,
            z: 105.0,
            ..Default::default()
        },
    });
    let mut t = fish_jump();
    t.value[0] = 0;
    t.value[1] = 0;
    t.value[3] = 0;
    t.value[6] = 2; // Pos Init does not run the Unit Init direction gate
    t.value[8] = 0; // preserve owner rotation
    t.value[11] = 0;
    let sc = create(&server, &t, &owner, &target, 0.0, 0.0, 0.0, 0.0).unwrap();
    assert!(matches!(sc.behavior(), Behavior::BallisticLeap(_)));
    sc.lock().state = ScState::Running;
    client.drain();
    sc.tick(&server, Duration::from_millis(125));
    let tr = npc.data().unit.transform;
    near(tr.x, 105.625);
    near(tr.z, 100.625);
    near(tr.rot_z, 0.0);
    let frame = client.drain().pop().unwrap().1;
    let mut s = PacketStream::from_bytes(frame);
    s.read_bc().unwrap();
    let type_byte = s.read_u8().unwrap();
    let mt = MoveType::read(type_byte, &mut s).unwrap();
    assert_eq!(mt.velocity(), (5000, 0, 5000));
    sc.end(&server, true);
}

#[test]
fn native_stop_on_predicted_overshoot_does_not_teleport_or_divide_by_zero() {
    let mut t = fish_jump();
    t.value[1] = 60;
    let start = Vec3::default();
    let mut arc = LeapArc::new(&t, start, start, Some(Vec3::new(0.0, 1.0, 0.0)), 1.0).unwrap();
    assert!(arc.advance(start, 0.125).is_none());
    assert_eq!(arc.elapsed, 0.0);
    assert!(arc.advance(start, f32::NAN).is_none());
    assert!(arc.advance(start, 0.0).is_none());
    t.value[7] = 0;
    let mut arc = LeapArc::new(&t, start, start, Some(Vec3::new(0.0, 1.0, 0.0)), 1.0).unwrap();
    let (end, velocity, ended) = arc.advance(start, 0.125).unwrap();
    near(end.y, 4.0);
    assert!(end.z.is_finite() && velocity.z.is_finite() && ended);
}

struct Ground;
impl TerrainSource for Ground {
    fn sample(&self, _x: i32, _y: i32) -> f32 {
        100.0
    }
    fn size(&self) -> (i32, i32) {
        (1024, 1024)
    }
}

#[tokio::test]
async fn arc_clears_low_obstacle_and_stops_at_high_solid_contact() {
    for high in [false, true] {
        let (server, _client, _player, npc) = super::tests::setup();
        server
            .systems
            .ai
            .collision
            .test_files
            .set(Arc::new(HashMap::new()))
            .unwrap();
        server
            .systems
            .ai
            .collision
            .test_terrain
            .set(Arc::new(Ground))
            .unwrap_or_else(|_| panic!("terrain"));
        let pw = crate::systems::ai::collision::phys_world(&server, 0).unwrap();
        let geometry = pw
            .lock()
            .world
            .register_primitive(&Primitive::Box(BoxPrim::aligned(
                PhysicsVec3::ZERO,
                PhysicsVec3::new(1.0, 0.1, if high { 10.0 } else { 0.5 }),
            )))
            .unwrap();
        let model = Arc::new(ModelPhys {
            path: "test/leap-obstacle.cgf".into(),
            parts: vec![ModelPart {
                slot: 0,
                geom: geometry,
            }],
            blob_bytes: 0,
        });
        pw.lock().insert_cell(CellContent {
            cell: (0, 0),
            instances: vec![StaticInstance {
                model,
                pos: PhysicsVec3::new(105.0, 102.0, if high { 110.0 } else { 100.5 }),
                q: Quat::IDENTITY,
                scale: 1.0,
                flags_default: 0x18FFF,
                density: 0.0,
                ent_flags_or: 0x8000,
                vegetation: false,
            }],
            ..Default::default()
        });
        let owner = WorldObject::from(&npc);
        let sc = create(
            &server,
            &fish_jump(),
            &owner,
            &Target::Obj(owner.clone()),
            0.0,
            0.0,
            0.0,
            0.0,
        )
        .unwrap();
        sc.lock().state = ScState::Running;
        owner.with_unit(|u| u.unit_mut().skill_state.active_skill_controller = Some(sc.clone()));
        for _ in 0..8 {
            if sc.state() == ScState::Ended {
                break;
            }
            sc.tick(&server, Duration::from_millis(125));
        }
        assert_eq!(sc.state(), ScState::Ended);
        let tr = npc.data().unit.transform;
        if high {
            assert!(tr.y < 101.9 && tr.z > 100.0, "{tr:?}");
        } else {
            near(tr.y, 104.0);
            near(tr.z, 100.0);
        }
        assert!(active(&owner).is_none());
    }
}

#[tokio::test]
async fn owner_departure_ends_arc_without_moving_detached_npc() {
    let (server, mut client, _player, npc) = super::tests::setup();
    let owner = WorldObject::from(&npc);
    let sc = create(
        &server,
        &fish_jump(),
        &owner,
        &Target::Obj(owner.clone()),
        0.0,
        0.0,
        0.0,
        0.0,
    )
    .unwrap();
    sc.lock().state = ScState::Running;
    owner.with_unit(|u| u.unit_mut().skill_state.active_skill_controller = Some(sc.clone()));
    server.world.world(0).unwrap().delete(&npc);
    client.drain();
    let before = npc.data().unit.transform;
    sc.tick(&server, Duration::from_millis(125));
    assert_eq!(sc.state(), ScState::Ended);
    assert!(active(&owner).is_none());
    assert_eq!(npc.data().unit.transform, before);
    assert!(client.drain().is_empty());
}
