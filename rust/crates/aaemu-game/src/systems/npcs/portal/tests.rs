//! Portal flow tests: summoned portals (open / use / teleport ended / delete), the portal book
//! (delete, notify sub zone) and the return-point lookups.

use std::collections::HashMap;

use super::packets::*;
use super::*;
use crate::data::world_data::{DistrictReturnPoint, Zone};
use crate::data::GameData;
use crate::net::packet::encode;
use crate::net::registry::ClientPacket;
use crate::opcodes::sc;
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};

fn body(p: &dyn crate::net::packet::ServerPacket) -> Vec<u8> {
    encode(p)[6..].to_vec()
}

fn recall(id: u32, sub_zone: u32, name: &str) -> Portal {
    Portal {
        id,
        name: name.into(),
        zone_id: TEST_ZONE,
        x: 300.0,
        y: 400.0,
        z: 50.0,
        z_rot: 90.0,
        sub_zone_id: sub_zone,
        ..Portal::default()
    }
}

fn data() -> GameData {
    let mut d = GameData::empty();
    d.portals.recalls.insert(
        77,
        vec![recall(5, 77, "Recall A"), recall(6, 77, "Recall B")],
    );
    d.portals.district_return_points = vec![
        DistrictReturnPoint {
            id: 1,
            district_id: 9,
            faction_id: 101,
            return_point_id: 86,
        },
        DistrictReturnPoint {
            id: 2,
            district_id: 9,
            faction_id: 102,
            return_point_id: 87,
        },
    ];
    d.zones.zones.insert(
        TEST_ZONE,
        Zone {
            id: 1,
            name: "test".into(),
            zone_key: TEST_ZONE,
            group_id: 0,
            closed: false,
            faction_id: 0,
            zone_climate_id: 0,
        },
    );
    let respawn = |id: u32, sub: u32, x: f32| Portal {
        id,
        name: format!("Respawn {id}"),
        x,
        y: 100.0,
        z: 10.0,
        sub_zone_id: sub,
        ..Portal::default()
    };
    d.systems.npcs.portal = PortalTables::from_parts(
        vec![
            respawn(86, 1, 150.0),
            respawn(87, 2, 120.0),
            respawn(88, 3, 900.0),
        ],
        vec![respawn(221, 50, 10.0)],
        vec![OpenPortalReagents {
            id: 5,
            open_portal_effect_id: 1,
            item_id: 417,
            amount: 0,
            priority: 1,
        }],
        vec![],
    )
    .unwrap();
    d.systems.npcs.portal.recalls_key = HashMap::from([(5, 77), (6, 77)]);
    for (id, level) in [
        (PORTAL_ENTRANCE_TEMPLATE_ID, 1u8),
        (PORTAL_EXIT_TEMPLATE_ID, 2),
    ] {
        d.systems.npcs.npc.templates.insert(
            id,
            NpcTemplate {
                id,
                level,
                model_id: 1000 + id,
                ..NpcTemplate::default()
            },
        );
    }
    d
}

/// A portal NPC teleporting to (500, 600, 70) with a 90 degree yaw.
fn portal_npc(server: &GameServer, at: (f32, f32)) -> NpcRef {
    let mut npc = Npc::new(PORTAL_EXIT_TEMPLATE_ID);
    npc.unit.transform.set_xyz(at.0, at.1, 100.0);
    npc.unit.transform.zone_id = TEST_ZONE;
    npc.unit.hp = 1;
    let mut tp = Transform {
        zone_id: TEST_ZONE,
        ..Transform::default()
    };
    tp.set_position(500.0, 600.0, 70.0, 0.0, 0.0, 90.0);
    npc.portal = Some(PortalInfo {
        teleport_position: tp,
        linked_portal_obj_id: 0,
    });
    let npc = Entity::new(server.ids.object.get_next_id().unwrap(), npc);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&npc, &server.view_ctx());
    npc
}

#[tokio::test]
async fn use_portal_teleports_and_teleport_ended_resends_objects() {
    let server = test_server_with(data());
    let mut client = TestClient::new(&server, 1, 1);
    let me = spawn_player(
        &server,
        &client,
        test_character(1, "Alice", 100.0, 100.0),
        0x10,
    );
    client.conn.active_char = Some(me.clone());
    let portal = portal_npc(&server, (101.0, 101.0));
    client.drain();

    CSUsePortalPacket {
        obj_id: portal.obj_id,
        only_my_portal: false,
    }
    .handle(&server, &mut client.conn)
    .await
    .unwrap();
    let frames = client.drain();
    assert_eq!(frames.len(), 1);
    assert_eq!(frames[0].0, sc::SCTeleportUnitPacket);
    assert_eq!(
        frames[0].1,
        body(&SCTeleportUnitPacket {
            reason: 0,
            error_message: 0,
            x: 500.0,
            y: 600.0,
            z: 70.0,
            z2: std::f32::consts::PI / 180.0 * 90.0,
        })
    );
    assert!(me.data().disabled_set_position);
    // Same instance: the character itself is not moved (the client moves and reports).
    assert_eq!(me.data().unit.transform.x, 100.0);

    // Not a portal / unknown object: nothing.
    let mut plain = Npc::new(1);
    plain.unit.transform.set_xyz(102.0, 102.0, 100.0);
    let plain = Entity::new(server.ids.object.get_next_id().unwrap(), plain);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&plain, &server.view_ctx());
    client.drain();
    use_portal(&server, &me, plain.obj_id);
    use_portal(&server, &me, 0xABCDEF);
    assert!(client.drain().is_empty());

    // CSTeleportEnded: position updates allowed again, neighbours re-sent.
    CSTeleportEndedPacket {
        x: 500.0,
        y: 600.0,
        z: 70.0,
        ori: [0; 16],
    }
    .handle(&server, &mut client.conn)
    .await
    .unwrap();
    assert!(!me.data().disabled_set_position);
    let ops = client.drain_ops();
    assert_eq!(
        ops,
        vec![
            sc::SCUnitStatePacket,
            sc::SCUnitPointsPacket,
            sc::SCUnitStatePacket,
            sc::SCUnitPointsPacket
        ]
    );
}

#[tokio::test]
async fn use_portal_overburdened_is_refused() {
    struct Burdened;
    impl crate::systems::npcs::hooks::SkillsBridge for Burdened {
        fn check_buff_tag(&self, _: &GameServer, _: &WorldObject, tag: u32) -> bool {
            tag == TAG_OVERBURDENED
        }
    }
    let server = test_server_with(data());
    server.systems.npcs.hooks.skills.install(Arc::new(Burdened));
    let mut client = TestClient::new(&server, 1, 1);
    let me = spawn_player(
        &server,
        &client,
        test_character(1, "Alice", 100.0, 100.0),
        0x10,
    );
    let portal = portal_npc(&server, (101.0, 101.0));
    client.drain();
    use_portal(&server, &me, portal.obj_id);
    let frames = client.drain();
    assert_eq!(frames.len(), 1);
    assert_eq!(frames[0].0, sc::SCErrorMsgPacket);
    assert_eq!(
        frames[0].1,
        body(&SCErrorMsgPacket::new(
            error_message::CANNOT_USE_PORTAL_WITH_BACKPACK,
            0,
            true
        ))
    );
    assert!(!me.data().disabled_set_position);
}

#[tokio::test]
async fn use_portal_into_other_instance_loads_it() {
    let server = test_server_with(data());
    let mut client = TestClient::new(&server, 1, 1);
    let me = spawn_player(
        &server,
        &client,
        test_character(1, "Alice", 100.0, 100.0),
        0x10,
    );
    let portal = portal_npc(&server, (101.0, 101.0));
    if let Some(p) = portal.data().portal.as_mut() {
        p.teleport_position.instance_id = 7;
        p.teleport_position.world_id = 3;
    }
    client.drain();
    use_portal(&server, &me, portal.obj_id);
    let ops = client.drain_ops();
    assert_eq!(
        ops,
        vec![sc::SCLoadInstancePacket, sc::SCTeleportUnitPacket]
    );
    let t = me.data().unit.transform;
    // `Transform = TeleportPosition.Clone(); InstanceId = TeleportPosition.WorldId`.
    assert_eq!((t.x, t.y, t.z, t.instance_id), (500.0, 600.0, 70.0, 3));
}

#[tokio::test]
async fn open_portal_spawns_linked_portals_and_delete_kills_both() {
    let server = test_server_with(data());
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Alice", 100.0, 100.0);
    c.portals.private_portals.push(recall(4097, 0, "My spot"));
    let me = spawn_player(&server, &client, c, 0x10);
    client.drain();

    let effect = PortalEffectObject {
        kind: 2,
        id: 4097,
        x: 102.0,
        y: 103.0,
        z: 100.0,
    };
    let (entrance, exit) = open_portal(&server, &me, &effect).expect("portals");
    {
        let e = entrance.data();
        assert_eq!(e.unit.template_id, PORTAL_ENTRANCE_TEMPLATE_ID);
        assert_eq!(e.unit.model_id, 1000 + PORTAL_ENTRANCE_TEMPLATE_ID);
        assert_eq!(e.unit.level, 1);
        assert_eq!(e.unit.owner_id, 1);
        assert_eq!(e.unit.faction_id, 101);
        assert_eq!(e.unit.name, "My spot");
        assert_eq!((e.unit.transform.x, e.unit.transform.y), (102.0, 103.0));
        let p = e.portal.as_ref().unwrap();
        assert_eq!(p.linked_portal_obj_id, exit.obj_id);
        assert_eq!(
            (
                p.teleport_position.x,
                p.teleport_position.y,
                p.teleport_position.rot_z
            ),
            (300.0, 400.0, 90.0)
        );
    }
    {
        let x = exit.data();
        assert_eq!(x.unit.template_id, PORTAL_EXIT_TEMPLATE_ID);
        assert_eq!(
            (
                x.unit.transform.x,
                x.unit.transform.y,
                x.unit.transform.rot_z
            ),
            (300.0, 400.0, 90.0)
        );
        assert_eq!(
            x.portal.as_ref().unwrap().linked_portal_obj_id,
            entrance.obj_id
        );
    }
    // Only the entrance is near the player.
    let ops = client.drain_ops();
    assert_eq!(ops, vec![sc::SCUnitStatePacket, sc::SCUnitPointsPacket]);

    // Unknown book entry: nothing.
    assert!(open_portal(&server, &me, &PortalEffectObject { id: 1, ..effect }).is_none());

    // KillPortalTask on the entrance: death broadcast, removal, and the exit goes too when it
    // still has HP.
    entrance.data().unit.hp = 5;
    exit.data().unit.hp = 5;
    delete_portal_npc(&server, &entrance);
    let frames = client.drain();
    assert_eq!(frames[0].0, sc::SCUnitDeathPacket);
    assert_eq!(
        frames[0].1,
        body(&SCUnitDeathPacket {
            obj_id: entrance.obj_id,
            kill_reason: KILL_REASON_PORTAL_TIMEOUT,
            resurrect_wait_time: 0,
            lost_exp: 0,
            durability_loss_rate: 0
        })
    );
    assert!(frames.iter().any(|f| f.0 == sc::SCUnitsRemovedPacket));
    let world = server.world.world(0).unwrap();
    assert!(world.npc(entrance.obj_id).is_none());
    assert!(world.npc(exit.obj_id).is_none());
    assert_eq!(entrance.data().unit.hp, 0);
    assert_eq!(exit.data().unit.hp, 0);
}

#[tokio::test]
async fn open_portal_needs_reagents() {
    let mut d = data();
    d.systems.npcs.portal.open_portal_inland_reagents[0].amount = 1;
    let server = test_server_with(d);
    let client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Alice", 100.0, 100.0);
    c.portals.private_portals.push(recall(4097, 0, "My spot"));
    let me = spawn_player(&server, &client, c, 0x10);
    let effect = PortalEffectObject {
        kind: 2,
        id: 4097,
        x: 102.0,
        y: 103.0,
        z: 100.0,
    };
    assert!(open_portal(&server, &me, &effect).is_none());
    assert!(server.world.world(0).unwrap().all_npcs().is_empty());
}

#[tokio::test]
async fn portal_book_delete_and_notify_sub_zone() {
    let server = test_server_with(data());
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Alice", 100.0, 100.0);
    c.portals.private_portals.push(recall(4097, 0, "My spot"));
    c.return_district_id = 9;
    let me = spawn_player(&server, &client, c, 0x10);
    client.drain();

    // NotifySubZone: visited district added, the book sent once per recall of the sub zone.
    notify_sub_zone(&server, &me, 77);
    let frames = client.drain();
    let ops: Vec<u16> = frames.iter().map(|f| f.0).collect();
    assert_eq!(
        ops,
        vec![
            sc::SCCharacterPortalsPacket,
            sc::SCCharacterReturnDistrictsPacket,
            sc::SCCharacterPortalsPacket,
            sc::SCCharacterReturnDistrictsPacket
        ]
    );
    // Return point of district 9 for faction 101 = 86.
    assert_eq!(frames[1].1[frames[1].1.len() - 4..], 86u32.to_le_bytes());
    {
        let c = me.data();
        assert_eq!(c.portals.visited_districts.len(), 1);
        assert_eq!(c.portals.visited_districts[0].sub_zone, 77);
        assert_eq!(c.portals.district_portals.len(), 2);
    }
    // Already visited / no recall: nothing.
    notify_sub_zone(&server, &me, 77);
    notify_sub_zone(&server, &me, 78);
    assert!(client.drain().is_empty());

    // CSDeletePortal: private (type != 1) and district (type 1); no packet.
    CSDeletePortalPacket {
        portal_type: 2,
        portal_id: 4097,
    }
    .handle(&server, &mut client.conn)
    .await
    .unwrap(); // no active char: ignored
    client.conn.active_char = Some(me.clone());
    CSDeletePortalPacket {
        portal_type: 2,
        portal_id: 4097,
    }
    .handle(&server, &mut client.conn)
    .await
    .unwrap();
    delete_portal(&server, &me, 1, 5);
    delete_portal(&server, &me, 2, 999);
    assert!(client.drain().is_empty());
    {
        let c = me.data();
        assert!(c.portals.private_portals.is_empty());
        assert!(c.portals.visited_districts.is_empty());
        // Mirrored: the district list is not rebuilt.
        assert_eq!(c.portals.district_portals.len(), 2);
    }
    assert_eq!(
        server.systems.npcs.portals.pending_removals(1),
        Some(PendingRemovals {
            private_portals: vec![4097],
            visited_districts: vec![77]
        })
    );

    // AddPrivatePortal / ChangePrivatePortalName.
    add_private_portal(&server, &me, 1.0, 2.0, 3.0, 4.0, TEST_ZONE, "New");
    assert_eq!(client.drain_ops(), vec![sc::SCCharacterPortalsPacket]);
    let id = me.data().portals.private_portals[0].id;
    assert!(change_private_portal_name(&server, &me, id, "Renamed"));
    assert!(!change_private_portal_name(&server, &me, id + 1, "x"));
    assert_eq!(client.drain_ops(), vec![sc::SCPortalInfoSavedPacket]);
    assert_eq!(me.data().portals.private_portals[0].name, "Renamed");
}

#[tokio::test]
async fn lookups_and_closest_return_portal() {
    let server = test_server_with(data());
    assert_eq!(
        recall_by_id(&server, 6).map(|p| p.name.as_str()),
        Some("Recall B")
    );
    assert!(recall_by_id(&server, 7).is_none());
    assert_eq!(respawn_by_id(&server, 87).unwrap().sub_zone_id, 2);
    // ZoneId from the synthetic main_world.
    assert_eq!(respawn_by_sub_zone(&server, 1).unwrap().zone_id, TEST_ZONE);
    assert_eq!(world_gates_by_id(&server, 221).unwrap().sub_zone_id, 50);
    assert!(world_gates_by_sub_zone(&server, 51).is_none());
    assert_eq!(district_return_point(&server, 9, 102), 87);
    assert_eq!(district_return_point_any(&server, 9), 86);
    assert_eq!(district_return_point_any(&server, 10), 0);

    let client = TestClient::new(&server, 1, 1);
    // At (100, 100): 87 (x 120) is closest but reserved for faction 102.
    let me = spawn_player(
        &server,
        &client,
        test_character(1, "Alice", 100.0, 100.0),
        0x10,
    );
    assert_eq!(closest_return_portal(&server, &me).id, 86);
    me.data().unit.faction_id = 102;
    assert_eq!(closest_return_portal(&server, &me).id, 87);
    // A closed zone is skipped for players, not for GMs.
    let mut d = data();
    d.zones.zones.get_mut(&TEST_ZONE).unwrap().closed = true;
    let server = test_server_with(d);
    let client = TestClient::new(&server, 1, 1);
    let me = spawn_player(
        &server,
        &client,
        test_character(1, "Alice", 100.0, 100.0),
        0x10,
    );
    let p = closest_return_portal(&server, &me);
    assert_eq!((p.id, p.x, p.y, p.zone_id), (0, 100.0, 100.0, TEST_ZONE));
    me.data().access_level = 100;
    assert_eq!(closest_return_portal(&server, &me).id, 86);
}

#[test]
#[ignore = "needs AAEMU_COMPACT_DB"]
fn real_compact_db_portal_tables() {
    let Ok(path) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn = rusqlite::Connection::open(&path).unwrap();
    let app = std::path::Path::new(&path)
        .parent()
        .unwrap()
        .parent()
        .unwrap();
    let files = crate::data::client_files::ClientFileManager::from_sources(&[], app);
    let ctx = crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: app,
        files: &files,
        language: "en_us",
    };
    let t = PortalTables::load(&ctx).unwrap();
    let count = |table: &str| -> usize {
        conn.query_row(&format!("SELECT COUNT(*) FROM {table}"), [], |r| {
            r.get::<_, i64>(0)
        })
        .unwrap() as usize
    };
    assert_eq!(
        t.open_portal_inland_reagents.len(),
        count("open_portal_inland_reagents")
    );
    assert_eq!(
        t.open_portal_outland_reagents.len(),
        count("open_portal_outland_reagents")
    );
    assert!(!t.respawns.is_empty());
    assert!(!t.world_gates.is_empty());
    eprintln!(
        "recall ids {} respawns {} worldgates {} inland {} outland {}",
        t.recalls_key.len(),
        t.respawns.len(),
        t.world_gates.len(),
        t.open_portal_inland_reagents.len(),
        t.open_portal_outland_reagents.len()
    );
}
