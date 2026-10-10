//! Integration tests against a real ArcheAge 1.2 client `game_pak` (read-only).
//!
//! Ignored by default. Run with:
//!
//! ```text
//! cargo test -p aaemu-commons --test client_pak -- --ignored --nocapture
//! ```
//!
//! The pak is taken from `AAEMU_CLIENT_PAK`, defaulting to `D:\AA1.2\game_pak`; the tests pass
//! trivially (with a message) when it is not present.

use std::io::Read;
use std::path::PathBuf;
use std::time::Instant;

use aaemu_commons::aapak::{AAPak, PakFileType};
use aaemu_commons::client_files::{default_test_client_pak, ClientDataConfig, ClientFileManager};

fn pak_path() -> Option<PathBuf> {
    let p = default_test_client_pak();
    if p.is_none() {
        eprintln!("client game_pak not found (set AAEMU_CLIENT_PAK); skipping");
    }
    p
}

#[test]
#[ignore = "needs a real 1.2 client game_pak (AAEMU_CLIENT_PAK, default D:\\AA1.2\\game_pak)"]
fn open_real_pak_and_read_world_xml() {
    let Some(path) = pak_path() else { return };
    let started = Instant::now();
    let pak = AAPak::open(&path, true).expect("open game_pak");
    eprintln!(
        "opened {} in {:?}: type {:?}, {} files, {} extra entries, FAT at {:#x}",
        path.display(),
        started.elapsed(),
        pak.pak_type(),
        pak.file_count(),
        pak.extra_files.len(),
        pak.header.first_file_info_offset
    );
    assert!(pak.read_only());
    assert_ne!(pak.pak_type(), PakFileType::Csv);
    assert!(pak.file_count() > 1000);
    assert_eq!(pak.file_count() as u32, pak.header.file_count);

    // The world list the game server loads (WorldManager: game/worlds/*/world.xml).
    let world_xml = "game/worlds/main_world/world.xml";
    let info = pak
        .get_file_by_name(world_xml)
        .expect("main_world/world.xml in pak")
        .clone();
    let data = pak.read_file(world_xml).unwrap().unwrap();
    assert_eq!(data.len() as i64, info.size);
    let text = aaemu_commons::file_manager::decode_text(&data);
    let doc = roxmltree::Document::parse(&text).expect("world.xml parses");
    let root = doc.root_element();
    assert_eq!(root.tag_name().name(), "World");
    assert_eq!(root.attribute("name"), Some("main_world"));
    eprintln!(
        "{world_xml}: {} bytes, cellXCount={:?} cellYCount={:?}",
        data.len(),
        root.attribute("cellXCount"),
        root.attribute("cellYCount")
    );

    // The stored MD5 matches the data (the 1.2 client leaves most MD5 fields zeroed).
    use md5::{Digest, Md5};
    let digest: [u8; 16] = Md5::digest(&data).into();
    assert!(
        info.md5 == digest || info.md5 == [0; 16],
        "md5 of {world_xml}"
    );
    let with_md5 = pak.files().filter(|f| f.md5 != [0; 16]).count();
    eprintln!("{with_md5} files have a non-zero md5");
    if let Some(f) = pak.files().find(|f| f.md5 != [0; 16] && f.size < 1_000_000) {
        let d = pak.read_file(&f.name).unwrap().unwrap();
        assert_eq!(
            <[u8; 16]>::from(Md5::digest(&d)),
            f.md5,
            "md5 of {}",
            f.name
        );
    }

    // Streams on cloned handles read the same bytes.
    let mut s = pak
        .export_file_as_stream_cloned("game\\worlds\\main_world\\world.xml")
        .unwrap()
        .unwrap();
    let mut again = Vec::new();
    s.read_to_end(&mut again).unwrap();
    assert_eq!(again, data);
}

#[test]
#[ignore = "needs a real 1.2 client game_pak (AAEMU_CLIENT_PAK, default D:\\AA1.2\\game_pak)"]
fn client_file_manager_on_real_pak() {
    let Some(path) = pak_path() else { return };
    let config = ClientDataConfig {
        sources: vec![path.to_string_lossy().into_owned()],
    };
    let m = ClientFileManager::from_config(&config);
    assert_eq!(m.sources().len(), 1);

    // WorldManager.Load: all world.xml files under game/worlds.
    let worlds = m.get_files_in_directory("game/worlds", "world.xml", true);
    eprintln!(
        "{} world.xml files, e.g. {:?}",
        worlds.len(),
        &worlds[..worlds.len().min(5)]
    );
    assert!(worlds
        .iter()
        .any(|w| w == "game/worlds/main_world/world.xml"));
    for w in &worlds {
        assert!(m.get_file_as_string(w).contains("<World"), "{w}");
    }

    // WorldCell: heightmaps of main_world cells.
    let cells = m.get_files_in_directory("game/worlds/main_world/cells", "heightmap.dat", true);
    eprintln!("{} main_world heightmap.dat files", cells.len());
    assert!(!cells.is_empty());
    let hm = m.get_file_bytes(&cells[0]).unwrap().unwrap();
    assert!(hm.len() > 32);

    // SubZoneManager / TransferGameData: level design files.
    let subzones = m.get_files_in_directory(
        "game/worlds/main_world/level_design",
        "subzone_area.xml",
        true,
    );
    eprintln!("{} subzone_area.xml files", subzones.len());

    // Non-recursive listing of a zone folder (WorldTemplate: game/worlds/<w>/zone/<id>/*.bai).
    let all_bai = m.get_files_in_directory("game/worlds/main_world/zone", "*.bai", true);
    eprintln!("{} .bai files under main_world/zone", all_bai.len());
    // (the 1.2 client has no .bai files, so also check a non-recursive *.xml listing)
    let xml = m.get_files_in_directory("game/worlds/main_world", "*.xml", false);
    eprintln!("{} .xml files directly in main_world: {xml:?}", xml.len());
    assert!(xml.iter().any(|x| x == "game/worlds/main_world/world.xml"));
    assert!(xml.iter().all(|x| x.matches('/').count() == 3));
    if let Some(first) = all_bai.first() {
        let dir = &first[..first.rfind('/').unwrap()];
        let bai = m.get_files_in_directory(dir, "*.bai", false);
        eprintln!("{} .bai files directly in {dir}", bai.len());
        assert!(bai.contains(first));
        assert!(bai.iter().all(|b| b[..b.rfind('/').unwrap()] == *dir));
    }

    assert!(!m.file_exists("game/worlds/does_not_exist/world.xml"));
    assert_eq!(
        m.get_file_as_string("game/worlds/does_not_exist/world.xml"),
        ""
    );
}
