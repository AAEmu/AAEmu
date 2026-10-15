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
        ch.unit.hp = 100;
        ch.unit.max_hp = 100;
        ch.obj_id = server.ids.object.get_next_id().unwrap();
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
    dungeon_server_with(dungeon_data())
}

fn dungeon_server_with(data: GameData) -> Arc<GameServer> {
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
        data,
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

fn form_party(server: &GameServer, a: &Pc, b: &Pc) {
    let teams = &server.systems.social.teams;
    let name = b.p.data().unit.name.clone();
    teams.ask_to_join(server, &a.p, &name, 0, true, Some(&b.p));
    teams.reply_to_join_team(server, &b.p, 0, true, a.p.character_id, false, "", false);
}

#[tokio::test(flavor = "multi_thread", worker_threads = 2)]
async fn simultaneous_party_entries_share_one_run_and_loading_reserves_capacity() {
    let mut data = dungeon_data();
    data.systems
        .instances
        .indun
        .zones
        .get_mut(&DUNGEON_GROUP)
        .unwrap()
        .max_players = 2;
    let server = dungeon_server_with(data);
    let a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    let b = Pc::new(&server, 2, "Beta", 100.0, 100.0);
    let c = Pc::new(&server, 3, "Gamma", 100.0, 100.0);
    form_party(&server, &a, &b);
    let sa = server.clone();
    let pa = a.p.clone();
    let sb = server.clone();
    let pb = b.p.clone();
    let first = tokio::task::spawn_blocking(move || {
        indun::request_dungeon_instance(&sa, &pa, DUNGEON_ZONE, 0)
    });
    let second = tokio::task::spawn_blocking(move || {
        indun::request_dungeon_instance(&sb, &pb, DUNGEON_ZONE, 0)
    });
    assert!(first.await.unwrap() && second.await.unwrap());
    assert_eq!(server.systems.instances.all_dungeons().len(), 1);
    let d = server.systems.instances.all_dungeons()[0].clone();
    assert!(d.is_full(&server));
    form_party(&server, &a, &c);
    assert!(!dungeon::queue_player(&server, &d, &c.p));
    assert_eq!(
        server.systems.instances.indun.entry_count(3, DUNGEON_GROUP),
        0
    );
    await_loader(&d).await;
    assert_eq!(a.instance(), b.instance());
    assert_eq!(worlds::character_count(&d.world(&server).unwrap()), 2);
    dungeon::destroy_dungeon(&server, &d);
    assert_eq!((a.instance(), b.instance()), (0, 0));
}

#[tokio::test]
async fn party_kick_during_loading_cancels_entry_and_solo_run_can_become_party_owned() {
    let server = dungeon_server();
    let a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    let b = Pc::new(&server, 2, "Beta", 100.0, 100.0);
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    assert!(!d.is_team_owned());
    form_party(&server, &a, &b);
    use_enter_func(&server, &b.p, DUNGEON_ZONE);
    assert!(d.is_team_owned());
    let team = server
        .systems
        .social
        .teams
        .team_by_obj_id(a.p.obj_id)
        .unwrap();
    server.systems.social.teams.ask_risky_team(
        &server,
        &a.p,
        team.id,
        b.p.character_id,
        crate::systems::social::team::risky_action::KICK,
    );
    await_loader(&d).await;
    assert_eq!(b.instance(), 0);
    assert!(!d.has_access(b.p.character_id));
    assert!(d.st().enter_requests.is_empty());
    dungeon::leave_dungeon_tick(&server, &d);
    assert!(d.world_id().is_none());
    assert!(server.systems.instances.all_dungeons().is_empty());
}

#[tokio::test]
async fn separate_parties_are_isolated_and_leader_reset_creates_fresh_progress() {
    let server = dungeon_server();
    let a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    let b = Pc::new(&server, 2, "Beta", 100.0, 100.0);
    let c = Pc::new(&server, 3, "Gamma", 100.0, 100.0);
    let e = Pc::new(&server, 4, "Delta", 100.0, 100.0);
    form_party(&server, &a, &b);
    form_party(&server, &c, &e);
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    use_enter_func(&server, &c.p, DUNGEON_ZONE);
    let d1 = server.systems.instances.all_dungeons()[0].clone();
    let d2 = server.systems.instances.all_dungeons()[1].clone();
    assert!(indun::reset_dungeon_instance(&server, &a.p, DUNGEON_ZONE).is_err());
    await_loader(&d1).await;
    await_loader(&d2).await;
    use_enter_func(&server, &b.p, DUNGEON_ZONE);
    assert_eq!(a.instance(), b.instance());
    assert_ne!(a.instance(), c.instance());
    d1.set_room_cleared(5);
    assert!(!d2.is_room_cleared(5));
    assert!(indun::reset_dungeon_instance(&server, &a.p, DUNGEON_ZONE).is_err());
    use_exit_func(&server, &a.p);
    use_exit_func(&server, &b.p);
    assert!(indun::reset_dungeon_instance(&server, &b.p, DUNGEON_ZONE).is_err());
    indun::reset_dungeon_instance(&server, &a.p, DUNGEON_ZONE).unwrap();
    assert!(d1.world_id().is_none());
    assert!(d2.world_id().is_some());
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    let fresh = server
        .systems
        .instances
        .all_dungeons()
        .into_iter()
        .find(|d| d.has_access(1))
        .unwrap();
    assert_ne!(fresh.uid, d1.uid); // world ids can be recycled, run identities cannot
    assert!(!fresh.is_room_cleared(5));
    await_loader(&fresh).await;
    use_enter_func(&server, &b.p, DUNGEON_ZONE);
    assert_eq!(b.instance(), a.instance());
    assert_eq!(
        server.systems.instances.indun.entry_count(1, DUNGEON_GROUP),
        2
    );
    dungeon::destroy_dungeon(&server, &fresh);
    dungeon::destroy_dungeon(&server, &d2);
}

#[tokio::test]
async fn same_run_reentry_does_not_charge_another_entry_and_destroy_cancels_loader() {
    let mut data = dungeon_data();
    data.systems
        .instances
        .indun
        .zones
        .get_mut(&DUNGEON_GROUP)
        .unwrap()
        .enter_count = 1;
    let server = dungeon_server_with(data);
    let a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    await_loader(&d).await;
    use_exit_func(&server, &a.p);
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    assert_eq!(a.instance(), d.world_id().unwrap());
    assert_eq!(
        server.systems.instances.indun.entry_count(1, DUNGEON_GROUP),
        1
    );
    use_exit_func(&server, &a.p);
    indun::reset_dungeon_instance(&server, &a.p, DUNGEON_ZONE).unwrap();
    assert!(!indun::request_dungeon_instance(
        &server,
        &a.p,
        DUNGEON_ZONE,
        0
    ));
    assert!(server.systems.instances.all_dungeons().is_empty());
    let b = Pc::new(&server, 2, "Beta", 100.0, 100.0);
    use_enter_func(&server, &b.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    dungeon::destroy_dungeon(&server, &d);
    tokio::time::sleep(Duration::from_millis(150)).await;
    assert_eq!(b.instance(), 0);
    assert!(server.systems.instances.all_dungeons().is_empty());
    assert!(d.st().enter_requests.is_empty());
}

#[derive(Default)]
struct DungeonItems {
    items: std::sync::Mutex<Vec<(u32, u32)>>,
    fail: std::sync::atomic::AtomicBool,
    charges: std::sync::atomic::AtomicUsize,
}

impl crate::systems::npcs::hooks::EconomyBridge for DungeonItems {
    fn acquire_item(
        &self,
        _server: &GameServer,
        p: &PlayerRef,
        _task: u8,
        item: u32,
        _count: i32,
        _grade: i32,
    ) -> bool {
        if self.fail.load(std::sync::atomic::Ordering::Relaxed) {
            return false;
        }
        self.items.lock().unwrap().push((p.character_id, item));
        true
    }
    fn bag_consume_item(
        &self,
        _server: &GameServer,
        _p: &PlayerRef,
        _task: u8,
        _item: u32,
        count: i32,
    ) -> i32 {
        self.charges
            .fetch_add(1, std::sync::atomic::Ordering::Relaxed);
        count
    }
}

#[tokio::test]
async fn dungeon_admission_consumes_one_ticket_and_failed_admission_leaves_no_run() {
    let mut data = dungeon_data();
    data.systems
        .instances
        .indun
        .zones
        .get_mut(&DUNGEON_GROUP)
        .unwrap()
        .item_id = 123;
    let server = dungeon_server_with(data);
    let a = Pc::new(&server, 1, "Alpha", 100.0, 100.0);
    // Without an item transaction hook, entry must fail closed.
    assert!(!indun::request_dungeon_instance(
        &server,
        &a.p,
        DUNGEON_ZONE,
        0
    ));
    assert!(server.systems.instances.all_dungeons().is_empty());
    assert_eq!(
        server.systems.instances.indun.entry_count(1, DUNGEON_GROUP),
        0
    );
    let items = Arc::new(DungeonItems::default());
    server.systems.npcs.hooks.economy.install(items.clone());
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    await_loader(&d).await;
    use_exit_func(&server, &a.p);
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    assert_eq!(items.charges.load(std::sync::atomic::Ordering::Relaxed), 1);
    dungeon::destroy_dungeon(&server, &d);
}

fn active_mentor_quest(p: &PlayerRef, quest_id: u32) {
    use crate::systems::quests::{model::kind, state::Quest};
    let mut c = p.data();
    let mut quest = Quest::new_blank(quest_id, quest_id);
    quest.step = kind::PROGRESS;
    let h = c.quest_state.alloc(quest);
    c.quest_state.active.add(quest_id, h);
}

fn mentor_chest_fixture(data: &mut GameData) {
    use crate::systems::npcs::doodad::templates::*;
    let def = &super::mentoring::DUNGEONS[1]; // Burnt Castle: two pairs
    let dd = &mut data.systems.npcs.doodad;
    let groups: Vec<_> = def
        .phases
        .iter()
        .map(|&id| DoodadFuncGroups {
            id,
            almighty: def.chest_id,
            group_kind_id: doodad_func_group_kind::NORMAL,
            ..Default::default()
        })
        .collect();
    dd.templates.insert(
        def.chest_id,
        DoodadTemplate {
            id: def.chest_id,
            func_groups: groups,
            ..Default::default()
        },
    );
    for (stage, &phase) in def.phases.iter().enumerate() {
        let (name, func) = if matches!(stage, 2 | 4) {
            (
                "DoodadFuncLootItem",
                FuncTemplate::LootItem(DoodadFuncLootItem {
                    id: phase,
                    item_id: if stage == 2 { 31589 } else { 31590 },
                    count_min: 1,
                    count_max: 1,
                    percent: 10000,
                    ..Default::default()
                }),
            )
        } else {
            (
                "DoodadFuncFakeUse",
                FuncTemplate::FakeUse(DoodadFuncFakeUse {
                    id: phase,
                    fake_skill_id: if stage == 1 { 24656 } else { 24657 },
                    ..Default::default()
                }),
            )
        };
        dd.func_templates
            .entry(name)
            .or_default()
            .insert(phase, func);
        dd.funcs_by_groups.insert(
            phase,
            vec![DoodadFunc {
                group_id: phase,
                func_id: phase,
                func_key: phase,
                func_type: name.into(),
                next_phase: def.phases.get(stage + 1).copied().unwrap_or(20858) as i32,
                ..Default::default()
            }],
        );
    }
}

#[tokio::test]
async fn mentor_chests_require_boss_party_quests_levels_and_seal_order() {
    use crate::systems::npcs::doodad::manager;
    let mut data = dungeon_data();
    mentor_chest_fixture(&mut data);
    let server = dungeon_server_with(data);
    let mentor = Pc::new(&server, 1, "Mentor", 100.0, 100.0);
    let mentee = Pc::new(&server, 2, "Mentee", 100.0, 100.0);
    let late = Pc::new(&server, 3, "Late", 100.0, 100.0);
    mentor.p.data().unit.level = 40;
    mentee.p.data().unit.level = 39;
    late.p.data().unit.level = 39;
    active_mentor_quest(&mentor.p, 6088);
    active_mentor_quest(&mentee.p, 6087);
    active_mentor_quest(&late.p, 6087);
    form_party(&server, &mentor, &mentee);
    use_enter_func(&server, &mentor.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    await_loader(&d).await;
    use_enter_func(&server, &mentee.p, DUNGEON_ZONE);
    let world = d.world(&server).unwrap();
    let items = Arc::new(DungeonItems::default());
    server.systems.npcs.hooks.economy.install(items.clone());
    let mut boss = Npc::new(10518);
    boss.unit.transform.set_xyz(200.0, 200.0, 20.0);
    let boss = Entity::new(0x8000, boss);
    world.spawn(&boss, &server.view_ctx());
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&mentor.p)),
    );
    let mut chests = world.all_doodads();
    chests.sort_by_key(|d| d.obj_id);
    assert_eq!(chests.len(), 2);
    // Duplicate death signals never generate extra rewards.
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&mentor.p)),
    );
    assert_eq!(world.all_doodads().len(), 2);
    let chest = &chests[0];
    let use_chest = |p: &PlayerRef, skill, phase| {
        manager::use_doodad(
            &server,
            &world,
            chest,
            Some(&WorldObject::from(p)),
            skill,
            phase,
        )
    };
    use_chest(&mentee.p, 24656, 0);
    assert_eq!(chest.data().func_group_id, 21051);
    use_chest(&mentor.p, 9999, 0);
    assert_eq!(chest.data().func_group_id, 21051);
    use_chest(&mentor.p, 24657, 20857);
    assert_eq!(chest.data().func_group_id, 21051);
    use_chest(&mentor.p, 24657, 0);
    assert_eq!(chest.data().func_group_id, 20854);
    // A qualified late arrival was absent at the kill, and cannot take the second seal.
    form_party(&server, &mentor, &late);
    use_enter_func(&server, &late.p, DUNGEON_ZONE);
    use_chest(&late.p, 24656, 0);
    assert_eq!(chest.data().func_group_id, 20854);
    mentee.p.data().unit.level = 40;
    use_chest(&mentee.p, 24656, 0);
    assert_eq!(chest.data().func_group_id, 20854);
    mentee.p.data().unit.level = 39;
    items.fail.store(true, std::sync::atomic::Ordering::Relaxed);
    use_chest(&mentee.p, 24656, 0);
    assert_eq!(chest.data().func_group_id, 20855);
    assert!(items.items.lock().unwrap().is_empty());
    items
        .fail
        .store(false, std::sync::atomic::Ordering::Relaxed);
    use_chest(&mentee.p, 0, 0);
    assert_eq!(chest.data().func_group_id, 20856);
    // The mentee quest may already have completed when the mentor collects their seal.
    mentee.p.data().quest_state.active.remove(6087);
    use_chest(&mentor.p, 24657, 0);
    assert_eq!(chest.data().func_group_id, 20858);
    use_chest(&mentor.p, 24657, 0);
    use_chest(&mentee.p, 24656, 0);
    assert_eq!(*items.items.lock().unwrap(), vec![(2, 31589), (1, 31590)]);
    dungeon::destroy_dungeon(&server, &d);
    // Recycled world ids cannot carry a delayed boss death into a fresh run.
    active_mentor_quest(&mentee.p, 6087);
    use_enter_func(&server, &mentor.p, DUNGEON_ZONE);
    let fresh = server.systems.instances.all_dungeons()[0].clone();
    await_loader(&fresh).await;
    use_enter_func(&server, &mentee.p, DUNGEON_ZONE);
    let fresh_world = fresh.world(&server).unwrap();
    assert_eq!(fresh_world.id, world.id);
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&mentor.p)),
    );
    assert!(fresh_world.all_doodads().is_empty());
    let mut replacement = Npc::new(10518);
    replacement.unit.transform.set_xyz(200.0, 200.0, 20.0);
    let replacement = Entity::new(boss.obj_id, replacement);
    fresh_world.spawn(&replacement, &server.view_ctx());
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&mentor.p)),
    );
    assert!(fresh_world.all_doodads().is_empty());
    events::on_do_die(
        &server,
        &WorldObject::from(&replacement),
        Some(&WorldObject::from(&mentor.p)),
    );
    assert_eq!(fresh_world.all_doodads().len(), 2);
    dungeon::destroy_dungeon(&server, &fresh);
}

#[tokio::test]
async fn mentor_boss_kill_without_both_quests_cannot_be_qualified_afterwards() {
    let mut data = dungeon_data();
    mentor_chest_fixture(&mut data);
    let server = dungeon_server_with(data);
    let a = Pc::new(&server, 1, "Mentor", 100.0, 100.0);
    let b = Pc::new(&server, 2, "Mentee", 100.0, 100.0);
    a.p.data().unit.level = 40;
    b.p.data().unit.level = 31;
    active_mentor_quest(&a.p, 6088);
    form_party(&server, &a, &b);
    use_enter_func(&server, &a.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    await_loader(&d).await;
    use_enter_func(&server, &b.p, DUNGEON_ZONE);
    let world = d.world(&server).unwrap();
    let mut boss = Npc::new(10518);
    boss.unit.transform.set_xyz(200.0, 200.0, 20.0);
    let boss = Entity::new(0x8000, boss);
    world.spawn(&boss, &server.view_ctx());
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&a.p)),
    );
    assert!(world.all_doodads().is_empty());
    active_mentor_quest(&b.p, 6087);
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&a.p)),
    );
    assert!(world.all_doodads().is_empty());
    dungeon::destroy_dungeon(&server, &d);
}

#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB"]
async fn real_mentor_chest_seals_complete_daily_quests_and_award_gilda() {
    use crate::systems::{npcs::doodad, quests};
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let app = Path::new(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = crate::data::client_files::ClientFileManager::from_sources(&[], &app);
    let mut data = dungeon_data();
    data.items = crate::data::items::ItemData::load(&conn).unwrap();
    data.systems.npcs.doodad = doodad::data::DoodadData::load_from(&conn).unwrap();
    data.systems.quests = quests::Data::load_from(&conn, &files).unwrap();
    // Verify every version-specific binding against the real tables, including level ranges
    // and all five chest phases, rather than accepting constants without data evidence.
    for def in super::mentoring::DUNGEONS {
        let dd = &data.systems.npcs.doodad;
        assert!(dd.template(def.chest_id).is_some());
        for phase in def.phases {
            assert!(!dd.funcs_for_group(phase).is_empty());
        }
        for (quest, range) in [
            (def.mentor_quest, def.mentor_levels),
            (def.mentee_quest, def.mentee_levels),
        ] {
            let template = data.systems.quests.template(quest).unwrap();
            let components: Vec<_> = template
                .components
                .iter()
                .filter_map(|c| data.systems.quests.component(*c))
                .filter(|c| c.kind_id == quests::model::kind::START)
                .collect();
            assert!(!components.is_empty());
            let reqs = data
                .systems
                .quests
                .unit_reqs
                .requirements("QuestComponent", components[0].id);
            assert!(reqs
                .iter()
                .any(|r| r.kind == quests::unit_reqs::req_kind::LEVEL
                    && r.value1 == u32::from(range.0)));
            assert!(reqs
                .iter()
                .any(|r| r.kind == quests::unit_reqs::req_kind::MAX_LEVEL
                    && r.value1 == u32::from(range.1)));
        }
    }
    let server = dungeon_server_with(data);
    crate::systems::economy::bridges::install(&server);
    quests::bridges::install(&server);
    let mentor = Pc::new(&server, 1, "Mentor", 100.0, 100.0);
    let mentee = Pc::new(&server, 2, "Mentee", 100.0, 100.0);
    mentor.p.data().unit.level = 40;
    mentee.p.data().unit.level = 31;
    form_party(&server, &mentor, &mentee);
    let mut lucius = Npc::new(14422);
    lucius.unit.transform.set_xyz(100.0, 100.0, 100.0);
    let lucius = Entity::new(0x8001, lucius);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&lucius, &server.view_ctx());
    assert_eq!(
        quests::events::add_quest_from_npc(&server, &mentor.p, 6088, lucius.obj_id),
        Some(true)
    );
    assert_eq!(
        quests::events::add_quest_from_npc(&server, &mentee.p, 6087, lucius.obj_id),
        Some(true)
    );
    server.systems.quests.run_queued_evaluations(&server);
    use_enter_func(&server, &mentor.p, DUNGEON_ZONE);
    let d = server.systems.instances.all_dungeons()[0].clone();
    await_loader(&d).await;
    use_enter_func(&server, &mentee.p, DUNGEON_ZONE);
    let world = d.world(&server).unwrap();
    let mut boss = Npc::new(10518);
    boss.unit.transform.set_xyz(200.0, 200.0, 20.0);
    let boss = Entity::new(0x8000, boss);
    world.spawn(&boss, &server.view_ctx());
    events::on_do_die(
        &server,
        &WorldObject::from(&boss),
        Some(&WorldObject::from(&mentor.p)),
    );
    let chest = world
        .all_doodads()
        .into_iter()
        .find(|d| d.data().template_id == 7659)
        .expect("mentor chest");
    for (p, skill) in [(&mentor.p, 24657), (&mentee.p, 24656), (&mentor.p, 24657)] {
        doodad::manager::use_doodad(
            &server,
            &world,
            &chest,
            Some(&WorldObject::from(p)),
            skill,
            0,
        );
        server.systems.quests.run_queued_evaluations(&server);
    }
    let gilda = |p: &PlayerRef| {
        let bag = p.data().inventory.bag().clone();
        let count = bag.lock().unwrap().items_by_template(23633, -1).1;
        count
    };
    assert_eq!(gilda(&mentor.p), 3);
    assert_eq!(gilda(&mentee.p), 1);
    assert!(quests::events::has_quest_completed_in(
        &mentor.p.data(),
        6088
    ));
    assert!(quests::events::has_quest_completed_in(
        &mentee.p.data(),
        6087
    ));
    doodad::manager::use_doodad(
        &server,
        &world,
        &chest,
        Some(&WorldObject::from(&mentor.p)),
        24657,
        0,
    );
    assert_eq!(gilda(&mentor.p), 3);
    quests::daily_reset(&server);
    assert!(!quests::events::has_quest_completed_in(
        &mentor.p.data(),
        6088
    ));
    assert!(!quests::events::has_quest_completed_in(
        &mentee.p.data(),
        6087
    ));
    dungeon::destroy_dungeon(&server, &d);
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
    camp_server_kills(corps_size, victory_score, 0)
}

fn camp_server_kills(
    corps_size: u32,
    victory_score: i32,
    victory_kill_count: u32,
) -> Arc<GameServer> {
    camp_server_with(corps_size, victory_score, victory_kill_count, |_| {})
}

fn camp_server_with(
    corps_size: u32,
    victory_score: i32,
    victory_kill_count: u32,
    tweak: impl FnOnce(&mut GameRuleSet),
) -> Arc<GameServer> {
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
    let mut rule_set = GameRuleSet {
        id: 22,
        corps_size,
        corps1_faction_id: 159,
        corps2_faction_id: 160,
        time_opening: 4,
        time_ending: 0,
        time_playing: 15,
        victory_score,
        battlefield_id: 7,
        victory_kill_count,
        killstreak_skills: [0; 24],
        ..GameRuleSet::default()
    };
    tweak(&mut rule_set);
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
            rule_set: Some(rule_set),
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
    // Per-corps size 1; both applied as "any corps", so only the any-count grows.
    let state = |any_count| {
        body(&SCInstantGameStatePacket {
            battlefield_id: 7,
            corps_size: 1,
            corps1_count: 0,
            corps2_count: 0,
            any_count,
        })
    };
    assert_eq!(
        e.frames(),
        vec![
            (sc::SCAppliedToInstantGamePacket, applied.clone()),
            (sc::SCInstantGameStatePacket, state(1)),
            (sc::SCInstantGameStatePacket, state(2)),
        ]
    );
    assert_eq!(
        f.frames(),
        vec![
            (sc::SCAppliedToInstantGamePacket, applied),
            (sc::SCInstantGameStatePacket, state(2)),
        ]
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
    // Echo is corps 2: the packet names corps 2 and carries both totals.
    let point = |total| {
        body(&SCInstantGameAddPointPacket {
            zone_instance_id: zid,
            corps: corps::CORPS2,
            points: 30,
            score1: 0,
            score2: total,
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
        // Bonus set: loser only (2 deaths, no kills).
        .write_u32(instant_game::bonus_set::LOSER)
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
        // Bonus set: victory + no death + top PC kill.
        .write_u32(
            instant_game::bonus_set::VICTORY
                | instant_game::bonus_set::NO_DEATH
                | instant_game::bonus_set::TOP_PC_KILL,
        )
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
    // Decline: removed from the game, applied again (no Cancel: that would reset the 1.2
    // client to not-applied).
    let mut decline = vec![0];
    decline.extend_from_slice(&1u64.to_le_bytes());
    h.frame(&server, cs::CSJoinInstantGamePacket, &decline)
        .await;
    assert!(!game.has_player(&h.p));
    assert_eq!(mgr.queued(7), Some(vec![7]));
    assert_eq!(
        h.ops(),
        vec![
            sc::SCAppliedToInstantGamePacket,
            sc::SCInstantGameStatePacket
        ]
    );
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

/// Two players matched, joined and entered on battlefield 7; the game has started.
async fn started_1v1(server: &Arc<GameServer>) -> (Pc, Pc, Arc<instant_game::InstantGame>) {
    let mut a = Pc::new(server, 5, "Echo", 100.0, 100.0);
    let mut b = Pc::new(server, 6, "Foxtrot", 120.0, 100.0);
    for pc in [&a, &b] {
        instant_game::apply_to_battlefield(server, 7, corps::ANY, &pc.p);
    }
    instant_game::battlefield_tick(server);
    let game = server.systems.instances.instant_games.games()[0].clone();
    let mut join = vec![1];
    join.extend_from_slice(&1u64.to_le_bytes());
    a.frame(server, cs::CSJoinInstantGamePacket, &join).await;
    b.frame(server, cs::CSJoinInstantGamePacket, &join).await;
    let entered = 1u64.to_le_bytes();
    a.frame(server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    b.frame(server, cs::CSEnteredInstantGameWorldPacket, &entered)
        .await;
    a.frames();
    b.frames();
    (a, b, game)
}

fn end_reason(frames: &[(u16, Vec<u8>)]) -> Option<u8> {
    frames
        .iter()
        .find(|(op, _)| *op == sc::SCInstantGameEndPacket)
        .map(|(_, b)| b[8])
}

#[tokio::test]
async fn battlefield_zero_victory_score_waits_for_timeover() {
    // The real Drill Camp rule set (22) has victory_score = 0: the C# check
    // `Score >= VictoryScore` ended such a game on the first kill.
    let server = camp_server_kills(1, 0, 0);
    let (mut a, mut b, game) = started_1v1(&server).await;
    events::on_do_die(
        &server,
        &WorldObject::from(&b.p),
        Some(&WorldObject::from(&a.p)),
    );
    assert_eq!(end_reason(&a.frames()), None);
    assert_eq!(end_reason(&b.frames()), None);
    assert_eq!(server.systems.instances.instant_games.games().len(), 1);
    // Time over: the killer's corps wins by kill count, reason TimeoverCompareKillCount.
    instant_game::finish_timeover(&server, &game);
    assert_eq!(
        end_reason(&a.frames()),
        Some(instant_game::ENDING_TIMEOVER_COMPARE_KILL_COUNT)
    );
    let killer_corps = game.st().members[0].result;
    let st = game.st();
    let (winner, loser) = if killer_corps == corps::CORPS1 {
        (st.corps1.state, st.corps2.state)
    } else {
        (st.corps2.state, st.corps1.state)
    };
    assert_eq!(
        (winner, loser),
        (
            instant_game::victory_state::WIN,
            instant_game::victory_state::LOSE
        )
    );
}

#[tokio::test]
async fn battlefield_victory_kill_count_ends_game() {
    let server = camp_server_kills(1, 0, 1);
    let (mut a, b, _game) = started_1v1(&server).await;
    events::on_do_die(
        &server,
        &WorldObject::from(&b.p),
        Some(&WorldObject::from(&a.p)),
    );
    assert_eq!(
        end_reason(&a.frames()),
        Some(instant_game::ENDING_ACHIEVEMENT_KILL_COUNT)
    );
}

#[tokio::test]
async fn battlefield_level_min_refuses_apply() {
    // Test characters are level 10; a level-50 gate (the Gladiator rule set's) refuses the
    // apply with the client's level-requirements message and never queues.
    let server = camp_server_with(1, 60, 0, |r| r.level_min = 50);
    let mut p = Pc::new(&server, 7, "Lowbie", 100.0, 100.0);
    p.frames();
    p.frame(&server, cs::CSApplyToInstantGamePacket, &[7, 0, 0, 0, 2])
        .await;
    assert_eq!(
        p.frames(),
        vec![(
            sc::SCAppliedToInstantGamePacket,
            body(&SCAppliedToInstantGamePacket {
                battlefield_id: 7,
                corps: corps::ANY,
                error_message_id: instant_game::ERROR_LEVEL_REQUIREMENTS,
            })
        )]
    );
    assert_eq!(server.systems.instances.instant_games.queued(7), None);
}

#[tokio::test]
async fn battlefield_corps_head_kill_wins() {
    let server = camp_server_with(1, 0, 0, |r| {
        r.victory_kill_corps1_head = 13353;
        r.victory_kill_corps2_head = 13352;
    });
    let (mut a, _b, game) = started_1v1(&server).await;
    let killer_corps = game.st().members[0].result;
    let target = game.rule_set.corps_head_target(killer_corps);
    assert!(target == 13352 || target == 13353);
    // A non-target NPC only counts towards the non-PC-kill bonus.
    let mob = WorldObject::from(&Entity::new(0x301, Npc::new(9999)));
    events::on_do_die(&server, &mob, Some(&WorldObject::from(&a.p)));
    assert_eq!(end_reason(&a.frames()), None);
    assert_eq!(game.st().members[0].npc_kills, 1);
    // The target Polestar ends the game for the killer's corps.
    let head = WorldObject::from(&Entity::new(0x302, Npc::new(target)));
    events::on_do_die(&server, &head, Some(&WorldObject::from(&a.p)));
    assert_eq!(
        end_reason(&a.frames()),
        Some(instant_game::ENDING_ACHIEVEMENT_KILL_CORPS_HEAD)
    );
    let st = game.st();
    let winner = if killer_corps == corps::CORPS1 {
        st.corps1.state
    } else {
        st.corps2.state
    };
    assert_eq!(winner, instant_game::victory_state::WIN);
    assert_eq!(st.members[0].npc_kills, 2);
}

#[tokio::test]
async fn battlefield_end_awards_bonus_honor() {
    let server = camp_server_with(1, 30, 0, |r| {
        r.bonus_winner = 50;
        r.bonus_loser = 10;
        r.bonus_no_death = 7;
        r.bonus_top_enemy_pc_kill = 10;
        r.bonus_top_enemy_non_pc_kill = 3;
    });
    let (a, b, game) = started_1v1(&server).await;
    // One kill reaches the 30-point victory score and pays out at the result.
    events::on_do_die(
        &server,
        &WorldObject::from(&b.p),
        Some(&WorldObject::from(&a.p)),
    );
    let st = game.st();
    assert_eq!(st.members[0].bonus, 50 + 7 + 10);
    assert_eq!(
        st.members[0].bonus_set,
        instant_game::bonus_set::VICTORY
            | instant_game::bonus_set::TOP_PC_KILL
            | instant_game::bonus_set::NO_DEATH
    );
    assert_eq!(st.members[1].bonus, 10);
    assert_eq!(st.members[1].bonus_set, instant_game::bonus_set::LOSER);
    drop(st);
    assert_eq!(a.p.data().honor_point, 67);
    assert_eq!(b.p.data().honor_point, 10);
}
