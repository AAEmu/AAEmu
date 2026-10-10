use std::io::Cursor;

use super::*;

struct TempDir(PathBuf);

impl TempDir {
    fn new(tag: &str) -> Self {
        let p = std::env::temp_dir().join(format!("aaemu_uft_{tag}_{}", std::process::id()));
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

fn run_app(dir: &Path, input: &str) -> (String, App<Cursor<Vec<u8>>, Vec<u8>>) {
    let mut app = App::new(Cursor::new(input.as_bytes().to_vec()), Vec::new());
    app.warning_pause = Duration::ZERO;
    app.run(dir).unwrap();
    let out = String::from_utf8(app.out.clone()).unwrap();
    (out, app)
}

const NPC: &str = r#"[
  {"Id": 1, "UnitId": 100, "Position": {"X": 10.5, "Y": 20.0, "Z": 30.25, "RotationZ": 32}},
  {"Id": 2, "UnitId": 101, "Count": 3, "Scale": 0.0, "FuncGroupId": 7, "Position": {"RotationX": -5, "RotationY": 127}}
]"#;

const DOODAD: &str = r#"[{"Id": 3, "UnitId": 200, "Scale": 2.5, "Position": {"X": 1, "RotationX": 10, "RotationY": 20, "RotationZ": 30}}]"#;

#[test]
fn converts_and_restores() {
    let tmp = TempDir::new("conv");
    let data = tmp.0.join("Data");
    let w = data.join("Worlds").join("w1");
    std::fs::create_dir_all(&w).unwrap();
    std::fs::write(w.join("npc_spawns.json"), NPC).unwrap();
    std::fs::write(w.join("doodad_spawns.json"), DOODAD).unwrap();

    let (out, app) = run_app(&data, "\ny\nn\n");
    assert!(
        out.contains("Found 2 files found (1 NPC, 1 Doodad and 0 transfers)."),
        "{out}"
    );
    assert!(out.contains("Converting files ..."));
    assert!(!out.contains("HIGHLY ADVISED"));
    assert!(out.ends_with("Deleted 0 old files.\n"));
    assert_eq!(app.old_entries_found, 3);
    assert_eq!(app.old_converted_files.len(), 2);
    assert_eq!(
        std::fs::read_to_string(w.join("npc_spawns.json.old")).unwrap(),
        NPC
    );

    let nl = aaemu_commons::utils::json_helper::PLATFORM_NEWLINE;
    let npc = std::fs::read_to_string(w.join("npc_spawns.json")).unwrap();
    let expected = [
        "[",
        "  {",
        "    \"Id\": 1,",
        "    \"UnitId\": 100,",
        "    \"Position\": {",
        "      \"X\": 10.5,",
        "      \"Y\": 20.0,",
        "      \"Z\": 30.25,",
        "      \"Yaw\": 90.0",
        "    }",
        "  },",
        "  {",
        "    \"Id\": 2,",
        "    \"UnitId\": 101,",
        "    \"Count\": 3,",
        "    \"Position\": {",
        "      \"Roll\": -14.0625,",
        "      \"Pitch\": 357.1875",
        "    },",
        "    \"FuncGroupId\": 7",
        "  }",
        "]",
    ]
    .join(nl);
    assert_eq!(npc, expected);
    let doodad = std::fs::read_to_string(w.join("doodad_spawns.json")).unwrap();
    assert!(doodad.contains("\"Scale\": 2.5"), "{doodad}");
    assert!(doodad.contains("\"Roll\""));

    // Second run: backups found, restore them, then abort.
    let (out, _) = run_app(&data, "\n\nn\n");
    assert!(out.contains("Also found 2 backup files from previous conversions"));
    assert!(out.contains("Restored 2 files !"));
    assert!(out.ends_with("Aborted.\n"));
    assert_eq!(
        std::fs::read_to_string(w.join("npc_spawns.json")).unwrap(),
        NPC
    );
    assert!(!w.join("npc_spawns.json.old").exists());

    // Convert already-converted data: warning box, delete backups.
    run_app(&data, "\ny\ny\n");
    assert!(!w.join("npc_spawns.json.old").exists());
    let (out, app) = run_app(&data, "\ny\ny\n");
    assert!(out.contains("HIGHLY ADVISED"), "{out}");
    assert_eq!(app.old_entries_found, 0);
    assert!(out.contains("Deleted 2 old files."));
    assert!(!w.join("npc_spawns.json.old").exists());
}

#[test]
fn already_converted_and_errors() {
    let tmp = TempDir::new("err");
    let f = tmp.0.join("npc_spawns.json");
    std::fs::write(&f, "[]").unwrap();
    std::fs::write(tmp.0.join("npc_spawns.json.old"), "[]").unwrap();
    let mut app = App::new(Cursor::new(Vec::new()), Vec::new());
    app.convert_npc_spawns_file(&f).unwrap();
    assert!(String::from_utf8(app.out.clone())
        .unwrap()
        .starts_with("Already converted"));

    std::fs::remove_file(tmp.0.join("npc_spawns.json.old")).unwrap();
    std::fs::write(&f, "null").unwrap();
    assert!(matches!(
        app.convert_npc_spawns_file(&f),
        Err(ToolError::NullReference(_))
    ));
    std::fs::write(&f, r#"[{"Position": null}]"#).unwrap();
    assert!(matches!(
        app.convert_npc_spawns_file(&f),
        Err(ToolError::NullReference(_))
    ));
    std::fs::write(&f, "{").unwrap();
    assert!(matches!(
        app.convert_npc_spawns_file(&f),
        Err(ToolError::Json { .. })
    ));
    std::fs::write(&f, "[]").unwrap();
    app.convert_npc_spawns_file(&f).unwrap();
    assert_eq!(std::fs::read_to_string(&f).unwrap(), "[]");

    // EOF on every prompt: no restore, abort.
    let (out, _) = run_app(&tmp.0, "");
    assert!(out.ends_with("Aborted.\n"));
}
