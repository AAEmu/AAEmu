//! End-to-end flow tests of the NPC / doodad subsystem on real data (gated by
//! `AAEMU_COMPACT_DB`): the real `main_world` spawn files are loaded into a world, a player
//! enters a populated region and sees exactly the NPCs / doodads around it, and using a doodad
//! broadcasts its phase change.

use std::collections::HashSet;
use std::path::Path;
use std::sync::Arc;
use std::time::Duration;

use sqlx::mysql::MySqlPoolOptions;

use crate::config::AppConfig;
use crate::data::client_files::ClientFileManager;
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::data::GameData;
use crate::opcodes::sc;
use crate::systems::npcs::spawn_manager::{self, SpawnManager, TemplateExists};
use crate::systems::DataLoadCtx;
use crate::test_support::{test_character, TestClient, TEST_ZONE};
use crate::world::{Player, WorldObject};
use crate::GameServer;

/// main_world is 31 x 31 cells of 1024 m in the 1.2 client; a synthetic template of that size
/// keeps every spawn position inside the region grid.
const CELLS: i32 = 32;

fn real_server(db: &str) -> Arc<GameServer> {
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .expect("compact db");
    let app_path = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../../AAEmu.Game");
    let files = ClientFileManager::from_sources(&[], &app_path);
    let mut data = GameData::empty();
    data.systems.npcs = super::Data::load(&DataLoadCtx {
        conn: &conn,
        app_path: &app_path,
        files: &files,
        language: "en_us",
    })
    .expect("npcs data");
    data.worlds = WorldTemplates::from_templates(
        vec![WorldTemplate::synthetic(
            0,
            "main_world",
            CELLS,
            CELLS,
            TEST_ZONE,
        )],
        Vec::new(),
    )
    .expect("world");
    let mut config = AppConfig {
        id: 1,
        ..AppConfig::default()
    };
    config.app_path = app_path;
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1;
    let pool = MySqlPoolOptions::new()
        .acquire_timeout(Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    let server = Arc::new(GameServer::new(config, pool, data));
    server.set_self();
    server
}

fn load_spawns(server: &Arc<GameServer>) -> Arc<SpawnManager> {
    let world = server.world.world(0).expect("main_world");
    let m = server.systems.npcs.spawns.get_or_create(&world);
    let gd = &server.game_data;
    let sd = &gd.systems.npcs.npc_spawners;
    let npc = |id| gd.systems.npcs.npc.exist(id);
    let doodad = |id| gd.systems.npcs.doodad.exist(id);
    let transfer = |id| sd.transfer_template_ids.contains(&id);
    let gimmick = |id| gd.systems.npcs.gimmick.exist(id);
    let slave = |id| sd.slave_template_ids.contains(&id);
    let exists = TemplateExists {
        npc: &npc,
        doodad: &doodad,
        transfer: &transfer,
        gimmick: &gimmick,
        slave: &slave,
    };
    let path = server.config.app_path.join("Data/Worlds/main_world");
    m.load_spawn_files(server, &path, &exists)
        .expect("spawn files");
    m
}

/// The bc object id at the start of a body.
fn bc(b: &[u8]) -> u32 {
    u32::from(b[0]) | u32::from(b[1]) << 8 | u32::from(b[2]) << 16
}

#[tokio::test]
#[ignore]
async fn real_spawns_player_sees_npcs_and_doodads_and_doodad_use_broadcasts() {
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let server = real_server(&db);
    let m = load_spawns(&server);
    let world = server.world.world(0).unwrap();

    // Doodads: SpawnManager.SpawnAll's doodad part (synchronously here).
    let spawned_doodads = m.spawn_doodads(&server);
    assert!(spawned_doodads > 40_000, "{spawned_doodads} doodads");

    // A player enters the Nuian starting area (first entries of npc_spawns.json, UnitId 502).
    let mut client = TestClient::new(&server, 1, 1);
    let mut c = test_character(1, "Tester", 23039.76, 11540.64);
    c.unit.transform.set_xyz(23039.76, 11540.64, 252.7);
    c.obj_id = 0x10_0001;
    let p = Player::new(c, client.conn.handle());
    server.world.try_add_character(&p);
    client.conn.active_char = Some(p.clone());
    world.spawn(&p, &server.view_ctx());

    // WorldManager.ActiveRegionTick: the spawners with the player in radius spawn their NPCs.
    spawn_manager::active_region_tick(&server);

    let player_obj = WorldObject::from(&p);
    let npcs: Vec<_> = world.around(&player_obj, |o| o.as_npc().cloned());
    assert!(!npcs.is_empty(), "no NPCs spawned around the player");
    assert!(
        npcs.iter().any(|n| n.data().unit.template_id == 502),
        "template 502 not around"
    );
    let doodads: Vec<_> = world.around(&player_obj, |o| o.as_doodad().cloned());
    assert!(!doodads.is_empty(), "no doodads around the player");

    // Every NPC around was announced with SCUnitStatePacket, every doodad with
    // SCDoodadCreated / SCDoodadsCreated.
    let frames = client.drain();
    let unit_states: HashSet<u32> = frames
        .iter()
        .filter(|(op, _)| *op == sc::SCUnitStatePacket)
        .map(|(_, b)| bc(b))
        .collect();
    for n in &npcs {
        assert!(
            unit_states.contains(&n.obj_id),
            "npc {} not shown",
            n.obj_id
        );
    }
    let doodads_announced: usize = frames
        .iter()
        .map(|(op, b)| match *op {
            o if o == sc::SCDoodadsCreatedPacket => usize::from(b[0]),
            o if o == sc::SCDoodadCreatedPacket => 1,
            _ => 0,
        })
        .sum();
    assert!(
        doodads_announced >= doodads.len(),
        "{doodads_announced} announced, {} around",
        doodads.len()
    );
    println!(
        "player sees {} npcs ({} unit states), {} doodads ({} announced)",
        npcs.len(),
        unit_states.len(),
        doodads.len(),
        doodads_announced
    );

    // Doodad use: the first doodad around whose current phase has a skill func that leads to
    // another phase.
    let dd = &server.game_data.systems.npcs.doodad;
    let target = doodads.iter().find_map(|d| {
        let g = d.data().func_group_id;
        dd.funcs_for_group(g)
            .iter()
            .find(|f| f.skill_id > 0 && f.next_phase > 0 && f.next_phase as u32 != g)
            .map(|f| (d.clone(), f.skill_id, g))
    });
    let Some((doodad, skill_id, old_group)) = target else {
        panic!("no usable doodad around the player");
    };
    client.drain();
    super::doodad::manager::use_doodad(&server, &world, &doodad, Some(&player_obj), skill_id, 0);
    let new_group = doodad.data().func_group_id;
    assert_ne!(new_group, old_group, "phase did not change");
    let changed: Vec<_> = client
        .drain()
        .into_iter()
        .filter(|(op, b)| *op == sc::SCDoodadPhaseChangedPacket && bc(b) == doodad.obj_id)
        .collect();
    assert!(
        !changed.is_empty(),
        "no SCDoodadPhaseChangedPacket broadcast"
    );
    println!(
        "doodad {} (template {}) used with skill {skill_id}: phase {old_group} -> {new_group}",
        doodad.obj_id,
        doodad.data().template_id
    );
}
