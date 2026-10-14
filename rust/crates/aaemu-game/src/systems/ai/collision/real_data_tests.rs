//! Gated tests against the real 1.2 client data:
//! `AAEMU_GAME_PAK=D:\AA1.2\game_pak cargo test -p aaemu-game real_collision -- --ignored --nocapture`.
//!
//! * the sector index order of `brush.dat` / `vegetation.dat` ([`super::cells::SECTOR_X_MAJOR`])
//!   is the one that puts trees on the terrain;
//! * a few main_world cells load (counts, models, timing, memory of the physics blobs);
//! * ground heights from the physics world (rays 0x101) match the NPC spawn heights of
//!   `Data/Worlds/main_world/npc_spawns.json` and survive unit-state serialization even where
//!   the approximate navigation height differs;
//! * rays hit brushes / vegetation (static entities) above the terrain.

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;
use std::time::Instant;

use aaemu_physics::math::Vec3;

use crate::data::client_files::ClientFileManager;
use crate::data::world_templates::WorldTemplates;
use crate::systems::npcs::heightmap::{HeightmapConfig, HeightmapWorlds, CELL_HMAP_RESOLUTION};

use super::cells;
use super::models::ModelCache;
use super::query::{mask, ray_flags};
use super::terrain::{terrain_elevation, TerrainSource};
use super::world::{prepare_cell, Owner, PhysWorld};
use super::{Files, HeightmapTerrain};

fn app_path() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app")
}

struct Env {
    files: Arc<ClientFileManager>,
    hm: Arc<HeightmapWorlds>,
    template_id: u32,
    cells: (i32, i32),
    ocean: f32,
}

fn env() -> Option<Env> {
    let pak = std::env::var("AAEMU_GAME_PAK").ok()?;
    let files = Arc::new(ClientFileManager::from_sources(&[pak], &app_path()));
    let worlds = WorldTemplates::load(&files, &app_path()).expect("world templates");
    let t = worlds
        .templates()
        .iter()
        .find(|t| t.name == "main_world")
        .expect("main_world")
        .clone();
    let hm = Arc::new(HeightmapWorlds::new(
        files.clone(),
        &worlds,
        HeightmapConfig {
            heightmaps_enable: true,
            geo_data_mode: false,
            pre_load_terrain: false,
        },
    ));
    Some(Env {
        files,
        hm,
        template_id: t.id,
        cells: (t.cell_x, t.cell_y),
        ocean: t.ocean_level,
    })
}

fn terrain(e: &Env) -> Arc<HeightmapTerrain> {
    Arc::new(HeightmapTerrain {
        worlds: e.hm.clone(),
        template_id: e.template_id,
        size: (
            e.cells.0 * CELL_HMAP_RESOLUTION,
            e.cells.1 * CELL_HMAP_RESOLUTION,
        ),
    })
}

/// NPC spawn positions of main_world (`"X": …, "Y": …, "Z": …` in the lenient JSON).
fn npc_spawns() -> Vec<[f32; 3]> {
    let p = app_path().join("Data/Worlds/main_world/npc_spawns.json");
    let Ok(text) = std::fs::read_to_string(p) else {
        return Vec::new();
    };
    let re = regex::Regex::new(
        r#""X"\s*:\s*(-?[0-9.]+)\s*,\s*"Y"\s*:\s*(-?[0-9.]+)\s*,\s*"Z"\s*:\s*(-?[0-9.]+)"#,
    )
    .unwrap();
    re.captures_iter(&text)
        .filter_map(|c| Some([c[1].parse().ok()?, c[2].parse().ok()?, c[3].parse().ok()?]))
        .collect()
}

/// The `(sector, local position)` of every vegetation record (raw, to test both orders).
fn raw_vegetation(d: &[u8]) -> Vec<(usize, Vec3)> {
    let u = |o: usize| u32::from_le_bytes([d[o], d[o + 1], d[o + 2], d[o + 3]]);
    let f = |o: usize| f32::from_bits(u(o));
    let mut out = Vec::new();
    for i in 0..16 {
        let (off, size) = (u(4 + 4 * i) as usize, u(68 + 4 * i) as usize);
        for k in 0..size / 64 {
            let r = off + k * 64;
            out.push((i, Vec3::new(f(r + 39), f(r + 43), f(r + 47))));
        }
    }
    out
}

#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_collision_sector_order() {
    let Some(e) = env() else { return };
    let t = terrain(&e);
    let (mut good_x_major, mut good_y_major, mut total) = (0usize, 0usize, 0usize);
    for cell in [(9, 14), (10, 14), (16, 20), (20, 16), (12, 22)] {
        let path = format!(
            "game/worlds/main_world/cells/{:03}_{:03}/client/vegetation.dat",
            cell.0, cell.1
        );
        let Some(d) = e.files.read_file(&path) else {
            continue;
        };
        for (i, p) in raw_vegetation(&d) {
            let base = (cell.0 as f32 * 1024.0, cell.1 as f32 * 1024.0);
            let xm = ((i / 4) as f32 * 256.0, (i % 4) as f32 * 256.0);
            let ym = ((i % 4) as f32 * 256.0, (i / 4) as f32 * 256.0);
            let hx = terrain_elevation(t.as_ref(), base.0 + xm.0 + p.x, base.1 + xm.1 + p.y);
            let hy = terrain_elevation(t.as_ref(), base.0 + ym.0 + p.x, base.1 + ym.1 + p.y);
            total += 1;
            good_x_major += usize::from((hx - p.z).abs() < 2.0);
            good_y_major += usize::from((hy - p.z).abs() < 2.0);
        }
    }
    println!(
        "vegetation on terrain: x-major {good_x_major}/{total}, y-major {good_y_major}/{total}"
    );
    assert!(total > 1000, "{total}");
    let chosen = if cells::SECTOR_X_MAJOR {
        good_x_major
    } else {
        good_y_major
    };
    assert!(chosen * 10 > total * 8, "{chosen}/{total}");
    assert!(chosen >= good_x_major.max(good_y_major));
}

#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_collision_cells_load_and_answer() {
    let Some(e) = env() else { return };
    let files = Files::Client(e.files.clone());
    let models = ModelCache::new(Arc::new(files.clone()));
    let xml = e
        .files
        .read_file("game/worlds/main_world/vegetation.xml")
        .map(|b| crate::data::client_files::decode_text(&b))
        .unwrap_or_default();
    let groups: HashMap<u16, cells::VegetationGroup> = cells::read_vegetation_groups(&xml)
        .into_iter()
        .map(|g| (g.id, g))
        .collect();
    println!("vegetation groups {}", groups.len());
    assert!(groups.len() > 1000);
    {
        // why vegetation records are skipped
        let d = e
            .files
            .read_file("game/worlds/main_world/cells/013_014/client/vegetation.dat")
            .unwrap();
        let recs = cells::read_vegetation(&d).unwrap();
        let (mut no_group, mut no_model, mut no_file, mut no_coll, mut ok) = (0, 0, 0, 0, 0);
        let mut no_coll_models: HashMap<String, usize> = HashMap::new();
        for r in &recs {
            match groups.get(&r.group) {
                None => no_group += 1,
                Some(g) if g.model.is_empty() => no_model += 1,
                Some(g) => match models.get(&g.model) {
                    None => {
                        no_file += 1;
                        *no_coll_models.entry(g.model.clone()).or_default() += 1;
                    }
                    Some(m) if !m.collides() => no_coll += 1,
                    Some(_) => ok += 1,
                },
            }
        }
        let mut top: Vec<_> = no_coll_models.into_iter().collect();
        top.sort_by_key(|t| std::cmp::Reverse(t.1));
        top.truncate(5);
        println!("veg 013_014: no group {no_group}, no model {no_model}, no file/physics {no_file}, no default slot {no_coll}, ok {ok}; top {top:?}");
    }
    let spawns = npc_spawns();
    let worlds = WorldTemplates::load(&e.files, &app_path()).expect("world templates");
    let nav = super::super::geodata::manager::AiGeoData::new(
        worlds
            .get_world_template_by_id(e.template_id)
            .expect("main_world"),
        true,
        Arc::new(super::super::geodata::ClientBaiFiles(e.files.clone())),
        Arc::new(super::super::geodata::bai::UsedAreaNames::default()),
        None,
    );
    let mut prefix = aaemu_commons::PacketStream::new();
    prefix
        .write_bc(1)
        .write_str("")
        .write_u8(1) // NPC
        .write_bc(1)
        .write_u32(1)
        .write_u32(0)
        .write_u8(0)
        .write_str("");
    let position_offset = prefix.len();
    // the cells with the most NPC spawns
    let mut per_cell: HashMap<(i32, i32), usize> = HashMap::new();
    for s in &spawns {
        *per_cell
            .entry(((s[0] / 1024.0) as i32, (s[1] / 1024.0) as i32))
            .or_default() += 1;
    }
    let mut cells_sorted: Vec<_> = per_cell.into_iter().collect();
    cells_sorted.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
    let chosen: Vec<(i32, i32)> = cells_sorted.iter().take(4).map(|c| c.0).collect();
    println!("cells {chosen:?}");
    let pw = PhysWorld::new(
        1,
        e.template_id,
        "main_world",
        e.cells.0,
        e.cells.1,
        e.ocean,
        Some(terrain(&e) as Arc<dyn TerrainSource>),
    );
    let t0 = Instant::now();
    for &c in &chosen {
        let tc = Instant::now();
        let content = prepare_cell(&files, &models, "main_world", &groups, c, true, true);
        println!(
            "cell {c:?}: {} brush records, {} vegetation records, {} instances, {} skipped, prepare {:?}",
            content.brush_records,
            content.vegetation_records,
            content.instances.len(),
            content.skipped,
            tc.elapsed()
        );
        pw.end_load(Some(content), c);
    }
    let st = pw.lock();
    println!(
        "loaded {} cells in {:?}: {} brushes, {} vegetation; models {} (loaded {}, missing {}, no physics {}, failed {}), blobs {} KB, model load {} ms",
        st.cells.len(),
        t0.elapsed(),
        st.stats.brushes,
        st.stats.vegetation,
        models.len(),
        models.stats.loaded.load(std::sync::atomic::Ordering::Relaxed),
        models.stats.missing.load(std::sync::atomic::Ordering::Relaxed),
        models.stats.no_physics.load(std::sync::atomic::Ordering::Relaxed),
        models.stats.failed.load(std::sync::atomic::Ordering::Relaxed),
        models.blob_bytes() / 1024,
        models.stats.load_micros.load(std::sync::atomic::Ordering::Relaxed) / 1000,
    );
    assert!(st.stats.brushes + st.stats.vegetation > 100);
    assert_eq!(
        models
            .stats
            .failed
            .load(std::sync::atomic::Ordering::Relaxed),
        0
    );
    drop(st);

    // ground under the NPC spawns of the loaded cells
    let t = terrain(&e);
    let (mut n, mut near, mut on_static, mut terrain_near) = (0, 0, 0, 0);
    let mut worst = Vec::new();
    let (mut nav_above, mut nav_below) = (0, 0);
    let mut nav_worst = Vec::new();
    let tq = Instant::now();
    for s in spawns
        .iter()
        .filter(|s| chosen.contains(&((s[0] / 1024.0) as i32, (s[1] / 1024.0) as i32)))
    {
        let mut st = pw.lock();
        let hit = st.raycast(
            Vec3::new(s[0], s[1], s[2] + 2.0),
            Vec3::new(s[0], s[1], s[2] - 100.0),
            mask::STATIC_TERRAIN,
            ray_flags::DEFAULT,
            &[],
        );
        drop(st);
        n += 1;
        let te = terrain_elevation(t.as_ref(), s[0], s[1]);
        if (te - s[2]).abs() < 1.0 {
            terrain_near += 1;
        }
        if let Some(h) = hit {
            if (h.pos.z - s[2]).abs() < 1.0 {
                near += 1;
                let mut npc = crate::models::npc::Npc::new(1);
                npc.has_ai = true;
                npc.spawner_z = Some(s[2]);
                npc.unit.transform.zone_id =
                    worlds.get_zone_id(worlds.get_world_template_by_id(e.template_id), s[0], s[1]);
                npc.unit
                    .transform
                    .set_position(s[0], s[1], h.pos.z, 0.0, 0.0, 0.0);
                let before = npc.unit.transform;
                let mut position = aaemu_commons::PacketStream::new();
                position.write_position(s[0], s[1], h.pos.z);
                let state = crate::world::unit_state::npc_unit_state(1, &mut npc, "", 0);
                assert_eq!(npc.unit.transform, before);
                assert_eq!(
                    &state.body[position_offset..position_offset + position.len()],
                    position.as_bytes(),
                    "collision floor at {s:?}"
                );
                let nav_z = nav.get_height(crate::systems::skills::controllers::Vec3::new(
                    s[0], s[1], h.pos.z,
                ));
                if nav_z != 0.0 {
                    let delta = nav_z - h.pos.z;
                    nav_above += usize::from(delta > 1.0);
                    nav_below += usize::from(delta < -1.0);
                    nav_worst.push((*s, h.pos.z, nav_z, delta));
                }
            } else if worst.len() < 5 {
                worst.push((*s, h.pos.z, h.owner));
            }
            if !matches!(h.owner, Some(Owner::Terrain)) {
                on_static += 1;
            }
        }
    }
    println!(
        "{n} spawns: ground within 1 m {near}, terrain elevation within 1 m {terrain_near}, on static geometry {on_static}; {:?} per ray; e.g. {worst:?}",
        tq.elapsed() / n.max(1) as u32
    );
    nav_worst.sort_by(|a, b| b.3.total_cmp(&a.3));
    nav_worst.truncate(5);
    println!(
        "grounded spawns vs navmesh: {nav_above} over 1 m above ground, {nav_below} over 1 m below; worst {nav_worst:?}"
    );
    assert!(n > 50);
    assert!(near * 10 >= n * 9, "{near}/{n}");
    assert!(near >= terrain_near);
    assert!(
        nav_above > 0,
        "fixture covers a navmesh height above the collision floor"
    );
    // some spawns stand on brushes (bridges, floors, docks)
    assert!(on_static > 0);
}

/// The doodad model references of the DB resolve to colliders (cgf / vegetation / prefab).
#[test]
#[ignore = "needs AAEMU_GAME_PAK and AAEMU_COMPACT_DB"]
fn real_collision_doodad_models_resolve() {
    let Some(e) = env() else { return };
    let Ok(db) = std::env::var("AAEMU_COMPACT_DB") else {
        return;
    };
    let conn =
        rusqlite::Connection::open_with_flags(db, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY)
            .expect("db");
    let data = super::data::CollisionData::load(&conn).expect("collision data");
    println!(
        "{} func group models, {} actor models",
        data.func_group_models.len(),
        data.living.len()
    );
    assert!(data.func_group_models.len() > 10_000);
    let files = Files::Client(e.files.clone());
    let models = ModelCache::new(Arc::new(files.clone()));
    let mut prefab_xml = HashMap::new();
    let mut by_kind: HashMap<&str, (usize, usize)> = HashMap::new();
    let mut refs: Vec<&String> = data.func_group_models.values().collect();
    refs.sort();
    refs.dedup();
    for m in refs.iter().take(1500) {
        let r = super::objects::parse_model_ref(m);
        let kind = match &r {
            super::objects::ModelRef::Brush(_) => "cgf",
            super::objects::ModelRef::Vegetation(_) => "vegetation",
            super::objects::ModelRef::Prefab { .. } => "prefab",
            super::objects::ModelRef::Cga(_) => "cga",
            super::objects::ModelRef::Dummy => "dummy",
        };
        let ok = super::objects::resolve_shape(&files, &models, &mut prefab_xml, &r).is_some();
        let c = by_kind.entry(kind).or_default();
        c.0 += 1;
        c.1 += usize::from(ok);
    }
    println!("resolved (refs, with physics): {by_kind:?}");
    assert!(by_kind.get("cgf").is_some_and(|c| c.1 * 2 > c.0));
    assert!(by_kind.get("prefab").is_some_and(|c| c.1 > 0));
    // list the models whose physics fails to load
    for m in refs.iter().take(1500) {
        if let super::objects::ModelRef::Brush(p)
        | super::objects::ModelRef::Cga(p)
        | super::objects::ModelRef::Vegetation(p) = super::objects::parse_model_ref(m)
        {
            if let Some(b) = e.files.read_file(&p) {
                if let Err(err) = super::models::load_model(&p, &b) {
                    println!("load failure: {err}");
                }
            }
        }
    }
    assert_eq!(
        models
            .stats
            .failed
            .load(std::sync::atomic::Ordering::Relaxed),
        0
    );
}

/// Walking height resolution for the six Mistfiend (NPC 4227) spawns of Blackbreath Cave
/// (`Data/Worlds/main_world/npc_spawns.json`): their authored Z sits below the heightmap
/// surface, so the production `systems::ai::npc::reference_height_in` must never lift a
/// walker above its level plus the 2 m step-up probe, and must prefer a collision floor when
/// the probe finds one. Prints independent terrain / probe / production diagnostics.
#[tokio::test]
#[ignore = "needs AAEMU_GAME_PAK"]
async fn real_collision_mistfiend_cave_walking_height() {
    use crate::systems::npcs::json;
    let Some(e) = env() else { return };
    let text = std::fs::read_to_string(app_path().join("Data/Worlds/main_world/npc_spawns.json"))
        .expect("npc_spawns.json");
    let doc = json::parse(&text).expect("spawns parse");
    let mut spawns = Vec::new();
    for entry in json::array(&doc).expect("spawn array") {
        let Some(o) = entry.as_object() else { continue };
        if json::u32_or(o, "UnitId", 0).unwrap_or(0) != 4227 {
            continue;
        }
        let Some(p) = json::prop(o, "Position").and_then(|p| p.as_object()) else {
            continue;
        };
        spawns.push([
            json::f32_or(p, "X", 0.0).unwrap_or(0.0),
            json::f32_or(p, "Y", 0.0).unwrap_or(0.0),
            json::f32_or(p, "Z", 0.0).unwrap_or(0.0),
        ]);
    }
    assert_eq!(spawns.len(), 6, "NPC 4227 spawns: {spawns:?}");

    // A real server: main_world as instance 0 (`WorldManager.CreateStaticInstances`) with the
    // client heightmaps and the collision service (lazy pool, never used).
    let worlds = WorldTemplates::load(&e.files, &app_path()).expect("world templates");
    let mut config = crate::config::AppConfig {
        id: 1,
        ..Default::default()
    };
    config.connections.mysql_provider.host = "127.0.0.1".into();
    config.connections.mysql_provider.port = 1;
    let db = sqlx::mysql::MySqlPoolOptions::new()
        .acquire_timeout(std::time::Duration::from_millis(200))
        .connect_lazy_with(config.connections.mysql_provider.connect_options());
    let mut data = crate::data::GameData::empty();
    data.worlds = worlds;
    let server = crate::server::GameServer::new(config, db, data);
    server.systems.npcs.heightmaps.install(e.hm.clone());

    let mut above = 0;
    for s in &spawns {
        let (x, y, z) = (s[0], s[1], s[2]);
        let worlds = &server.game_data.worlds;
        let zone = worlds.get_zone_id(worlds.get_world_template_by_name("main_world"), x, y);
        let ai = crate::systems::npcs::heightmap::ReferenceHeightAi {
            can_fly: false,
            hold_or_idle: false,
            spawner_z: Some(z),
            world_z: z,
        };
        let resolved =
            crate::systems::ai::npc::reference_height_in(&server, 0, Some(ai), x, y, z, zone);
        let probe = super::ground_height_probe(
            &server,
            0,
            x,
            y,
            z,
            super::GROUND_PROBE_UP,
            super::GROUND_PROBE_DOWN,
        );
        let terrain = super::terrain_height(&server, 0, x, y);
        above += usize::from(terrain.is_some_and(|t| t > z + super::GROUND_PROBE_UP));
        println!(
            "Mistfiend ({x:.2}, {y:.2}, {z:.3}): probe {probe:?}, terrain {terrain:?}, resolved {resolved:.3}"
        );
        // A probe hit is the preferred collision floor (a hit at Z = 0 included).
        if let Some(h) = probe.flatten() {
            assert!(
                (resolved - h).abs() < 1e-3,
                "walking must stand on the collision floor {h:.3} at ({x:.2}, {y:.2}, {z:.3}), got {resolved:.3}"
            );
        }
        assert!(
            resolved <= z + super::GROUND_PROBE_UP + 1e-3,
            "walking must not elevate ({x:.2}, {y:.2}, {z:.3}) to {resolved:.3}"
        );
    }
    println!("{above}/6 columns have the heightmap surface above the step range");
    assert!(above > 0, "the cave fixture must cover a surface above");
}
