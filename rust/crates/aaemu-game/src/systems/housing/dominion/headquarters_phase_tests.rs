//! The original defender headquarters has two phase-only groups. Its legacy SiegePeriod
//! implementation must never make a character-owned spawn recurse indefinitely.

use std::sync::Arc;

use super::headquarters::DEFENSE_HQ_TEMPLATE;
use crate::data::world_templates::WorldSpawnPosition;
use crate::data::GameData;
use crate::server::GameServer;
use crate::systems::npcs::doodad::data::DoodadData;
use crate::systems::npcs::doodad::manager;
use crate::systems::npcs::doodad::spawner::{DoodadSpawner, DoodadSpawnerDef, SpawnerBase};
use crate::systems::npcs::doodad::templates::*;
use crate::test_support::{spawn_player, test_character, test_server_with, TestClient, TEST_ZONE};
use crate::world::{DoodadRef, WorldObject};

fn defender_data() -> DoodadData {
    let mut data = DoodadData::default();
    let mut template = DoodadTemplate {
        id: DEFENSE_HQ_TEMPLATE,
        ..Default::default()
    };
    for (group, kind, func, period, next) in [(8173, 1, 4, 0, 8238), (8238, 2, 5, 3, 8173)] {
        let group = DoodadFuncGroups {
            id: group,
            almighty: DEFENSE_HQ_TEMPLATE,
            group_kind_id: kind,
            ..Default::default()
        };
        data.all_func_groups_index
            .insert(group.id, data.all_func_groups.len());
        data.all_func_groups.push(group.clone());
        template.func_groups.push(group.clone());
        let function = FuncTemplate::SiegePeriod(DoodadFuncSiegePeriod {
            id: func,
            siege_period_id: period,
            next_phase: next,
            defense: true,
        });
        data.phase_func_templates
            .entry(function.func_type())
            .or_default()
            .insert(func, function);
        data.phase_funcs
            .entry(group.id)
            .or_default()
            .push(DoodadPhaseFunc {
                group_id: group.id,
                func_id: func,
                func_type: "DoodadFuncSiegePeriod".into(),
            });
    }
    data.template_order.push(DEFENSE_HQ_TEMPLATE);
    data.templates.insert(DEFENSE_HQ_TEMPLATE, template);
    data
}

fn server(data: DoodadData) -> GameServer {
    let mut game_data = GameData::empty();
    game_data.systems.npcs.doodad = data;
    let server = test_server_with(game_data);
    server.systems.npcs.doodads.tasks.set_manual(true);
    server
}

/// This is the same character-owned spawner path used by `/spawn doodad 3410`.
fn spawn_for_gm(server: &GameServer) -> (DoodadRef, TestClient) {
    let client = TestClient::new(server, 1, 1001);
    let player = spawn_player(
        server,
        &client,
        test_character(1, "HeadquartersGm", 100.0, 100.0),
        0x100,
    );
    let spawner = Arc::new(DoodadSpawner::new(
        DoodadSpawnerDef {
            base: SpawnerBase {
                unit_id: DEFENSE_HQ_TEMPLATE,
                position: WorldSpawnPosition {
                    world_id: 0,
                    zone_id: TEST_ZONE,
                    x: 100.0,
                    y: 103.0,
                    z: 100.0,
                    ..Default::default()
                },
                ..Default::default()
            },
            scale: 1.0,
            count: 1,
            related_ids: None,
            respawn_doodad_template_id: 0,
        },
        Some(0),
    ));
    let doodad = spawner
        .spawn_for_character(server, 0x500, 0, player.obj_id)
        .unwrap();
    let world = server.world.world(0).unwrap();
    assert!(Arc::ptr_eq(&world.doodad(doodad.obj_id).unwrap(), &doodad));
    assert!(!doodad.data().phase.deleted);
    assert_eq!(spawner.spawned().len(), 1);
    assert!(server
        .systems
        .npcs
        .doodads
        .tasks
        .pending_delays()
        .is_empty());
    (doodad, client)
}

#[tokio::test]
async fn original_defender_phase_only_graph_spawns_for_gm_without_stack_overflow() {
    let server = server(defender_data());
    assert!(server
        .game_data
        .systems
        .npcs
        .doodad
        .funcs_for_group(8173)
        .is_empty());
    assert!(server
        .game_data
        .systems
        .npcs
        .doodad
        .funcs_for_group(8238)
        .is_empty());
    let (doodad, _client) = spawn_for_gm(&server);
    assert_eq!(doodad.data().func_group_id, 8173);
    // Reinitializing this retained object is also bounded; there is no queued phase loop.
    let world = server.world.world(0).unwrap();
    manager::init_doodad(&server, &world, &doodad);
    assert_eq!(doodad.data().func_group_id, 8173);
    assert!(!doodad.data().phase.deleted);
    let gm = world.unit(doodad.data().owner_obj_id).unwrap();
    manager::do_change_phase(&server, &world, &doodad, Some(&gm), 8238);
    assert_eq!(doodad.data().func_group_id, 8238);
    manager::init_doodad(&server, &world, &doodad);
    assert_eq!(doodad.data().func_group_id, 8238);
    assert!(!doodad.data().phase.deleted);
}

#[tokio::test]
async fn separate_phase_entries_do_not_reuse_the_previous_recursion_path() {
    let mut data = defender_data();
    data.phase_func_templates.clear();
    data.phase_funcs.clear();
    let function = FuncTemplate::RatioChange(DoodadFuncRatioChange {
        id: 1,
        ratio: 0,
        next_phase: 8238,
    });
    data.phase_func_templates
        .entry(function.func_type())
        .or_default()
        .insert(1, function);
    data.phase_funcs.insert(
        8173,
        vec![DoodadPhaseFunc {
            group_id: 8173,
            func_id: 1,
            func_type: "DoodadFuncRatioChange".into(),
        }],
    );
    let server = server(data);
    server
        .systems
        .npcs
        .doodads
        .set_forced_phase_ratio(Some(9999));
    let (doodad, _client) = spawn_for_gm(&server);
    assert_eq!(doodad.data().func_group_id, 8173);
    server.systems.npcs.doodads.set_forced_phase_ratio(Some(0));
    let world = server.world.world(0).unwrap();
    let owner_id = doodad.data().owner_obj_id;
    let gm = world.unit(owner_id).unwrap();
    manager::do_change_phase(&server, &world, &doodad, Some(&gm), 8173);
    assert_eq!(doodad.data().func_group_id, 8238);
    assert!(!doodad.data().phase.deleted);
    assert!(world.object(doodad.obj_id).is_some());
}

#[tokio::test]
async fn unconditional_phase_only_cycle_is_bounded_without_a_siege_period_handler() {
    let mut data = defender_data();
    // Isolate the generic guard: unconditional ratio functions can create the same cycle
    // without relying on any unimplemented period matching or on a character caster.
    data.phase_func_templates.clear();
    data.phase_funcs.clear();
    for (group, id, next) in [(8173, 1, 8238), (8238, 2, 8173)] {
        let function = FuncTemplate::RatioChange(DoodadFuncRatioChange {
            id,
            ratio: 10000,
            next_phase: next,
        });
        data.phase_func_templates
            .entry(function.func_type())
            .or_default()
            .insert(id, function);
        data.phase_funcs
            .entry(group)
            .or_default()
            .push(DoodadPhaseFunc {
                group_id: group,
                func_id: id,
                func_type: "DoodadFuncRatioChange".into(),
            });
    }
    let server = server(data);
    let world = server.world.world(0).unwrap();
    let doodad = manager::create(&server, &world, 0x500, DEFENSE_HQ_TEMPLATE, None, true).unwrap();
    doodad.data().transform.set_xyz(100.0, 103.0, 100.0);
    manager::spawn(&server, &world, &doodad);
    manager::init_doodad(&server, &world, &doodad);
    assert_eq!(doodad.data().func_group_id, 8173);
    assert!(!doodad.data().phase.deleted);
    assert!(world.object(doodad.obj_id).is_some());
    assert!(server
        .systems
        .npcs
        .doodads
        .tasks
        .pending_delays()
        .is_empty());
}

#[tokio::test]
#[ignore = "requires original read-only 1.2 compact database"]
async fn real_defender_headquarters_gm_spawn_keeps_retained_object_without_recursion() {
    let conn = rusqlite::Connection::open_with_flags(
        std::env::var("AAEMU_COMPACT_DB").unwrap(),
        rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY,
    )
    .unwrap();
    let data = DoodadData::load_from(&conn).unwrap();
    for (group, id, period, next) in [(8173, 4, 0, 8238), (8238, 5, 3, 8173)] {
        assert!(data.funcs_for_group(group).is_empty());
        assert!(matches!(
            data.phase_func_template(id, "DoodadFuncSiegePeriod"),
            Some(FuncTemplate::SiegePeriod(function))
                if function.siege_period_id == period
                    && function.next_phase == next
                    && function.defense
        ));
    }
    let server = server(data);
    let (doodad, _client) = spawn_for_gm(&server);
    assert_eq!(doodad.data().func_group_id, 8173);
    assert!(super::runtime::is_live_object(
        &server,
        &WorldObject::from(&doodad)
    ));
    let world = server.world.world(0).unwrap();
    let gm = world.unit(doodad.data().owner_obj_id).unwrap();
    manager::do_change_phase(&server, &world, &doodad, Some(&gm), 8238);
    assert_eq!(doodad.data().func_group_id, 8238);
    assert!(super::runtime::is_live_object(
        &server,
        &WorldObject::from(&doodad)
    ));
}
