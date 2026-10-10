//! Port of AAEmu.UnitTests/Game/Models/Json/ModelsJsonConverterTests.cs
//!
//! `JsonModelsConverter` dispatches on the runtime type to the per-model converters; in Rust
//! each model has its converter as methods (`to_jvalue` = `WriteJson`,
//! `JsonPosition::from_json` = `JsonPositionConverter.ReadJson`), so `JsonConvert
//! .SerializeObject(x, converter)` is `x.to_jvalue().to_json()`. `CanConvert` is decided at
//! compile time: the four model types have converters, other types (`string`, `int`, `object`)
//! have none and cannot be passed at all, so only the `true` rows are runtime-checkable.

use serde_json::Value;

use super::*;
use crate::systems::services::webapi::json::JValue;

fn read(json: &str) -> JsonPosition {
    JsonPosition::from_json(&serde_json::from_str::<Value>(json).unwrap())
}

// ----- constructor / CanConvert / AddConverter -----

#[test]
fn constructor_default_creates_instance_with_converters() {
    // the four registered converters
    for v in [
        JsonPosition::default().to_jvalue(),
        JsonQuestSphere::default().to_jvalue(),
        JsonDoodadSpawns::default().to_jvalue(),
        JsonNpcSpawns::default().to_jvalue(),
    ] {
        assert!(matches!(v, JValue::Object(_)));
    }
}

#[test]
fn can_convert_returns_correct_value() {
    // typeof(JsonPosition / JsonQuestSphere / JsonDoodadSpawns / JsonNpcSpawns) -> true
    assert!(JsonPosition::default()
        .to_jvalue()
        .to_json()
        .starts_with('{'));
    assert!(JsonQuestSphere::default()
        .to_jvalue()
        .to_json()
        .starts_with('{'));
    assert!(JsonDoodadSpawns::default()
        .to_jvalue()
        .to_json()
        .starts_with('{'));
    assert!(JsonNpcSpawns::default()
        .to_jvalue()
        .to_json()
        .starts_with('{'));
    // typeof(string / int / object) -> false: no converter exists (compile-time)
}

#[test]
fn add_converter_with_valid_types_adds_converter() {
    let json = JsonPosition::default().to_jvalue().to_json();
    assert_eq!(json, r#"{"X":0.0,"Y":0.0,"Z":0.0}"#);
}

// ----- WriteJson -----

#[test]
fn write_json_with_json_position_writes_correct_json() {
    let position = JsonPosition {
        x: 100.5,
        y: 200.5,
        z: 300.5,
        ..JsonPosition::default()
    };

    let json = position.to_jvalue().to_json();

    assert!(json.contains("100.5"));
    assert!(json.contains("200.5"));
    assert!(json.contains("300.5"));
}

#[test]
fn write_json_with_rotation_values_writes_rotation() {
    let position = JsonPosition {
        x: 1.0,
        y: 2.0,
        z: 3.0,
        yaw: 45.0,
        pitch: 30.0,
        roll: 15.0,
    };

    let json = position.to_jvalue().to_json();

    assert!(json.contains("45"));
    assert!(json.contains("30"));
    assert!(json.contains("15"));
    assert_eq!(
        json,
        r#"{"X":1.0,"Y":2.0,"Z":3.0,"Roll":15,"Yaw":45,"Pitch":30}"#
    );
}

#[test]
fn write_json_with_zero_rotation_omits_rotation_fields() {
    let position = JsonPosition {
        x: 1.0,
        y: 1.0,
        z: 1.0,
        ..JsonPosition::default()
    };

    let json = position.to_jvalue().to_json();

    assert!(!json.contains("Yaw"));
    assert!(!json.contains("Pitch"));
    assert!(!json.contains("Roll"));
}

// ----- array serialization -----

#[test]
fn serialize_object_with_array_of_positions_writes_correct_json() {
    let positions = [
        JsonPosition {
            x: 1.0,
            y: 2.0,
            z: 3.0,
            ..JsonPosition::default()
        },
        JsonPosition {
            x: 4.0,
            y: 5.0,
            z: 6.0,
            ..JsonPosition::default()
        },
    ];

    let json = JValue::Array(positions.iter().map(JsonPosition::to_jvalue).collect()).to_json();

    for d in ["1", "2", "3", "4", "5", "6"] {
        assert!(json.contains(d), "{json}");
    }
}

// ----- ReadJson -----

#[test]
fn read_json_with_valid_json_position_returns_position() {
    let result = read(r#"{"X":10.5,"Y":20.5,"Z":30.5}"#);

    assert_eq!(result.x, 10.5);
    assert_eq!(result.y, 20.5);
    assert_eq!(result.z, 30.5);
}

#[test]
fn read_json_with_rotation_returns_position_with_rotation() {
    let result = read(r#"{"X":1,"Y":2,"Z":3,"Yaw":45,"Pitch":30,"Roll":15}"#);

    assert_eq!(result.yaw, 45.0);
    assert_eq!(result.pitch, 30.0);
    assert_eq!(result.roll, 15.0);
}

// ----- edge cases -----

#[test]
fn read_json_with_various_values_returns_correct_position() {
    for (json, x, y, z) in [
        (r#"{"X":0,"Y":0,"Z":0}"#, 0.0f32, 0.0f32, 0.0f32),
        (
            r#"{"X":-100.5,"Y":-200.5,"Z":-300.5}"#,
            -100.5,
            -200.5,
            -300.5,
        ),
        (
            r#"{"X":1.23456789,"Y":2.34567890,"Z":3.45678901}"#,
            // the C# float literals 1.23456789f, 2.34567890f, 3.45678901f
            1.234_567_89_f64 as f32,
            2.345_678_90_f64 as f32,
            3.456_789_01_f64 as f32,
        ),
    ] {
        let result = read(json);
        assert_eq!((result.x, result.y, result.z), (x, y, z), "{json}");
    }
}

// ----- JsonNpcSpawns -----

#[test]
fn serialize_npc_spawns_with_all_fields_writes_correct_json() {
    let npc_spawns = JsonNpcSpawns {
        id: 1,
        unit_id: 100,
        title: Some("Test NPC".into()),
        follow_path: Some("/path/to/follow".into()),
        position: Some(JsonPosition {
            x: 1000.0,
            y: 2000.0,
            z: 3000.0,
            yaw: 90.0,
            ..JsonPosition::default()
        }),
        scale: 1.5,
        ..JsonNpcSpawns::default()
    };

    let json = npc_spawns.to_jvalue().to_json();

    assert!(json.contains('1'));
    assert!(json.contains("100"));
    assert!(json.contains("Test NPC"));
    assert!(json.contains("1.5"));
}

#[test]
fn serialize_npc_spawns_with_zero_position_omits_rotation() {
    let npc_spawns = JsonNpcSpawns {
        id: 1,
        unit_id: 1,
        title: Some("test".into()),
        follow_path: Some("test".into()),
        position: Some(JsonPosition {
            x: 1.0,
            y: 1.0,
            z: 1.0,
            ..JsonPosition::default()
        }),
        scale: 1.0,
        ..JsonNpcSpawns::default()
    };

    let json = npc_spawns.to_jvalue().to_json();

    assert!(!json.contains("Yaw"));
    assert!(!json.contains("Pitch"));
    assert!(!json.contains("Roll"));
}

// ----- JsonDoodadSpawns -----

#[test]
fn serialize_doodad_spawns_writes_correct_json() {
    let doodad_spawns = JsonDoodadSpawns {
        id: 42,
        unit_id: 100,
        position: Some(JsonPosition {
            x: 500.0,
            y: 600.0,
            z: 700.0,
            ..JsonPosition::default()
        }),
        ..JsonDoodadSpawns::default()
    };

    let json = doodad_spawns.to_jvalue().to_json();

    assert!(json.contains("42"));
    assert!(json.contains("100"));
}
