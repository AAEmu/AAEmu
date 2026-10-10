use super::*;

struct TempDir(PathBuf);

impl TempDir {
    fn new(tag: &str) -> Self {
        let p = std::env::temp_dir().join(format!("aaemu_wc_{tag}_{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&p);
        std::fs::create_dir_all(&p).unwrap();
        Self(p)
    }
}

impl Drop for TempDir {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

fn args(a: &[&str]) -> Vec<String> {
    a.iter().map(|s| s.to_string()).collect()
}

#[test]
fn parses_args_like_csharp() {
    let o = parse_args(&args(&[
        "-IN", "a", "-o", "b", "-export", "GIMP", "-split", "-v", "v1",
    ]));
    assert_eq!(o.input, "a");
    assert_eq!(o.output, "b");
    assert_eq!(o.export, Export::Gimp);
    assert_eq!(o.split_size, 512);
    assert_eq!(o.version, VersionCalc::V1);
    let o = parse_args(&args(&["-in"]));
    assert_eq!(o.input, "");
    let o = parse_args(&args(&["-e", "unity", "-ver", "x", "/?"]));
    assert_eq!(
        (o.export, o.version, o.show_help),
        (Export::Unity, VersionCalc::Draft, true)
    );
}

/// Builds a heightmap.dat with 256 nodes of `n_size` x `n_size` constant raw value `raw`.
fn heightmap(n_size: i32, raw: u16) -> Vec<u8> {
    let mut nodes = Vec::new();
    for sx in 0..16 {
        for sy in 0..16 {
            nodes.push(7u8);
            nodes.extend_from_slice(&[0, 0, 0]);
            for f in [sx as f32 * 32.0, sy as f32 * 32.0, 0.0, 0.0, 0.0, 0.0] {
                nodes.extend_from_slice(&f.to_le_bytes());
            }
            nodes.push(0);
            nodes.extend_from_slice(&0.0f32.to_le_bytes()); // fOffset
            nodes.extend_from_slice(&0.01f32.to_le_bytes()); // fRange
            nodes.extend_from_slice(&n_size.to_le_bytes());
            nodes.extend_from_slice(&0i32.to_le_bytes());
            for _ in 0..n_size * n_size {
                nodes.extend_from_slice(&raw.to_le_bytes());
            }
            nodes.extend_from_slice(&[0u8; 20 + 36]);
        }
    }
    let mut file = vec![23u8, 0, 1, 2];
    let chunk = 4 + 4 * 6 + 4 + nodes.len();
    file.extend_from_slice(&(chunk as i32).to_le_bytes());
    for _ in 0..4 {
        file.extend_from_slice(&0i32.to_le_bytes());
    }
    file.extend_from_slice(&1.0f32.to_le_bytes());
    file.extend_from_slice(&0.0f32.to_le_bytes());
    file.extend_from_slice(&nodes);
    file
}

#[test]
fn converts_synthetic_world() {
    let tmp = TempDir::new("synthetic");
    let input = tmp.0.join("worlds");
    let output = tmp.0.join("out");
    std::fs::create_dir_all(&output).unwrap();
    let wdir = input.join("tiny");
    std::fs::create_dir_all(
        wdir.join("cells")
            .join("001_000")
            .join("client")
            .join("terrain"),
    )
    .unwrap();
    std::fs::write(
        wdir.join("world.xml"),
        r#"<World name="tiny" cellXCount="2" cellYCount="1" oceanLevel="100" maxTerrainHeight="4096"><ZoneList><Zone name="z" id="5"><cellList><cell x="1" y="0"><sectorList><sector x="2" y="3"/></sectorList></cell></cellList></Zone></ZoneList></World>"#,
    )
    .unwrap();
    std::fs::write(
        wdir.join("cells/001_000/client/terrain/heightmap.dat"),
        heightmap(33, 0x100),
    )
    .unwrap();

    let out = output.to_string_lossy().into_owned();
    run(&args(&[
        "-in",
        &input.to_string_lossy(),
        "-out",
        &out,
        "-export",
        "unity",
    ]))
    .unwrap();

    let nl = aaemu_commons::utils::json_helper::PLATFORM_NEWLINE;
    let worlds = std::fs::read_to_string(format!("{out}\\worlds.json")).unwrap();
    assert_eq!(
        worlds,
        [
            "[",
            "  {",
            "    \"Id\": 0,",
            "    \"Name\": \"tiny\",",
            "    \"MaxHeight\": 4096.0,",
            "    \"HeightMaxCoeff\": 31.99951171875,",
            "    \"OceanLevel\": 100.0,",
            "    \"CellX\": 2,",
            "    \"CellY\": 1",
            "  }",
            "]"
        ]
        .join(nl)
    );
    let zones = std::fs::read_to_string(format!("{out}\\tiny\\zones.json")).unwrap();
    assert_eq!(
        zones,
        r#"[{"Id":5,"Name":"z","Cells":[{"X":1,"Y":0,"Sectors":[{"X":2,"Y":3}]}]}]"#
    );
    let hmap = std::fs::read(format!("{out}\\tiny\\hmap.dat")).unwrap();
    assert_eq!(hmap.len(), 4 * 3 + 8 + 4 + 1 + 1 + 512 * 512 * 2);
    assert_eq!(&hmap[0..12], &[1, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0]);
    assert_eq!(
        f64::from_le_bytes(hmap[12..20].try_into().unwrap()),
        65535.0 / 2048.0
    );
    assert_eq!(&hmap[20..24], &2i32.to_le_bytes());
    assert_eq!(hmap[24], 1); // cell 0 empty
    assert_eq!(hmap[25], 0); // cell 1 has data
                             // raw 0x100 -> (0x100 >> 4) * iStep(4) * 0.05 = 3.2 m -> * coeff = 102
    let first = u16::from_le_bytes([hmap[26], hmap[27]]);
    assert_eq!(first, 102);
    let raw = std::fs::read(format!("{out}\\tiny.hmap.raw")).unwrap();
    assert_eq!(raw.len(), 1024 * 512 * 2);
}

#[test]
fn missing_nodes_is_an_error() {
    let tmp = TempDir::new("broken");
    let input = tmp.0.join("worlds");
    let wdir = input.join("w");
    std::fs::create_dir_all(wdir.join("cells/000_000/client/terrain")).unwrap();
    std::fs::write(
        wdir.join("world.xml"),
        r#"<World name="w" cellXCount="1" cellYCount="1"/>"#,
    )
    .unwrap();
    let mut hm = heightmap(0, 0); // nodes without data are filtered out
    hm.truncate(hm.len());
    std::fs::write(wdir.join("cells/000_000/client/terrain/heightmap.dat"), hm).unwrap();
    let r = convert_world(
        &input.to_string_lossy(),
        &wdir.join("world.xml"),
        VersionCalc::Draft,
    );
    assert!(matches!(r, Err(ConvertError::Heightmap { .. })));
}
