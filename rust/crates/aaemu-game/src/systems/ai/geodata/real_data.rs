//! Gated navmesh tests on real `.bai` data:
//!
//! * `AAEMU_BAI_SAMPLES=D:\aa-re\zone\agent_geom\data` — the extracted sample sectors
//!   (`bai\{036_056,038_058,039_059}` from the 10.x pak, `bai12\038_058` from the 1.2 pak);
//! * `AAEMU_GAME_PAK=<1.2 game_pak>` — whole main_world cells through `AiGeoData`.
//!
//! `cargo test -p aaemu-game real_nav -- --ignored --nocapture`

use std::collections::HashMap;
use std::path::PathBuf;
use std::sync::Arc;

use crate::data::client_files::ClientFileManager;
use crate::data::world_templates::{WorldTemplate, WorldTemplates};
use crate::systems::skills::controllers::Vec3;

use super::astar::PathNode;
use super::bai::{AreasMission, BaiFiles, NetMission, UsedAreaNames, VertexMission};
use super::graph::DEFAULT_AGENT_RADIUS;
use super::manager::AiGeoData;
use super::ClientBaiFiles;

fn samples() -> Option<PathBuf> {
    let p = PathBuf::from(std::env::var("AAEMU_BAI_SAMPLES").ok()?);
    p.is_dir().then_some(p)
}

/// The sample folders as `game/worlds/main_world/paths/<folder>/<file>`.
struct SampleFiles(HashMap<String, Vec<u8>>);

impl SampleFiles {
    fn load(dir: &std::path::Path) -> Self {
        let mut m = HashMap::new();
        for sector in std::fs::read_dir(dir).unwrap().filter_map(Result::ok) {
            let folder = sector.file_name().to_string_lossy().to_string();
            for f in std::fs::read_dir(sector.path())
                .unwrap()
                .filter_map(Result::ok)
            {
                let name = f.file_name().to_string_lossy().to_lowercase();
                m.insert(
                    format!("game/worlds/main_world/paths/{folder}/{name}"),
                    std::fs::read(f.path()).unwrap(),
                );
            }
        }
        Self(m)
    }
}

impl BaiFiles for SampleFiles {
    fn files_in_directory(&self, dir: &str, pattern: &str) -> Vec<String> {
        let pre = pattern.split('*').next().unwrap_or("");
        let mut v: Vec<String> = self
            .0
            .keys()
            .filter(|k| k.starts_with(dir))
            .filter(|k| k.rsplit('/').next().unwrap_or("").starts_with(pre))
            .cloned()
            .collect();
        v.sort();
        v
    }
    fn file_exists(&self, name: &str) -> bool {
        self.0.contains_key(name)
    }
    fn read_file(&self, name: &str) -> Option<Vec<u8>> {
        self.0.get(name).cloned()
    }
}

/// The documented counts (`sub_navmesh.md` §2): (dir, nodes, links, verts, areas version,
/// forbidden areas, nav modifiers).
/// (dir, nodes, links, verts, areas version, (forbidden areas, nav modifiers)).
type Expected = (
    &'static str,
    usize,
    usize,
    usize,
    i32,
    Option<(usize, usize)>,
);

const EXPECTED: [Expected; 4] = [
    ("bai/038_058", 3661, 10960, 1840, 22, Some((124, 2))),
    ("bai/039_059", 2030, 6126, 1013, 22, Some((286, 9))),
    ("bai/036_056", 12, 28, 11, 22, None),
    ("bai12/038_058", 3653, 10936, 1836, 21, None),
];

#[test]
#[ignore = "needs AAEMU_BAI_SAMPLES"]
fn real_nav_samples_parse_like_the_engine() {
    let Some(root) = samples() else { return };
    for (dir, nodes, links, verts, areas_version, areas) in EXPECTED {
        let d = root.join(dir);
        if !d.is_dir() {
            continue;
        }
        let (_, px, py) = super::bai::zone_and_offsets_by_name(&dir[dir.len() - 7..]);
        let off = Vec3::new(px as f32 * 256.0, py as f32 * 256.0, 0.0);
        let read = |n: &str| std::fs::read(d.join(n)).unwrap();
        let net = NetMission::read(&read("netmission0.bai"), 0, off).unwrap();
        assert_eq!((net.nodes.len(), net.links.len()), (nodes, links), "{dir}");
        // +42: 10.x mostly 9 (also 8 / 10), 1.2 mostly 1 (also 0 / 2): bit 0, not `== 1`
        let set = net
            .links
            .iter()
            .filter(|l| l.is_pure_triangular_link)
            .count();
        println!("{dir}: +42 bit 0 set on {set} of {} links", net.links.len());
        assert!(set * 100 >= net.links.len() * 95, "{dir}");
        let v = VertexMission::read(&read("vertsmission0.bai"), 0, off).unwrap();
        assert_eq!(v.obstacles.len(), verts, "{dir}");
        // every triangle corner index is inside the verts file
        for n in net.nodes.iter().filter(|n| n.nav_type() == 2) {
            assert!(
                n.obstacle.iter().all(|&i| i >= 0 && (i as usize) < verts),
                "{dir}: {n:?}"
            );
        }
        let data = read("areasmission0.bai");
        assert_eq!(
            i32::from_le_bytes(data[..4].try_into().unwrap()),
            areas_version
        );
        let a = AreasMission::read(&data, 0, off, &UsedAreaNames::default())
            .unwrap_or_else(|e| panic!("{dir}: {e:?}"));
        if let Some((forbidden, mods)) = areas {
            assert_eq!(
                (a.forbidden_areas.len(), a.navigation_modifiers.len()),
                (forbidden, mods),
                "{dir}"
            );
        }
        for m in &a.navigation_modifiers {
            println!(
                "{dir} navmod {}: type {} wpc {} altered {} h {} auto {} z {}..{} bld {} light {}",
                m.name,
                m.mission_type,
                m.waypoint_connections,
                m.altered,
                m.height,
                m.node_auto_connect_distance,
                m.min_z,
                m.max_z,
                m.building_id,
                m.ai_light_level
            );
            assert!(m.min_z <= m.max_z, "{dir}: {m:?}");
        }
        if let Ok(hide) = std::fs::read(d.join("hidemission0.bai")) {
            assert!(
                NetMission::read(&hide, 0, off).is_err(),
                "hide v54 is rejected"
            );
        }
        println!("{dir}: ok");
    }
}

/// The 10.x samples (sectors 038_058 and 039_059 share a corner) merged through `AiGeoData`:
/// the navmesh height at every triangle centroid is the centroid's z, and A* routes across the
/// sector with the default NPC radius.
#[test]
#[ignore = "needs AAEMU_BAI_SAMPLES"]
fn real_nav_samples_graph_height_and_route() {
    let Some(root) = samples() else { return };
    for set in ["bai", "bai12"] {
        let dir = root.join(set);
        if !dir.is_dir() {
            continue;
        }
        let t = WorldTemplate::synthetic(0, "main_world", 12, 16, 100);
        let g = AiGeoData::new(
            &t,
            true,
            Arc::new(SampleFiles::load(&dir)),
            Arc::new(UsedAreaNames::default()),
            None,
        );
        g.cell_bai(9, 14).expect("cell");
        let (centroids, stats) = {
            let graph = g.graph();
            let c: Vec<Vec3> = graph
                .nodes
                .iter()
                .filter(|n| n.triangle.is_some())
                .map(|n| n.pos)
                .collect();
            (c, graph.stats)
        };
        println!("{set}: {stats:?}");
        assert!(!centroids.is_empty());
        // the node positions lie inside their triangles but ~0.2 m above the plane (median;
        // large triangles over uneven ground are off by up to tens of metres)
        let mut errs: Vec<f32> = centroids
            .iter()
            .map(|c| (g.get_height(Vec3::new(c.x, c.y, c.z + 50.0)) - c.z).abs())
            .collect();
        errs.sort_by(f32::total_cmp);
        let (median, p90) = (errs[errs.len() / 2], errs[errs.len() * 9 / 10]);
        println!(
            "{set}: {} triangle nodes, node z vs plane height: median {median}, p90 {p90}, max {}",
            errs.len(),
            errs[errs.len() - 1]
        );
        assert!(median < 0.3 && p90 < 2.0, "{set}");
        // routes inside 038_058 (world 9728..9984, 14848..15104)
        let pairs = [
            ((9900.0, 14950.0), (9970.0, 15090.0)),
            ((9800.0, 14950.0), (9950.0, 15050.0)),
            ((9900.0, 14950.0), (9750.0, 15050.0)),
            ((9740.0, 14860.0), (9970.0, 15090.0)),
        ];
        let mut routed = 0;
        for ((ax, ay), (bx, by)) in pairs {
            let mut pn = PathNode::default();
            let path = pn
                .find_path_with_radius(
                    &g,
                    Vec3::new(ax, ay, 0.0),
                    Vec3::new(bx, by, 0.0),
                    DEFAULT_AGENT_RADIUS,
                )
                .unwrap();
            println!("{set}: ({ax},{ay}) -> ({bx},{by}): {} points", path.len());
            if path.len() >= 2 {
                routed += 1;
                assert_eq!(path.last().copied(), Some(pn.end_point_pos));
            }
        }
        assert!(routed >= 2, "{set}");
    }
}

/// A whole main_world cell of the 1.2 pak: the 16 sectors merge (border triangles are
/// de-duplicated) and a route crosses from one sector into the next.
#[test]
#[ignore = "needs AAEMU_GAME_PAK"]
fn real_nav_pak_cell_merges_sectors() {
    let Ok(pak) = std::env::var("AAEMU_GAME_PAK") else {
        return;
    };
    let app = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("app");
    let files = Arc::new(ClientFileManager::from_sources(&[pak], &app));
    let worlds = WorldTemplates::load(&files, &app).expect("world templates");
    let main = worlds
        .get_world_template_by_name("main_world")
        .expect("main_world");
    let bai: Arc<dyn BaiFiles + Send + Sync> = Arc::new(ClientBaiFiles(files.clone()));
    let g = AiGeoData::new(main, true, bai, Arc::new(UsedAreaNames::default()), None);
    // cell (9, 14): sectors 36..39 x 56..59 (w_gweonid_forest)
    g.cell_bai(9, 14).expect("cell");
    let stats = g.graph().stats;
    println!("cell (9,14): {stats:?}");
    assert_eq!(stats.folders, 16);
    assert!(stats.nodes_merged > 0, "border triangles are shared");
    assert!(stats.vertices_merged > 0);
    // from sector 038_058 into 039_058 (x border 9984)
    let (a, b) = (
        Vec3::new(9900.0, 14950.0, 0.0),
        Vec3::new(10060.0, 14950.0, 0.0),
    );
    let mut pn = PathNode::default();
    let path = pn.find_path(&g, a, b).unwrap();
    println!("route {} points: {path:?}", path.len());
    assert!(path.len() >= 2);
    assert!(path.iter().any(|p| p.x < 9984.0) && path.iter().any(|p| p.x > 9984.0));
    let z = g.get_height(Vec3::new(9984.0, 14950.0, 500.0));
    println!("height at the sector border: {z}");
    assert!(z > 100.0 && z < 400.0);
}
