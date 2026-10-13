use std::time::Duration;

use super::*;
use crate::data::world_data::{Zone, ZoneConflict};
use crate::data::world_templates::WorldSpawnPosition;
use crate::data::GameData;
use crate::models::npc::Npc;
use crate::opcodes::sc;
use crate::systems::instances::data::{TowerDefProgKillTarget, TowerDefProgSpawnTarget};
use crate::systems::npcs::doodad::spawner::{NpcSpawnerDef, SpawnerBase};
use crate::systems::npcs::npc::data::NpcTemplate;
use crate::systems::npcs::npc::spawner_data::{NpcSpawnerData, NpcSpawnerNpc, NpcSpawnerTemplate};
use crate::systems::npcs::spawn_manager::{backend::TestBackend, TemplateExists};
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{DoodadRef, Entity, WorldInstance};

const KEY: EventKey = EventKey {
    world_id: 0,
    id: 12,
    zone_group_id: 5,
};

struct Backend;
impl TestBackend for Backend {
    fn npc_create(&self, server: &GameServer, _: u32, id: u32) -> Option<NpcRef> {
        let mut n = Npc::new(id);
        n.unit.hp = 100;
        n.unit.max_hp = 100;
        Some(Entity::new(server.ids.object.get_next_id().ok()?, n))
    }
    fn doodad_create(
        &self,
        server: &GameServer,
        _: &Arc<WorldInstance>,
        _: u32,
        template: u32,
    ) -> Option<DoodadRef> {
        Some(Entity::new(
            server.ids.object.get_next_id().ok()?,
            crate::models::doodad::Doodad::new(template),
        ))
    }
}

fn fixture() -> Arc<GameServer> {
    let mut data = GameData::empty();
    data.zones.zones.insert(
        TEST_ZONE,
        Zone {
            id: 77,
            zone_key: TEST_ZONE,
            group_id: 5,
            ..Default::default()
        },
    );
    let td = &mut data.systems.instances.tower_defs;
    td.tower_defs.insert(
        12,
        TowerDef {
            id: 12,
            target_npc_spawn_id: 10,
            first_wave_after: 3.0,
            force_end_time: 100.0,
            time_of_day: 18.0,
            time_of_day_day_interval: 1,
            progs: vec![69, 70],
            ..Default::default()
        },
    );
    td.progs.insert(
        69,
        TowerDefProg {
            id: 69,
            tower_def_id: 12,
            cond_to_next_time: 5.0,
            cond_comp_by_and: true,
            spawn_targets: vec![TowerDefProgSpawnTarget {
                id: 1,
                spawn_target_id: 20,
                spawn_target_type: "NpcSpawner".into(),
                despawn_on_next_step: true,
            }],
            kill_targets: vec![TowerDefProgKillTarget {
                id: 1,
                kill_target_id: 700,
                kill_target_type: "Npc".into(),
                kill_count: 2,
            }],
        },
    );
    td.progs.insert(
        70,
        TowerDefProg {
            id: 70,
            tower_def_id: 12,
            cond_to_next_time: 10.0,
            cond_comp_by_and: true,
            ..Default::default()
        },
    );
    for id in [500, 600, 700] {
        data.systems.npcs.npc.templates.insert(
            id,
            NpcTemplate {
                id,
                ..Default::default()
            },
        );
    }
    data.systems.npcs.npc_spawners = NpcSpawnerData::from_parts(
        vec![
            NpcSpawnerTemplate::with_member(10, 500),
            NpcSpawnerTemplate::with_member(20, 600),
        ],
        vec![
            NpcSpawnerNpc {
                id: 1,
                ..NpcSpawnerNpc::with_member(10, 500)
            },
            NpcSpawnerNpc {
                id: 2,
                ..NpcSpawnerNpc::with_member(20, 600)
            },
        ],
    )
    .unwrap();
    let server = Arc::new(test_server_with(data));
    *server.systems.npcs.spawns.test_backend.lock().unwrap() = Some(Arc::new(Backend));
    let world = server.world.world(0).unwrap();
    let spawns = server.systems.npcs.spawns.get_or_create(&world);
    for id in [500, 600] {
        spawns
            .add_npc_spawner(
                &server,
                NpcSpawnerDef {
                    base: SpawnerBase {
                        unit_id: id,
                        position: WorldSpawnPosition {
                            zone_id: TEST_ZONE,
                            x: 100.0,
                            y: 100.0,
                            z: 100.0,
                            ..Default::default()
                        },
                        ..Default::default()
                    },
                    count: 1,
                    npc_spawner_ids: Vec::new(),
                },
            )
            .unwrap();
    }
    server
}

fn advance(seconds: u64) {
    spawn_manager::advance_test_clock(Duration::from_secs(seconds));
}

#[tokio::test]
async fn tower_def_doodad_waves_clean_only_their_owned_objects() {
    let mut server = fixture();
    let data = Arc::get_mut(&mut Arc::get_mut(&mut server).unwrap().game_data).unwrap();
    for id in [8000, 8001] {
        data.systems.npcs.doodad.templates.insert(
            id,
            crate::systems::npcs::doodad::templates::DoodadTemplate {
                id,
                ..Default::default()
            },
        );
        data.systems
            .instances
            .tower_defs
            .progs
            .get_mut(&69)
            .unwrap()
            .spawn_targets
            .push(TowerDefProgSpawnTarget {
                id,
                spawn_target_id: id,
                spawn_target_type: "DoodadAlmighty".into(),
                despawn_on_next_step: id == 8000,
            });
    }
    let dir = std::env::temp_dir().join(format!("aaemu-tower-doodads-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    std::fs::write(
        dir.join("npc_spawns.json"),
        r#"[
        {"UnitId":500,"Position":{"X":100,"Y":100,"Z":100}},
        {"UnitId":600,"Position":{"X":100,"Y":100,"Z":100}}
    ]"#,
    )
    .unwrap();
    std::fs::write(
        dir.join("doodad_spawns.json"),
        r#"[
        {"UnitId":8000,"Position":{"X":101,"Y":100,"Z":100}},
        {"UnitId":8001,"Position":{"X":102,"Y":100,"Z":100}}
    ]"#,
    )
    .unwrap();
    let world = server.world.world(0).unwrap();
    server
        .systems
        .npcs
        .spawns
        .get(0)
        .unwrap()
        .load_spawn_files(
            &server,
            &dir,
            &TemplateExists {
                npc: &|_| true,
                doodad: &|_| true,
                transfer: &|_| false,
                gimmick: &|_| false,
                slave: &|_| false,
            },
        )
        .unwrap();
    std::fs::remove_file(dir.join("npc_spawns.json")).unwrap();
    std::fs::remove_file(dir.join("doodad_spawns.json")).unwrap();
    std::fs::remove_dir(&dir).unwrap();
    let unrelated = Entity::new(9000, crate::models::doodad::Doodad::new(8000));
    world.spawn(&unrelated, &server.view_ctx());
    start(&server, KEY, TEST_ZONE).unwrap();
    next_wave(&server, KEY, 1).unwrap();
    let owned: Vec<_> = world
        .all_doodads()
        .into_iter()
        .filter(|d| !Arc::ptr_eq(d, &unrelated))
        .collect();
    assert_eq!(owned.len(), 2);
    next_wave(&server, KEY, 2).unwrap();
    // The new wave may reuse a released ID; only the original handle can survive.
    for doodad in &owned {
        assert_eq!(
            world
                .doodad(doodad.obj_id)
                .is_some_and(|current| Arc::ptr_eq(&current, doodad)),
            doodad.data().template_id == 8001
        );
    }
    end(&server, KEY).unwrap();
    assert!(owned.iter().all(|d| world.doodad(d.obj_id).is_none()));
    assert!(world.doodad(unrelated.obj_id).is_some());
}

fn kill(server: &GameServer, id: u32, template: u32) -> NpcRef {
    let mut n = Npc::new(template);
    n.unit.transform.instance_id = 0;
    n.unit.transform.zone_id = TEST_ZONE;
    let npc = Entity::new(id, n);
    super::super::events::on_do_die(server, &WorldObject::from(&npc), None);
    npc
}

#[tokio::test]
async fn tower_def_lifecycle_broadcasts_progresses_and_cleans_up() {
    let server = fixture();
    let mut c1 = TestClient::new(&server, 1, 1);
    let mut c2 = TestClient::new(&server, 2, 2);
    let p1 = spawn_player(&server, &c1, test_character(1, "One", 100.0, 100.0), 5000);
    spawn_player(&server, &c2, test_character(2, "Two", 100.0, 100.0), 5001);
    c1.drain();
    c2.drain();
    send_zone_list(&server, &p1);
    assert!(c1.drain().is_empty());
    assert!(
        server
            .systems
            .npcs
            .spawns
            .get(0)
            .unwrap()
            .get_all_spawners()
            .is_empty(),
        "controllers must not spawn at startup"
    );
    start(&server, KEY, TEST_ZONE).unwrap();
    for client in [&mut c1, &mut c2] {
        assert!(client.drain_ops().contains(&sc::SCTowerDefStartPacket));
    }
    assert_eq!(infos(&server, 0)[0].current_step, 0);
    assert!(start(&server, KEY, TEST_ZONE).is_err());
    assert!(next_wave(&server, KEY, 2).is_err());
    advance(3);
    tick(&server);
    assert_eq!(infos(&server, 0)[0].current_step, 1);
    let wave_npc = server
        .world
        .world(0)
        .unwrap()
        .npc_by_template_id(600)
        .unwrap();
    // Descendants inherit cleanup policy from the controller.
    let mut child = Npc::new(700);
    child.unit.hp = 100;
    child.unit.max_hp = 100;
    child.unit.owner_id = wave_npc.obj_id;
    child.unit.transform.zone_id = TEST_ZONE;
    let child = Entity::new(server.ids.object.get_next_id().unwrap(), child);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&child, &server.view_ctx());
    on_spawn(&server, &child);
    let mut summoned = Npc::new(700);
    summoned.unit.hp = 100;
    summoned.unit.max_hp = 100;
    let summoned = Entity::new(server.ids.object.get_next_id().unwrap(), summoned);
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&summoned, &server.view_ctx());
    assert_eq!(summoned.data().unit.owner_id, 0);
    on_summoned(
        &server,
        &WorldObject::from(&wave_npc),
        std::slice::from_ref(&summoned),
    );
    let unrelated = Entity::new(9000, Npc::new(700));
    server
        .world
        .world(0)
        .unwrap()
        .spawn(&unrelated, &server.view_ctx());
    // Death callbacks are deduplicated. The timer and both kills are required.
    let dead = kill(&server, 8000, 700);
    on_death(&server, &WorldObject::from(&dead));
    advance(5);
    tick(&server);
    assert_eq!(infos(&server, 0)[0].current_step, 1);
    // A new handle reusing the same id counts as a new death.
    kill(&server, 8000, 700);
    tick(&server);
    assert_eq!(infos(&server, 0)[0].current_step, 2);
    assert!(server.world.object(wave_npc.obj_id).is_none());
    assert!(server.world.object(child.obj_id).is_none());
    assert!(server.world.object(summoned.obj_id).is_none());
    assert!(server.world.object(unrelated.obj_id).is_some());
    send_zone_list(&server, &p1);
    assert!(c1.drain_ops().contains(&sc::SCTowerDefListPacket));
    advance(10);
    tick(&server);
    assert!(infos(&server, 0).is_empty());
    assert!(c2.drain_ops().contains(&sc::SCTowerDefEndPacket));
    assert!(server
        .world
        .world(0)
        .unwrap()
        .npc_by_template_id(500)
        .is_none());
    start(&server, KEY, TEST_ZONE).unwrap();
    cleanup_world(&server, 0);
    assert!(infos(&server, 0).is_empty());
}

#[test]
fn tower_def_or_conditions_use_any_kill_or_timer() {
    let p = TowerDefProg {
        cond_comp_by_and: false,
        cond_to_next_time: 10.0,
        kill_targets: vec![
            TowerDefProgKillTarget {
                id: 1,
                kill_target_id: 7,
                kill_target_type: "Npc".into(),
                kill_count: 2,
            },
            TowerDefProgKillTarget {
                id: 2,
                kill_target_id: 8,
                kill_target_type: "Npc".into(),
                kill_count: 1,
            },
        ],
        ..Default::default()
    };
    assert!(!wave_complete(&p, &HashMap::new(), 1.0));
    assert!(wave_complete(&p, &HashMap::from([(8, 1)]), 1.0));
    assert!(wave_complete(&p, &HashMap::new(), 10.0));
}

#[tokio::test]
async fn tower_def_cleanup_preserves_loot_corpses_until_their_normal_timer() {
    let server = fixture();
    start(&server, KEY, TEST_ZONE).unwrap();
    next_wave(&server, KEY, 1).unwrap();
    let corpse = server
        .world
        .world(0)
        .unwrap()
        .npc_by_template_id(600)
        .unwrap();
    {
        let mut npc = corpse.data();
        npc.unit.hp = 0;
        npc.unit.has_loot_left = true;
        npc.dead_time_ms = crate::world::now_ms();
    }
    let spawns = server.systems.npcs.spawns.get(0).unwrap();
    let deadline = spawn_manager::add_secs(spawn_manager::now(), 30.0);
    spawns.add_despawn(&server, WorldObject::from(&corpse), Some(deadline));
    spawns.add_respawn(&server, WorldObject::from(&corpse), Some(deadline));
    next_wave(&server, KEY, 2).unwrap();
    end(&server, KEY).unwrap();
    assert!(infos(&server, 0).is_empty());
    assert!(server.world.object(corpse.obj_id).is_some());
    assert!(corpse.data().unit.has_loot_left);
    assert!(spawns.pending_respawns().is_empty());
    assert_eq!(
        spawns.pending_despawns(),
        vec![(corpse.obj_id, Some(deadline))]
    );
    advance(29);
    spawns.check_respawns_tick(&server);
    assert!(server.world.object(corpse.obj_id).is_some());
    advance(1);
    spawns.check_respawns_tick(&server);
    assert!(server.world.object(corpse.obj_id).is_none());

    start(&server, KEY, TEST_ZONE).unwrap();
    let target = server
        .world
        .world(0)
        .unwrap()
        .npc_by_template_id(500)
        .unwrap();
    target.data().unit.hp = 0;
    target.data().unit.has_loot_left = true;
    target.data().dead_time_ms = crate::world::now_ms();
    spawns.add_despawn(&server, WorldObject::from(&target), Some(deadline));
    cleanup_world(&server, 0);
    assert!(server.world.object(target.obj_id).is_none());
    assert!(spawns.pending_despawns().is_empty());
}

#[tokio::test]
async fn tower_def_gm_controls_run_the_event_and_report_readiness() {
    use crate::systems::services::commands::scripts::{self, harness, ScriptEnv};
    let server = fixture();
    scripts::install(&server.systems.services.commands, ScriptEnv::default());
    assert!(server.systems.services.commands.reload());
    let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
    let (_, out) = harness::run(&server, &gm.p, "towerdef list").await;
    assert!(out
        .iter()
        .any(|s| s.contains("World 0, zone 100, zone group 5")));
    assert!(out.iter().any(|s| s.contains("12: ready")));
    gm.frames();
    harness::run_at(&server, &gm.p, "towerdef start 12", 0).await;
    assert!(infos(&server, 0).is_empty());
    let (_, out) = harness::run(&server, &gm.p, "tower_def start 12").await;
    assert!(out[0].contains("Tower defense 12: start"));
    assert_eq!(infos(&server, 0)[0].key.zone_group_id, 5);
    harness::run(&server, &gm.p, "towerdef next 12 1").await;
    assert_eq!(infos(&server, 0)[0].current_step, 1);
    let (_, out) = harness::run(&server, &gm.p, "towerdef status").await;
    assert!(out.iter().any(|s| s.contains("12: running, wave 1")));
    harness::run(&server, &gm.p, "towerdef end 12").await;
    assert!(infos(&server, 0).is_empty());
}

#[tokio::test]
async fn tower_def_end_kills_are_scoped_and_target_death_ends_the_event() {
    let mut server = fixture();
    let data = Arc::get_mut(&mut Arc::get_mut(&mut server).unwrap().game_data).unwrap();
    let def = data
        .systems
        .instances
        .tower_defs
        .tower_defs
        .get_mut(&12)
        .unwrap();
    def.kill_npc_count = 1;
    start(&server, KEY, TEST_ZONE).unwrap();
    let mut outside = Npc::new(500);
    outside.unit.transform.instance_id = 1;
    outside.unit.transform.zone_id = TEST_ZONE;
    on_death(&server, &WorldObject::from(&Entity::new(8000, outside)));
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1);
    // Reusing the target's id does not make an unrelated NPC the defended target.
    let target = server
        .world
        .world(0)
        .unwrap()
        .npc_by_template_id(500)
        .unwrap();
    let mut replacement = Npc::new(700);
    replacement.unit.transform.zone_id = TEST_ZONE;
    on_death(
        &server,
        &WorldObject::from(&Entity::new(target.obj_id, replacement)),
    );
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1);
    // A summoned NPC at step zero is not the defended target.
    let mut child = Npc::new(700);
    child.unit.owner_id = target.obj_id;
    child.unit.transform.zone_id = TEST_ZONE;
    let child = Entity::new(8100, child);
    on_spawn(&server, &child);
    on_death(&server, &WorldObject::from(&child));
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1);
    on_death(&server, &WorldObject::from(&target));
    tick(&server);
    assert!(infos(&server, 0).is_empty());
}

#[tokio::test]
async fn tower_def_preflight_and_force_end() {
    let mut server = fixture();
    let data = Arc::get_mut(&mut Arc::get_mut(&mut server).unwrap().game_data).unwrap();
    data.systems
        .instances
        .tower_defs
        .progs
        .get_mut(&70)
        .unwrap()
        .spawn_targets
        .push(TowerDefProgSpawnTarget {
            id: 2,
            spawn_target_id: 7297,
            spawn_target_type: "DoodadAlmighty".into(),
            despawn_on_next_step: false,
        });
    assert!(start(&server, KEY, TEST_ZONE)
        .unwrap_err()
        .to_string()
        .contains("Missing doodad template"));
    assert!(infos(&server, 0).is_empty());
    assert!(server.world.world(0).unwrap().all_npcs().is_empty());
    let data = Arc::get_mut(&mut Arc::get_mut(&mut server).unwrap().game_data).unwrap();
    data.systems
        .instances
        .tower_defs
        .progs
        .get_mut(&70)
        .unwrap()
        .spawn_targets
        .clear();
    start(&server, KEY, TEST_ZONE).unwrap();
    let target = server
        .world
        .world(0)
        .unwrap()
        .npc_by_template_id(500)
        .unwrap();
    let s = server
        .systems
        .npcs
        .spawns
        .npc_spawner_of(target.obj_id)
        .unwrap();
    s.do_despawn(&server, &target);
    advance(100);
    tick(&server);
    assert!(infos(&server, 0).is_empty());
    let m = server.systems.npcs.spawns.get(0).unwrap();
    assert!(m.pending_despawns().is_empty());
    assert!(m.pending_respawns().is_empty());
    m.check_respawns_tick(&server);
}

#[tokio::test]
async fn tower_def_schedule_handles_midnight_intervals_and_disabled_config() {
    let mut server = fixture();
    server.config.update(|c| {
        c.dungeons
            .tower_defense
            .events
            .push(TowerDefenseEventConfig {
                id: 12,
                world_id: 0,
                zone_group_id: 5,
                zone_id: TEST_ZONE,
            })
    });
    server.time.set(17.0);
    initialize(&server).unwrap();
    server.time.set(18.0);
    tick(&server);
    assert!(infos(&server, 0).is_empty());
    server
        .config
        .update(|c| c.dungeons.tower_defense.enabled = true);
    server.time.set(17.0);
    initialize(&server).unwrap();
    server.time.set(18.1);
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1);
    end(&server, KEY).unwrap();
    tick(&server);
    assert!(
        infos(&server, 0).is_empty(),
        "must not restart in the same clock interval"
    );
    let data = Arc::get_mut(&mut Arc::get_mut(&mut server).unwrap().game_data).unwrap();
    let def = data
        .systems
        .instances
        .tower_defs
        .tower_defs
        .get_mut(&12)
        .unwrap();
    def.time_of_day = 0.0;
    def.time_of_day_day_interval = 2;
    server.time.set(23.9);
    tick(&server);
    server.time.set(0.0);
    tick(&server);
    assert!(infos(&server, 0).is_empty(), "skip day 1");
    server.time.set(23.9);
    tick(&server);
    server.time.set(0.0);
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1, "start on day 2 at midnight");
}

#[tokio::test]
async fn tower_def_war_event_runs_once_per_war_and_stops_on_peace() {
    let mut server = fixture();
    let s = Arc::get_mut(&mut server).unwrap();
    s.config.update(|c| {
        c.dungeons.tower_defense.enabled = true;
        c.dungeons
            .tower_defense
            .events
            .push(TowerDefenseEventConfig {
                id: 12,
                world_id: 0,
                zone_group_id: 5,
                zone_id: TEST_ZONE,
            });
    });
    let conflict = ZoneConflict {
        zone_group_id: 5,
        war_tower_def_id: 12,
        ..Default::default()
    };
    Arc::get_mut(&mut s.game_data)
        .unwrap()
        .zones
        .conflicts
        .push(conflict.clone());
    server
        .systems
        .social
        .conflicts
        .init(&[conflict], crate::world::now_ms());
    initialize(&server).unwrap();
    server.systems.social.conflicts.set_state(
        &server,
        5,
        crate::data::world_data::conflict_type::WAR,
    );
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1);
    end(&server, KEY).unwrap();
    tick(&server);
    assert!(infos(&server, 0).is_empty());
    server.systems.social.conflicts.set_state(
        &server,
        5,
        crate::data::world_data::conflict_type::PEACE,
    );
    tick(&server);
    server.systems.social.conflicts.set_state(
        &server,
        5,
        crate::data::world_data::conflict_type::WAR,
    );
    tick(&server);
    assert_eq!(infos(&server, 0).len(), 1);
    server.systems.social.conflicts.set_state(
        &server,
        5,
        crate::data::world_data::conflict_type::PEACE,
    );
    tick(&server);
    assert!(infos(&server, 0).is_empty());
}

/// Audit against the actual 1.2 content without MySQL or running the live server.
#[tokio::test]
#[ignore = "needs AAEMU_COMPACT_DB and AAEMU_CLIENT_PAK"]
async fn real_tower_def_event_readiness() {
    let (Ok(path), Ok(pak)) = (
        std::env::var("AAEMU_COMPACT_DB"),
        std::env::var("AAEMU_CLIENT_PAK"),
    ) else {
        return;
    };
    let app = std::path::Path::new(&path)
        .parent()
        .unwrap()
        .parent()
        .unwrap();
    let files = crate::data::client_files::ClientFileManager::from_sources(&[pak], app);
    let conn =
        rusqlite::Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .unwrap();
    let mut data = GameData::empty();
    data.systems.instances.tower_defs = super::super::data::TowerDefData::load(&conn).unwrap();
    assert_eq!(data.systems.instances.tower_defs.progs.len(), 89);
    assert_eq!(
        data.systems.instances.tower_defs.tower_defs[&12]
            .progs
            .len(),
        13
    );
    data.systems.npcs.npc_spawners = NpcSpawnerData::load_from(&conn).unwrap();
    for row in conn
        .prepare("SELECT id FROM npcs")
        .unwrap()
        .query_map([], |r| r.get::<_, u32>(0))
        .unwrap()
    {
        let id = row.unwrap();
        data.systems.npcs.npc.templates.insert(
            id,
            NpcTemplate {
                id,
                ..Default::default()
            },
        );
    }
    data.zones = crate::data::world_data::ZoneData::load(&conn, 0).unwrap();
    let mut server = test_server_with(data);
    let worlds = crate::data::world_templates::WorldTemplates::load(&files, app).unwrap();
    // Keep the real zone grid, replacing the test helper's synthetic world.
    let world = worlds
        .get_world_template_by_name("main_world")
        .unwrap()
        .clone();
    Arc::get_mut(&mut server.game_data).unwrap().worlds = worlds;
    server.world.remove_world(0);
    server.world.create_world_instance(Arc::new(world), 0);
    let spawns = server
        .systems
        .npcs
        .spawns
        .get_or_create(&server.world.world(0).unwrap());
    spawns
        .load_spawn_files(
            &server,
            &app.join("Data/Worlds/main_world"),
            &TemplateExists {
                npc: &|_| true,
                doodad: &|_| false,
                transfer: &|_| false,
                gimmick: &|_| false,
                slave: &|_| false,
            },
        )
        .unwrap();
    let mut ready = Vec::new();
    let mut ids: Vec<_> = server
        .game_data
        .systems
        .instances
        .tower_defs
        .tower_defs
        .keys()
        .copied()
        .collect();
    ids.sort_unstable();
    for id in ids {
        let target =
            server.game_data.systems.instances.tower_defs.tower_defs[&id].target_npc_spawn_id;
        let Some(source) = spawns.tower_def_spawners(target).into_iter().next() else {
            eprintln!("event {id}: unavailable, missing target spawner {target} positions");
            continue;
        };
        let zone_id = source.position().zone_id;
        let Some(zone) = server.game_data.zones.zone_by_key(zone_id) else {
            eprintln!("event {id}: unavailable, target position has no 1.2 zone");
            continue;
        };
        let group = zone.group_id as u16;
        let key = EventKey {
            world_id: 0,
            id,
            zone_group_id: group,
        };
        let result = validate(&server, key, zone_id);
        eprintln!("event {id}: group {group}, zone {zone_id}, {result:?}");
        if result.is_ok() {
            ready.push(id);
        }
    }
    for id in [3, 5, 6, 12, 13, 15, 18] {
        assert!(ready.contains(&id), "event {id}: {ready:?}");
    }
}
