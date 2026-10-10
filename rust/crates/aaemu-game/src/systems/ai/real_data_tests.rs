//! Gated tests against the real client data / server data:
//! `AAEMU_GAME_PAK=<game_pak> cargo test -p aaemu-game real_ai -- --ignored`.
//!
//! * every `.bai` file of every world in the pak parses with the readers (the version / format
//!   errors the C# logs and skips are counted and must match the known bad files);
//! * zone geodata (instances with `zone/<key>/*.bai`) and main_world path blocks load through
//!   `AiGeoData` like `LoadZoneBaiFiles` / `WorldCell.LoadBaiFiles`, A* finds routes on them and
//!   `GetHeight` answers from the navmesh;
//! * every `Data/Path/*.path` file loads (`AiPathsManager.Load`) and parses for the
//!   `Simulation` too.

use std::collections::BTreeMap;
use std::path::PathBuf;
use std::sync::Arc;

use crate::data::client_files::ClientFileManager;
use crate::data::world_templates::WorldTemplates;
use crate::systems::skills::controllers::Vec3;

use super::geodata::astar::PathNode;
use super::geodata::bai::{
    parent_folder_name, zone_and_offsets_by_name, AreasMission, BaiFiles, NetMission,
    UsedAreaNames, VertexMission,
};
use super::geodata::manager::AiGeoData;
use super::geodata::ClientBaiFiles;

fn app_path() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../../AAEmu.Game")
}

fn pak_files() -> Option<Arc<ClientFileManager>> {
    let pak = std::env::var("AAEMU_GAME_PAK").ok()?;
    Some(Arc::new(ClientFileManager::from_sources(
        &[pak],
        &app_path(),
    )))
}

#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_ai_every_bai_file_parses() {
    let Some(files) = pak_files() else { return };
    let pak = files.sources()[0].game_pak().expect("pak");
    let names: Vec<String> = pak
        .file_names()
        .filter(|n| n.to_lowercase().ends_with(".bai"))
        .map(str::to_string)
        .collect();
    assert!(!names.is_empty());
    let used = UsedAreaNames::default();
    let mut by_kind: BTreeMap<&str, (usize, usize)> = BTreeMap::new();
    let mut failures = Vec::new();
    let (mut nodes, mut links, mut areas, mut obstacles) = (0usize, 0usize, 0usize, 0usize);
    for n in &names {
        let lower = n.to_lowercase();
        let file = lower.rsplit('/').next().unwrap_or("");
        let folder = parent_folder_name(n);
        let (zone_key, px, py) = zone_and_offsets_by_name(&folder);
        let off = Vec3::new(px as f32 * 256.0, py as f32 * 256.0, 0.0);
        let data = files.read_file(n).expect("read");
        let (kind, res): (&str, Result<(), String>) =
            if file.starts_with("netmission") || file.starts_with("hidemission") {
                let r = NetMission::read(&data, zone_key, off).map(|m| {
                    nodes += m.nodes.len();
                    links += m.links.len();
                });
                ("net", r.map_err(|e| e.0))
            } else if file.starts_with("areasmission") {
                if data.len() <= 20 {
                    ("areas-skipped", Ok(()))
                } else {
                    let r = AreasMission::read(&data, zone_key, off, &used).map(|m| {
                        areas += m.forbidden_areas.len()
                            + m.forbidden_boundaries.len()
                            + m.designer_forbidden_areas.len();
                    });
                    ("areas", r.map_err(|e| e.0))
                }
            } else if file.starts_with("vertsmission") {
                let r = VertexMission::read(&data, zone_key, off)
                    .map(|m| obstacles += m.obstacles.len());
                ("verts", r.map_err(|e| e.0))
            } else {
                ("other", Ok(()))
            };
        let e = by_kind.entry(kind).or_default();
        e.0 += 1;
        if let Ok(dump) = std::env::var("AAEMU_BAI_DUMP") {
            // keep the area files (failing and parsing ones) for the C# differential harness
            if kind == "areas" && (e.0.is_multiple_of(50) || res.is_err()) {
                let out = PathBuf::from(dump).join(n.replace('/', "__"));
                let _ = std::fs::write(out, &data);
            }
        }
        if let Err(msg) = res {
            e.1 += 1;
            failures.push(format!("{n}: {msg}"));
        }
    }
    println!(
        "{} bai files: {by_kind:?}; {nodes} nodes, {links} links, {areas} forbidden shapes, {obstacles} obstacles",
        names.len()
    );
    for f in failures.iter().take(20) {
        println!("  {f}");
    }
    assert!(nodes > 0 && links > 0);
    assert!(
        failures.len() * 100 <= names.len(),
        "{} of {} files failed",
        failures.len(),
        names.len()
    );
}

#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_ai_geodata_loads_and_routes() {
    let Some(files) = pak_files() else { return };
    let worlds = WorldTemplates::load(&files, &app_path()).expect("world templates");
    let bai: Arc<dyn BaiFiles + Send + Sync> = Arc::new(ClientBaiFiles(files.clone()));
    let used = Arc::new(UsedAreaNames::default());
    let mut zone_worlds = 0;
    let mut routed = 0;
    for t in worlds.templates() {
        let g = AiGeoData::new(t, true, bai.clone(), used.clone(), None);
        let zones = g.zone_bai();
        if zones.is_empty() {
            continue;
        }
        zone_worlds += 1;
        // A route between two navmesh nodes of the first zone
        let (_, l) = &zones[0];
        let Some(net) = l.net_mission_readers.iter().find(|n| n.nodes.len() > 10) else {
            continue;
        };
        let a = net.nodes[0].pos;
        let b = net.nodes[net.nodes.len() / 2].pos;
        assert_eq!(g.get_height(a), a.z, "{}: navmesh height", t.name);
        let mut pn = PathNode {
            zone_key: zones[0].0,
            ..PathNode::default()
        };
        let path = pn.find_path(&g, a, b).expect("find path");
        if path.len() >= 2 {
            routed += 1;
            assert_eq!(*path.first().unwrap(), a);
            assert_eq!(*path.last().unwrap(), b);
        }
    }
    println!("{zone_worlds} worlds with zone geodata, {routed} routed");
    assert!(zone_worlds > 0);
    assert!(routed > 0);

    // main_world: the path blocks of a cell with navmesh
    let main = worlds
        .get_world_template_by_name("main_world")
        .expect("main_world");
    let g = AiGeoData::new(main, true, bai, used, None);
    assert!(g.zone_bai().is_empty(), "main_world uses path blocks");
    let mut loaded = 0;
    let mut with_nodes = 0;
    'cells: for cx in 10..main.cell_x.min(24) {
        for cy in 10..main.cell_y.min(24) {
            let grid = g.cell_bai(cx, cy).expect("cell");
            loaded += 1;
            let nodes: usize = grid
                .iter()
                .flatten()
                .flatten()
                .map(|l| {
                    l.net_mission_readers
                        .iter()
                        .map(|n| n.nodes.len())
                        .sum::<usize>()
                })
                .sum();
            if nodes > 0 {
                with_nodes += 1;
                if with_nodes >= 3 {
                    break 'cells;
                }
            }
        }
    }
    println!("main_world: {loaded} cells loaded, {with_nodes} with navmesh nodes");
    assert!(with_nodes > 0);
}

#[test]
fn real_ai_path_files_load() {
    let paths = super::path_handler::AiPathsManager::default();
    let folder = app_path().join("Data").join("Path");
    if !folder.is_dir() {
        return;
    }
    paths.set_folder(&folder);
    let loaded = paths.load();
    let files = std::fs::read_dir(&folder)
        .unwrap()
        .filter_map(Result::ok)
        .filter(|e| e.path().extension().is_some_and(|x| x == "path"))
        .count();
    assert!(loaded > 0);
    assert_eq!(loaded, files, "every path file has points");
    // the Simulation reads the same files with ExtractValue
    for e in std::fs::read_dir(&folder).unwrap().filter_map(Result::ok) {
        let text = std::fs::read_to_string(e.path()).unwrap();
        for line in text.lines() {
            for i in 1..=3 {
                assert!(
                    super::simulation::extract_value(line, i).is_ok(),
                    "{}: {line}",
                    e.path().display()
                );
            }
        }
    }
}

/// Differential check of `AreasMissionReader` against the C# reader run on the same files
/// (`AAEMU_BAI_DUMP` = the files dumped by [`real_ai_every_bai_file_parses`], `AAEMU_BAI_EXPECTED`
/// = the C# harness output: name, `OK <counts>` / `ERR <message>`), processed in ordinal name
/// order with one shared `UsedAreaNames` like the harness.
#[test]
#[ignore = "needs AAEMU_BAI_DUMP / AAEMU_BAI_EXPECTED"]
fn real_ai_areas_missions_match_csharp() {
    let (Ok(dir), Ok(expected)) = (
        std::env::var("AAEMU_BAI_DUMP"),
        std::env::var("AAEMU_BAI_EXPECTED"),
    ) else {
        return;
    };
    let expected = std::fs::read_to_string(expected).unwrap();
    let mut want: BTreeMap<String, String> = BTreeMap::new();
    for line in expected.lines() {
        if let Some((n, r)) = line.split_once('\t') {
            let r = if r.starts_with("ERR") {
                "ERR".to_string()
            } else {
                r.to_string()
            };
            want.insert(n.to_string(), r);
        }
    }
    let mut names: Vec<PathBuf> = std::fs::read_dir(&dir)
        .unwrap()
        .filter_map(Result::ok)
        .map(|e| e.path())
        .collect();
    names.sort();
    let used = UsedAreaNames::default();
    let mut mismatches = Vec::new();
    for p in names {
        let name = p.file_name().unwrap().to_str().unwrap().replace("__", "/");
        let folder = parent_folder_name(&name);
        let (zone, px, py) = zone_and_offsets_by_name(&folder);
        let data = std::fs::read(&p).unwrap();
        let got = match AreasMission::read(
            &data,
            zone,
            Vec3::new(px as f32 * 256.0, py as f32 * 256.0, 0.0),
            &used,
        ) {
            Ok(m) => format!(
                "OK {} {} {} {} {} {}",
                m.forbidden_areas.len(),
                m.navigation_modifiers.len(),
                m.designer_forbidden_areas.len(),
                m.forbidden_boundaries.len(),
                m.extra_link_costs.len(),
                m.designer_paths.len()
            ),
            Err(_) => "ERR".into(),
        };
        if want.get(&name) != Some(&got) {
            mismatches.push(format!("{name}: want {:?} got {got}", want.get(&name)));
        }
    }
    assert!(mismatches.is_empty(), "{}", mismatches.join("\n"));
}

/// Every cell `object.dat` of every world parses (`ObjectsFile.ReadFile`) and its water volumes
/// become water areas (`WaterBodies.AddFromCellData`). With `AAEMU_OBJ_DUMP` the files are
/// written there (`<world>__<cx>__<cy>__<ocean bits>.dat`) for the C# harness; with
/// `AAEMU_OBJ_EXPECTED` (the harness output) every file's prefab summary and water areas must
/// match the C# bit for bit.
#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_ai_object_dat_water_bodies() {
    use super::physics::objects_file::{ObjectsFile, Prefab};
    use super::physics::water::WaterBodies;
    let Some(files) = pak_files() else { return };
    let worlds = WorldTemplates::load(&files, &app_path()).expect("world templates");
    let dump = std::env::var("AAEMU_OBJ_DUMP").ok();
    let mut lines: BTreeMap<String, Vec<String>> = BTreeMap::new();
    let (mut total, mut failed, mut water_prefabs, mut areas) = (0, 0, 0, 0);
    for t in worlds.templates() {
        for cx in 0..t.cell_x {
            for cy in 0..t.cell_y {
                let f = format!(
                    "game/worlds/{}/cells/{cx:03}_{cy:03}/client/object.dat",
                    t.name
                );
                let Some(bytes) = files.read_file(&f) else {
                    continue;
                };
                total += 1;
                let key = format!(
                    "{}__{cx}__{cy}__{:08X}.dat",
                    t.name,
                    t.ocean_level.to_bits()
                );
                if let Some(d) = &dump {
                    std::fs::write(PathBuf::from(d).join(&key), &bytes).expect("dump");
                }
                let objects = ObjectsFile::read(&bytes);
                if !objects.ok {
                    failed += 1;
                }
                let waters = objects
                    .prefabs
                    .iter()
                    .filter(|p| matches!(p, Prefab::Water(_)))
                    .count();
                water_prefabs += waters;
                let mut out = vec![format!(
                    "OBJ\t{}\t{}\t{}",
                    objects.ok,
                    objects.prefabs.len(),
                    waters
                )];
                let w = WaterBodies::new(t.ocean_level);
                w.add_from_cell_data(None, t.ocean_level, cx, cy, &objects);
                for a in w.areas_snapshot() {
                    areas += 1;
                    let b = a.bounding_box;
                    out.push(format!(
                        "AREA\t{}\t{:?}\t{}\t{:08X} {:08X} {:08X} {:08X}\t{:08X}\t{:08X}\t{:08X}\t{:08X}\t{:08X} {:08X}\t{:08X}",
                        a.name,
                        a.area_type,
                        a.points.len(),
                        b.x.to_bits(),
                        b.y.to_bits(),
                        b.width.to_bits(),
                        b.height.to_bits(),
                        a.highest.to_bits(),
                        a.lowest.to_bits(),
                        a.depth.to_bits(),
                        a.flow_speed_signed.to_bits(),
                        a.flow_axis.0.to_bits(),
                        a.flow_axis.1.to_bits(),
                        a.river_width.to_bits()
                    ));
                }
                lines.insert(key, out);
            }
        }
    }
    println!("{total} object.dat files, {failed} not fully read, {water_prefabs} water volumes, {areas} water areas");
    assert!(total > 0 && water_prefabs > 0 && areas > 0);
    if let Ok(expected) = std::env::var("AAEMU_OBJ_EXPECTED") {
        let text = std::fs::read_to_string(expected).expect("expected file");
        let mut want: BTreeMap<String, Vec<String>> = BTreeMap::new();
        for l in text.lines() {
            if let Some((k, rest)) = l.split_once('\t') {
                want.entry(k.to_string())
                    .or_default()
                    .push(rest.to_string());
            }
        }
        let mut mismatches = Vec::new();
        for (k, got) in &lines {
            match want.get(k) {
                Some(w) if w == got => {}
                Some(w) => mismatches.push(format!("{k}:\n  want {w:?}\n  got  {got:?}")),
                None => mismatches.push(format!("{k}: missing in the C# output")),
            }
        }
        assert!(
            mismatches.is_empty(),
            "{} mismatches:\n{}",
            mismatches.len(),
            mismatches
                .iter()
                .take(10)
                .cloned()
                .collect::<Vec<_>>()
                .join("\n")
        );
    }
}
