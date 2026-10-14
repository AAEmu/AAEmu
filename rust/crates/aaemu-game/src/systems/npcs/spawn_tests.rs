//! Tests of the spawners (`npc::spawner`, `doodad::spawner`) and `spawn_manager`: spawn-file
//! loading, NPC spawn / death / despawn / respawn timing, doodad respawns, generated and event
//! spawners. NPC / doodad creation goes through a test backend while the managers are stubs.

use std::collections::HashSet;
use std::sync::Arc;
use std::time::Duration;

use sqlx::mysql::MySqlPoolOptions;

use crate::config::{AppConfig, WorldConfig};
use crate::data::skills::{BuffKind, BuffTemplate};
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::data::GameData;
use crate::models::doodad::Doodad;
use crate::models::npc::Npc;
use crate::opcodes::sc;
use crate::server::GameServer;
use crate::systems::ai::framework::AiKind;
use crate::systems::npcs::doodad::spawner::{NpcSpawnerDef, SpawnerBase};
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::systems::npcs::npc::spawner::NpcSpawner;
use crate::systems::npcs::npc::spawner_data::{
    npc_spawner_category, NpcSpawnerData, NpcSpawnerNpc, NpcSpawnerTemplate,
};
use crate::systems::npcs::spawn_manager::{
    self, backend::TestBackend, SpawnManager, TemplateExists, FIRST_FAKE_SPAWNER_ID,
};
use crate::systems::quests::model::kind;
use crate::systems::quests::templates::{ActKind, QuestComponentTemplate, QuestTemplate};
use crate::systems::skills::state::{Aggro, CancelToken, SkillRun, SkillTaskKind, SkillTaskSlot};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient};
use crate::world::unit_state::UNIT_STATE_FORCED_BUFFS;
use crate::world::{DoodadRef, Entity, NpcRef, Player, PlayerRef, WorldInstance, WorldObject};

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

/// [`test_server_with`] with a synthetic `main_world` of `cell_x` x `cell_y` cells: authored
/// positions far outside the default 2x2-cell grid (the Mistfiend cave points at x ~ 22900,
/// y ~ 9400) need a world that covers them. Lazy DB / no network, forced buffs included.
fn server_with_world(data: GameData, cell_x: i32, cell_y: i32) -> Arc<GameServer> {
    let mut config = AppConfig {
        id: 1,
        ..AppConfig::default()
    };
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1; // nothing listens here
    let db = MySqlPoolOptions::new()
        .acquire_timeout(Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    let mut data = data;
    data.worlds = WorldTemplates::from_templates(
        vec![WorldTemplate::synthetic(
            0,
            "main_world",
            cell_x,
            cell_y,
            crate::test_support::TEST_ZONE,
        )],
        Vec::new(),
    )
    .expect("synthetic world");
    for id in UNIT_STATE_FORCED_BUFFS {
        let mut t = BuffTemplate::simple(id, BuffKind::Good);
        t.duration = 0;
        data.skills.buffs.insert(id, t);
    }
    Arc::new(GameServer::new(config, db, data))
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

// ---------------------------------------------------------------------------------------------
// activation (bounded region prewarming)
// ---------------------------------------------------------------------------------------------

/// [`test_server_with`] whose spawner data is replaced by one authored template + member (NPC
/// 500) for activation settings the shared [`server`] templates do not cover.
fn activation_server(template: NpcSpawnerTemplate) -> Arc<GameServer> {
    let mut game = GameData::empty();
    game.systems.npcs.npc_spawners =
        NpcSpawnerData::from_parts(vec![template], vec![member(100, 10, 500)]).unwrap();
    let s = Arc::new(test_server_with(game));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

/// [`activation_server`] with static `NpcTemplate`s inserted (the resident classification and
/// the AI leash read them).
fn activation_server_with_npcs(
    template: NpcSpawnerTemplate,
    npcs: Vec<NpcTemplate>,
) -> Arc<GameServer> {
    let mut game = GameData::empty();
    for npc in npcs {
        game.systems.npcs.npc.templates.insert(npc.id, npc);
    }
    game.systems.npcs.npc_spawners =
        NpcSpawnerData::from_parts(vec![template], vec![member(100, 10, 500)]).unwrap();
    let s = Arc::new(test_server_with(game));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

/// [`activation_server_with_npcs`] with exactly one `NpcTemplate`.
fn activation_server_with_npc(template: NpcSpawnerTemplate, npc: NpcTemplate) -> Arc<GameServer> {
    activation_server_with_npcs(template, vec![npc])
}

/// One activation pass with its structured stats.
fn activate(server: &Arc<GameServer>) -> spawn_manager::ActivationStats {
    spawn_manager::active_region_tick_stats(server)
}

/// The single loaded ordinary spawner of the manager (test helper).
fn only_spawner(manager: &Arc<SpawnManager>) -> Arc<NpcSpawner> {
    manager
        .get_all_spawners()
        .into_values()
        .flatten()
        .next()
        .expect("one ordinary spawner")
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

    // No player: nothing happens.
    spawn_manager::active_region_tick(&server);
    assert_eq!(server.world.world(0).unwrap().all_npcs().len(), 0);

    // The player arrives: the very next pass sees it. Player presence invalidates the cached
    // "no player" answer immediately (no 10 s cache wait).
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 510.0, 500.0), 5000);
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
    // NPC 777 has no game-data spawner: a fake template is generated and the entry is kept.
    assert_eq!(m.add_npc_spawner(&server, npc_def(777, 1.0, 1.0)), Ok(1));
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
    assert_eq!(m.counts()[0], 1);
    let s = m
        .get_all_spawners()
        .values()
        .flatten()
        .next()
        .unwrap()
        .clone();
    assert_eq!(s.spawner_id, FIRST_FAKE_SPAWNER_ID);
    assert_eq!(s.id, 1);
    assert_eq!(s.npc_spawner_ids, vec![FIRST_FAKE_SPAWNER_ID]);
    assert_eq!(s.spawnable_npcs()[0].member_id, 777);
    // The next entry of the same NPC uses the generated template.
    assert_eq!(m.add_npc_spawner(&server, npc_def(777, 50.0, 1.0)), Ok(1));
    assert_eq!(m.counts()[0], 2);
    let s2 = m
        .get_all_spawners()
        .values()
        .flatten()
        .find(|s| s.id == 2)
        .unwrap()
        .clone();
    assert_eq!(s2.spawner_id, FIRST_FAKE_SPAWNER_ID);
    assert_eq!(s2.spawnable_npcs()[0].member_id, 777);
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
async fn fake_spawner_first_and_repeated_placements_spawn_and_respawn() {
    let server = server();
    let m = manager(&server);
    // First placement: no game-data spawner, so the generated template gets a real spawner.
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(777, 100.0, 100.0)),
        Ok(1)
    );
    // A repeated placement of the same NPC clones the generated template.
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(777, 900.0, 900.0)),
        Ok(1)
    );
    let spawners: Vec<_> = m.get_all_spawners().into_values().flatten().collect();
    assert_eq!(spawners.len(), 2);
    let (first, repeated) = (spawners[0].clone(), spawners[1].clone());
    assert_eq!((first.id, repeated.id), (1, 2));
    assert_eq!(first.spawner_id, FIRST_FAKE_SPAWNER_ID);
    assert_eq!(repeated.spawner_id, FIRST_FAKE_SPAWNER_ID);
    assert!(!Arc::ptr_eq(first.shared(), repeated.shared()));
    for s in [&first, &repeated] {
        assert_eq!(s.npc_spawner_ids, vec![FIRST_FAKE_SPAWNER_ID]);
        assert_eq!(s.template().unwrap().npcs[0].member_id, 777);
        assert_eq!(s.spawnable_npcs()[0].member_id, 777);
    }

    let _p1 = spawn_player(
        &server,
        &TestClient::new(&server, 1, 1),
        test_character(1, "P", 100.0, 100.0),
        5000,
    );
    let _p2 = spawn_player(
        &server,
        &TestClient::new(&server, 2, 2),
        test_character(2, "Q", 900.0, 900.0),
        5001,
    );
    spawn_manager::active_region_tick(&server);
    let world = server.world.world(0).unwrap();
    assert_eq!(world.all_npcs().len(), 2, "both placements spawn");
    let first_npc = first.spawned_npcs().values().next().unwrap()[0].clone();
    let repeated_npc = repeated.spawned_npcs().values().next().unwrap()[0].clone();
    {
        let d = first_npc.data();
        assert_eq!((d.unit.transform.x, d.unit.transform.y), (100.0, 100.0));
        assert_eq!(d.spawner_id, first.id);
    }
    {
        let d = repeated_npc.data();
        assert_eq!((d.unit.transform.x, d.unit.transform.y), (900.0, 900.0));
        assert_eq!(d.spawner_id, repeated.id);
    }
    assert!(Arc::ptr_eq(
        &server
            .systems
            .npcs
            .spawns
            .npc_spawner_of(first_npc.obj_id)
            .unwrap(),
        &first
    ));
    assert!(Arc::ptr_eq(
        &server
            .systems
            .npcs
            .spawns
            .npc_spawner_of(repeated_npc.obj_id)
            .unwrap(),
        &repeated
    ));

    // The first placement dies and respawns on its own timer.
    first_npc.data().unit.hp = 0;
    spawn_manager::on_npc_died(&server, &first_npc);
    assert_eq!(m.pending_respawns().len(), 1);
    advance(11);
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    spawn_manager::active_region_tick(&server);
    assert_eq!(first.current_spawn_count(), 1, "first placement respawned");
    assert_eq!(world.all_npcs().len(), 3);

    // The repeated placement keeps its spawner link and respawns too.
    repeated_npc.data().unit.hp = 0;
    spawn_manager::on_npc_died(&server, &repeated_npc);
    assert_eq!(m.pending_respawns().len(), 1);
    advance(11);
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        repeated.current_spawn_count(),
        1,
        "repeated placement respawned"
    );
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
async fn npc_spawn_duplicate_filter_includes_z() {
    let server = server();
    let m = manager(&server);
    let dir = crate::data::pak::test_util::temp_path("npc_spawns_z");
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(
        dir.join("npc_spawns.json"),
        r#"[ { "UnitId": 500, "Position": { "X": 100, "Y": 100, "Z": 50 } },
             { "UnitId": 500, "Position": { "X": 100.5, "Y": 100.5, "Z": 50.5 } },
             { "UnitId": 500, "Position": { "X": 100.5, "Y": 100.5, "Z": 90 } },
             { "UnitId": 500, "Position": { "X": 300, "Y": 300, "Z": 50 } } ]"#,
    )
    .unwrap();
    let npc = |id: u32| id < 90_000;
    let yes = |_| true;
    let exists = TemplateExists {
        npc: &npc,
        doodad: &yes,
        transfer: &yes,
        gimmick: &yes,
        slave: &yes,
    };
    let stats = m.load_spawn_files(&server, &dir, &exists).unwrap();
    assert_eq!(
        (
            stats.npc_entries,
            stats.npc_duplicates,
            stats.npc_entries_with_spawners,
            stats.npc_spawners
        ),
        (4, 1, 3, 3)
    );
    let mut positions: Vec<(f32, f32, f32)> = m
        .get_all_spawners()
        .into_values()
        .flatten()
        .filter(|s| s.unit_id == 500)
        .map(|s| {
            let p = s.position();
            (p.x, p.y, p.z)
        })
        .collect();
    positions.sort_by(|a, b| a.partial_cmp(b).unwrap());
    assert_eq!(
        positions,
        vec![
            (100.0, 100.0, 50.0),
            (100.5, 100.5, 90.0),
            (300.0, 300.0, 50.0),
        ],
        "same XY on another floor survives; the same-floor neighbour is a duplicate"
    );
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

    let forced = s.force_spawn(&server, 0);
    assert!(
        forced.is_some(),
        "ForceSpawn repopulates after the teardown"
    );
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

#[tokio::test]
async fn activation_sees_player_arrival_on_the_first_pass() {
    let server = server();
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 500.0, 500.0))
        .unwrap();
    let spawner = only_spawner(&m);

    // A pass without players caches the "no player" answer.
    let stats = activate(&server);
    assert_eq!(stats.created, 0);
    assert_eq!(spawner.current_spawn_count(), 0);

    // The player arrives at the authored position; the very next pass activates it (player
    // presence invalidates the cached answer immediately, no 10 s wait).
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 510.0, 500.0), 5000);
    let stats = activate(&server);
    assert_eq!(stats.created, 1, "arrival is visible to the same pass");
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn radius_cache_follows_same_region_movement_immediately() {
    let mut t = template(10, 1);
    t.test_radius_npc = 1.0; // 50 m
    let server = activation_server(t);
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 110.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    // Inside the authored radius: `Update` spawns.
    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 1);

    // Within the same 64 m region but outside the authored radius (50.9 m): the movement
    // itself invalidates the cached proximity, so the despawn happens immediately.
    player.data().unit.transform.set_xyz(64.0, 64.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    assert_eq!(player.region(), Some((0, 1, 1)), "still the same region");
    spawner.update(&server);
    m.check_respawns_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        0,
        "no stale in-radius result"
    );
    assert!(world.all_npcs().is_empty());

    // Moving back within the region re-spawns just as immediately.
    player.data().unit.transform.set_xyz(110.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    spawner.update(&server);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_leave_retains_warm_mobs_until_the_retention_window_ends() {
    let server = server(); // World.NpcWarmRetentionSeconds defaults to 60
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
    let original = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();

    // Leaving the prewarm ring starts the stable retention countdown; the mob is not despawned.
    player.data().unit.transform.set_xyz(1200.0, 1200.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    activate(&server);
    m.check_respawns_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "warm mob retained after the player left"
    );

    // Re-entering before the expiry keeps the very same instance (no despawn, no refill).
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 0);
    assert_eq!(stats.reclaimed, 0);
    assert_eq!(spawner.current_spawn_count(), 1);
    let returned = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();
    assert!(Arc::ptr_eq(&original, &returned), "same identity and state");

    // Leave again; once the retention interval passed, the warm mob is reclaimed (no respawn
    // delay is synthesized) and a later return refills normally.
    player.data().unit.transform.set_xyz(1200.0, 1200.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    activate(&server);
    advance(60);
    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 1);
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
    assert!(world.all_npcs().is_empty());
    assert!(
        m.pending_respawns().is_empty(),
        "a healthy distance reclaim must not synthesize a respawn"
    );

    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 1);

    assert_eq!(spawner.current_spawn_count(), 1);
}

/// A nonaggressive service-role template (merchant): resident under the default configuration.
fn merchant_template(id: u32) -> NpcTemplate {
    NpcTemplate {
        id,
        merchant: true,
        ..NpcTemplate::default()
    }
}

/// Production movement of `player` far outside every warm ring (visibility updates dispatched).
fn leave_world(server: &Arc<GameServer>, player: &PlayerRef) {
    player.data().unit.transform.set_xyz(1200.0, 1200.0, 100.0);
    server
        .world
        .world(0)
        .unwrap()
        .add_visible_object(player, &server.view_ctx());
}

#[tokio::test]
async fn activation_retention_zero_reclaims_on_departure() {
    let server = server();
    server
        .config
        .update(|c| c.world.npc_warm_retention_seconds = 0);
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    assert_eq!(activate(&server).created, 1);
    leave_world(&server, &player);
    let stats = activate(&server);
    assert_eq!(
        stats.reclaimed, 1,
        "retention 0 reclaims on the leaving pass"
    );
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
    assert!(m.pending_respawns().is_empty());
}

#[tokio::test]
async fn activation_resident_service_npcs_false_honors_config() {
    let server = activation_server_with_npc(template(10, 1), merchant_template(500));
    server
        .config
        .update(|c| c.world.npc_resident_service_npcs = false);
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    assert_eq!(activate(&server).created, 1);
    leave_world(&server, &player);
    activate(&server); // books the stable countdown (default retention 60)
    advance(60);
    let stats = activate(&server);
    assert_eq!(
        stats.reclaimed, 1,
        "resident disabled: ordinary retention applies to the vendor"
    );
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
}

#[tokio::test]
async fn activation_resident_service_npc_stays_resident_with_dormant_ai() {
    let server = activation_server_with_npc(template(10, 1), merchant_template(500));
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    assert_eq!(activate(&server).created, 1);
    let vendor = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();
    let vendor_id = vendor.obj_id;
    client.drain();

    // The vendor AI exists and ticks while the player is in its region; age out the
    // initialization grace first so only the region selection can keep it a candidate.
    server.systems.ai.ais.add_ai(&vendor, AiKind::Dummy);
    for _ in 0..21 {
        crate::systems::ai::manager::tick_all(&server, 100);
    }
    let visits = server.systems.ai.ais.visits(vendor_id).unwrap();
    assert!(visits > 0, "the vendor is ticked while a player is nearby");

    // Leaving does not reclaim the vendor, even far beyond the retention interval; its AI falls
    // out of the region candidate selection (no blanket always-active).
    leave_world(&server, &player);
    for _ in 0..5 {
        let stats = activate(&server);
        assert_eq!(stats.reclaimed, 0);
    }
    advance(600);
    for _ in 0..5 {
        let stats = activate(&server);
        assert_eq!(stats.reclaimed, 0);
        crate::systems::ai::manager::tick_all(&server, 100);
        m.check_respawns_tick(&server);
    }
    assert_eq!(
        server.systems.ai.ais.visits(vendor_id),
        Some(visits),
        "a far dormant resident AI is not a tick candidate"
    );
    assert_eq!(spawner.current_spawn_count(), 1);
    let still = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();
    assert!(Arc::ptr_eq(&still, &vendor), "same resident identity");
    assert!(world.npc(vendor_id).is_some());
}

#[tokio::test]
async fn activation_reclaim_protects_activity_and_stale_targets_expire() {
    let server = server();
    let m = manager(&server);
    for x in [100.0, 104.0, 108.0] {
        m.add_npc_spawner(&server, npc_def(500, x, 100.0)).unwrap();
    }
    let spawners: Vec<Arc<NpcSpawner>> = m.get_all_spawners().into_values().flatten().collect();
    assert_eq!(spawners.len(), 3);
    let (combat, caster, escort) = (
        spawners[0].clone(),
        spawners[1].clone(),
        spawners[2].clone(),
    );
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    assert_eq!(activate(&server).created, 3);
    let npc_of = |s: &Arc<NpcSpawner>| s.spawned_npcs().into_values().flatten().next().unwrap();
    let fighter = npc_of(&combat);
    let casting = npc_of(&caster);
    let escorting = npc_of(&escort);

    // Actual combat: a live opponent inside the authored leash.
    let foe = Entity::new(90_001, {
        let mut n = Npc::new(600);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        n.unit.transform.set_xyz(110.0, 100.0, 100.0);
        n
    });
    world.spawn(&foe, &server.view_ctx());
    {
        let mut d = fighter.data();
        d.unit.is_in_battle = true;
        d.unit.current_target = foe.obj_id;
        d.unit
            .skill_state
            .aggro_table
            .insert(foe.obj_id, Aggro::default());
    }
    // Casting.
    casting.data().unit.skill_state.skill_task = Some(SkillTaskSlot {
        kind: SkillTaskKind::Cast,
        skill: SkillRun::new(
            Arc::new(crate::systems::skills::tests::skill_template(1)),
            1,
        ),
        cancel: CancelToken::new(),
    });
    // Persistent escort work (follow target).
    server.systems.ai.ais.add_ai(&escorting, AiKind::Dummy);
    let follow = WorldObject::from(&player);
    crate::systems::ai::manager::post(&server, escorting.obj_id, move |c| {
        c.ai.follow_unit = Some(follow);
    });

    leave_world(&server, &player);
    activate(&server); // books the countdown
    advance(60);
    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 0, "combat / cast / escort are protected");
    m.check_respawns_tick(&server);
    assert_eq!(world.all_npcs().len(), 4, "nothing was removed");

    // The opponent is deleted: the stale aggro target no longer protects the fighter.
    world.delete(WorldObject::from(&foe));
    casting.data().unit.skill_state.skill_task = None;
    crate::systems::ai::manager::post(&server, escorting.obj_id, |c| c.ai.follow_unit = None);

    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 3);
    m.check_respawns_tick(&server);
    assert!(world.all_npcs().is_empty());
}

#[tokio::test]
async fn activation_reclaim_preserves_authored_always_active_ai() {
    let server = activation_server_with_npc(
        template(10, 1),
        NpcTemplate {
            id: 500,
            activate_ai_always: true,
            ..NpcTemplate::default()
        },
    );
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    assert_eq!(activate(&server).created, 1);
    let npc = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();
    let npc_id = npc.obj_id;
    server.systems.ai.ais.add_ai(&npc, AiKind::Dummy);

    leave_world(&server, &player);
    activate(&server); // books the countdown
    advance(60);
    let stats = activate(&server);
    assert_eq!(
        stats.reclaimed, 0,
        "an authored ActivateAiAlways AI is an important actor"
    );
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 1);

    // Without its AI the placement follows the ordinary retention again.
    server.systems.ai.ais.remove(npc_id);
    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 1);
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
}

#[tokio::test]
async fn activation_reclaims_a_large_population_in_budgeted_steps() {
    let mut t = template(10, 5);
    t.min_population = 5;
    let server = activation_server(t);
    server.config.update(|c| c.world.npc_spawns_per_tick = 2);
    let m = manager(&server);
    let mut def = npc_def(500, 100.0, 100.0);
    def.count = 5;
    assert_eq!(m.add_npc_spawner(&server, def), Ok(1));
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    // The authored population fills over the budgeted passes (2 NPCs per pass).
    for _ in 0..3 {
        activate(&server);
    }
    assert_eq!(spawner.current_spawn_count(), 5);

    // Leaving starts the countdown; the reclaim removes a few NPCs per pass, never the whole
    // placement at once, and the limit counts removed NPCs.
    leave_world(&server, &player);
    activate(&server);
    advance(60);
    for (reclaimed, remaining) in [(2usize, 3i32), (2, 1), (1, 0)] {
        let stats = activate(&server);
        assert_eq!(stats.reclaimed, reclaimed, "budgeted drain step");
        assert_eq!(
            spawner.current_spawn_count(),
            remaining,
            "healthy removals complete in the reclamation pass"
        );
        m.check_respawns_tick(&server);
        assert_eq!(
            spawner.current_spawn_count(),
            remaining,
            "nothing is left queued for the respawn tick"
        );
    }
    assert_eq!(spawner.current_spawn_count(), 0);
    assert!(m.pending_respawns().is_empty());
    assert!(
        m.pending_despawns().is_empty(),
        "an immediate reclaim leaves no queued despawn"
    );
}

/// A placement mixing a resident member (merchant 500), a plain hostile member (600) and an
/// authored `ActivateAiAlways` member (700) is reclaimed per live NPC: only the nonresident,
/// idle members expire and the resident / always-active ones keep their identity.
#[tokio::test]
async fn activation_mixed_placement_reclaims_nonresidents_and_keeps_residents() {
    let mut game = GameData::empty();
    game.systems
        .npcs
        .npc
        .templates
        .insert(500, merchant_template(500));
    game.systems.npcs.npc.templates.insert(
        600,
        NpcTemplate {
            id: 600,
            aggression: true,
            ..NpcTemplate::default()
        },
    );
    game.systems.npcs.npc.templates.insert(
        700,
        NpcTemplate {
            id: 700,
            activate_ai_always: true,
            ..NpcTemplate::default()
        },
    );
    game.systems.npcs.npc_spawners = NpcSpawnerData::from_parts(
        vec![template(10, 3)],
        vec![
            member(100, 10, 500),
            member(110, 10, 600),
            member(120, 10, 700),
        ],
    )
    .unwrap();
    let server = Arc::new(test_server_with(game));
    server.set_self();
    *server.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));

    let m = manager(&server);
    let mut def = npc_def(500, 100.0, 100.0);
    def.count = 3;
    assert_eq!(m.add_npc_spawner(&server, def), Ok(1));
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    assert_eq!(activate(&server).created, 3);
    let by_template = |template_id: u32| {
        spawner
            .spawned_npcs()
            .into_values()
            .flatten()
            .find(|n| n.data().unit.template_id == template_id)
            .expect("member spawned")
    };
    let vendor = by_template(500);
    let hostile = by_template(600);
    let always = by_template(700);
    let (vendor_id, hostile_id, always_id) = (vendor.obj_id, hostile.obj_id, always.obj_id);
    drop(hostile);
    // The authored ActivateAiAlways member has an actual AI (the test backend creates none).
    server.systems.ai.ais.add_ai(&always, AiKind::Dummy);

    leave_world(&server, &player);
    activate(&server); // books the stable countdown
    advance(60);
    let stats = activate(&server);
    assert_eq!(
        stats.reclaimed, 1,
        "only the idle nonresident member expires"
    );
    m.check_respawns_tick(&server);
    assert!(world.npc(hostile_id).is_none(), "hostile reclaimed");
    assert!(
        world.npc(always_id).is_some(),
        "an authored ActivateAiAlways AI is protected"
    );
    let vendor_now = world.npc(vendor_id).expect("resident kept");
    assert!(
        Arc::ptr_eq(&vendor_now, &vendor),
        "the resident keeps its identity"
    );
    assert_eq!(spawner.current_spawn_count(), 2);

    // Without its AI the always-active member follows the ordinary lifecycle again.
    server.systems.ai.ais.remove(always_id);
    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 1);
    m.check_respawns_tick(&server);
    assert!(world.npc(always_id).is_none());
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "the resident vendor stays"
    );
    assert!(world.npc(vendor_id).is_some());

    // Returning refills the reclaimed members while the vendor stays the very same NPC.
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 2, "the reclaimed members refill");
    assert_eq!(spawner.current_spawn_count(), 3);
    let vendor_now = world.npc(vendor_id).expect("resident kept through rewarm");
    assert!(Arc::ptr_eq(&vendor_now, &vendor), "same vendor identity");
}

/// Runtime ownership excludes an NPC from residency even when its actual template fills a
/// residency role: a spawn owned by a player / creator follows its owner's lifecycle and is
/// eligible for distance reclamation like any nonresident.
#[tokio::test]
async fn activation_runtime_owned_resident_template_is_not_kept() {
    let server = activation_server_with_npc(template(10, 1), merchant_template(500));
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    assert_eq!(activate(&server).created, 1);
    let vendor = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();
    vendor.data().unit.owner_id = player.data().id;
    let vendor_id = vendor.obj_id;

    leave_world(&server, &player);
    activate(&server); // books the stable countdown
    advance(60);
    let stats = activate(&server);
    assert_eq!(
        stats.reclaimed, 1,
        "a runtime-owned NPC is never a resident"
    );
    m.check_respawns_tick(&server);
    assert!(server.world.world(0).unwrap().npc(vendor_id).is_none());
}

/// A player returning after the retention expiry but before the 1 s respawn tick ran must not
/// lose the refilled NPC to a stale queued despawn: distance reclamation completes in its own
/// pass, so the rewarm refills and the refill survives the next respawn tick.
#[tokio::test]
async fn activation_expiry_rewarm_before_respawn_tick_keeps_the_refilled_npc() {
    let server = server();
    server
        .config
        .update(|c| c.world.npc_warm_retention_seconds = 0);
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    assert_eq!(activate(&server).created, 1);
    leave_world(&server, &player);
    assert_eq!(
        activate(&server).reclaimed,
        1,
        "retention 0 reclaims on the departure pass"
    );

    // Return after the expiry pass but before any respawn tick: the reclaim already completed
    // (no queued booking), so the placement simply refills for the returning player.
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 1, "the placement refills in the same window");
    m.check_respawns_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        1,
        "no stale booking deletes the refilled NPC"
    );
    assert_eq!(world.all_npcs().len(), 1);
}

#[tokio::test]
async fn activation_dormancy_preserves_corpse_and_respawn_deadlines() {
    let server = activation_server(template(10, 1));
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    assert_eq!(activate(&server).created, 1);
    // The spawn overwrites `RespawnTime` with `random(SpawnDelayMin, SpawnDelayMax)`, so the
    // authored 30 s deadline is set after the first spawn (corpse despawn_time is the fixture's
    // 20 s).
    spawner.set_respawn_time(30);
    let victim = spawner
        .spawned_npcs()
        .into_values()
        .flatten()
        .next()
        .unwrap();
    let victim_id = victim.obj_id;
    {
        let mut d = victim.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &victim);
    let pending = m.pending_respawns();
    assert_eq!(pending.len(), 1);
    let deadline = pending[0].1.expect("authored respawn deadline");

    // The player leaves; the corpse despawns on its own deadline and the respawn reservation
    // (and with it the object ID) survives dormancy.
    leave_world(&server, &player);
    advance(20);
    m.check_respawns_tick(&server);
    assert!(world.npc(victim_id).is_none(), "corpse removed on schedule");
    assert_eq!(
        m.pending_respawns(),
        vec![(victim_id, Some(deadline))],
        "the respawn reservation keeps its deadline and ID"
    );

    // The retention window expires: the empty cold placement is still tracked, nothing is
    // reclaimed (a pending timer is never a distance-reclamation target) and no untracked NPC
    // appears from the timer.
    activate(&server); // books the countdown
    advance(60);
    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 0);
    assert!(world.all_npcs().is_empty());

    // The respawn deadline fires on its own schedule while the player is still away.
    m.check_respawns_tick(&server);
    assert!(m.pending_respawns().is_empty());
    assert!(world.all_npcs().is_empty());

    // Returning refills normally.
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_authored_schedule_wins_over_residency_and_retention() {
    let mut t = template(10, 1);
    t.start_time = 1.0;
    t.end_time = 2.0;
    let server = activation_server_with_npc(t, merchant_template(500));
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    server.time.set(1.5);
    assert_eq!(activate(&server).created, 1);
    leave_world(&server, &player);

    // Outside the authored window the resident vendor is still despawned as prescribed:
    // authored removal has priority over residency and the retention window.
    server.time.set(5.0);
    let stats = activate(&server);
    assert_eq!(
        stats.reclaimed, 0,
        "authored despawn, not a distance reclaim"
    );
    advance(20); // the authored corpse despawn time
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
    assert!(world.all_npcs().is_empty());
}

/// Quest fixture: NPC 500 accepts quest 100 (which then waits in PROGRESS on an unfinished hunt
/// objective), NPC 501 reports quest 110 (both nonaggressive).
fn quest_server() -> Arc<GameServer> {
    let mut game = GameData::empty();
    for id in [500u32, 501] {
        game.systems.npcs.npc.templates.insert(
            id,
            NpcTemplate {
                id,
                ..NpcTemplate::default()
            },
        );
    }
    {
        let q = &mut game.systems.quests;
        q.add_template(QuestTemplate {
            id: 100,
            ..QuestTemplate::default()
        });
        q.add_component(QuestComponentTemplate {
            id: 100,
            quest_id: 100,
            kind_id: kind::START,
            ..QuestComponentTemplate::default()
        });
        q.add_act(100, 100, 100, 0, ActKind::ConAcceptNpc { npc_id: 500 });
        q.add_component(QuestComponentTemplate {
            id: 101,
            quest_id: 100,
            kind_id: kind::PROGRESS,
            ..QuestComponentTemplate::default()
        });
        q.add_act(
            101,
            101,
            101,
            1,
            ActKind::ObjMonsterHunt {
                npc_id: 600,
                use_alias: false,
                alias_id: 0,
                highlight_doodad_id: 0,
                highlight_doodad_phase: -1,
            },
        );
        q.add_template(QuestTemplate {
            id: 110,
            ..QuestTemplate::default()
        });
        q.add_component(QuestComponentTemplate {
            id: 110,
            quest_id: 110,
            kind_id: kind::READY,
            ..QuestComponentTemplate::default()
        });
        q.add_act(
            110,
            110,
            110,
            0,
            ActKind::ConReportNpc {
                npc_id: 501,
                use_alias: false,
                alias_id: 0,
            },
        );
        q.finish();
    }
    game.systems.npcs.npc_spawners = NpcSpawnerData::from_parts(
        vec![template(10, 1), template(20, 1)],
        vec![member(100, 10, 500), member(200, 20, 501)],
    )
    .unwrap();
    let s = Arc::new(test_server_with(game));
    s.set_self();
    *s.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    s
}

#[tokio::test]
async fn activation_quest_giver_and_reporter_stay_resident_and_resend_state() {
    let server = quest_server();
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    m.add_npc_spawner(&server, npc_def(501, 104.0, 100.0))
        .unwrap();
    let spawners: Vec<Arc<NpcSpawner>> = m.get_all_spawners().into_values().flatten().collect();
    assert_eq!(spawners.len(), 2);
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    assert_eq!(activate(&server).created, 2);
    let mut ids: Vec<u32> = world.all_npcs().iter().map(|n| n.obj_id).collect();
    ids.sort_unstable();
    assert_eq!(ids.len(), 2);
    client.drain();

    // Accept quest 100 at the quest giver through the client packet path; it then waits in
    // PROGRESS on its unfinished hunt objective.
    use crate::net::registry::ClientPacket;
    use crate::systems::quests::packets::CSStartQuestContextPacket;
    client.conn.active_char = Some(player.clone());
    let giver_id = world
        .all_npcs()
        .iter()
        .find(|n| n.data().unit.template_id == 500)
        .expect("quest giver")
        .obj_id;
    CSStartQuestContextPacket {
        quest_context_id: 100,
        npc_obj_id: giver_id,
        doodad_obj_id: 0,
        sphere_id: 0,
    }
    .handle(&server, &mut client.conn)
    .await
    .unwrap();
    server.systems.quests.run_queued_evaluations(&server);
    {
        let c = player.data();
        assert_eq!(c.quests.active.len(), 1, "quest accepted");
        let active = &c.quests.active[0];
        assert_eq!(active.template_id, 100);
        assert_eq!(active.step, kind::PROGRESS, "active quest in progress");
        assert_eq!(active.objectives[0], 0);
    }
    let ops = client.drain_ops();
    assert!(
        ops.contains(&sc::SCQuestContextStartedPacket),
        "the accepted quest started: {ops:?}"
    );
    assert!(
        ops.contains(&sc::SCQuestContextUpdatedPacket),
        "the quest update reached the client: {ops:?}"
    );

    // Leaving beyond the warm horizon and the retention interval keeps both placements and the
    // accepted quest state.
    leave_world(&server, &player);
    activate(&server);
    advance(600);
    let stats = activate(&server);
    assert_eq!(stats.reclaimed, 0);
    m.check_respawns_tick(&server);
    let mut still: Vec<u32> = world.all_npcs().iter().map(|n| n.obj_id).collect();
    still.sort_unstable();
    assert_eq!(still, ids, "same quest giver and reporter");
    {
        let c = player.data();
        let active = &c.quests.active[0];
        assert_eq!(
            (active.template_id, active.step, active.objectives[0]),
            (100, kind::PROGRESS, 0),
            "the active quest and its progress survive retention"
        );
    }

    // Returning re-announces the very same NPCs through the normal visibility path.
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let ops = client.drain_ops();
    assert!(
        ops.contains(&sc::SCUnitStatePacket),
        "state packets resent for the resident quest NPCs: {ops:?}"
    );
    let mut returned: Vec<u32> = world.all_npcs().iter().map(|n| n.obj_id).collect();
    returned.sort_unstable();
    assert_eq!(returned, ids);
    {
        let c = player.data();
        let active = &c.quests.active[0];
        assert_eq!(
            (active.template_id, active.step, active.objectives[0]),
            (100, kind::PROGRESS, 0),
            "the quest is still in progress after the return"
        );
    }
}

#[tokio::test]
async fn activation_ignores_other_instance_players_at_the_same_position() {
    let server = server();
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let world = server.world.world(0).unwrap();
    let w1 = server
        .world
        .create_world_instance(world.template.clone(), 1);

    // A player in instance 1 at the spawner's exact coordinates must not activate it.
    let client1 = TestClient::new(&server, 1, 1);
    let p1 = Player::new(
        test_character(1, "Other", 100.0, 100.0),
        client1.conn.handle(),
    );
    server.world.try_add_character(&p1);
    w1.spawn(&p1, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 0, "another instance's player does not count");
    assert_eq!(spawner.current_spawn_count(), 0);

    // Sanity: a player of the owning instance does activate it.
    let client0 = TestClient::new(&server, 2, 2);
    let _p0 = spawn_player(
        &server,
        &client0,
        test_character(2, "Mine", 100.0, 100.0),
        5001,
    );
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_prewarms_one_ring_beyond_client_visibility_without_sending_state() {
    let server = server();
    let m = manager(&server);
    // The player region is (1, 1); the spawner sits three regions away (region (4, 1)): outside
    // the client visibility neighbourhood (radius 2) and outside the authored 150 m radius
    // (192 m), but inside the default prewarm ring (2 + NpcPrewarmRegionMargin 1).
    m.add_npc_spawner(&server, npc_def(500, 292.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let mut client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    client.drain();

    let stats = activate(&server);
    assert_eq!(stats.created, 1, "baseline NPC prepared beyond visibility");
    assert_eq!(spawner.current_spawn_count(), 1);
    assert!(
        !spawner.is_player_in_spawn_radius(&server),
        "the player is outside the authored radius"
    );
    assert!(
        client.drain().is_empty(),
        "a prewarmed NPC sends no state before the player can see it"
    );

    // Entering the visible neighbourhood announces the prepared NPC through the normal
    // visibility path.
    player.data().unit.transform.set_xyz(250.0, 100.0, 100.0);
    server
        .world
        .world(0)
        .unwrap()
        .add_visible_object(&player, &server.view_ctx());
    assert!(
        client.drain_ops().contains(&sc::SCUnitStatePacket),
        "the prepared NPC is shown once the player sees its region"
    );
}

#[tokio::test]
async fn activation_budget_limits_created_npcs_per_pass_nearest_first() {
    let server = server();
    server.config.update(|c| c.world.npc_spawns_per_tick = 2);
    let m = manager(&server);
    for x in [100.0, 104.0, 108.0, 112.0, 116.0] {
        assert_eq!(m.add_npc_spawner(&server, npc_def(500, x, 100.0)), Ok(1));
    }
    let spawners: Vec<Arc<NpcSpawner>> = m.get_all_spawners().into_values().flatten().collect();
    assert_eq!(spawners.len(), 5);
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    // Pass 1: two creations, and they are the two nearest spawners to the player.
    let stats = activate(&server);
    assert!(stats.created <= 2, "budget respected: {stats:?}");
    assert_eq!(stats.created, 2);
    assert_eq!(spawners[0].current_spawn_count(), 1);
    assert_eq!(spawners[1].current_spawn_count(), 1);
    assert_eq!(
        spawners[2].current_spawn_count(),
        0,
        "nearer spawners are created first"
    );
    let mut total = stats.created;

    // Unfilled targets continue over the next passes.
    for expected in [2usize, 1, 0] {
        let stats = activate(&server);
        assert!(stats.created <= 2, "budget respected: {stats:?}");
        assert_eq!(stats.created, expected, "unfilled targets continue later");
        total += stats.created;
    }
    assert_eq!(total, 5);
    assert_eq!(
        server.world.world(0).unwrap().all_npcs().len(),
        5,
        "all targets fill over passes"
    );
}

#[tokio::test]
async fn activation_prioritizes_visible_neighbourhoods_and_rotates_instances() {
    let server = server();
    server.config.update(|c| c.world.npc_spawns_per_tick = 1);
    let m0 = manager(&server);
    // Visible ring of the player: one region away (region (2, 1)).
    m0.add_npc_spawner(&server, npc_def(500, 160.0, 100.0))
        .unwrap();
    // Prewarm-only: three regions away (region (4, 1)).
    m0.add_npc_spawner(&server, npc_def(500, 292.0, 100.0))
        .unwrap();
    let spawners: Vec<_> = m0.get_all_spawners().into_values().flatten().collect();
    let visible = spawners
        .iter()
        .find(|s| s.position().x == 160.0)
        .expect("visible spawner")
        .clone();
    let prewarm = spawners
        .iter()
        .find(|s| s.position().x == 292.0)
        .expect("prewarm spawner")
        .clone();

    let client0 = TestClient::new(&server, 1, 1);
    let _p0 = spawn_player(
        &server,
        &client0,
        test_character(1, "P", 100.0, 100.0),
        5000,
    );

    // Instance 1 with its own player and ordinary spawner.
    let world0 = server.world.world(0).unwrap();
    let w1 = server
        .world
        .create_world_instance(world0.template.clone(), 1);
    let m1 = server.systems.npcs.spawns.get_or_create(&w1);
    m1.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let other = only_spawner(&m1);
    let client1 = TestClient::new(&server, 2, 2);
    let p1 = Player::new(test_character(2, "Q", 100.0, 100.0), client1.conn.handle());
    server.world.try_add_character(&p1);
    w1.spawn(&p1, &server.view_ctx());

    // Pass 1: the visible candidate wins over the prewarm ring.
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(visible.current_spawn_count(), 1);
    assert_eq!(prewarm.current_spawn_count(), 0, "prewarm waits");
    assert_eq!(other.current_spawn_count(), 0);

    // Pass 2: the rotation starts with instance 1, so it progresses too.
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(other.current_spawn_count(), 1, "second instance progresses");
    assert_eq!(prewarm.current_spawn_count(), 0, "visible still first");

    // Pass 3: nothing visible is left to fill, so the prewarm ring follows.
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(prewarm.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_respects_management_gates_and_event_triggers() {
    let server = server();
    let m = manager(&server);
    let mut managed = npc_def(500, 100.0, 100.0);
    managed.base.spawn_group_id = 73;
    assert_eq!(m.add_npc_spawner(&server, managed), Ok(1));
    let managed = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    let stats = activate(&server);
    assert_eq!(stats.created, 0, "authored management group not enabled");
    assert_eq!(managed.current_spawn_count(), 0);

    managed.shared().set_management_enabled(true);
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(managed.current_spawn_count(), 1);

    // Event spawners are indexed nowhere: only their own trigger spawns them.
    let mut ev = npc_def(500, 100.0, 100.0);
    ev.npc_spawner_ids = vec![5, 6];
    assert_eq!(m.add_npc_spawner(&server, ev), Ok(0));
    let event = m.get_npc_spawner(6).remove(0);
    activate(&server);
    assert_eq!(
        event.current_spawn_count(),
        0,
        "event entry waits for its trigger"
    );
    event.do_event_spawn(&server);
    assert_eq!(event.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_respects_authored_spawn_windows_and_respawn_delays() {
    let mut t = template(10, 1);
    t.start_time = 1.0;
    t.end_time = 2.0;
    let server = activation_server(t);
    let m = manager(&server);
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    server.time.set(5.0);
    let stats = activate(&server);
    assert_eq!(stats.created, 0, "outside the authored window");
    assert_eq!(spawner.current_spawn_count(), 0);

    server.time.set(1.5);
    let stats = activate(&server);
    assert_eq!(stats.created, 1);

    // The killed NPC's own respawn deadline is not bypassed by the pass.
    let victim = spawner.spawned_npcs().values().next().unwrap()[0].clone();
    spawner.set_respawn_time(30);
    {
        let mut d = victim.data();
        d.unit.hp = 0;
        d.dead_time_ms = crate::world::now_ms();
    }
    spawn_manager::on_npc_died(&server, &victim);
    activate(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        0,
        "respawn deadline still pending"
    );
    spawn_manager::advance_test_clock(Duration::from_secs(31));
    m.check_respawns_tick(&server);
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_index_follows_added_spawners_and_delete_all() {
    let server = server();
    let m = manager(&server);
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    // Build the (empty) index with no ordinary spawner yet.
    assert_eq!(activate(&server).created, 0);

    // A newly added ordinary spawner enters the index without a player move (the membership
    // generation invalidates the index).
    m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0))
        .unwrap();
    let stats = activate(&server);
    assert_eq!(stats.created, 1, "new spawner entered the index");
    assert_eq!(world.all_npcs().len(), 1);

    // Delete-all drops it and invalidates the index again.
    m.delete_all_spawners(&server);
    assert_eq!(activate(&server).created, 0);
    assert!(world.all_npcs().is_empty());

    // Re-adding after the teardown still lands in the index.
    m.add_npc_spawner(&server, npc_def(500, 120.0, 100.0))
        .unwrap();
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(world.all_npcs().len(), 1);
}

#[tokio::test]
async fn activation_authored_radius_beyond_the_ring_and_exact_sphere() {
    let mut t = template(10, 1);
    t.test_radius_npc = 7.0; // 350 m
    let server = activation_server(t);
    // Retention 0 keeps this test focused on the far-authored sphere: leaving the exact sphere
    // reclaims on the same pass.
    server
        .config
        .update(|c| c.world.npc_warm_retention_seconds = 0);
    let m = manager(&server);
    let client = TestClient::new(&server, 1, 1);
    let player = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    // The index (far-authored part included) is built before the placement exists: a
    // runtime-added large-radius spawner still enters it without a player move.
    assert_eq!(activate(&server).created, 0);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 100.0, 420.0)),
        Ok(1)
    );
    let spawner = only_spawner(&m);

    // Region (1, 6) is five regions away: outside the default 2 + 1 ring, but the player is
    // inside the authored 350 m 3D sphere.
    let stats = activate(&server);
    assert_eq!(
        stats.created, 1,
        "authored radius beyond the configured ring"
    );
    assert_eq!(spawner.current_spawn_count(), 1);
    assert!(spawner.is_player_in_spawn_radius(&server));

    // Same horizontal position but far above: outside the exact 3D sphere, so the authored-only
    // candidate leaves the warm set again (and its NPC is despawned).
    player.data().unit.transform.set_xyz(100.0, 100.0, 400.0);
    world.add_visible_object(&player, &server.view_ctx());
    assert_eq!(player.region(), Some((0, 1, 1)), "still region (1, 1)");
    assert!(!spawner.is_player_in_spawn_radius(&server));
    assert_eq!(activate(&server).created, 0);
    m.check_respawns_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 0);
    assert!(world.all_npcs().is_empty());

    // Returning into the sphere refills on the first pass.
    player.data().unit.transform.set_xyz(100.0, 100.0, 100.0);
    world.add_visible_object(&player, &server.view_ctx());
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_authored_radius_is_honored_with_margin_zero() {
    let mut t = template(10, 1);
    t.test_radius_npc = 4.0; // 200 m
    let server = activation_server(t);
    server
        .config
        .update(|c| c.world.npc_prewarm_region_margin = 0);
    let m = manager(&server);
    // Region (1, 4): three regions away, outside the margin-0 ring (2), 160 m from the player.
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 100.0, 260.0)),
        Ok(1)
    );
    // Region (1, 5): 220 m away, outside the authored 200 m sphere.
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 100.0, 320.0)),
        Ok(1)
    );
    let spawners: Vec<Arc<NpcSpawner>> = m.get_all_spawners().into_values().flatten().collect();
    let inside = spawners
        .iter()
        .find(|s| s.position().y == 260.0)
        .unwrap()
        .clone();
    let outside = spawners
        .iter()
        .find(|s| s.position().y == 320.0)
        .unwrap()
        .clone();
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    let stats = activate(&server);
    assert_eq!(
        stats.created, 1,
        "margin 0 still honors the authored radius"
    );
    assert_eq!(inside.current_spawn_count(), 1);
    assert_eq!(
        outside.current_spawn_count(),
        0,
        "outside the exact 3D sphere"
    );
}

#[tokio::test]
async fn activation_visible_beats_far_authored_within_the_budget() {
    let mut t = template(10, 1);
    t.test_radius_npc = 7.0; // 350 m
    let server = activation_server(t);
    server.config.update(|c| c.world.npc_spawns_per_tick = 1);
    let m = manager(&server);
    // Visible neighbourhood (region (2, 1)).
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 160.0, 100.0)),
        Ok(1)
    );
    // Far-authored only: region (1, 6), 320 m away, inside the 350 m sphere.
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 100.0, 420.0)),
        Ok(1)
    );
    let spawners: Vec<Arc<NpcSpawner>> = m.get_all_spawners().into_values().flatten().collect();
    let visible = spawners
        .iter()
        .find(|s| s.position().x == 160.0)
        .unwrap()
        .clone();
    let far = spawners
        .iter()
        .find(|s| s.position().y == 420.0)
        .unwrap()
        .clone();
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);

    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(visible.current_spawn_count(), 1);
    assert_eq!(
        far.current_spawn_count(),
        0,
        "far-authored waits below visible"
    );

    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(far.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_far_authored_ignores_other_instance_players() {
    let mut t = template(10, 1);
    t.test_radius_npc = 7.0; // 350 m
    let server = activation_server(t);
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 100.0, 420.0)),
        Ok(1)
    );
    let spawner = only_spawner(&m);
    let world = server.world.world(0).unwrap();
    let w1 = server
        .world
        .create_world_instance(world.template.clone(), 1);

    // A player of another instance at the placement's exact position must not activate it.
    let client1 = TestClient::new(&server, 1, 1);
    let p1 = Player::new(
        test_character(1, "Other", 100.0, 420.0),
        client1.conn.handle(),
    );
    server.world.try_add_character(&p1);
    w1.spawn(&p1, &server.view_ctx());
    assert_eq!(
        activate(&server).created,
        0,
        "another instance's player does not count"
    );
    assert_eq!(spawner.current_spawn_count(), 0);

    // A player of the owning instance within the authored sphere activates it.
    let client0 = TestClient::new(&server, 2, 2);
    let _p0 = spawn_player(
        &server,
        &client0,
        test_character(2, "Mine", 100.0, 100.0),
        5001,
    );
    assert_eq!(activate(&server).created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
}

#[tokio::test]
async fn activation_retries_placements_whose_members_are_initialized_later() {
    let server = activation_server(template(10, 1));
    let m = manager(&server);
    assert_eq!(
        m.add_npc_spawner(&server, npc_def(500, 100.0, 100.0)),
        Ok(1)
    );
    let spawner = only_spawner(&m);
    let client = TestClient::new(&server, 1, 1);
    let _p = spawn_player(&server, &client, test_character(1, "P", 100.0, 100.0), 5000);
    let world = server.world.world(0).unwrap();

    // A temporarily empty member list must not drop the placement from the cached queue.
    let mut cleared = (*spawner.template().unwrap()).clone();
    cleared.npcs.clear();
    spawner.initialize_spawnable_npcs(Some(&cleared));
    assert!(spawner.spawnable_npcs().is_empty());
    assert_eq!(activate(&server).created, 0);

    // The members are initialized later: with players standing still (no movement, reload or
    // clock advance) the next pass creates the NPC from the same cached queue.
    let template = spawner.template().unwrap();
    spawner.initialize_spawnable_npcs(Some(template.as_ref()));
    let stats = activate(&server);
    assert_eq!(stats.created, 1);
    assert_eq!(spawner.current_spawn_count(), 1);
    assert_eq!(world.all_npcs().len(), 1);
}

#[test]
fn prewarm_margin_and_tick_interval_are_clamped() {
    let mut w = WorldConfig::default();
    assert_eq!(spawn_manager::prewarm_region_margin(&w), 1);
    w.npc_prewarm_region_margin = 100;
    assert_eq!(spawn_manager::prewarm_region_margin(&w), 4);
    w.npc_spawn_tick_ms = 0;
    assert_eq!(
        spawn_manager::spawn_tick_interval(&w),
        Duration::from_millis(250),
        "0 never spins"
    );
    w.npc_spawn_tick_ms = 1;
    assert_eq!(
        spawn_manager::spawn_tick_interval(&w),
        Duration::from_millis(50)
    );
    w.npc_spawn_tick_ms = u64::MAX;
    assert_eq!(
        spawn_manager::spawn_tick_interval(&w),
        Duration::from_millis(60_000)
    );
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
    type NpcGrid = std::collections::HashMap<(u32, i64, i64, i64), Vec<(f32, f32, f32)>>;
    let mut kept: NpcGrid = std::collections::HashMap::new();
    let mut fake_seen: HashSet<u32> = HashSet::new();
    let (mut dup, mut missing, mut entries_with_spawners, mut fake, mut event) = (0, 0, 0, 0, 0);
    let mut clones = 0usize;
    let cell2 = |v: f32| (v / 2.0).floor() as i64;
    for e in &npc_raw {
        let unit = num(e, "UnitId") as u32;
        let pos = e.get("Position").unwrap();
        let (x, y, z) = (
            num(pos, "X") as f32,
            num(pos, "Y") as f32,
            num(pos, "Z") as f32,
        );
        let mut is_dup = false;
        for dx in -1..=1 {
            for dy in -1..=1 {
                for dz in -1..=1 {
                    if let Some(v) = kept.get(&(unit, cell2(x) + dx, cell2(y) + dy, cell2(z) + dz))
                    {
                        if v.iter().any(|&(kx, ky, kz)| {
                            (kx - x).abs() < 2.0 && (ky - y).abs() < 2.0 && (kz - z).abs() < 2.0
                        }) {
                            is_dup = true;
                        }
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
        } else {
            // Every placement keeps a spawner now: the first generates the template, later
            // ones clone it.
            if fake_seen.insert(unit) {
                fake += 1;
            }
            1
        };
        if n > 0 {
            entries_with_spawners += 1;
            clones += n;
            kept.entry((unit, cell2(x), cell2(y), cell2(z)))
                .or_default()
                .push((x, y, z));
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

/// The authored Mistfiend (NPC 4227) cave placements of the shipped `main_world` files with the
/// real compact DB: all six load (template 4323, 55 s delay), a player at the first authored
/// point activates them through the normal `active_region_tick`, they spawn at their authored Z,
/// and a killed one respawns after 55 s at the same cave position. NPCs are created by the test
/// backend (the managers are stubs); activation is the real runtime path.
#[tokio::test]
#[ignore]
async fn real_data_mistfiend_cave_activation_and_respawn() {
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
    // The authored cave positions sit at x ~ 22900 / y ~ 9400, far outside the default 2x2-cell
    // test world: 32x16 cells (32768 m x 16384 m) cover them so the NPCs get real regions.
    let server = server_with_world(data, 32, 16);
    server.set_self();
    *server.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
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
    m.load_spawn_files(&server, &world_path, &exists).unwrap();

    // Exactly the six authored Mistfiend placements survive the load.
    let authored = [
        (22932.89f32, 9428.54f32, 556.25f32),
        (22948.07, 9426.05, 557.12),
        (22846.81, 9448.47, 593.0),
        (22905.03, 9428.53, 572.36),
        (22883.19, 9434.90, 585.6),
        (22859.00, 9434.70, 591.6),
    ];
    let mistfiends: Vec<_> = m
        .get_all_spawners()
        .into_values()
        .flatten()
        .filter(|s| s.unit_id == 4227)
        .collect();
    assert_eq!(mistfiends.len(), 6, "six authored Mistfiend placements");
    let mut loaded: Vec<(f32, f32, f32)> = mistfiends
        .iter()
        .map(|s| {
            let p = s.position();
            (p.x, p.y, p.z)
        })
        .collect();
    loaded.sort_by(|a, b| a.partial_cmp(b).unwrap());
    let mut expected = authored;
    expected.sort_by(|a, b| a.partial_cmp(b).unwrap());
    assert_eq!(loaded, expected);

    // A player inside the cave at the known Mistfiend point activates them.
    let client = TestClient::new(&server, 1, 1);
    let mut character = test_character(1, "Cave", authored[0].0, authored[0].1);
    character
        .unit
        .transform
        .set_xyz(authored[0].0, authored[0].1, authored[0].2);
    let _p = spawn_player(&server, &client, character, 5000);
    spawn_manager::active_region_tick(&server);

    let world = server.world.world(0).unwrap();
    for s in &mistfiends {
        let spawned = s.spawned_npcs();
        let list = spawned
            .get(&s.spawner_id)
            .unwrap_or_else(|| panic!("spawner {} spawned nothing", s.id));
        assert_eq!(list.len(), 1, "spawner {}", s.id);
        let npc = list[0].clone();
        {
            let d = npc.data();
            let p = s.position();
            assert_eq!((d.unit.transform.x, d.unit.transform.y), (p.x, p.y));
            assert_eq!(d.unit.transform.z, p.z, "spawns at the authored Z");
            assert_eq!(d.spawner_id, s.id);
        }
        assert!(
            world.npc(npc.obj_id).is_some(),
            "spawner {} NPC is not in the world",
            s.id
        );
        assert!(
            npc.region().is_some(),
            "spawner {} NPC has no world region (authored position outside the grid?)",
            s.id
        );
        assert!(npc.is_visible(), "spawner {} NPC is not visible", s.id);
    }

    // Kill one: template 4323's 55 s delay, respawn keeping the cave position.
    let spawner = &mistfiends[0];
    let victim = spawner.spawned_npcs().get(&spawner.spawner_id).unwrap()[0].clone();
    victim.data().unit.hp = 0;
    spawn_manager::on_npc_died(&server, &victim);
    assert_eq!(m.pending_respawns().len(), 1);
    assert_eq!(spawner.respawn_time(), 55);
    advance(54);
    m.check_respawns_tick(&server);
    spawn_manager::active_region_tick(&server);
    assert_eq!(
        spawner.current_spawn_count(),
        0,
        "respawn waits the full 55 s"
    );
    advance(1);
    m.check_respawns_tick(&server);
    spawn_manager::active_region_tick(&server);
    assert_eq!(spawner.current_spawn_count(), 1, "respawned after 55 s");
    let replacement = spawner
        .spawned_npcs()
        .get(&spawner.spawner_id)
        .and_then(|l| l.iter().find(|n| !n.data().unit.is_dead()).cloned())
        .unwrap();
    let p = spawner.position();
    let d = replacement.data();
    assert_eq!(
        (d.unit.transform.x, d.unit.transform.y, d.unit.transform.z),
        (p.x, p.y, p.z),
        "cave position retained"
    );
    assert!(world.npc(replacement.obj_id).is_some());
    drop(_p);
}
