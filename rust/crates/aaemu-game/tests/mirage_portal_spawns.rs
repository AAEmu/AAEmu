//! Real-asset audit of the `main_world` Mirage Isle entrance doodads (template 4894) against the
//! original 1.2 client portals: every `game/worlds/main_world/map_data/doodad_map/*.dat` is a
//! sequence of 16-byte little-endian records (`u32` template id + three `f32` coordinates).
//!
//! `AAEMU_CLIENT_PAK=D:\AA1.2\game_pak cargo test -p aaemu-game --test mirage_portal_spawns -- --ignored`

use aaemu_game::data::pak::Pak;
use aaemu_game::systems::npcs::doodad::spawner::{parse_list, DoodadSpawnerDef};

/// Doodad template id of the Mirage Isle entrance portals.
const PORTAL_TEMPLATE_ID: u32 = 4894;
/// Accepted deviation between an authored spawn and the original client marker.
const MAX_DISTANCE_M: f64 = 0.03;

fn distance(a: [f32; 3], b: [f32; 3]) -> f64 {
    let dx = f64::from(a[0]) - f64::from(b[0]);
    let dy = f64::from(a[1]) - f64::from(b[1]);
    let dz = f64::from(a[2]) - f64::from(b[2]);
    (dx * dx + dy * dy + dz * dz).sqrt()
}

#[test]
#[ignore = "needs AAEMU_CLIENT_PAK"]
fn mirage_entrance_doodads_match_client_markers() {
    let Ok(pak_path) = std::env::var("AAEMU_CLIENT_PAK") else {
        return;
    };
    let pak = Pak::open(&pak_path).unwrap_or_else(|e| {
        panic!("AAEMU_CLIENT_PAK {pak_path:?} is not a valid 1.2 game_pak: {e}")
    });

    let mut markers = Vec::new();
    for info in pak.files_in_directory("game/worlds/main_world/map_data/doodad_map") {
        if !info.name.ends_with(".dat") {
            continue;
        }
        let data = pak
            .read_entry(info)
            .unwrap_or_else(|| panic!("cannot read pak entry {:?}", info.name));
        assert_eq!(
            data.len() % 16,
            0,
            "{}: {} bytes is not a multiple of the 16-byte marker record",
            info.name,
            data.len()
        );
        for record in data.as_chunks::<16>().0 {
            let template = u32::from_le_bytes(record[0..4].try_into().unwrap());
            if template == PORTAL_TEMPLATE_ID {
                markers.push([
                    f32::from_le_bytes(record[4..8].try_into().unwrap()),
                    f32::from_le_bytes(record[8..12].try_into().unwrap()),
                    f32::from_le_bytes(record[12..16].try_into().unwrap()),
                ]);
            }
        }
    }
    assert!(
        !markers.is_empty(),
        "no template {PORTAL_TEMPLATE_ID} markers found in {pak_path:?}"
    );

    let spawns_path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("app/Data/Worlds/main_world/doodad_spawns.json");
    let text = std::fs::read_to_string(&spawns_path)
        .unwrap_or_else(|e| panic!("cannot read {}: {e}", spawns_path.display()));
    let spawns = parse_list(&text, DoodadSpawnerDef::from_json)
        .unwrap_or_else(|e| panic!("{} does not parse: {e}", spawns_path.display()));
    let portals: Vec<_> = spawns
        .iter()
        .filter(|s| s.base.unit_id == PORTAL_TEMPLATE_ID)
        .collect();
    assert!(
        !portals.is_empty(),
        "no template {PORTAL_TEMPLATE_ID} spawns shipped"
    );
    assert!(
        portals.iter().all(|p| p.count == 1),
        "a template {PORTAL_TEMPLATE_ID} spawn uses Count != 1"
    );

    assert_eq!(
        portals.len(),
        markers.len(),
        "{} portal spawns vs {} client markers",
        portals.len(),
        markers.len()
    );
    for (index, portal) in portals.iter().enumerate() {
        let p = portal.base.position;
        let position = [p.x, p.y, p.z];
        let matched = markers
            .iter()
            .filter(|marker| distance(position, **marker) <= MAX_DISTANCE_M)
            .count();
        assert_eq!(
            matched, 1,
            "spawn #{index} at {position:?} matches {matched} client markers"
        );
    }
    for (index, marker) in markers.iter().enumerate() {
        let matched = portals
            .iter()
            .filter(|portal| {
                let p = portal.base.position;
                distance([p.x, p.y, p.z], *marker) <= MAX_DISTANCE_M
            })
            .count();
        assert_eq!(
            matched, 1,
            "client marker #{index} at {marker:?} matches {matched} spawns"
        );
    }
}
