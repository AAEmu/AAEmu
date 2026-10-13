//! Tests of the spawners (`npc::spawner`, `doodad::spawner`) and `spawn_manager`: spawn-file
//! loading, NPC spawn / death / despawn / respawn timing, doodad respawns, generated and event
//! spawners. NPC / doodad creation goes through a test backend while the managers are stubs.

use std::collections::HashSet;
use std::sync::Arc;
use std::time::Duration;

use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::models::npc::Npc;
use crate::server::GameServer;
use crate::systems::npcs::doodad::spawner::{NpcSpawnerDef, SpawnerBase};
use crate::systems::npcs::npc::spawner_data::{
    npc_spawner_category, NpcSpawnerData, NpcSpawnerNpc, NpcSpawnerTemplate,
};
use crate::systems::npcs::spawn_manager::{
    self, backend::TestBackend, SpawnManager, TemplateExists, FIRST_FAKE_SPAWNER_ID,
};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::{DoodadRef, Entity, NpcRef, WorldInstance, WorldObject};

struct Backend;

impl TestBackend for Backend {
    fn npc_create(&self, server: &GameServer, obj_id: u32, template_id: u32) -> Option<NpcRef> {
        if template_id == 0 || template_id >= 90_000 {
            return None; // "not exist at db"
        }
        let id = if obj_id == 0 {
            server.ids.object.get_next_id().ok()?
        } else {
            obj_id
        };
        let mut n = Npc::new(template_id);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.level = 10;
        Some(Entity::new(id, n))
    }

    fn doodad_create(
        &self,
        server: &GameServer,
        world: &Arc<WorldInstance>,
        obj_id: u32,
        template_id: u32,
    ) -> Option<DoodadRef> {
        let id = if obj_id == 0 {
            server.ids.object.get_next_id().ok()?
        } else {
            obj_id
        };
        let d = Entity::new(id, Doodad::new(template_id));
        d.data().transform.instance_id = world.id;
        Some(d)
    }
}

fn template(id: u32, max_pop: u32) -> NpcSpawnerTemplate {
    NpcSpawnerTemplate {
        id,
        name: format!("t{id}"),
        max_population: max_pop,
        spawn_delay_min: 10.0,
        spawn_delay_max: 10.0,
        test_radius_npc: 3.0,
        ..NpcSpawnerTemplate::default()
    }
}

fn member(id: u32, spawner: u32, npc: u32) -> NpcSpawnerNpc {
    NpcSpawnerNpc {
        id,
        npc_spawner_template_id: spawner,
        member_id: npc,
        member_type: "Npc".into(),
        weight: 1.0,
        unit_id: 0,
    }
}

/// Spawner templates: 1 (Test Warrior, fake template source), 10 (NPC 500), 20 + 21 (NPC 600,
/// two spawners), 30 (NPC 700, 2 members of an unsupported type + one valid).
fn server() -> Arc<GameServer> {
    let mut data = GameData::empty();
    data.items.templates.insert(
        crate::data::items::ITEM_COINS,
        crate::data::items::ItemTemplate::plain(crate::data::items::ITEM_COINS, i32::MAX),
    );
    let mut t30 = template(30, 3);
    t30.npc_spawner_category_id = npc_spawner_category::AUTOCREATED;
    data.systems.npcs.npc_spawners = NpcSpawnerData::from_parts(
        vec![
            template(1, 1),
            template(10, 1),
            template(20, 1),
            template(21, 1),
            t30,
        ],
        vec![
            member(1, 1, 1),
            member(100, 10, 500),
            member(200, 20, 600),
            member(210, 21, 600),
            member(300, 30, 700),
            NpcSpawnerNpc {
                member_type: "Doodad".into(),
                ..member(301, 30, 701)
            },
        ],
    )
    .unwrap();
    let s = Arc::new(test_server_with(data));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

fn manager(server: &Arc<GameServer>) -> Arc<SpawnManager> {
    let world = server.world.world(0).unwrap();
    server.systems.npcs.spawns.get_or_create(&world)
}

fn npc_def(unit_id: u32, x: f32, y: f32) -> NpcSpawnerDef {
    let mut base = SpawnerBase {
        unit_id,
        ..SpawnerBase::default()
    };
    base.position.x = x;
    base.position.y = y;
    base.position.z = 100.0;
    NpcSpawnerDef {
        base,
        count: 1,
        npc_spawner_ids: Vec::new(),
    }
}

fn advance(secs: u64) {
    spawn_manager::advance_test_clock(Duration::from_secs(secs));
}

#[tokio::test]
async fn authored_management_groups_gate_population_and_preserve_other_groups() {
    let server = server();
    let m = manager(&server);
    let mut managed = npc_def(500, 100.0, 100.0);
    managed.base.position.zone_id = crate::test_support::TEST_ZONE;
    managed.base.spawn_group_id = 73;
    m.add_npc_spawner(&server, managed).unwrap();
    let mut other = npc_def(600, 102.0, 100.0);
    other.base.position.zone_id = crate::test_support::TEST_ZONE;
    other.base.spawn_group_id = 74;
    m.add_npc_spawner(&server, other).unwrap();
    let spawner = m
        .get_all_spawners()
        .into_values()
        .flatten()
        .find(|s| s.unit_id == 500)
        .unwrap();
    spawner.do_event_spawn(&server);
    let world = server.world.world(0).unwrap();
    assert!(world.all_npcs().is_empty());
    assert_eq!(
        m.set_management_group(&server, crate::test_support::TEST_ZONE + 1, 73, true),
        0
    );
    assert_eq!(
        m.set_management_group(&server, crate::test_support::TEST_ZONE, 73, true),
        1
    );
    assert_eq!(world.all_npcs().len(), 1);
    let npc = world.all_npcs().pop().unwrap();
    assert_eq!(npc.data().unit.template_id, 500);
    m.set_management_group(&server, crate::test_support::TEST_ZONE, 73, false);
    assert!(world.all_npcs().is_empty());
    spawner.do_event_spawn(&server);
    assert!(world.all_npcs().is_empty());
    assert!(m
        .get_all_spawners()
        .into_values()
        .flatten()
        .filter(|s| s.unit_id == 600)
        .all(|s| !s.shared().management_enabled()));
}

#[tokio::test]
async fn npc_spawn_death_despawn_respawn_timing() {
    let server = server();
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 500.0, 500.0)),
        Ok(1)
    );
    let spawners = m.get_all_spawners();
    let spawner = spawners.values().flatten().next().unwrap().clone();
    assert_eq!((spawner.id, spawner.spawner_id), (1, 10));
    assert_eq!(spawner.npc_spawner_ids, vec![10]);
    assert_eq!(spawner.respawn_time(), 15);

    // No player: nothing happens (and "no player" is cached for 10 s).
    spawn_manager::active_region_tick(&server);
    assert_eq!(server.world.world(0).unwrap().all_npcs().len(), 0);

    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 510.0, 500.0), 5000);
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        0,
        "radius cache still says no player"
    );

    advance(10);
    spawn_manager::active_region_tick(&server);
    let world = server.world.world(0).unwrap();
    let npcs = world.all_npcs();
    assert_eq!(npcs.len(), 1);
    let npc = npcs[0].clone();
    {
        let d = npc.data();
        assert_eq!(d.unit.template_id, 500);
        assert_eq!(d.spawner_id, 1);
        assert_eq!((d.unit.transform.x, d.unit.transform.y), (500.0, 500.0));
    }
    assert!(npc.is_visible());
    assert_eq!(spawner.current_spawn_count(), 1);
    assert_eq!(
        spawner.respawn_time(),
        10,
        "Random(SpawnDelayMin, SpawnDelayMax)"
    );
    assert!(spawner.is_spawn_scheduled());
    assert!(Arc::ptr_eq(
        &server
            .systems
            .npcs
            .spawns
            .npc_spawner_of(npc.obj_id)
            .unwrap(),
        &spawner
    ));

    // Another tick does not spawn more (IsSpawnScheduled / max population).
    spawn_manager::active_region_tick(&server);
    assert_eq!(world.all_npcs().len(), 1);

    // Death: respawn in RespawnTime (10 s), corpse despawn in DespawnTime (20 s).
    {
        let mut d = npc.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &npc);
    assert_eq!(m.pending_respawns().len(), 1);
    assert_eq!(m.pending_despawns().len(), 1);
    assert_eq!(spawner.scheduled_count(), 1);

    advance(11);
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    assert!(
        !spawner.is_spawn_scheduled(),
        "respawn tick re-enables spawning"
    );
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        world.all_npcs().len(),
        2,
        "respawn proceeds while the corpse is still lootable"
    );
    assert_eq!(spawner.current_spawn_count(), 1);
    let replacement = world
        .all_npcs()
        .into_iter()
        .find(|n| !n.data().unit.is_dead())
        .unwrap();
    assert!(!Arc::ptr_eq(&replacement, &npc));
    spawn_manager::active_region_tick(&server);
    assert_eq!(world.all_npcs().len(), 2, "no duplicate living population");

    advance(8);
    m.check_respawns_tick(&server);
    assert_eq!(m.pending_despawns().len(), 1, "despawn not due before 20 s");
    advance(1);
    m.check_respawns_tick(&server);
    assert!(m.pending_despawns().is_empty());
    assert!(world.npc(npc.obj_id).is_none(), "corpse deleted");
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "corpse cleanup keeps the replacement"
    );
    assert!(server
        .systems
        .npcs
        .spawns
        .npc_spawner_of(npc.obj_id)
        .is_none());

    spawn_manager::active_region_tick(&server);
    let npcs = world.all_npcs();
    assert_eq!(npcs.len(), 1, "respawned");
    assert!(Arc::ptr_eq(&npcs[0], &replacement));
    assert_eq!(spawner.current_spawn_count(), 1);
    assert_eq!(
        spawner.scheduled_count(),
        0,
        "DecrementCount after the respawn"
    );
    drop(player);
}

#[tokio::test]
async fn fully_looted_corpse_uses_shortened_deadline_and_respawn_keeps_its_own_timer() {
    use crate::data::items::ITEM_COINS;
    use crate::models::item::Item;
    use crate::systems::economy::loot_packets::{loot_owner_type, LootingRule};
    use crate::systems::economy::looting::LootManager;
    use crate::systems::npcs::hooks::EconomyBridge;

    struct LootBridge;
    impl EconomyBridge for LootBridge {
        fn npc_has_loot_items(&self, server: &GameServer, npc: &NpcRef) -> bool {
            server.systems.economy.loot.item_count(npc.obj_id) > 0
        }
    }

    let server = server();
    super::install_hooks(&server);
    server
        .systems
        .npcs
        .hooks
        .economy
        .install(Arc::new(LootBridge));
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 300.0, 300.0))
        .unwrap();
    let spawner = m
        .get_all_spawners()
        .values()
        .flatten()
        .next()
        .unwrap()
        .clone();
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(
        &server,
        &client,
        test_character(1, "Looter", 300.0, 300.0),
        5000,
    );
    player.data().money = 0;
    spawn_manager::active_region_tick(&server);
    let npc = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    let loot = server.systems.economy.loot.container(npc.obj_id);
    {
        let mut loot = loot.lock().unwrap();
        loot.owner_type = loot_owner_type::NPC;
        loot.creation_time_ms = chrono::Utc::now().timestamp_millis() - 30_000;
        loot.team_looting_rule = Some(LootingRule::default());
        loot.eligible_players.push(player.character_id);
        loot.register_items(
            [3, 7]
                .map(|count| {
                    let mut item = Item::new(0);
                    item.template_id = ITEM_COINS;
                    item.count = count;
                    item
                })
                .to_vec(),
        );
    }
    npc.data().unit.hp = 0;
    spawn_manager::on_npc_died(&server, &npc);
    let extended = server
        .systems
        .npcs
        .spawns
        .despawn_field(npc.obj_id)
        .unwrap();
    assert!(extended > spawn_manager::now() + Duration::from_secs(300));

    assert!(LootManager::try_take_loot(
        &server, &player, npc.obj_id, 1, false
    ));
    assert_eq!(server.systems.economy.loot.item_count(npc.obj_id), 1);
    assert_eq!(
        server.systems.npcs.spawns.despawn_field(npc.obj_id),
        Some(extended)
    );
    assert!(LootManager::try_take_loot(
        &server, &player, npc.obj_id, 2, false
    ));
    assert_eq!(server.systems.economy.loot.item_count(npc.obj_id), 0);
    assert_eq!(player.data().money, 10);
    let shortened = server
        .systems
        .npcs
        .spawns
        .despawn_field(npc.obj_id)
        .unwrap();
    assert!(shortened < spawn_manager::now() + Duration::from_secs(3));

    advance(1);
    m.check_respawns_tick(&server);
    assert!(server.world.world(0).unwrap().npc(npc.obj_id).is_some());
    advance(2);
    m.check_respawns_tick(&server);
    assert!(
        server.world.world(0).unwrap().npc(npc.obj_id).is_none(),
        "looted corpse removed after 2 s, not 5 minutes"
    );
    assert!(m.pending_despawns().is_empty());
    assert_eq!(m.pending_respawns().len(), 1);
    let other_id = server.ids.object.get_next_id().unwrap();
    assert_ne!(
        other_id, npc.obj_id,
        "pending respawn reserves the corpse ID"
    );
    // Keep this ID allocated while the pending respawn finishes.
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        0,
        "respawn still waits for 10 s"
    );
    advance(8);
    m.check_respawns_tick(&server);
    assert_eq!(
        server.ids.object.get_next_id().unwrap(),
        npc.obj_id,
        "ID is released when both timers finish"
    );
    spawn_manager::active_region_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 1);
    server.ids.object.release_id(other_id);
}

#[tokio::test]
async fn npc_despawn_time_with_respawn_later_than_despawn() {
    let server = server();
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 300.0, 300.0))
        .unwrap();
    let spawner = m
        .get_all_spawners()
        .values()
        .flatten()
        .next()
        .unwrap()
        .clone();
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 300.0, 300.0), 5000);
    spawn_manager::active_region_tick(&server);
    let npc = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    spawner.set_respawn_time(40);
    npc.data().unit.hp = 0;
    spawn_manager::on_npc_died(&server, &npc);

    advance(21);
    m.check_respawns_tick(&server);
    assert!(m.pending_despawns().is_empty(), "corpse gone at 20 s");
    assert_eq!(
        m.pending_respawns().len(),
        1,
        "respawn still pending (own time)"
    );
    advance(10);
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        0,
        "IsSpawnScheduled until the respawn time"
    );
    advance(10);
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    spawn_manager::active_region_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn multi_spawner_npc_and_unsupported_member() {
    let server = server();
    let m = manager(&server);
    // NPC 600 has two game-data spawners: two clones sharing position / SpawnedNpcs.
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(600, 100.0, 100.0)),
        Ok(2)
    );
    let clones: Vec<_> = m.get_all_spawners().values().flatten().cloned().collect();
    assert_eq!(clones.len(), 2);
    assert_eq!((clones[0].id, clones[0].spawner_id), (1, 20));
    assert_eq!((clones[1].id, clones[1].spawner_id), (2, 21));
    assert_eq!(clones[0].npc_spawner_ids, vec![20, 21]);
    assert!(Arc::ptr_eq(clones[0].shared(), clones[1].shared()));
    // Neither is "optimal": not Autocreated, no schedule, no time window.
    assert!(!clones[0].is_optimal_spawner(&server));
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    spawn_manager::active_region_tick(&server);
    assert_eq!(server.world.world(0).unwrap().all_npcs().len(), 0);

    // NPC 700: one valid member and one unsupported member type (logged, skipped).
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(700, 900.0, 900.0)),
        Ok(1)
    );
    let s700 = m
        .get_all_spawners()
        .values()
        .flatten()
        .find(|s| s.unit_id == 700)
        .unwrap()
        .clone();
    let _p2 = spawn_player(
        &server,
        &TestClient::new(&server, 2, 2),
        test_character(2, "Q", 900.0, 900.0),
        5001,
    );
    s700.update(&server);
    let spawned = s700.spawned_npcs();
    assert_eq!(spawned.get(&30).map(Vec::len), Some(1));
}

#[tokio::test]
async fn fake_and_event_spawners() {
    let server = server();
    let m = manager(&server);
    // NPC 777 has no game-data spawner: a fake template is generated, the entry is dropped.
    assert_eq!(m.add_npc_spawner(&server, npc_def(777, 1.0, 1.0)), Ok(0));
    let data = &server.game_data.systems.npcs.npc_spawners;
    assert_eq!(data.get_spawner_ids(777), Some(vec![FIRST_FAKE_SPAWNER_ID]));
    let fake = data
        .get_npc_spawner_template(FIRST_FAKE_SPAWNER_ID)
        .unwrap();
    assert_eq!(fake.name, "t1", "clone of template 1");
    assert_eq!(
        fake.npcs,
        vec![NpcSpawnerNpc {
            id: FIRST_FAKE_SPAWNER_ID,
            npc_spawner_template_id: FIRST_FAKE_SPAWNER_ID,
            member_id: 777,
            member_type: "Npc".into(),
            weight: 1.0,
            unit_id: 0,
        }]
    );
    assert_eq!(m.counts()[0], 0);
    // The next entry of the same NPC uses the generated template.
    assert_eq!(m.add_npc_spawner(&server, npc_def(777, 50.0, 1.0)), Ok(1));
    let s = m
        .get_all_spawners()
        .values()
        .flatten()
        .next()
        .unwrap()
        .clone();
    assert_eq!(s.spawner_id, FIRST_FAKE_SPAWNER_ID);
    assert_eq!(s.id, 1);
    assert_eq!(
        server.systems.npcs.spawns.peek_fake_spawner_id(),
        FIRST_FAKE_SPAWNER_ID + 1
    );

    // Event spawner: NpcSpawnerIds given; Id = last id, new template with the unit.
    let mut ev = npc_def(500, 10.0, 10.0);
    ev.npc_spawner_ids = vec![5, 6];
    assert_eq!(m.add_npc_spawner(&server, ev), Ok(0));
    assert_eq!(m.counts()[1], 1);
    let evs = m.get_npc_spawner(6);
    assert_eq!(evs.len(), 1);
    assert!(m.get_npc_spawner(5).is_empty());
    let t = evs[0].template().unwrap();
    assert_eq!((t.id, t.npcs[0].member_id, t.max_population), (6, 500, 1));
    evs[0].do_event_spawn(&server);
    assert_eq!(evs[0].current_spawn_count(), 1);
    evs[0].do_event_spawn(&server);
    assert_eq!(evs[0].current_spawn_count(), 1, "MaxPopulation");

    // [0] is "no ids".
    let mut zero = npc_def(500, 700.0, 700.0);
    zero.npc_spawner_ids = vec![0];
    assert_eq!(m.add_npc_spawner(&server, zero), Ok(1));
    assert!(m.clone_npc_event_spawners(4, 9));
    assert!(m.remove_npc_event_spawners(9));
}

#[tokio::test]
async fn fake_spawner_without_template_one_is_an_error() {
    let mut data = GameData::empty();
    data.systems.npcs.npc_spawners = NpcSpawnerData::from_parts(vec![], vec![]).unwrap();
    let server = Arc::new(test_server_with(data));
    let m = manager(&server);
    assert!(m.add_npc_spawner(&server, npc_def(1, 0.0, 0.0)).is_err());
}

#[tokio::test]
async fn doodad_spawner_spawn_decrease_count_respawn() {
    let server = server();
    let m = manager(&server);
    let world = server.world.world(0).unwrap();
    let dir = crate::data::pak::test_util::temp_path("doodad_spawns");
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(
        dir.join("doodad_spawns.json"),
        r#"[ { "UnitId": 42, "RespawnTime": 5, "Scale": 2.0,
               "Position": { "X": 100, "Y": 200, "Z": 50, "Yaw": 90 } },
             { "UnitId": 42, "Position": { "X": 100.001, "Y": 200, "Z": 50 } },
             { "UnitId": 43, "RespawnTime": 0, "SpecialLink": "justice.x",
               "Position": { "X": 10, "Y": 10, "Z": 1 } } ]"#,
    )
    .unwrap();
    let yes = |_| true;
    let exists = TemplateExists {
        npc: &yes,
        doodad: &yes,
        transfer: &yes,
        gimmick: &yes,
        slave: &yes,
    };
    let stats = m.load_spawn_files(&server, &dir, &exists).unwrap();
    assert_eq!(
        (
            stats.doodad_entries,
            stats.doodad_duplicates,
            stats.doodad_spawners
        ),
        (3, 1, 2)
    );
    assert_eq!(m.spawn_doodads(&server), 2);
    let spawners = m.doodad_spawners();
    let s = spawners.iter().find(|s| s.unit_id == 42).unwrap().clone();
    assert_eq!(s.id, 1);
    assert_eq!(m.get_special_doodad_spawners("justice.").len(), 1);
    assert_eq!(m.get_doodad_spawners_by_unit_id(43).len(), 1);
    let d = s.last().unwrap();
    {
        let x = d.data();
        assert_eq!((x.template_id, x.scale, x.spawner_id), (42, 2.0, 1));
        assert!((x.transform.rot_z - std::f32::consts::FRAC_PI_2).abs() < 1e-6);
        assert!(x.plant_time > 0);
    }
    assert!(world.doodad(d.obj_id).is_some());
    assert_eq!(s.spawned().len(), 1);

    // DecreaseCount: delete now, respawn after RespawnTime.
    s.decrease_count(&server, &d);
    assert!(world.doodad(d.obj_id).is_none());
    assert_eq!(m.pending_respawns().len(), 1);
    advance(4);
    m.check_respawns_tick(&server);
    assert_eq!(m.pending_respawns().len(), 1);
    advance(2);
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    let d2 = s.last().unwrap();
    assert!(!Arc::ptr_eq(&d, &d2));
    assert!(world.doodad(d2.obj_id).is_some());

    // RespawnDoodadTemplateId overrides one respawn.
    s.set_respawn_doodad_template_id(99);
    let d3 = s.spawn(&server, 0).unwrap();
    assert_eq!(d3.data().template_id, 99);
    assert_eq!(s.respawn_doodad_template_id(), 0);
    assert!(
        s.spawn(&server, 5).is_none(),
        "Spawn(objId != 0) returns null"
    );

    // DespawnAllSpawnedDoodads also catches the orphan d2 (Spawn reset _spawned).
    s.despawn_all_spawned_doodads(&server);
    assert!(world.doodad(d2.obj_id).is_none());
    assert!(world.doodad(d3.obj_id).is_none());
    assert!(s.last().is_none());

    // RespawnTime 0: DecreaseCount clears Last and does not schedule.
    let s43 = spawners.iter().find(|s| s.unit_id == 43).unwrap().clone();
    let d43 = s43.last().unwrap();
    s43.decrease_count(&server, &d43);
    assert!(s43.last().is_none());
    assert!(m.pending_respawns().is_empty());
    let _ = std::fs::remove_dir_all(&dir);
}

#[tokio::test]
async fn load_spawn_files_order_filters_and_positions() {
    let server = server();
    let m = manager(&server);
    let dir = crate::data::pak::test_util::temp_path("npc_spawns");
    std::fs::create_dir_all(&dir).unwrap();
    // Reverse file order: npc_spawns_extra.json is read before npc_spawns.json.
    std::fs::write(
        dir.join("npc_spawns.json"),
        r#"[ { "UnitId": 500, "Position": { "X": 101, "Y": 101, "Z": 5 } },  // duplicate
             { "UnitId": 500, "Position": { "X": 110, "Y": 101, "Z": 5 } },
             { "UnitId": 90001, "Position": { "X": 1, "Y": 1 } } ]"#,
    )
    .unwrap();
    std::fs::write(
        dir.join("npc_spawns_extra.json"),
        "[ { \"UnitId\": 500, \"Position\": { \"X\": 100, \"Y\": 100, \"Z\": 5, \"Yaw\": 180 } } ]",
    )
    .unwrap();
    std::fs::write(dir.join("npc_spawns_empty.json"), "  \n").unwrap();
    std::fs::write(
        dir.join("gimmick_spawns.json"),
        r#"[ { "Position": { "X": 5, "Y": 5, "Yaw": 90 }, "EntityGuid": 0x10 },
             { "UnitId": 3, "Position": { "X": 5, "Y": 5 } } ]"#,
    )
    .unwrap();
    std::fs::write(
        dir.join("transfer_spawns.json"),
        r#"[ { "UnitId": 3, "Position": { "X": 1, "Y": 2, "Yaw": 90 } }, { "UnitId": 4, "Position": {} } ]"#,
    )
    .unwrap();
    std::fs::write(
        dir.join("slave_spawns.json"),
        r#"[ { "UnitId": 73, "Position": {} } ]"#,
    )
    .unwrap();
    let npc = |id: u32| id < 90_000;
    let no_three = |id: u32| id != 3;
    let yes = |_| true;
    let exists = TemplateExists {
        npc: &npc,
        doodad: &yes,
        transfer: &no_three,
        gimmick: &no_three,
        slave: &yes,
    };
    let stats = m.load_spawn_files(&server, &dir, &exists).unwrap();
    assert_eq!(stats.npc_files, 2);
    assert_eq!(stats.npc_entries, 4);
    assert_eq!(stats.npc_duplicates, 1);
    assert_eq!(stats.npc_missing_templates, 1);
    assert_eq!(stats.npc_spawners, 2);
    let spawners: Vec<_> = m.get_all_spawners().into_values().flatten().collect();
    // The extra file came first (yaw 180 -> PI), then the non-duplicate of npc_spawns.json.
    let p0 = spawners[0].position();
    assert_eq!(
        (p0.x, p0.world_id, p0.zone_id),
        (100.0, 0, crate::test_support::TEST_ZONE)
    );
    assert!((p0.yaw - std::f32::consts::PI).abs() < 1e-6);
    assert_eq!(spawners[1].position().x, 110.0);
    assert_eq!((stats.gimmick_entries, stats.gimmick_spawners), (2, 1));
    let g = m.gimmick_spawners();
    assert_eq!(
        g[0].position.unwrap().yaw,
        90.0,
        "gimmick yaw is not converted"
    );
    assert_eq!(g[0].entity_guid, 16);
    assert_eq!((stats.transfer_entries, stats.transfer_spawners), (2, 1));
    assert_eq!((stats.slave_entries, stats.slave_spawners), (1, 1));
    // Ids: npc clones 1, 2; transfer 3; gimmick 4; slave 5 (load order npc, doodad, transfer,
    // gimmick, slave).
    assert_eq!(g[0].id, 4);
    assert_eq!(m.get_next_spawner_id(), 6);

    // A parse failure aborts the load.
    std::fs::write(dir.join("slave_spawns.json"), "[ { \"UnitId\": 1.5 } ]").unwrap();
    assert!(m.load_spawn_files(&server, &dir, &exists).is_err());
    let _ = std::fs::remove_dir_all(&dir);
}

#[tokio::test]
async fn despawn_all_and_delete_all_spawners() {
    let server = server();
    let m = manager(&server);
    let world = server.world.world(0).unwrap();
    m.add_npc_spawner(&server, npc_def(500, 50.0, 50.0))
        .unwrap();
    let s = m
        .get_all_spawners()
        .values()
        .flatten()
        .next()
        .unwrap()
        .clone();
    s.do_spawn(&server);
    assert_eq!(world.all_npcs().len(), 1);
    // A spawner-less NPC is deleted directly.
    let lone = Entity::new(server.ids.object.get_next_id().unwrap(), Npc::new(5));
    world.spawn(&lone, &server.view_ctx());
    assert_eq!(m.despawn_all(&server), 2);
    assert!(world.npc(lone.obj_id).is_none());
    assert_eq!(s.respawn_time(), 9_999_999);
    assert_eq!(m.pending_despawns().len(), 1);
    advance(21);
    m.check_respawns_tick(&server);
    assert!(world.all_npcs().is_empty());

    s.set_spawn_scheduled(false);
    s.do_spawn(&server);
    assert_eq!(world.all_npcs().len(), 1);
    m.delete_all_spawners(&server);
    assert!(world.all_npcs().is_empty());
    assert_eq!(m.counts()[0], 0);
    assert_eq!(s.parent_world(), None);
}

#[tokio::test]
async fn spawn_effect_spawner_at_unit() {
    let server = server();
    let m = manager(&server);
    let world = server.world.world(0).unwrap();
    let caster = Entity::new(server.ids.object.get_next_id().unwrap(), Npc::new(1));
    {
        let mut d = caster.data();
        d.unit
            .transform
            .set_position(30.0, 40.0, 5.0, 0.0, 0.0, 1.0);
        d.unit.faction_id = 77;
    }
    world.spawn(&caster, &server.view_ctx());
    let caster_obj = WorldObject::from(&caster);
    // NPC 600 has spawners [20, 21]: spawner id 20, template 20 rewritten to the first member.
    let s = m.get_npc_spawner_at(&server, 600, &caster_obj).unwrap();
    assert_eq!((s.id, s.unit_id), (20, 600));
    let p = s.position();
    assert_eq!((p.x, p.y, p.z, p.yaw), (30.0, 40.0, 5.0, 1.0));
    s.set_respawn_time(0);
    let effect = crate::systems::npcs::npc::spawner::SpawnEffectParams {
        use_summoner_faction: true,
        use_summoner_aggro_target: false,
        life_time: 0.0,
    };
    s.do_spawn_effect(&server, s.id, &effect, Some(&caster_obj), None);
    let spawned = s.spawned_npcs();
    let npc = spawned.values().next().unwrap()[0].clone();
    assert_eq!(npc.data().unit.faction_id, 77);
    assert_eq!(s.respawn_time(), 0);
    // No game-data spawner: a new id from the object id manager.
    let s2 = m.get_npc_spawner_at(&server, 4321, &caster_obj).unwrap();
    assert_eq!(s2.npc_spawner_ids, vec![s2.id]);
    assert_eq!(s2.template().unwrap().npcs[0].member_id, 4321);
}

#[tokio::test]
async fn spawn_lock_is_reentrant() {
    let server = server();
    let a = server.systems.npcs.spawns.spawn_lock();
    let b = server.systems.npcs.spawns.spawn_lock();
    drop(a);
    drop(b);
    let s2 = server.clone();
    std::thread::spawn(move || drop(s2.systems.npcs.spawns.spawn_lock()))
        .join()
        .unwrap();
}

// ---------------------------------------------------------------------------------------------
// real data
// ---------------------------------------------------------------------------------------------

fn id_set(conn: &rusqlite::Connection, table: &str) -> HashSet<u32> {
    let mut stmt = conn.prepare(&format!("SELECT id FROM {table}")).unwrap();
    stmt.query_map([], |r| r.get::<_, i64>(0))
        .unwrap()
        .map(|v| v.unwrap() as u32)
        .collect()
}

/// `SpawnManager.Load` spawn files of the real `main_world` with the real compact DB.
#[tokio::test]
#[ignore]
async fn real_data_main_world_spawn_files() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let world_path =
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("app/Data/Worlds/main_world");
    if !world_path.exists() {
        return;
    }
    let conn = rusqlite::Connection::open(&db).unwrap();
    let mut data = GameData::empty();
    data.systems.npcs.npc_spawners = NpcSpawnerData::load_from(&conn).unwrap();
    let npcs = id_set(&conn, "npcs");
    let doodads = id_set(&conn, "doodad_almighties");
    let sd = &data.systems.npcs.npc_spawners;
    let (transfers, gimmicks, slaves) = (
        sd.transfer_template_ids.clone(),
        id_set(&conn, "gimmicks"),
        sd.slave_template_ids.clone(),
    );
    let server = Arc::new(test_server_with(data));
    let m = manager(&server);
    let npc = |id: u32| npcs.contains(&id);
    let doodad = |id: u32| doodads.contains(&id);
    let transfer = |id: u32| transfers.contains(&id);
    let gimmick = |id: u32| gimmicks.contains(&id);
    let slave = |id: u32| slaves.contains(&id);
    let exists = TemplateExists {
        npc: &npc,
        doodad: &doodad,
        transfer: &transfer,
        gimmick: &gimmick,
        slave: &slave,
    };
    let stats = m.load_spawn_files(&server, &world_path, &exists).unwrap();
    println!("{stats:#?}");
    let c = m.counts();
    println!("counts (npc clones, event, doodad, transfer, gimmick, slave) = {c:?}");

    // Independent re-count of the raw JSON (straightforward quadratic filters on a grid).
    let raw = |name: &str| -> Vec<serde_json::Value> {
        let text = std::fs::read_to_string(world_path.join(name)).unwrap();
        crate::systems::npcs::json::parse(&text)
            .unwrap()
            .as_array()
            .unwrap()
            .clone()
    };
    let num = |v: &serde_json::Value, k: &str| {
        v.get(k).and_then(serde_json::Value::as_f64).unwrap_or(0.0)
    };
    let npc_raw = raw("npc_spawns.json");
    let mut kept: std::collections::HashMap<(u32, i64, i64), Vec<(f32, f32)>> =
        std::collections::HashMap::new();
    let mut fake_seen: HashSet<u32> = HashSet::new();
    let (mut dup, mut missing, mut entries_with_spawners, mut fake, mut event) = (0, 0, 0, 0, 0);
    let mut clones = 0usize;
    let cell2 = |v: f32| (v / 2.0).floor() as i64;
    for e in &npc_raw {
        let unit = num(e, "UnitId") as u32;
        let pos = e.get("Position").unwrap();
        let (x, y) = (num(pos, "X") as f32, num(pos, "Y") as f32);
        let mut is_dup = false;
        for dx in -1..=1 {
            for dy in -1..=1 {
                if let Some(v) = kept.get(&(unit, cell2(x) + dx, cell2(y) + dy)) {
                    if v.iter()
                        .any(|&(kx, ky)| (kx - x).abs() < 2.0 && (ky - y).abs() < 2.0)
                    {
                        is_dup = true;
                    }
                }
            }
        }
        if is_dup {
            dup += 1;
            continue;
        }
        if !npcs.contains(&unit) {
            missing += 1;
            continue;
        }
        let ids: Vec<u32> = e
            .get("NpcSpawnerIds")
            .and_then(|v| v.as_array())
            .map(|a| a.iter().map(|v| v.as_f64().unwrap() as u32).collect())
            .unwrap_or_default();
        if !ids.is_empty() && ids != [0] {
            event += 1;
            continue;
        }
        let static_ids = {
            let mut stmt = conn
                .prepare_cached("SELECT COUNT(*) FROM npc_spawner_npcs WHERE member_id = ?1")
                .unwrap();
            stmt.query_row([unit], |r| r.get::<_, i64>(0)).unwrap() as usize
        };
        let n = if static_ids > 0 {
            static_ids
        } else if fake_seen.insert(unit) {
            fake += 1;
            0
        } else {
            1
        };
        if n > 0 {
            entries_with_spawners += 1;
            clones += n;
            kept.entry((unit, cell2(x), cell2(y)))
                .or_default()
                .push((x, y));
        }
    }
    assert_eq!(stats.npc_entries, npc_raw.len());
    assert_eq!(stats.npc_duplicates, dup);
    assert_eq!(stats.npc_missing_templates, missing);
    assert_eq!(stats.npc_event_spawners, event);
    assert_eq!(stats.npc_fake_spawners, fake);
    assert_eq!(stats.npc_entries_with_spawners, entries_with_spawners);
    assert_eq!(stats.npc_spawners, clones);
    assert_eq!(c[0], clones);

    let doodad_raw = raw("doodad_spawns.json");
    type Grid = std::collections::HashMap<(u32, i64, i64), Vec<(f32, f32, f32)>>;
    let mut grid = Grid::new();
    let (mut ddup, mut dmissing, mut dkept) = (0, 0, 0);
    for e in &doodad_raw {
        let unit = num(e, "UnitId") as u32;
        let pos = e.get("Position").unwrap();
        let (x, y, z) = (
            num(pos, "X") as f32,
            num(pos, "Y") as f32,
            num(pos, "Z") as f32,
        );
        let cell = |v: f32| (v * 10.0).floor() as i64;
        let mut is_dup = false;
        for dx in -1..=1 {
            for dy in -1..=1 {
                if let Some(v) = grid.get(&(unit, cell(x) + dx, cell(y) + dy)) {
                    if v.iter().any(|&(a, b, c)| {
                        (a - x).abs() < 0.01 && (b - y).abs() < 0.01 && (c - z).abs() < 0.01
                    }) {
                        is_dup = true;
                    }
                }
            }
        }
        if is_dup {
            ddup += 1;
            continue;
        }
        if !doodads.contains(&unit) {
            dmissing += 1;
            continue;
        }
        dkept += 1;
        grid.entry((unit, cell(x), cell(y)))
            .or_default()
            .push((x, y, z));
    }
    assert_eq!(stats.doodad_entries, doodad_raw.len());
    assert_eq!(stats.doodad_duplicates, ddup);
    assert_eq!(stats.doodad_missing_templates, dmissing);
    assert_eq!(stats.doodad_spawners, dkept);
    assert_eq!(c[2], dkept);

    assert_eq!(stats.gimmick_entries, raw("gimmick_spawns.json").len());
    assert_eq!(stats.transfer_entries, raw("transfer_spawns.json").len());
    assert_eq!(stats.slave_entries, raw("slave_spawns.json").len());
    assert!(stats.npc_spawners > 10_000);
    assert!(stats.doodad_spawners > 10_000);
    assert!(stats.gimmick_spawners > 0 && stats.transfer_spawners > 0 && stats.slave_spawners > 0);
    assert_eq!(
        c[3..],
        [
            stats.transfer_spawners,
            stats.gimmick_spawners,
            stats.slave_spawners
        ]
    );
}
