//! Flow tests of the instances subsystem without MySQL: a party dungeon (creation, loading,
//! entering, events / actions, room clear, leaving, party kick, cleanup), a system instance with
//! a disconnect, battlefield matchmaking / scoring, and (gated by `AAEMU_COMPACT_DB`) a dungeon
//! on the real 1.2 data with the real `Data/Worlds/instance_burntcastle_armory` spawn files.

use std::collections::HashMap;
use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use aaemu_commons::PacketStream;
use sqlx::mysql::MySqlPoolOptions;

use crate::config::AppConfig;
use crate::data::skills::{BuffKind, BuffTemplate};
use crate::data::world_data::{Zone, ZoneData, ZoneGroup};
use crate::data::world_templates::{
    WorldSpawnLocation, WorldSpawnPosition, WorldTemplate, WorldTemplates,
};
use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::models::npc::Npc;
use crate::net::packet::{encode, ServerPacket};
use crate::opcodes::{cs, sc};
use crate::systems::npcs::doodad::runtime::DoodadCtx;
use crate::systems::npcs::doodad::templates::{DoodadFuncEnterInstance, DoodadFuncExitIndun};
use crate::systems::npcs::portal::packets::SCLoadInstancePacket;
use crate::test_support::{test_character, TestClient, TEST_ZONE};
use crate::world::unit_state::UNIT_STATE_FORCED_BUFFS;
use crate::world::{Entity, Player, PlayerRef, WorldObject};
use crate::GameServer;

use super::data::*;
use super::events::CharEvent;
use super::instant_game::{self, corps};
use super::packets::*;
use super::{dungeon, events, indun, worlds};

const DUNGEON_ZONE: u32 = 236;
const DUNGEON_GROUP: u32 = 45;
const MIRAGE_ZONE: u32 = 260;
const MIRAGE_GROUP: u32 = 49;
const CAMP_ZONE: u32 = 198;
const DUNGEON_SPAWN: WorldSpawnPosition = WorldSpawnPosition {
    world_id: 1,
    zone_id: DUNGEON_ZONE,
    x: 200.0,
    y: 200.0,
    z: 20.0,
    yaw: 90.0,
    pitch: 0.0,
    roll: 0.0,
};

fn body(p: &dyn ServerPacket) -> Vec<u8> {
    encode(p)[6..].to_vec()
}

fn zone(id: u32, key: u32, group: u32) -> Zone {
    Zone {
        id,
        name: format!("zone_{key}"),
        zone_key: key,
        group_id: group,
        ..Zone::default()
    }
}

/// `template` with its single XML zone positioned by a `world_spawns.json` entry.
fn instance_template(
    id: u32,
    name: &str,
    zone_key: u32,
    spawn: WorldSpawnPosition,
) -> WorldTemplate {
    let mut t = WorldTemplate::synthetic(id, name, 2, 2, zone_key);
    t.xml_world.zones.get_mut(&zone_key).unwrap().spawn_position = spawn;
    t
}

fn lookup(name: &str, spawn: WorldSpawnPosition) -> WorldSpawnLocation {
    WorldSpawnLocation {
        name: Some(name.into()),
        spawn_position: Some(spawn),
    }
}

/// A server like `test_support::test_server_with`, with extra world templates / spawn lookups
/// and a zero party-kick delay.
fn build_server(
    mut data: GameData,
    mut templates: Vec<WorldTemplate>,
    lookups: Vec<WorldSpawnLocation>,
    app_path: Option<&Path>,
) -> Arc<GameServer> {
    let mut config = AppConfig {
        id: 1,
        ..AppConfig::default()
    };
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1;
    config.dungeons.auto_team_disband_kick_time = 0;
    if let Some(p) = app_path {
        config.app_path = p.to_path_buf();
    }
    let db = MySqlPoolOptions::new()
        .acquire_timeout(Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    templates.insert(
        0,
        WorldTemplate::synthetic(0, "main_world", 2, 2, TEST_ZONE),
    );
    data.worlds = WorldTemplates::from_templates(templates, lookups).expect("worlds");
    for id in UNIT_STATE_FORCED_BUFFS {
        let mut t = BuffTemplate::simple(id, BuffKind::Good);
        t.duration = 0;
        data.skills.buffs.insert(id, t);
    }
    let server = Arc::new(GameServer::new(config, db, data));
    server.set_self();
    server
}

fn zones(list: &[Zone]) -> ZoneData {
    let mut z = ZoneData::default();
    for x in list {
        z.zone_id_to_key.insert(x.id, x.zone_key);
        z.zones.insert(x.zone_key, x.clone());
        z.groups.entry(x.group_id).or_insert(ZoneGroup {
            id: x.group_id,
            name: format!("group_{}", x.group_id),
            ..ZoneGroup::default()
        });
    }
    z
}

/// A player in the world with a test connection.
struct Pc {
    c: TestClient,
    p: PlayerRef,
}

impl Pc {
    fn new(server: &GameServer, id: u32, name: &str, x: f32, y: f32) -> Self {
        let mut c = TestClient::new(server, id, 1000 + id);
        let mut ch = test_character(id, name, x, y);
        ch.is_online = true;
        ch.obj_id = 0x100 * id;
        let p = Player::new(ch, c.conn.handle());
        server.world.try_add_character(&p);
        let world = server.world.world(0).expect("main_world");
        world.spawn(&p, &server.view_ctx());
        c.conn.active_char = Some(p.clone());
        Self { c, p }
    }

    fn frames(&mut self) -> Vec<(u16, Vec<u8>)> {
        self.c.drain()
    }

    fn ops(&mut self) -> Vec<u16> {
        self.c.drain_ops()
    }

    async fn frame(&mut self, server: &GameServer, opcode: u16, body: &[u8]) {
        let mut v = vec![0xDD, 0x01, 0, 0];
        v.extend_from_slice(&opcode.to_le_bytes());
        v.extend_from_slice(body);
        server.handle_frame(&mut self.c.conn, &v).await.unwrap();
    }

    /// `CSMoveUnitPacket` (a `UnitMoveType` to x, y, z).
    async fn move_to(&mut self, server: &GameServer, x: f32, y: f32, z: f32) {
        let mut s = PacketStream::new();
        s.write_bc(self.p.obj_id)
            .write_u8(1)
            .write_u32(1234)
            .write_u8(0x02)
            .write_position(x, y, z)
            .write_i16(0)
            .write_i16(0)
            .write_i16(0)
            .write_i8(0)
            .write_i8(0)
            .write_i8(0)
            .write_i8(0)
            .write_i8(0)
            .write_i8(0)
            .write_u8(1)
            .write_u8(0)
            .write_u8(0);
        self.frame(server, cs::CSMoveUnitPacket, s.as_bytes()).await;
    }

    fn instance(&self) -> u32 {
        self.p.data().unit.transform.instance_id
    }
}

fn removed_ids(frames: &[(u16, Vec<u8>)]) -> Vec<u32> {
    let mut out = Vec::new();
    for (op, b) in frames {
        if *op != sc::SCUnitsRemovedPacket {
            continue;
        }
        let n = u16::from_le_bytes([b[0], b[1]]) as usize;
        for i in 0..n {
            let o = 2 + i * 3;
            out.push(u32::from(b[o]) | u32::from(b[o + 1]) << 8 | u32::from(b[o + 2]) << 16);
        }
    }
    out
}

fn bc(b: &[u8]) -> u32 {
    u32::from(b[0]) | u32::from(b[1]) << 8 | u32::from(b[2]) << 16
}

fn load_instance(
    instance: u32,
    zone: u32,
    x: f32,
    y: f32,
    z: f32,
    rot: (f32, f32, f32),
) -> Vec<u8> {
    body(&SCLoadInstancePacket {
        instance_id: instance,
        zone_id: zone,
        x,
        y,
        z,
        ang_x: rot.0,
        ang_y: rot.1,
        ang_z: rot.2,
    })
}

fn dungeon_data() -> GameData {
    let mut data = GameData::empty();
    data.zones = zones(&[
        zone(TEST_ZONE, TEST_ZONE, 1),
        zone(DUNGEON_ZONE, DUNGEON_ZONE, DUNGEON_GROUP),
        zone(MIRAGE_ZONE, MIRAGE_ZONE, MIRAGE_GROUP),
    ]);
    let inst = &mut data.systems.instances;
    inst.zone_order = ZoneOrder {
        zones: vec![
            (TEST_ZONE, 1),
            (DUNGEON_ZONE, DUNGEON_GROUP),
            (MIRAGE_ZONE, MIRAGE_GROUP),
        ],
    };
    inst.indun.zones.insert(
        DUNGEON_GROUP,
        IndunZone {
            zone_group_id: DUNGEON_GROUP,
            name: "armory".into(),
            level_min: 1,
            level_max: 55,
            max_players: 5,
            enter_count: 3,
            ..IndunZone::default()
        },
    );
    inst.indun.zones.insert(
        MIRAGE_GROUP,
        IndunZone {
            zone_group_id: MIRAGE_GROUP,
            name: "mirage".into(),
            max_players: 190,
            pvp: false,
            ..IndunZone::default()
        },
    );
    // Scripting: NPC 900 killed -> room 5 cleared -> (spawner effect); room 6 (doodad 777,
    // radius 20) emptied -> NPCs tagged 3 removed.
    let actions = [
        (
            10,
            IndunActionKind::SetRoomCleareds { indun_room_id: 5 },
            11,
        ),
        (11, IndunActionKind::NpcSpawner, 0),
        (12, IndunActionKind::RemoveTaggedNpcs { tag_id: 3 }, 0),
    ];
    for (id, kind, next) in actions {
        inst.indun.actions.insert(
            id,
            IndunAction {
                id,
                detail_id: 0,
                zone_group_id: DUNGEON_GROUP,
                next_action_id: next,
                kind,
            },
        );
    }
    inst.indun.events.insert(
        DUNGEON_GROUP,
        vec![
            IndunEvent {
                id: 1,
                condition_id: 1,
                zone_group_id: DUNGEON_GROUP,
                start_action_id: 12,
                kind: IndunEventKind::NoAliveChInRooms { room_id: 6 },
            },
            IndunEvent {
                id: 2,
                condition_id: 2,
                zone_group_id: DUNGEON_GROUP,
                start_action_id: 10,
                kind: IndunEventKind::NpcKilleds { npc_id: 900 },
            },
        ],
    );
    inst.indun.rooms.insert(
        6,
        IndunRoom {
            id: 6,
            doodad_id: 777,
            radius: 20,
            zone_group_id: DUNGEON_GROUP,
        },
    );
    data.systems.skills.npc_tags.insert(3, vec![901]);
    data
}

fn dungeon_server() -> Arc<GameServer> {
    let mirage_spawn = WorldSpawnPosition {
        world_id: 2,
        zone_id: MIRAGE_ZONE,
        x: 300.0,
        y: 300.0,
        z: 50.0,
        yaw: 200.0,
        ..WorldSpawnPosition::default()
    };
    build_server(
        dungeon_data(),
        vec![
            instance_template(1, "instance_test", DUNGEON_ZONE, DUNGEON_SPAWN),
            instance_template(2, "arche_mall_world", MIRAGE_ZONE, mirage_spawn),
        ],
        vec![
            lookup("instance_test", DUNGEON_SPAWN),
            lookup("arche_mall_world", mirage_spawn),
        ],
        None,
    )
}

async fn await_loader(d: &Arc<dungeon::Dungeon>) {
    let h = d.st().loader.take().expect("loader scheduled");
    h.await.expect("loader");
}

/// Uses an `EnterInstance` doodad func (`DoodadFuncEnterInstance.Use`).
fn use_enter_func(server: &GameServer, p: &PlayerRef, zone_id: u32) {
    let world = server.world.world(0).unwrap();
    let doodad = Entity::new(0x9000, Doodad::new(1));
    let ctx = DoodadCtx::new(server, world, doodad);
    let f = DoodadFuncEnterInstance {
        id: 1,
        zone_id,
        item_id: 0,
    };
    crate::systems::npcs::doodad::funcs::world::enter_instance(
        &f,
        &ctx,
        Some(&WorldObject::from(p)),
    );
}

fn use_exit_func(server: &GameServer, p: &PlayerRef) {
    let world = server.world.world(0).unwrap();
    let doodad = Entity::new(0x9001, Doodad::new(1));
    let ctx = DoodadCtx::new(server, world, doodad);
    crate::systems::npcs::doodad::funcs::world::exit_indun(
        &DoodadFuncExitIndun::default(),
        &ctx,
        Some(&WorldObject::from(p)),
    );
}

#[tokio::test]
async fn party_dungeon_create_enter_script_leave_kick_and_cleanup() {
    let server = dungeon_server();
    let mut a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    let mut b = Pc::new(&server, 2, "Beta", 110.0, 100.0);
    let mut c = Pc::new(&server, 3, "Gamma", 105.0, 100.0);

    // A and B form a party.
    let teams = &server.systems.social.teams;
    teams.ask_to_join(&server, &a.p, "Beta", 0, true, Some(&b.p));
    teams.reply_to_join_team(&server, &b.p, 0, true, 1, false, "", false);
    let team_id = teams.team_by_obj_id(a.p.obj_id).expect("team").id;
    a.frames();
    b.frames();
    c.frames();

    // ---- A uses the dungeon entrance: the dungeon (instance 100) is created and loads.
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    let mwp = a.p.data().main_world_position.expect("return point saved");
    assert_eq!(
        (mwp.instance_id, mwp.zone_id, mwp.x, mwp.y, mwp.z),
        (0, TEST_ZONE, 100.0, 100.0, 100.0)
    );
    let d = server
        .systems
        .instances
        .dungeon_of_world(100)
        .expect("dungeon of instance 100");
    assert!(!d.is_system);
    assert!(d.is_team_owned());
    assert!(d.has_access(1) && d.has_access(2));
    assert_eq!(
        server.systems.instances.indun.entry_count(1, DUNGEON_GROUP),
        1
    );
    let processing = body(&SCProcessingInstancePacket {
        zone_id: DUNGEON_ZONE as i32,
    });
    // CreateWorldInstance(notifyPlayer) + QueuePlayer while loading.
    let f = a.frames();
    let p: Vec<_> = f
        .iter()
        .filter(|(op, _)| *op == sc::SCProcessingInstancePacket)
        .collect();
    assert_eq!(p.len(), 2);
    assert!(p.iter().all(|(_, b)| *b == processing));
    assert!(!f.iter().any(|(op, _)| *op == sc::SCLoadInstancePacket));

    // The room doodad and two NPCs exist in the instance before the loader registers again.
    let dworld = server.world.world(100).unwrap();
    let ctx = server.view_ctx();
    let mut room = Doodad::new(777);
    room.transform.set_xyz(205.0, 200.0, 20.0);
    let room = Entity::new(0x7000, room);
    dworld.spawn(&room, &ctx);
    let mut tagged = Npc::new(901);
    tagged.unit.transform.set_xyz(210.0, 210.0, 20.0);
    tagged.unit.hp = 10;
    tagged.unit.max_hp = 10;
    let tagged = Entity::new(0x7001, tagged);
    dworld.spawn(&tagged, &ctx);
    let mut boss = Npc::new(900);
    boss.unit.transform.set_xyz(220.0, 220.0, 20.0);
    let boss = Entity::new(0x7002, boss);
    dworld.spawn(&boss, &ctx);

    await_loader(&d).await;
    assert!(d.st().finished_loading);
    assert!(d.st().enter_requests.is_empty());
    // The room event found its doodad on the second registration.
    assert_eq!(
        server.systems.instances.events.regs(100).area_clear.len(),
        1
    );
    assert_eq!(
        server.systems.instances.events.regs(100).unit_killed.len(),
        2
    );
    // A was moved: SCLoadInstance(instance, zone, spawn, DegToRad(rotation)).
    let f = a.frames();
    let expected = load_instance(
        100,
        DUNGEON_ZONE,
        200.0,
        200.0,
        20.0,
        (0.0, 0.0, worlds::deg_to_rad(90.0)),
    );
    assert!(f.contains(&(sc::SCLoadInstancePacket, expected)));
    assert_eq!(a.instance(), 100);
    assert_eq!(a.p.data().unit.transform.zone_id, DUNGEON_ZONE);
    assert!(a.p.data().disabled_set_position);
    assert!(worlds::has_character(&dworld, 1));
    let ev = &server.systems.instances.events.characters;
    assert_eq!(ev.count(&a.p, CharEvent::DungeonLeave), 1);
    assert_eq!(ev.count(&a.p, CharEvent::TeamLeave), 1);

    // ---- CSInstanceLoaded, then the first move switches the region grid.
    a.frame(&server, cs::CSInstanceLoadedPacket, &[]).await;
    assert_eq!(
        a.ops(),
        vec![sc::SCUnitStatePacket, sc::SCDetailedTimeOfDayPacket]
    );
    assert!(!a.p.data().disabled_set_position);
    a.move_to(&server, 200.0, 200.0, 20.0).await;
    let f = a.frames();
    let gone = removed_ids(&f);
    assert!(gone.contains(&b.p.obj_id) && gone.contains(&c.p.obj_id));
    assert!(f.iter().any(|(op, _)| *op == sc::SCDoodadsCreatedPacket));
    assert!(f
        .iter()
        .any(|(op, b)| *op == sc::SCUnitStatePacket && bc(b) == 0x7001));
    assert!(removed_ids(&c.frames()).contains(&a.p.obj_id));
    assert!(removed_ids(&b.frames()).contains(&a.p.obj_id));
    assert_eq!(WorldObject::from(&a.p).region().map(|r| r.0), Some(100));

    // AreaClearTick: A is in room 6.
    dungeon::area_clear_tick(&server, &d);
    assert_eq!(server.systems.instances.events.room_player_count(1, 100), 1);

    // ---- B (party member) enters the loaded dungeon right away.
    use_enter_func(&server, &b.p, DUNGEON_ZONE);
    assert!(b
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCLoadInstancePacket));
    assert_eq!(b.instance(), 100);
    b.frame(&server, cs::CSInstanceLoadedPacket, &[]).await;
    b.move_to(&server, 260.0, 200.0, 20.0).await;
    assert!(a
        .frames()
        .iter()
        .any(|(op, body)| *op == sc::SCUnitStatePacket && bc(body) == b.p.obj_id));
    b.frames();

    // ---- The boss dies: room 5 cleared through the action chain.
    assert!(!d.is_room_cleared(5));
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&a.p)),
    );
    assert!(d.is_room_cleared(5));

    // ---- A leaves through the exit doodad: back to the main world return point.
    use_exit_func(&server, &a.p);
    let f = a.frames();
    let expected = load_instance(
        mwp.world_id,
        TEST_ZONE,
        100.0,
        100.0,
        100.0,
        (
            worlds::deg_to_rad(mwp.rot_x),
            worlds::deg_to_rad(mwp.rot_y),
            worlds::deg_to_rad(mwp.rot_z),
        ),
    );
    assert!(f.contains(&(sc::SCLoadInstancePacket, expected)));
    assert_eq!(a.instance(), 0);
    assert!(!worlds::has_character(&dworld, 1));
    assert_eq!(ev.count(&a.p, CharEvent::DungeonLeave), 0);
    a.frame(&server, cs::CSInstanceLoadedPacket, &[]).await;
    a.move_to(&server, 100.0, 100.0, 100.0).await;
    assert!(c
        .frames()
        .iter()
        .any(|(op, body)| *op == sc::SCUnitStatePacket && bc(body) == a.p.obj_id));
    assert!(removed_ids(&b.frames()).contains(&a.p.obj_id));

    // AreaClearTick: room 6 is empty now -> the tagged NPC is removed.
    assert!(dworld.npc(0x7001).is_some());
    dungeon::area_clear_tick(&server, &d);
    assert!(dworld.npc(0x7001).is_none());
    assert_eq!(server.systems.instances.events.room_player_count(1, 100), 0);

    // ---- B leaves the party: kicked out after the (zero) delay, the empty dungeon goes away.
    teams.ask_risky_team(
        &server,
        &b.p,
        team_id,
        2,
        crate::systems::social::team::risky_action::LEAVE,
    );
    let leave_party = body(&crate::packets::world::SCErrorMsgPacket::new(
        worlds::error_message::INSTANCE_LEAVE_PARTY,
        0,
        true,
    ));
    assert!(b
        .frames()
        .contains(&(sc::SCErrorMsgPacket, leave_party.clone())));
    // The party auto-disbands: A left the dungeon (unsubscribed), so no dungeon handler runs.
    assert!(!a.frames().contains(&(sc::SCErrorMsgPacket, leave_party)));
    assert_eq!(d.st().leave_requests.len(), 1);
    tokio::time::sleep(Duration::from_millis(5)).await;
    dungeon::leave_dungeon_tick(&server, &d);
    assert!(b
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCLoadInstancePacket));
    assert_eq!(b.instance(), 0);
    assert!(server.systems.instances.dungeon_of_world(100).is_none());
    assert!(!server.world.has_world(100));
    assert!(server.systems.npcs.spawns.get(100).is_none());
    assert!(d.world_id().is_none());
    // The instance id was released.
    assert_eq!(server.ids.world.get_next_id().unwrap(), 100);
}

#[tokio::test]
async fn dungeon_requirements_and_deletion() {
    let server = dungeon_server();
    let mut a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    a.p.data().unit.level = 60;
    a.frames();
    // Level too high.
    assert!(!indun::request_dungeon_instance(
        &server,
        &a.p,
        DUNGEON_ZONE,
        0
    ));
    assert_eq!(
        a.frames(),
        vec![(
            sc::SCErrorMsgPacket,
            body(&crate::packets::world::SCErrorMsgPacket::new(
                worlds::error_message::INSTANCE_LEVEL,
                0,
                true
            ))
        )]
    );
    // Unknown zone / not a dungeon.
    assert!(!indun::request_dungeon_instance(&server, &a.p, 999, 0));
    assert!(!indun::request_dungeon_instance(
        &server, &a.p, TEST_ZONE, 0
    ));
    a.p.data().unit.level = 10;
    // A solo dungeon: queued, then the entry limit (3 within 4 hours) applies.
    assert!(indun::request_dungeon_instance(
        &server,
        &a.p,
        DUNGEON_ZONE,
        0
    ));
    let d = server.systems.instances.dungeon_of_world(100).unwrap();
    assert!(!d.is_team_owned());
    assert!(d.is_queued(&a.p));
    // Asking again while queued: "try later".
    a.frames();
    assert!(indun::request_dungeon_instance(
        &server,
        &a.p,
        DUNGEON_ZONE,
        0
    ));
    assert_eq!(
        a.frames(),
        vec![(
            sc::SCErrorMsgPacket,
            body(&crate::packets::world::SCErrorMsgPacket::new(
                worlds::error_message::TRY_LATER_INSTANCE,
                0,
                true
            ))
        )]
    );
    await_loader(&d).await;
    assert_eq!(a.instance(), 100);
    // RequestDeletion: A gives up the access, nobody is left -> destroyed.
    let zone = indun::zone_by_id(&server, DUNGEON_ZONE);
    assert!(indun::request_deletion(&server, &a.p, zone.as_ref()));
    assert!(server.systems.instances.dungeon_of_world(100).is_none());
    a.frames();
    // Nothing left to unbind.
    assert!(indun::request_deletion(&server, &a.p, zone.as_ref()));
    assert_eq!(
        a.frames(),
        vec![(
            sc::SCErrorMsgPacket,
            body(&crate::packets::world::SCErrorMsgPacket::new(
                worlds::error_message::ALREADY_UNBOUND_INSTANCE,
                0,
                true
            ))
        )]
    );
    // Info tick with no instances left is a no-op.
    indun::indun_info_tick(&server);
}

#[tokio::test]
async fn system_instance_with_fixed_id_and_disconnect() {
    let server = dungeon_server();
    // `Dungeons.AutoCreate`: arche_mall_world as instance 1, channel 0.
    let d = indun::create_system_instance(&server, None, MIRAGE_ZONE, 0, true, 1)
        .expect("system instance");
    assert!(d.is_system);
    assert_eq!(d.world_id(), Some(1));
    assert_eq!(server.systems.instances.worlds.channel_id(1), 0);
    await_loader(&d).await;
    // A second request for the same zone group / channel returns the same instance.
    let again = indun::create_system_instance(&server, None, MIRAGE_ZONE, 0, true, 1).unwrap();
    assert!(Arc::ptr_eq(&d, &again));

    let mut p = Pc::new(&server, 4, "Delta", 100.0, 100.0);
    p.frames();
    let (ok, joined) = indun::request_system_instance(&server, &p.p, MIRAGE_ZONE, 0);
    assert!(ok);
    assert!(Arc::ptr_eq(&joined.unwrap(), &d));
    let expected = load_instance(
        1,
        MIRAGE_ZONE,
        300.0,
        300.0,
        50.0,
        (0.0, 0.0, worlds::deg_to_rad(200.0)),
    );
    assert!(p.frames().contains(&(sc::SCLoadInstancePacket, expected)));
    let ev = &server.systems.instances.events.characters;
    assert_eq!(ev.count(&p.p, CharEvent::Disconnect), 1);
    // System instances do not subscribe to team events.
    assert_eq!(ev.count(&p.p, CharEvent::TeamJoin), 0);
    let world = server.world.world(1).unwrap();
    assert!(worlds::has_character(&world, 4));

    // Hard disconnect: removed from the instance, unsubscribed.
    events::fire_disconnect(&server, &p.p);
    assert!(!worlds::has_character(&world, 4));
    assert_eq!(ev.count(&p.p, CharEvent::Disconnect), 0);
    assert_eq!(ev.count(&p.p, CharEvent::DungeonLeave), 0);
    // System instances survive (not expired, not destroyed by deletion requests).
    assert!(!d.is_expired());
    let zone = indun::zone_by_id(&server, MIRAGE_ZONE);
    indun::request_deletion(&server, &p.p, zone.as_ref());
    assert!(server.systems.instances.dungeon_of_world(1).is_some());
    indun::indun_info_tick(&server);
}

#[tokio::test]
async fn court_case_blocks_queueing() {
    let server = dungeon_server();
    let mut a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    // Without a trial the player is not in court; the check itself is TrialManager's.
    assert!(!server.systems.social.trials.is_player_in_court(1));
    assert!(indun::request_dungeon_instance(
        &server,
        &a.p,
        DUNGEON_ZONE,
        0
    ));
    a.frames();
}

// ---------------------------------------------------------------------------------------------
// Battlefields
// ---------------------------------------------------------------------------------------------

fn camp_server(corps_size: u32, victory_score: i32) -> Arc<GameServer> {
    let mut data = GameData::empty();
    data.zones = zones(&[
        zone(TEST_ZONE, TEST_ZONE, 1),
        zone(CAMP_ZONE, CAMP_ZONE, 31),
    ]);
    let point = |x: f32, y: f32| Point {
        zone_id: CAMP_ZONE,
        x,
        y,
        z: 199.0,
        ..Point::default()
    };
    data.systems.instances.battlefields.battlefields = HashMap::from([(
        7,
        Battlefield {
            id: 7,
            zone_key: CAMP_ZONE,
            spawns: Some(BattlefieldSpawns {
                battlefield_id: 7,
                corps1_spawn: Some(point(1101.1, 401.5)),
                corps2_spawn: Some(point(1229.0, 530.0)),
            }),
            rule_set: Some(GameRuleSet {
                id: 22,
                corps_size,
                corps1_faction_id: 159,
                corps2_faction_id: 160,
                time_opening: 4,
                time_ending: 0,
                time_playing: 15,
                victory_score,
                battlefield_id: 7,
            }),
        },
    )]);
    build_server(
        data,
        vec![WorldTemplate::synthetic(
            3,
            "instance_training_camp",
            2,
            2,
            CAMP_ZONE,
        )],
        Vec::new(),
        None,
    )
}

#[tokio::test]
async fn battlefield_queue_match_join_score_and_end() {
    let server = camp_server(1, 60);
    let mut e = Pc::new(&server, 5, "Echo", 100.0, 100.0);
    let mut f = Pc::new(&server, 6, "Foxtrot", 120.0, 100.0);
    e.frames();
    f.frames();

    // ---- Both apply (CSApplyToInstantGame) and get SCAppliedToInstantGame.
    e.frame(&server, cs::CSApplyToInstantGamePacket, &[7, 0, 0, 0, 2])
        .await;
    f.frame(&server, cs::CSApplyToInstantGamePacket, &[7, 0, 0, 0, 2])
        .await;
    let applied = body(&SCAppliedToInstantGamePacket {
        battlefield_id: 7,
        corps: corps::ANY,
        error_message_id: 0,
    });
    assert_eq!(
        e.frames(),
        vec![(sc::SCAppliedToInstantGamePacket, applied.clone())]
    );
    assert_eq!(
        f.frames(),
        vec![(sc::SCAppliedToInstantGamePacket, applied)]
    );
    // Applying twice is ignored; unknown battlefields are refused.
    instant_game::apply_to_battlefield(&server, 7, corps::ANY, &e.p);
    instant_game::apply_to_battlefield(&server, 99, corps::ANY, &e.p);
    assert!(e.frames().is_empty());
    let mgr = &server.systems.instances.instant_games;
    assert_eq!(mgr.queued(7), Some(vec![5, 6]));

    // ---- Matchmaking: one game (instance 100), E -> corps 2, F -> corps 1.
    instant_game::battlefield_tick(&server);
    assert_eq!(mgr.games().len(), 1);
    assert_eq!(mgr.queued(7), Some(vec![]));
    let game = mgr.games()[0].clone();
    assert!(game.is_full());
    assert_eq!(game.world_id, 100);
    let zid = ZoneInstanceId {
        zone_id: CAMP_ZONE,
        instance_id: 100,
    };
    let invite = |c| {
        body(&SCInviteToInstantGamePacket {
            zone_instance_id: zid,
            ruleset_id: 22,
            corps: c,
            qualified_id: 1,
        })
    };
    assert_eq!(
        e.frames(),
        vec![(sc::SCInviteToInstantGamePacket, invite(corps::CORPS2))]
    );
    assert_eq!(
        f.frames(),
        vec![(sc::SCInviteToInstantGamePacket, invite(corps::CORPS1))]
    );

    // ---- Both accept (CSJoinInstantGame) and are moved to their corps spawn.
    let mut join = vec![1];
    join.extend_from_slice(&1u64.to_le_bytes());
    e.frame(&server, cs::CSJoinInstantGamePacket, &join).await;
    f.frame(&server, cs::CSJoinInstantGamePacket, &join).await;
    assert!(e.frames().contains(&(
        sc::SCLoadInstancePacket,
        load_instance(100, CAMP_ZONE, 1229.0, 530.0, 199.0, (0.0, 0.0, 0.0))
    )));
    assert!(f.frames().contains(&(
        sc::SCLoadInstancePacket,
        load_instance(100, CAMP_ZONE, 1101.1, 401.5, 199.0, (0.0, 0.0, 0.0))
    )));
    assert_eq!(e.instance(), 100);
    assert!(e.p.data().main_world_position.is_some());

    // ---- Entered: joined packet + corps faction; the second entry starts the game.
    let entered = 1u64.to_le_bytes();
    e.frame(&server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    let ef = e.frames();
    assert_eq!(ef[0].0, sc::SCInstantGameJoinedPacket);
    assert_eq!(
        ef[0].1,
        body(&SCInstantGameJoinedPacket {
            zone_instance_id: zid,
            corps: corps::CORPS2,
            rule_set: game.rule_set
        })
    );
    assert_eq!(e.p.data().unit.faction_id, 160);
    assert_eq!(e.p.data().unit.origin_faction_id, 101);
    f.frame(&server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    let ff = f.frames();
    assert!(ff.iter().any(|(op, _)| *op == sc::SCInstantGameStartPacket));
    assert!(e
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCInstantGameStartPacket));
    assert_eq!(f.p.data().unit.faction_id, 159);

    // ---- E kills F: kill packet, killstreak, 30 points (victory at 60).
    events::on_do_die(
        &server,
        &WorldObject::from(&f.p),
        Some(&WorldObject::from(&e.p)),
    );
    let kill = body(&SCInstantGameKillPacket {
        zone_instance_id: zid,
        killer_id: 5,
        victim_id: 6,
        killer_corps: corps::CORPS2,
        victim_corps: corps::CORPS1,
        killer_killstreak: 1,
        killer_name: "Echo".into(),
        victim_name: "Foxtrot".into(),
        killer_corps_kills: 1,
        victim_corps_deaths: 0,
    });
    let point = |total| {
        body(&SCInstantGameAddPointPacket {
            zone_instance_id: zid,
            corps: corps::CORPS1,
            points: 30,
            score1: total,
            score2: 0,
            char_name: "Echo".into(),
        })
    };
    let streak = |n| {
        body(&SCInstantGameKillstreakPacket {
            zone_instance_id: zid,
            killstreak: n,
            skill_id: 0,
            enabled: true,
        })
    };
    assert_eq!(
        e.frames(),
        vec![
            (sc::SCInstantGameKillPacket, kill.clone()),
            (sc::SCInstantGameKillstreakPacket, streak(1)),
            (sc::SCInstantGameAddPointPacket, point(30)),
        ]
    );
    assert_eq!(
        f.frames(),
        vec![
            (sc::SCInstantGameKillPacket, kill),
            (sc::SCInstantGameAddPointPacket, point(30)),
        ]
    );

    // ---- Second kill: 60 points -> corps 2 wins, the result is sent, the game ends.
    events::on_do_die(
        &server,
        &WorldObject::from(&f.p),
        Some(&WorldObject::from(&e.p)),
    );
    let ef = e.frames();
    let end = ef
        .iter()
        .find(|(op, _)| *op == sc::SCInstantGameEndPacket)
        .expect("end packet");
    let mut s = PacketStream::new();
    s.write_u32(CAMP_ZONE).write_u32(100).write_u8(8);
    // corps 1 (F): lose, 0 points, 0 kills, 2 deaths
    s.write_u8(1)
        .write_u32(159)
        .write_i32(0)
        .write_i16(0)
        .write_i16(2)
        .write_u8(1);
    s.write_u32(6)
        .write_str("Foxtrot")
        .write_i32(0)
        .write_i32(0)
        .write_u32(0)
        .write_u16(0)
        .write_u16(0)
        .write_u16(0)
        .write_u16(2);
    // corps 2 (E): win, 60 points, 2 kills
    s.write_u8(2)
        .write_u32(160)
        .write_i32(60)
        .write_i16(2)
        .write_i16(0)
        .write_u8(1);
    s.write_u32(5)
        .write_str("Echo")
        .write_i32(60)
        .write_i32(0)
        .write_u32(0)
        .write_u16(2)
        .write_u16(2)
        .write_u16(0)
        .write_u16(0);
    assert_eq!(end.1, s.into_bytes());
    // TimeEnding is 0: the game is destroyed right away, the players go back.
    tokio::time::sleep(Duration::from_millis(50)).await;
    assert!(mgr.games().is_empty());
    assert!(!server.world.has_world(100));
    assert_eq!(e.instance(), 0);
    assert_eq!(e.p.data().unit.faction_id, 101);
    assert!(mgr.current(&e.p).is_none());
    assert!(e
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCLoadInstancePacket));
    assert_eq!(
        server
            .systems
            .instances
            .events
            .characters
            .count(&e.p, CharEvent::Kill),
        0
    );
}

#[tokio::test]
async fn battlefield_decline_requeues() {
    let server = camp_server(1, 60);
    let mut h = Pc::new(&server, 7, "Hotel", 100.0, 100.0);
    instant_game::apply_to_battlefield(&server, 7, corps::ANY, &h.p);
    instant_game::battlefield_tick(&server);
    let mgr = &server.systems.instances.instant_games;
    let game = mgr.games()[0].clone();
    // Not full (drill camp): kept in the queue list.
    assert!(!game.is_full());
    assert_eq!(mgr.queue_list().len(), 1);
    assert!(h
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCInviteToInstantGamePacket));
    // Decline: removed from the game, applied again.
    let mut decline = vec![0];
    decline.extend_from_slice(&1u64.to_le_bytes());
    h.frame(&server, cs::CSJoinInstantGamePacket, &decline)
        .await;
    assert!(!game.has_player(&h.p));
    assert_eq!(mgr.queued(7), Some(vec![7]));
    assert_eq!(h.ops(), vec![sc::SCAppliedToInstantGamePacket]);
    // The next tick refills the waiting game.
    instant_game::battlefield_tick(&server);
    assert!(game.has_player(&h.p));
    assert_eq!(mgr.games().len(), 1);
    // Cancel: withdraw from the game.
    h.frame(&server, cs::CSCancelInstantGamePacket, &[]).await;
    assert!(!game.has_player(&h.p));
    assert!(!instant_game::player_can_enter(&server, &h.p) || h.instance() == 0);
}

// ---------------------------------------------------------------------------------------------
// Real data (gated)
// ---------------------------------------------------------------------------------------------

/// The real 1.2 tables + the real `instance_burntcastle_armory` spawn files
/// (`AAEMU_COMPACT_DB=... cargo test -p aaemu-game real_dungeon -- --ignored`).
#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
#[ignore = "needs AAEMU_COMPACT_DB"]
async fn real_dungeon_spawns_load_and_leave_returns_to_main_world() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(&db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .expect("compact db");
    let app_path = Path::new(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = crate::data::client_files::ClientFileManager::from_sources(&[], &app_path);
    let ctx = crate::systems::DataLoadCtx {
        conn: &conn,
        app_path: &app_path,
        files: &files,
        language: "en_us",
    };
    let mut data = GameData::empty();
    data.zones = ZoneData::load(&conn, 0).expect("zones");
    data.systems.npcs = crate::systems::npcs::Data::load(&ctx).expect("npcs data");
    data.systems.instances = super::Data::load(&ctx).expect("instances data");
    let spawns_json =
        std::fs::read_to_string(app_path.join("Data/Worlds/world_spawns.json")).unwrap();
    let lookups: Vec<WorldSpawnLocation> = crate::data::world_templates::parse_world_spawns(
        spawns_json.trim_start_matches('\u{feff}'),
    )
    .unwrap()
    .into_iter()
    .flatten()
    .collect();
    let spawn = lookups
        .iter()
        .find(|l| l.name.as_deref() == Some("instance_burntcastle_armory"))
        .and_then(|l| l.spawn_position)
        .unwrap();
    let server = build_server(
        data,
        vec![instance_template(
            1,
            "instance_burntcastle_armory",
            236,
            spawn,
        )],
        lookups,
        Some(&app_path),
    );
    let mut a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    a.p.data().unit.level = 40;
    a.frames();
    // Zone id 168 is zone key 236 (group 45, "instance_burntcastle_armory").
    use_enter_func(&server, &a.p, 168);
    let d = server
        .systems
        .instances
        .dungeon_of_world(100)
        .expect("dungeon");
    await_loader(&d).await;
    let stats = server.systems.npcs.spawns.get(100).unwrap().counts();
    eprintln!("instance spawners: {stats:?}");
    assert!(stats[0] > 30, "npc spawners {stats:?}");
    let world = server.world.world(100).unwrap();
    assert!(!world.all_doodads().is_empty(), "the doodad spawned");
    let expected = load_instance(
        100,
        236,
        spawn.x,
        spawn.y,
        spawn.z,
        (
            worlds::deg_to_rad(spawn.roll),
            worlds::deg_to_rad(spawn.pitch),
            worlds::deg_to_rad(spawn.yaw),
        ),
    );
    assert!(a.frames().contains(&(sc::SCLoadInstancePacket, expected)));
    a.frame(&server, cs::CSInstanceLoadedPacket, &[]).await;
    a.move_to(&server, 199.0, 180.0, spawn.z).await;
    a.frames();
    // The active-region tick spawns the NPCs around the player.
    crate::systems::npcs::spawn_manager::active_region_tick(&server);
    let npcs = world.around(&WorldObject::from(&a.p), |o| o.as_npc().cloned());
    assert!(!npcs.is_empty(), "no NPCs spawned around the player");
    assert!(a
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCUnitStatePacket));
    // Leave: back to the saved main_world position.
    use_exit_func(&server, &a.p);
    assert_eq!(a.instance(), 0);
    let t = a.p.data().unit.transform;
    assert_eq!((t.zone_id, t.x, t.y, t.z), (TEST_ZONE, 100.0, 100.0, 100.0));
    assert!(a
        .frames()
        .iter()
        .any(|(op, _)| *op == sc::SCLoadInstancePacket));
}

#[tokio::test]
async fn battlefield_entered_requires_actual_instance_and_subscribes_kills_once() {
    let server = camp_server(1, 60);
    let mut p = Pc::new(&server, 7, "Invited", 100.0, 100.0);
    instant_game::apply_to_battlefield(&server, 7, corps::ANY, &p.p);
    instant_game::battlefield_tick(&server);
    let game = server
        .systems
        .instances
        .instant_games
        .current(&p.p)
        .unwrap();
    p.frames();
    let original_faction = p.p.data().unit.faction_id;
    let entered = 1u64.to_le_bytes();
    p.frame(&server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    assert!(p.frames().is_empty());
    assert!(game.st().members.is_empty());
    assert_eq!(p.p.data().unit.faction_id, original_faction);
    assert_eq!(
        server
            .systems
            .instances
            .events
            .characters
            .count(&p.p, CharEvent::Kill),
        0
    );
    let mut join = vec![1];
    join.extend_from_slice(&entered);
    p.frame(&server, cs::CSJoinInstantGamePacket, &join).await;
    assert_eq!(p.instance(), game.world_id);
    p.frames();
    p.frame(&server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    assert!(p.ops().contains(&sc::SCInstantGameJoinedPacket));
    assert_eq!(game.st().members.len(), 1);
    assert_eq!(
        server
            .systems
            .instances
            .events
            .characters
            .count(&p.p, CharEvent::Kill),
        1
    );
    p.frame(&server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    assert!(p.frames().is_empty());
    assert_eq!(game.st().members.len(), 1);
    assert_eq!(
        server
            .systems
            .instances
            .events
            .characters
            .count(&p.p, CharEvent::Kill),
        1
    );
}
