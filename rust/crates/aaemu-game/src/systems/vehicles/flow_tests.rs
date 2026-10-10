//! Flow tests of the vehicles subsystem: mount / dismount, pet exp, slave summon / drive /
//! despawn / death, a transfer following its route, shipyard build steps, and the gated
//! real-data tests (`AAEMU_COMPACT_DB`, `AAEMU_GAME_PAK`).

use std::path::Path;
use std::sync::{Arc, Mutex};
use std::time::Duration;

use aaemu_commons::PacketStream;
use sqlx::mysql::MySqlPoolOptions;

use crate::config::AppConfig;
use crate::data::client_files::ClientFileManager;
use crate::data::experience::{ExperienceTable, LevelTemplate};
use crate::data::items::{ItemTemplate, TemplateKind};
use crate::data::world_templates::{WorldSpawnPosition, WorldTemplate, WorldTemplates};
use crate::data::GameData;
use crate::models::container::slot_type;
use crate::models::item::Item;
use crate::models::movement::{MoveKind, MoveType, VehicleMove};
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::{SimpleSpawnerDef, SpawnerBase};
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::systems::skills::ext::ItemSkillHooks;
use crate::systems::DataLoadCtx;
use crate::test_support::*;
use crate::world::{PlayerRef, WorldObject};

use super::data::{
    attach_point, ShipyardStep, ShipyardTemplate, SlaveTemplate, TransferPath, TransferRoad,
    TransferTemplate, VehicleModel,
};
use super::{mates, shipyards, slaves, transfers};

const MATE_NPC: u32 = 900;
const MATE_ITEM: u32 = 5000;
const SLAVE_TEMPLATE: u32 = 68;
const SLAVE_ITEM: u32 = 5001;
/// Player obj ids outside the range the object id manager hands out in the tests.
const PA: u32 = 0x00F0_0001;
const PB: u32 = 0x00F0_0002;

fn exp_table() -> ExperienceTable {
    ExperienceTable::from_templates(
        (1..=3u8)
            .map(|l| LevelTemplate {
                level: l,
                total_exp: i32::from(l) * 1000,
                total_mate_exp: (i32::from(l) - 1) * 500,
                skill_points: 0,
            })
            .collect(),
    )
    .expect("levels")
}

/// MaxHp / MaxMp formulas of the vehicle kinds (`unit_formulas`: kind 14 / 15, owners 2, 4,
/// 5, 6).
fn pool_formulas() -> crate::data::formulas::FormulaManager {
    super::stats::tests::formulas(
        &[
            (1, "1000", 14, 2),
            (2, "100", 15, 2),
            (3, "500", 14, 4),
            (4, "800 + level", 14, 5),
            (5, "300", 15, 5),
            (6, "2000", 14, 6),
        ],
        &[],
    )
}

fn data() -> GameData {
    let mut d = GameData::empty();
    d.formulas = pool_formulas();
    d.experience = exp_table();
    d.systems.npcs.npc.templates.insert(
        MATE_NPC,
        NpcTemplate {
            id: MATE_NPC,
            name: "Horse".into(),
            level: 1,
            model_id: 77,
            scale: 1.0,
            ..NpcTemplate::default()
        },
    );
    d.items.templates.insert(
        MATE_ITEM,
        ItemTemplate {
            kind: TemplateKind::SummonMate { npc_id: MATE_NPC },
            ..ItemTemplate::plain(MATE_ITEM, 1)
        },
    );
    d.items.templates.insert(
        SLAVE_ITEM,
        ItemTemplate {
            kind: TemplateKind::SummonSlave {
                slave_id: SLAVE_TEMPLATE,
            },
            ..ItemTemplate::plain(SLAVE_ITEM, 1)
        },
    );
    d.systems.vehicles.npc_mount_skills = vec![super::data::NpcMountSkills {
        id: 1,
        npc_id: MATE_NPC,
        mount_skill_id: 11,
    }];
    d.systems.vehicles.slave_templates.insert(
        SLAVE_TEMPLATE,
        SlaveTemplate {
            id: SLAVE_TEMPLATE,
            name: "Cart".into(),
            model_id: 300,
            level: 1,
            slave_kind: super::data::slave_kind::MACHINE,
            spawn_y_offset: 3.0,
            portal_time: 2.0,
            ..SlaveTemplate::default()
        },
    );
    d
}

fn server_with(d: GameData) -> Arc<GameServer> {
    let s = Arc::new(test_server_with(d));
    s.set_self();
    s
}

/// A player with a summon item in the bag; returns (player, item id).
fn player_with_item(
    server: &GameServer,
    client: &TestClient,
    id: u32,
    obj_id: u32,
    at: (f32, f32),
    item_template: u32,
) -> (PlayerRef, u64) {
    let p = spawn_player(
        server,
        client,
        test_character(id, &format!("P{id}"), at.0, at.1),
        obj_id,
    );
    let item_id = 0x7000 + u64::from(id);
    let t = server
        .game_data
        .items
        .template(item_template)
        .unwrap()
        .clone();
    let mut item = Item::create(item_id, &t, 1, 0, 1, &server.game_data.items, 0);
    item.slot_type = slot_type::INVENTORY;
    item.slot = 0;
    let bag = p
        .data()
        .inventory
        .get(slot_type::INVENTORY)
        .cloned()
        .unwrap();
    bag.lock().unwrap().items.push(item);
    (p, item_id)
}

fn ops(frames: &[(u16, Vec<u8>)]) -> Vec<u16> {
    frames.iter().map(|f| f.0).collect()
}

fn body(frames: &[(u16, Vec<u8>)], op: u16) -> Vec<u8> {
    frames
        .iter()
        .find(|f| f.0 == op)
        .unwrap_or_else(|| panic!("no 0x{op:03x} in {:x?}", ops(frames)))
        .1
        .clone()
}

fn bc(v: u32) -> Vec<u8> {
    v.to_le_bytes()[..3].to_vec()
}

#[tokio::test]
async fn mount_and_dismount_flow() {
    let server = server_with(data());
    let mut a = TestClient::new(&server, 1, 1);
    let mut b = TestClient::new(&server, 2, 2);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), MATE_ITEM);
    let pb = spawn_player(&server, &b, test_character(2, "B", 110.0, 110.0), PB);
    a.drain();
    b.drain();

    mates::spawn_mount(&server, &pa, item_id);
    let fa = a.drain();
    assert_eq!(
        &ops(&fa)[..2],
        &[sc::SCItemTaskSuccessPacket, sc::SCMateSpawnedPacket]
    );
    let manager = server.systems.vehicles.mates.get(0);
    let mate = manager.get_active_mates(1).pop().expect("active mate");
    let tl = mate.data().unit.tl_id;
    // SCMateSpawnedPacket: tl, db id, item id, user state 1, exp of level 1 (0), mileage, delay,
    // the template's mount skill + 9 zeros.
    let mut s = PacketStream::new();
    s.write_u16(tl)
        .write_u32(mate.data().id)
        .write_u64(item_id)
        .write_u8(1)
        .write_i32(0)
        .write_i32(0)
        .write_u32(0)
        .write_u32(11);
    for _ in 0..9 {
        s.write_i32(0);
    }
    assert_eq!(body(&fa, sc::SCMateSpawnedPacket), s.as_bytes());
    // The mate appears to both players (SCUnitState + SCMateState + SCUnitPoints).
    for f in [&fa, &b.drain()] {
        let o = ops(f);
        let i = o
            .iter()
            .position(|op| *op == sc::SCUnitStatePacket)
            .unwrap();
        assert_eq!(
            &o[i..i + 3],
            &[
                sc::SCUnitStatePacket,
                sc::SCMateStatePacket,
                sc::SCUnitPointsPacket
            ]
        );
    }
    // The mount stands 3 m in front of the owner (yaw 0 -> +y).
    let t = mate.data().unit.transform;
    assert_eq!((t.x, t.y), (100.0, 103.0));

    // A non-owner cannot take the driver seat.
    mates::mount_mate(&server, &pb, u32::from(tl), attach_point::DRIVER, 1);
    assert!(a.drain().is_empty());
    assert!(!pb.data().is_riding);

    // The owner mounts: SCUnitAttachedPacket to everybody around, the rider becomes a child.
    mates::mount_mate(&server, &pa, u32::from(tl), attach_point::DRIVER, 1);
    let mut attached = bc(PA);
    attached.push(attach_point::DRIVER);
    attached.extend(bc(mate.obj_id));
    attached.push(1);
    for c in [&mut a, &mut b] {
        let f = c.drain();
        assert_eq!(body(&f, sc::SCUnitAttachedPacket), attached);
    }
    assert!(pa.data().is_riding);
    assert_eq!(pa.data().attached_point, attach_point::DRIVER);
    assert_eq!(pa.data().unit.transform.parent_obj_id, mate.obj_id);
    assert_eq!(
        mate.data().passenger(attach_point::DRIVER).unwrap().obj_id,
        PA
    );
    assert_eq!(
        server
            .systems
            .vehicles
            .mates
            .get(0)
            .get_is_mounted(PA)
            .map(|(m, p)| (m.obj_id, p)),
        Some((mate.obj_id, attach_point::DRIVER))
    );

    // Dismount: SCUnitDetachedPacket, the rider stands where the mount is.
    mates::unmount_mate(&server, &pa, u32::from(tl), attach_point::DRIVER, 4);
    let mut detached = bc(PA);
    detached.push(4);
    for c in [&mut a, &mut b] {
        let f = c.drain();
        assert_eq!(body(&f, sc::SCUnitDetachedPacket), detached);
    }
    assert!(!pa.data().is_riding);
    let t = pa.data().unit.transform;
    assert_eq!((t.parent_obj_id, t.x, t.y), (0, 100.0, 103.0));

    // Despawn (CSRemoveMatePacket): the mate leaves the world, its row is updated.
    mate.data().unit.hp = 42;
    mates::despawn_mate(&server, &pa, u32::from(tl));
    assert!(ops(&b.drain()).contains(&sc::SCUnitsRemovedPacket));
    assert!(server
        .systems
        .vehicles
        .mates
        .get(0)
        .get_active_mates(1)
        .is_empty());
    let db = server.systems.vehicles.mates.character_mates(1).unwrap();
    assert_eq!(db.mates[0].hp, 42);
    assert_eq!(db.mates[0].item_id, item_id);
    assert!(server.world.world(0).unwrap().mate(mate.obj_id).is_none());
}

#[tokio::test]
async fn second_summon_despawns_and_logout_keeps_stale_entry() {
    let server = server_with(data());
    let mut a = TestClient::new(&server, 1, 1);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), MATE_ITEM);
    mates::spawn_mount(&server, &pa, item_id);
    let m = server.systems.vehicles.mates.get(0);
    assert_eq!(m.get_active_mates(1).len(), 1);
    // Using the whistle again only despawns the active mate.
    mates::spawn_mount(&server, &pa, item_id);
    assert!(m.get_active_mates(1).is_empty());
    mates::spawn_mount(&server, &pa, item_id);
    let mate = m.get_active_mates(1).pop().unwrap();
    a.drain();
    // Logout path: the mate is deleted, but (C# bug) the list entry keyed by the character id
    // is removed by the owner's obj id and stays.
    mates::remove_and_despawn_all_active_owned_mates(&server, 0, 1);
    assert!(server.world.world(0).unwrap().mate(mate.obj_id).is_none());
    assert_eq!(m.get_active_mates(1).len(), 1);
}

#[tokio::test]
async fn pet_exp_and_level_up() {
    let server = server_with(data());
    let mut a = TestClient::new(&server, 1, 1);
    let mut b = TestClient::new(&server, 2, 2);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), MATE_ITEM);
    spawn_player(&server, &b, test_character(2, "B", 110.0, 110.0), PB);
    mates::spawn_mount(&server, &pa, item_id);
    let mate = server
        .systems
        .vehicles
        .mates
        .get(0)
        .get_active_mates(1)
        .pop()
        .unwrap();
    a.drain();
    b.drain();

    // 300 exp: no level up (level 2 needs 500 mate exp).
    mates::mate_xp_update_task(&server, &pa, &mate);
    let f = a.drain();
    let mut exp = bc(mate.obj_id);
    exp.extend(300i32.to_le_bytes());
    exp.push(0);
    assert_eq!(body(&f, sc::SCExpChangedPacket), exp);
    assert!(ops(&f).contains(&sc::SCChatMessagePacket));
    assert!(!ops(&f).contains(&sc::SCLevelChangedPacket));
    assert_eq!(mate.data().experience, 300);

    // +300 more: level 2, broadcast to everybody around.
    mates::add_exp(&server, &mate, 300);
    let f = a.drain();
    let mut lvl = bc(mate.obj_id);
    lvl.push(2);
    assert_eq!(body(&f, sc::SCLevelChangedPacket), lvl);
    assert_eq!(body(&b.drain(), sc::SCLevelChangedPacket), lvl);
    // The summon item and the mate row follow.
    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    assert_eq!((item.details.mate_exp, item.details.mate_level), (600, 2));
    assert!(item.dirty);
    let db = server.systems.vehicles.mates.character_mates(1).unwrap();
    assert_eq!((db.mates[0].xp, db.mates[0].level), (600, 2));

    // Over the cap: the overflow is cut at the max level (3 = 1000 mate exp).
    mates::add_exp(&server, &mate, 5000);
    assert_eq!(mate.data().unit.level, 3);
    assert_eq!(mate.data().experience, 1000);
    a.drain();
    // Max level: nothing happens.
    mates::add_exp(&server, &mate, 10);
    assert!(a.drain().is_empty());
    assert!(mates::is_max_level(&server, &mate.data()));
}

fn slave_data() -> GameData {
    data()
}

#[tokio::test]
async fn slave_summon_bind_drive_and_despawn() {
    let server = server_with(slave_data());
    let mut a = TestClient::new(&server, 1, 1);
    let mut b = TestClient::new(&server, 2, 2);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), SLAVE_ITEM);
    spawn_player(&server, &b, test_character(2, "B", 110.0, 110.0), PB);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    a.drain();
    b.drain();

    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    let slave = slaves::create_core(
        &server,
        &world,
        Some(&pa),
        None,
        SLAVE_TEMPLATE,
        Some(item),
        false,
        None,
        None,
    )
    .expect("slave");
    let (tl, db_id) = {
        let d = slave.data();
        (d.unit.tl_id, d.id)
    };
    assert!(db_id > 0, "a player slave gets a db id");
    let fa = a.drain();
    let o = ops(&fa);
    assert_eq!(o[0], sc::SCItemTaskSuccessPacket);
    assert!(o.contains(&sc::SCSlaveCreatedPacket));
    assert!(o.contains(&sc::SCSlaveStatePacket));
    assert_eq!(*o.last().unwrap(), sc::SCMySlavePacket);
    let mut created = bc(PA);
    created.extend(tl.to_le_bytes());
    created.extend(bc(slave.obj_id));
    created.push(0);
    created.extend(0i64.to_le_bytes());
    created.extend([2, 0, b'P', b'1']);
    assert_eq!(body(&fa, sc::SCSlaveCreatedPacket), created);
    let fb = b.drain();
    assert_eq!(body(&fb, sc::SCSlaveCreatedPacket), created);
    assert!(ops(&fb).contains(&sc::SCSlaveStatePacket));
    // Summon item details: type 2, the slave's db id.
    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    assert_eq!(
        (item.details.slave_type, item.details.slave_db_id),
        (2, db_id)
    );
    // Spawned 5 m (SpawnYOffset clamped to 5..50) in front, 90 degrees CCW of the owner.
    let t = slave.data().unit.transform;
    assert_eq!((t.x, t.y), (100.0, 105.0));
    assert!((t.rot_z - std::f32::consts::FRAC_PI_2).abs() < 1e-6);
    assert_eq!(
        slaves::get_active_slave_by_owner_obj_id(&world, PA).map(|s| s.obj_id),
        Some(slave.obj_id)
    );

    // Bind as driver: SCUnitAttached + SCSlaveBound to everybody around.
    slaves::bind_slave_by_tl(&server, &pa, u32::from(tl));
    for c in [&mut a, &mut b] {
        let f = c.drain();
        assert_eq!(
            ops(&f),
            vec![sc::SCUnitAttachedPacket, sc::SCSlaveBoundPacket]
        );
        let mut bound = 1u32.to_le_bytes().to_vec();
        bound.extend(bc(slave.obj_id));
        assert_eq!(body(&f, sc::SCSlaveBoundPacket), bound);
    }
    assert_eq!(
        slave.data().attached_characters,
        vec![(attach_point::DRIVER, PA)]
    );
    assert_eq!(pa.data().unit.transform.parent_obj_id, slave.obj_id);

    // Drive (VehicleMoveType): the move is applied and broadcast.
    let mv = MoveType::new(MoveKind::Vehicle(VehicleMove {
        x: 120.0,
        y: 125.0,
        z: 100.0,
        ..VehicleMove::default()
    }));
    a.conn.active_char = Some(pa.clone());
    crate::world::session::move_unit(&server, &mut a.conn, slave.obj_id, mv.clone()).await;
    let fb = b.drain();
    let mut expected = PacketStream::new();
    crate::models::movement::write_sc_one_unit_movement(&mut expected, slave.obj_id, &mv);
    assert_eq!(body(&fb, sc::SCOneUnitMovementPacket), expected.as_bytes());
    let t = slave.data().unit.transform;
    assert_eq!((t.x, t.y, t.z), (120.0, 125.0, 100.0));
    // The driver follows (child of the slave).
    let w = world.world_pos_rot(&WorldObject::from(&pa));
    assert_eq!((w.x, w.y), (120.0, 125.0));
    a.drain();

    // Discard (CSDiscardSlavePacket): SCUnitDetached.
    slaves::unbind_slave(
        &server,
        &pa,
        u32::from(tl),
        slaves::attach_reason::SLAVE_BINDING,
    );
    let mut det = bc(PA);
    det.push(slaves::attach_reason::SLAVE_BINDING);
    assert_eq!(body(&b.drain(), sc::SCUnitDetachedPacket), det);
    assert!(slave.data().attached_characters.is_empty());
    assert_eq!(pa.data().unit.transform.parent_obj_id, 0);
    a.drain();

    // Un-summon (CSDestroySlavePacket): despawn effect packets, removal scheduled.
    slaves::remove_active_slave(&server, &pa, tl, false);
    let fb = b.drain();
    assert_eq!(
        ops(&fb),
        vec![sc::SCSlaveDespawnPacket, sc::SCSlaveRemovedPacket]
    );
    let mut removed = bc(PA);
    removed.extend(tl.to_le_bytes());
    assert_eq!(body(&fb, sc::SCSlaveRemovedPacket), removed);
    assert!(
        world.slave(slave.obj_id).is_none(),
        "removed from the object table"
    );
    let sm = server.systems.npcs.spawns.get(0).unwrap();
    assert!(sm
        .pending_despawns()
        .iter()
        .any(|(id, at)| *id == slave.obj_id && at.is_some()));
    // Still visible until the despawn tick (portal time + 0.5 s).
    assert!(slave.core().is_visible());
    crate::systems::npcs::spawn_manager::advance_test_clock(Duration::from_secs(3));
    sm.check_respawns_tick(&server);
    assert!(!slave.core().is_visible());
    assert!(ops(&b.drain()).contains(&sc::SCUnitsRemovedPacket));
}

#[tokio::test]
async fn slave_death_marks_the_summon_item_destroyed() {
    let server = server_with(slave_data());
    let mut a = TestClient::new(&server, 1, 1);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), SLAVE_ITEM);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    let slave = slaves::create_core(
        &server,
        &world,
        Some(&pa),
        None,
        SLAVE_TEMPLATE,
        Some(item),
        false,
        None,
        None,
    )
    .unwrap();
    slave.data().unit.max_hp = 100;
    slave.data().unit.hp = 100;
    a.drain();
    let obj = WorldObject::from(&slave);
    let killer = WorldObject::from(&pa);
    crate::systems::skills::combat::reduce_current_hp(
        &server,
        &obj,
        Some(&killer),
        500,
        crate::systems::skills::types::KillReason::Damage,
    );
    let f = a.drain();
    let o = ops(&f);
    assert!(o.contains(&sc::SCUnitDeathPacket));
    assert!(o.contains(&sc::SCMySlavePacket));
    assert!(o.contains(&sc::SCSlaveRemovedPacket));
    // MarkSummoningItemAsDestroyed -> ItemTaskType.MateDeath (59)
    let tasks: Vec<&Vec<u8>> = f
        .iter()
        .filter(|x| x.0 == sc::SCItemTaskSuccessPacket)
        .map(|x| &x.1)
        .collect();
    assert_eq!(tasks.last().unwrap()[0], 59);
    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    assert_eq!(item.details.slave_is_destroyed, 1);
    // A destroyed vehicle cannot be summoned again before it is repaired.
    let sm = server.systems.npcs.spawns.get(0).unwrap();
    assert!(sm
        .pending_despawns()
        .iter()
        .any(|(id, _)| *id == slave.obj_id));
    crate::systems::npcs::spawn_manager::advance_test_clock(Duration::from_secs(21));
    sm.check_respawns_tick(&server);
    a.drain();
    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    assert!(slaves::create_core(
        &server,
        &world,
        Some(&pa),
        None,
        SLAVE_TEMPLATE,
        Some(item),
        false,
        None,
        None,
    )
    .is_some());
}

fn transfer_data() -> GameData {
    let mut d = GameData::empty();
    d.formulas = pool_formulas();
    let pos = |x: f32, y: f32| WorldSpawnPosition {
        world_id: 0,
        zone_id: TEST_ZONE,
        x,
        y,
        z: 100.0,
        ..WorldSpawnPosition::default()
    };
    d.systems.vehicles.transfer_templates.insert(
        6,
        TransferTemplate {
            id: 6,
            name: "Loop Carriage".into(),
            model_id: 654,
            wait_time: 0.0,
            cyclic: true,
            path_smoothing: 2.0,
            transfer_all_paths: vec![TransferPath {
                id: 1,
                owner_id: 6,
                owner_type: "Transfer".into(),
                path_name: "road".into(),
                wait_time_start: 0.0,
                wait_time_end: 0.0,
            }],
            transfer_roads: vec![TransferRoad {
                name: "road".into(),
                pos: vec![pos(100.0, 100.0), pos(110.0, 100.0), pos(110.0, 110.0)],
                ..TransferRoad::default()
            }],
            ..TransferTemplate::default()
        },
    );
    d.systems
        .vehicles
        .model_types
        .insert(654, (54, "VehicleModel".into()));
    d.systems.vehicles.vehicle_models.insert(
        54,
        VehicleModel {
            id: 54,
            velocity: 2.0,
            ang_vel: 10.0,
            wheeled_vehicle_mass: 1.0,
            wheeled_vehicle_max_gear: 1,
            wheeled_vehicle_gear_speed_ratio_1: 1.0,
            ..VehicleModel::default()
        },
    );
    d
}

#[tokio::test]
async fn transfer_follows_its_route() {
    let server = server_with(transfer_data());
    let mut b = TestClient::new(&server, 2, 2);
    spawn_player(&server, &b, test_character(2, "B", 105.0, 105.0), PB);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    b.drain();
    let def = SimpleSpawnerDef {
        base: SpawnerBase {
            id: 1,
            unit_id: 6,
            position: WorldSpawnPosition {
                world_id: 0,
                zone_id: TEST_ZONE,
                x: 101.0,
                y: 101.0,
                z: 100.0,
                ..WorldSpawnPosition::default()
            },
            ..SpawnerBase::default()
        },
        count: 1,
    };
    assert_eq!(transfers::spawn_all(&server, &world, vec![def]), 1);
    let motor = server
        .systems
        .vehicles
        .transfers
        .get(0)
        .get_transfers()
        .remove(0);
    {
        let t = motor.data();
        assert!(t.unit.is_in_patrol);
        assert_eq!((t.unit.transform.x, t.unit.transform.y), (100.0, 100.0));
        // nearest checkpoint: the first point (distance 0)
        assert_eq!(t.move_step_index, 0);
        assert_eq!(t.v_target, [100.0, 100.0, 100.0]);
        assert_eq!(t.unit.owner_id, 255);
        assert!(
            t.unit.buffs.check_buff(slaves::buff::UNTOUCHABLE)
                || server
                    .game_data
                    .systems
                    .skills
                    .buff(slaves::buff::UNTOUCHABLE)
                    .is_none()
        );
    }
    assert!(ops(&b.drain()).contains(&sc::SCUnitStatePacket));

    // Tick 1: at the checkpoint -> next point (110, 100).
    transfers::move_to(&server, &motor);
    assert_eq!(motor.data().move_step_index, 1);
    assert_eq!(motor.data().v_target, [110.0, 100.0, 100.0]);
    // Following ticks move toward it and broadcast SCOneUnitMovementPacket (transfer type 5).
    let mut moved = 0;
    for _ in 0..50 {
        transfers::move_to(&server, &motor);
        let f = b.drain();
        for (op, body) in &f {
            if *op == sc::SCOneUnitMovementPacket {
                assert_eq!(&body[..3], &bc(motor.obj_id)[..]);
                assert_eq!(body[3], 5);
                moved += 1;
            }
        }
        if motor.data().move_step_index == 2 {
            break;
        }
    }
    assert!(moved > 0);
    let t = motor.data();
    assert_eq!(
        t.move_step_index, 2,
        "reached (110, 100), heading to (110, 110)"
    );
    assert!((t.unit.transform.x - 110.0).abs() < 1.6);
    assert_eq!(t.v_target, [110.0, 110.0, 100.0]);
    // Moving north now: angle pi/2, Rot = (0, 0, sin(yaw'/2), cos(yaw'/2)) of the client yaw.
    drop(t);
    transfers::move_to(&server, &motor);
    let t = motor.data();
    assert!((t.angle - std::f64::consts::FRAC_PI_2).abs() < 0.2);
    assert_eq!(t.speed, 2.0);
}

#[test]
fn transfer_rotation_helpers() {
    // CalculateDirection: atan2(dy, dx)
    assert!(
        (transfers::calculate_direction([0.0; 3], [0.0, 1.0, 0.0]) - std::f64::consts::FRAC_PI_2)
            .abs()
            < 1e-12
    );
    // ConvertRadianToDirectionShort(pi/2): yaw = pi/2 - 3.14159/2 ~ 0 -> identity
    let q = transfers::convert_radian_to_direction_short(std::f64::consts::FRAC_PI_2);
    assert!(q[1].abs() < 1e-5 && (q[3] - 1.0).abs() < 1e-5);
    // negative angles wrap by 2 * 3.14159 first
    let q = transfers::convert_radian_to_direction_short(-std::f64::consts::FRAC_PI_2);
    #[allow(clippy::approx_constant)]
    let yaw = (-std::f64::consts::FRAC_PI_2 + 3.14159 * 2.0 - 3.14159 * 0.5) as f32;
    assert!((q[1] - (yaw * 0.5).sin()).abs() < 1e-6);
}

#[derive(Default)]
struct ShipItems {
    consumed: Mutex<Vec<(u8, u32, i32)>>,
    acquired: Mutex<Vec<(u8, u32, i32)>>,
}

impl ItemSkillHooks for ShipItems {
    fn item_count(&self, _server: &GameServer, _player: &PlayerRef, _template_id: u32) -> i32 {
        5
    }
    fn item_use_skill(&self, _server: &GameServer, template_id: u32) -> Option<(u32, bool)> {
        (template_id == 7000).then_some((8000, false))
    }
    fn consume_bag_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        task_type: u8,
        template_id: u32,
        amount: i32,
    ) -> i32 {
        self.consumed
            .lock()
            .unwrap()
            .push((task_type, template_id, amount));
        amount
    }
    fn acquire_default_item(
        &self,
        _server: &GameServer,
        _player: &PlayerRef,
        task_type: u8,
        template_id: u32,
        amount: i32,
    ) -> bool {
        self.acquired
            .lock()
            .unwrap()
            .push((task_type, template_id, amount));
        true
    }
}

#[tokio::test]
async fn shipyard_build_steps_progress() {
    let mut d = GameData::empty();
    d.formulas = pool_formulas();
    let step = |s: i32, model: u32, n: i32| {
        (
            s,
            ShipyardStep {
                id: s as u32 + 1,
                shipyard_id: 3,
                step: s,
                model_id: model,
                skill_id: 9000 + s as u32,
                num_actions: n,
                max_hp: 10,
            },
        )
    };
    d.systems.vehicles.shipyard_templates.insert(
        3,
        ShipyardTemplate {
            id: 3,
            name: "Clipper Shipyard".into(),
            main_model_id: 500,
            item_id: 7001,
            ceremony_anim_time: 0,
            origin_item_id: 7000,
            taxation_id: 2,
            steps: vec![step(0, 501, 2), step(1, 502, 1)],
            ..ShipyardTemplate::default()
        },
    );
    d.systems.vehicles.taxations.insert(2, 1000);
    let server = server_with(d);
    let hooks = Arc::new(ShipItems::default());
    server.systems.skills.ext.items.install(hooks.clone());
    let mut a = TestClient::new(&server, 1, 1);
    let mut b = TestClient::new(&server, 2, 2);
    let pa = spawn_player(&server, &a, test_character(1, "Owner", 100.0, 100.0), PA);
    spawn_player(&server, &b, test_character(2, "B", 110.0, 110.0), PB);
    pa.data().money = 5000;
    a.drain();
    b.drain();

    let sy = shipyards::create(&server, &pa, 3, 120.0, 120.0, 100.0, 0.5, 0, 0).expect("shipyard");
    assert_eq!(pa.data().money, 4000);
    assert_eq!(
        *hooks.consumed.lock().unwrap(),
        vec![(60, 7000, 1)],
        "the design is consumed (no reagents)"
    );
    let fa = a.drain();
    assert!(ops(&fa).contains(&sc::SCItemTaskSuccessPacket));
    assert!(ops(&fa).contains(&sc::SCShipyardStatePacket));
    assert!(ops(&b.drain()).contains(&sc::SCShipyardStatePacket));
    {
        let s = sy.data();
        assert_eq!(s.unit.model_id, 501);
        assert_eq!(s.all_action, 3);
        assert_eq!(s.data.hp, 1000);
        assert_eq!(s.data.owner_name, "Owner");
        assert_eq!(s.data.kind, 7000);
        assert_eq!(s.data.kind2, 1);
    }
    assert_eq!(shipyards::current_step_template(&server, &sy), Some(9000));

    // Step 0 needs 2 actions.
    shipyards::add_build_action(&server, &sy);
    shipyards::sync_build_state(&server, &sy);
    {
        let s = sy.data();
        assert_eq!((s.current_step, s.num_action), (0, 1));
        assert_eq!((s.data.actions, s.data.step), (1, 0));
    }
    shipyards::add_build_action(&server, &sy);
    shipyards::sync_build_state(&server, &sy);
    {
        let s = sy.data();
        assert_eq!((s.current_step, s.num_action, s.base_action), (1, 0, 2));
        assert_eq!(s.unit.model_id, 502);
        assert_eq!((s.data.actions, s.data.step), (2, 1));
    }
    assert_eq!(shipyards::current_step_template(&server, &sy), Some(9001));
    // Last action: done (-1, main model), ShipyardData reports all actions / step count.
    shipyards::add_build_action(&server, &sy);
    shipyards::sync_build_state(&server, &sy);
    {
        let s = sy.data();
        assert_eq!(s.current_step, -1);
        assert_eq!(s.unit.model_id, 500);
        assert_eq!((s.data.actions, s.data.step), (3, 2));
    }
    // Further actions are ignored.
    shipyards::add_build_action(&server, &sy);
    assert_eq!(sy.data().current_step, -1);

    // Launch ceremony: the ship item, step 1000 broadcast; the shipyard is removed afterwards.
    a.drain();
    shipyards::shipyard_completed_task(&server, &sy);
    assert_eq!(*hooks.acquired.lock().unwrap(), vec![(60, 7001, 1)]);
    let f = a.drain();
    let st = body(&f, sc::SCShipyardStatePacket);
    assert_eq!(&st[st.len() - 4..], &1000i32.to_le_bytes());
    shipyards::shipyard_completed(&server, &sy).await;
    assert!(server.systems.vehicles.shipyards.all().is_empty());
    assert!(server.world.world(0).unwrap().shipyard(sy.obj_id).is_none());
}

fn effect_args<'a>(
    caster: &WorldObject,
    target: crate::systems::skills::geometry::Target,
    target_caster: crate::systems::skills::types::SkillCastTarget,
    so: &'a crate::systems::skills::types::SkillObject,
) -> crate::systems::skills::effects::EffectArgs<'a> {
    crate::systems::skills::effects::EffectArgs {
        caster: Some(caster.clone()),
        caster_caster: crate::systems::skills::types::SkillCaster::Unit(caster.obj_id()),
        target,
        target_caster,
        cast: crate::systems::skills::types::CastAction::Skill {
            skill_id: 1,
            tl_id: 0,
        },
        source: crate::systems::skills::effects::EffectSource::default(),
        skill_object: so,
    }
}

#[tokio::test]
async fn repair_slave_effect_and_repair_cooldown() {
    use crate::systems::skills::data::{EffectKind, EffectTemplate};
    use crate::systems::skills::types::SkillCastTarget;
    let mut d = slave_data();
    d.systems
        .vehicles
        .repairable_slaves
        .insert(SLAVE_TEMPLATE, 77);
    let server = server_with(d);
    let mut a = TestClient::new(&server, 1, 1);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), SLAVE_ITEM);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    a.drain();
    let me = WorldObject::from(&pa);
    let so = Default::default();
    let target = SkillCastTarget::Item {
        obj_id: PA,
        id: item_id,
        type1: 0,
        type2: 0,
    };
    // Wrong repair effect: ItemFailedRepair (23).
    let args = effect_args(
        &me,
        crate::systems::skills::geometry::Target::Obj(me.clone()),
        target,
        &so,
    );
    let wrong = EffectTemplate::RepairSlave {
        id: 78,
        health: 0,
        mana: 0,
    };
    assert!(super::effects::apply_effect(
        &server,
        EffectKind::RepairSlaveEffect,
        &wrong,
        &args
    ));
    let f = a.drain();
    assert_eq!(&body(&f, sc::SCErrorMsgPacket)[..2], &23i16.to_le_bytes());
    // The right one: IsDestroyed = 0, RepairStartTime = now, ItemTaskType.RepairSlaves (74).
    let right = EffectTemplate::RepairSlave {
        id: 77,
        health: 0,
        mana: 0,
    };
    assert!(super::effects::apply_effect(
        &server,
        EffectKind::RepairSlaveEffect,
        &right,
        &args
    ));
    let f = a.drain();
    let task = body(&f, sc::SCItemTaskSuccessPacket);
    assert_eq!(task[0], 74);
    let item = mates::owned_item(&server, &pa, item_id).unwrap();
    assert!(item.details.slave_repair_start_time > 0);
    // The detail block now carries the repair time (8 bytes instead of 4).
    assert_eq!(item.detail_type_and_details().len(), 1 + 33);
    // Summoning during the 10 minute repair: SlaveSpawnErrorNeedRepairTime (540) with the
    // seconds left in the error's type field.
    assert!(slaves::create_core(
        &server,
        &world,
        Some(&pa),
        None,
        SLAVE_TEMPLATE,
        Some(item),
        false,
        None,
        None,
    )
    .is_none());
    let f = a.drain();
    let e = body(&f, sc::SCErrorMsgPacket);
    assert_eq!(&e[..2], &540i16.to_le_bytes());
    let left = u32::from_le_bytes(e[4..8].try_into().unwrap());
    assert!((598..=600).contains(&left), "{left}");
}

#[tokio::test]
async fn spawn_effect_creates_an_ownerless_test_slave() {
    use crate::systems::skills::data::effects::SpawnEffect;
    use crate::systems::skills::data::{EffectKind, EffectTemplate};
    let server = server_with(slave_data());
    let mut a = TestClient::new(&server, 1, 1);
    let pa = spawn_player(&server, &a, test_character(1, "P1", 100.0, 100.0), PA);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    a.drain();
    let me = WorldObject::from(&pa);
    let so = Default::default();
    let args = effect_args(
        &me,
        crate::systems::skills::geometry::Target::Obj(me.clone()),
        crate::systems::skills::types::SkillCastTarget::Unit(PA),
        &so,
    );
    let spawn = SpawnEffect {
        id: 1,
        owner_type_id: 2,
        sub_type: SLAVE_TEMPLATE,
        pos_dir_id: 0,
        pos_angle: 0.0,
        pos_distance: 0.0,
        ori_dir_id: 0,
        ori_angle: 90.0,
        use_summoner_faction: false,
        life_time: 0.0,
        despawn_on_creator_death: false,
        use_summoner_aggro_target: false,
        mate_state_id: 0,
    };
    let t = EffectTemplate::Spawn(spawn.clone());
    assert!(super::effects::apply_effect(
        &server,
        EffectKind::SpawnEffect,
        &t,
        &args
    ));
    let slave = slaves::all_slaves(&world).pop().expect("test slave");
    {
        let d = slave.data();
        assert_eq!(d.summoner_obj_id, 0, "owner-less");
        assert_eq!(d.id, 0, "not persistent");
        assert_eq!((d.unit.transform.x, d.unit.transform.y), (100.0, 102.0));
        assert!((d.unit.transform.rot_z - std::f32::consts::FRAC_PI_2).abs() < 1e-5);
        assert_ne!(d.leave_task, 0, "despawn task scheduled");
    }
    let o = ops(&a.drain());
    assert!(o.contains(&sc::SCUnitStatePacket));
    assert!(!o.contains(&sc::SCSlaveCreatedPacket));
    // An NPC spawn effect is not ours.
    let npc = EffectTemplate::Spawn(SpawnEffect {
        owner_type_id: 1,
        ..spawn
    });
    assert!(!super::effects::apply_effect(
        &server,
        EffectKind::SpawnEffect,
        &npc,
        &args
    ));
}

#[tokio::test]
async fn kill_exp_death_despawn_and_force_dismount() {
    let server = server_with(data());
    let mut a = TestClient::new(&server, 1, 1);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), MATE_ITEM);
    mates::spawn_mount(&server, &pa, item_id);
    let mate = mates::first_active_mate(&server, &pa).unwrap();
    let tl = u32::from(mate.data().unit.tl_id);
    a.drain();
    // Npc kill exp: every active mate + a message.
    mates::reward_kill_exp(&server, &pa, 100);
    let f = a.drain();
    assert!(ops(&f).contains(&sc::SCExpChangedPacket));
    assert!(ops(&f).contains(&sc::SCChatMessagePacket));
    assert_eq!(mate.data().experience, 100);
    // Team.IsObjMember helper.
    assert_eq!(
        mates::active_mate_owner_obj_id(&server, mate.obj_id),
        Some(PA)
    );

    // Stun / root while riding: thrown off; Template.Unmount only where we drive.
    mates::mount_mate(&server, &pa, tl, attach_point::DRIVER, 0);
    mates::unmount_riding_character(&server, &pa, true);
    assert!(!pa.data().is_riding);
    mates::mount_mate(&server, &pa, tl, attach_point::PASSENGER0, 0);
    mates::unmount_riding_character(&server, &pa, true);
    assert!(pa.data().is_riding, "not the driver");
    // ForceDismount: off any seat.
    assert!(super::force_dismount(
        &server,
        &pa,
        slaves::attach_reason::PREFAB_CHANGED
    ));
    assert!(!pa.data().is_riding);
    let f = a.drain();
    let mut det = bc(PA);
    det.push(slaves::attach_reason::PREFAB_CHANGED);
    assert!(f
        .iter()
        .any(|x| x.0 == sc::SCUnitDetachedPacket && x.1 == det));

    // DespawnMate on death: nothing while the character lives, the mate goes when it dies.
    pa.data().unit.hp = 100;
    mates::despawn_mate_on_death(&server, &WorldObject::from(&pa));
    assert!(mates::first_active_mate(&server, &pa).is_some());
    pa.data().unit.hp = 0;
    mates::despawn_mate_on_death(&server, &WorldObject::from(&pa));
    assert!(mates::first_active_mate(&server, &pa).is_none());
}

#[tokio::test]
async fn logout_despawns_mates_and_vehicles() {
    let server = server_with(slave_data());
    let mut a = TestClient::new(&server, 1, 1);
    let mut b = TestClient::new(&server, 2, 2);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), MATE_ITEM);
    spawn_player(&server, &b, test_character(2, "B", 110.0, 110.0), PB);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    mates::spawn_mount(&server, &pa, item_id);
    let slave = slaves::create_core(
        &server,
        &world,
        Some(&pa),
        None,
        SLAVE_TEMPLATE,
        None,
        false,
        None,
        None,
    )
    .unwrap();
    a.drain();
    b.drain();
    // +10 minutes of logout delay with an active vehicle.
    assert_eq!(super::leave_extra_logout_ms(&server, &pa), 600_000);
    super::on_leave_world(&server, &pa);
    // (C# bug) the deleted mate stays listed.
    assert!(mates::first_active_mate(&server, &pa).is_some_and(|m| m.data().deleted));
    assert!(world.slave(slave.obj_id).is_none());
    let o = ops(&b.drain());
    assert!(o.contains(&sc::SCUnitsRemovedPacket));
    assert!(o.contains(&sc::SCSlaveDespawnPacket));
    assert_eq!(super::leave_extra_logout_ms(&server, &pa), 0);
}

#[tokio::test]
async fn npc_and_social_hooks_reach_the_vehicles() {
    let server = server_with(data());
    // The npcs / social hook implementations call systems::vehicles directly.
    crate::systems::npcs::install_hooks(&server);
    crate::systems::social::install_hooks(&server);
    super::install_hooks(&server);
    let mut a = TestClient::new(&server, 1, 1);
    let (pa, item_id) = player_with_item(&server, &a, 1, PA, (100.0, 100.0), MATE_ITEM);
    a.drain();
    let caster = crate::systems::skills::types::SkillCaster::Item {
        obj_id: PA,
        item_id,
        item_template_id: MATE_ITEM,
        type1: 0,
        type2: 0,
    };
    server
        .systems
        .skills
        .ext
        .npcs()
        .spawn_mount(&server, &pa, &caster);
    let mate = mates::first_active_mate(&server, &pa).expect("summoned through the hook");
    assert!(!server
        .systems
        .skills
        .ext
        .npcs()
        .mate_is_max_level(&server, &mate));
    // social ForceDismount through the social hook slot
    let tl = u32::from(mate.data().unit.tl_id);
    mates::mount_mate(&server, &pa, tl, attach_point::DRIVER, 0);
    server
        .systems
        .social
        .hooks
        .get()
        .force_dismount(&server, &pa);
    assert!(!pa.data().is_riding);
    // Forwarded defaults stay the defaults.
    assert_eq!(
        server
            .systems
            .skills
            .ext
            .npcs()
            .npc_engage_combat_quest(&server, &crate::models::npc::Npc::new(1)),
        0
    );
}

// ---------------------------------------------------------------------------------------------
// Real data (gated)
// ---------------------------------------------------------------------------------------------

fn app_path() -> std::path::PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR")).join("app")
}

fn load_real(db: &str, sources: &[String]) -> super::Data {
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .expect("compact db");
    let app = app_path();
    let files = ClientFileManager::from_sources(sources, &app);
    super::Data::load(&DataLoadCtx {
        conn: &conn,
        app_path: &app,
        files: &files,
        language: "en_us",
    })
    .expect("vehicles data")
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_db_vehicle_tables() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let d = load_real(&db, &[]);
    assert_eq!(d.slave_templates.len(), 140);
    assert_eq!(d.transfer_templates.len(), 117);
    assert_eq!(d.npc_mount_skills.len(), 986);
    assert_eq!(d.mount_skills.len(), 351);
    assert_eq!(d.mount_attached_skills.len(), 212);
    assert_eq!(d.slave_mount_skills.len(), 324);
    assert_eq!(d.repairable_slaves.len(), 29);
    assert_eq!(d.shipyard_templates.len(), 15);
    assert_eq!(
        d.shipyard_templates
            .values()
            .map(|t| t.steps.len())
            .sum::<usize>(),
        85
    );
    assert_eq!(d.taxations.len(), 11);
    // Bindings land on their templates.
    let bindings: usize = d
        .transfer_templates
        .values()
        .map(|t| t.transfer_bindings.len())
        .sum();
    assert!(bindings > 0 && bindings <= 56);
    let paths: usize = d
        .transfer_templates
        .values()
        .map(|t| t.transfer_all_paths.len())
        .sum();
    assert!(paths > 0 && paths <= 444);
    let drops: usize = d
        .slave_templates
        .values()
        .map(|t| t.slave_drop_doodads.len())
        .sum();
    assert!(drops > 0 && drops <= 45);
    // Text booleans: `mountable` is read with GetBoolean (always false), `persist` / `on_water`
    // / `cyclic` with the string form.
    assert!(d.slave_templates.values().all(|t| !t.mountable));
    assert!(d.transfer_templates.values().any(|t| t.cyclic));
    // Boats have a ship model, carriages a vehicle model. In the 1.2 data 9 of the 49
    // boat-kind slaves have none (vehicle / prefab / missing models, e.g. 7, 19, 105), so
    // count them instead of sampling one (HashMap order made that flaky).
    let boats: Vec<_> = d
        .slave_templates
        .values()
        .filter(|t| t.is_a_boat())
        .collect();
    let with_ship_model = boats
        .iter()
        .filter(|t| d.ship_model(t.model_id).is_some())
        .count();
    assert_eq!((boats.len(), with_ship_model), (49, 40));
    assert!(d.ship_model(d.slave_templates[&9].model_id).is_some());
    let carriage = d.transfer_template(6).unwrap();
    assert!(d.vehicle_model(carriage.model_id).is_some());
    // slave_attach_points.json of the server data
    assert!(!d.attach_points.is_empty());
    let clipper = d
        .attach_points_for_slave(128)
        .expect("clipper attach points");
    assert!(clipper.iter().any(|(p, _)| *p == attach_point::DRIVER));
}

/// A server whose main_world covers the real map (31 x 31 cells), with the real vehicle data.
fn real_server(d: super::Data) -> Arc<GameServer> {
    let mut data = GameData::empty();
    data.systems.vehicles = d;
    data.worlds = WorldTemplates::from_templates(
        vec![WorldTemplate::synthetic(0, "main_world", 32, 32, TEST_ZONE)],
        Vec::new(),
    )
    .expect("world");
    let mut config = AppConfig {
        id: 1,
        ..AppConfig::default()
    };
    config.app_path = app_path();
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1;
    let pool = MySqlPoolOptions::new()
        .acquire_timeout(Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    let server = Arc::new(GameServer::new(config, pool, data));
    server.set_self();
    server
}

#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB and AAEMU_GAME_PAK"]
async fn real_transfer_follows_its_client_route() {
    let (Ok(db), Ok(pak)) = (
        std::env::var("AAEMU_COMPACT_DB"),
        std::env::var("AAEMU_GAME_PAK"),
    ) else {
        return;
    };
    let d = load_real(&db, &[pak]);
    let with_roads = d
        .transfer_templates
        .values()
        .filter(|t| !t.transfer_roads.is_empty())
        .count();
    assert!(with_roads > 10, "templates with client roads: {with_roads}");
    // 6: Salislead Peninsula ~ Liriot Hillside Loop Carriage (transfer_spawns.json entry).
    let t6 = d.transfer_template(6).expect("template 6").clone();
    assert!(!t6.transfer_roads.is_empty());
    assert!(t6.transfer_roads.iter().all(|r| r.pos.len() >= 2));
    let server = real_server(d);
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world);
    let first = t6.transfer_roads[0].pos[0];
    let def = SimpleSpawnerDef {
        base: SpawnerBase {
            id: 1,
            unit_id: 6,
            position: WorldSpawnPosition {
                world_id: 0,
                zone_id: TEST_ZONE,
                x: 12609.46,
                y: 15359.16,
                z: 158.5336,
                ..WorldSpawnPosition::default()
            },
            ..SpawnerBase::default()
        },
        count: 1,
    };
    transfers::spawn_all(&server, &world, vec![def]);
    let active = server.systems.vehicles.transfers.get(0).get_transfers();
    // The carriage and its boarding part (transfer_bindings).
    assert_eq!(active.len(), 1 + t6.transfer_bindings.len().min(1));
    let motor = active[0].clone();
    {
        let t = motor.data();
        assert!(t.unit.is_in_patrol, "on its route");
        assert_eq!((t.unit.transform.x, t.unit.transform.y), (first.x, first.y));
        assert_eq!(t.routes.len(), t6.transfer_roads.len());
    }
    // Run the 100 ms tick: the carriage must walk its checkpoints in order.
    let mut last_index = motor.data().move_step_index;
    let mut advanced = 0;
    for _ in 0..3000 {
        motor.data().wait_time_ms = 0;
        transfers::move_to(&server, &motor);
        let i = motor.data().move_step_index;
        if i != last_index {
            advanced += 1;
            last_index = i;
        }
    }
    assert!(advanced >= 3, "checkpoints passed: {advanced}");
    if let Some(part) = active.get(1) {
        // The boarding part trails 9.24 m behind the motor.
        let m = motor.data().unit.transform;
        let p = part.data().unit.transform;
        let d2 = ((m.x - p.x).powi(2) + (m.y - p.y).powi(2)).sqrt();
        assert!((d2 - 9.24417).abs() < 0.01, "trail distance {d2}");
    }
}
