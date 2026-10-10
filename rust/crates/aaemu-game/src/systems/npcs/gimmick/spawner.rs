//! `GimmickSpawner` (+ the `Spawner<T>` members) as read from `gimmick_spawns*.json`.

use serde_json::{Map, Value};

use crate::data::world_templates::WorldSpawnPosition;
use crate::systems::npcs::json;

/// `OffsetCoordinateType` names (`Unk0..Unk3`).
const OFFSET_COORDINATE_NAMES: [&str; 4] = ["Unk0", "Unk1", "Unk2", "Unk3"];
/// `VelocityCoordinateType` / `AngVelCoordinateType` names (`Unk0..Unk2`).
const VELOCITY_COORDINATE_NAMES: [&str; 3] = ["Unk0", "Unk1", "Unk2"];

/// `GimmickSpawner` (`Spawner<Gimmick>` + its own members). `ParentWorld` / `Last` are
/// `[JsonIgnore]` runtime state (the world is passed to [`super::spawn_from_spawner`]).
#[derive(Debug, Clone, PartialEq)]
pub struct GimmickSpawner {
    /// `Id` (index, assigned by the SpawnManager).
    pub id: u32,
    /// `SpawnerId`.
    pub spawner_id: u32,
    /// `UnitId`: the gimmick template id used by `Spawn` (0 = elevator, no template).
    pub unit_id: u32,
    pub follow_path: String,
    pub follow_npc: u32,
    /// `Position` (`null` when absent: the C# then throws in the SpawnManager).
    pub position: Option<WorldSpawnPosition>,
    pub respawn_time: i32,
    pub despawn_time: i32,
    pub special_link: String,
    /// `GimmickId` ("here we mean TemplateId"; only used by the skill-effect constructor).
    pub gimmick_id: u32,
    pub entity_guid: i64,
    pub wait_time: f32,
    pub top_z: f32,
    pub middle_z: f32,
    pub bottom_z: f32,
    pub rotation_x: f32,
    pub rotation_y: f32,
    pub rotation_z: f32,
    pub rotation_w: f32,
    pub scale: f32,
    pub count: u32,
    pub offset_from_source: bool,
    /// `OffsetCoordinateType` (any integer, as Newtonsoft does not validate enums).
    pub offset_coordinate_id: i32,
    pub offset_x: f32,
    pub offset_y: f32,
    pub offset_z: f32,
    /// `VelocityCoordinateType`.
    pub velocity_coordinate_id: i32,
    pub velocity_x: f32,
    pub velocity_y: f32,
    pub velocity_z: f32,
    /// `AngVelCoordinateType`.
    pub ang_vel_coordinate_id: i32,
    pub ang_vel_x: f32,
    pub ang_vel_y: f32,
    pub ang_vel_z: f32,
}

impl Default for GimmickSpawner {
    /// The JSON default constructor (`RespawnTime = 15`, `DespawnTime = 20`, `Count = 0`).
    fn default() -> Self {
        Self {
            id: 0,
            spawner_id: 0,
            unit_id: 0,
            follow_path: String::new(),
            follow_npc: 0,
            position: None,
            respawn_time: 15,
            despawn_time: 20,
            special_link: String::new(),
            gimmick_id: 0,
            entity_guid: 0,
            wait_time: 0.0,
            top_z: 0.0,
            middle_z: 0.0,
            bottom_z: 0.0,
            rotation_x: 0.0,
            rotation_y: 0.0,
            rotation_z: 0.0,
            rotation_w: 0.0,
            scale: 0.0,
            count: 0,
            offset_from_source: false,
            offset_coordinate_id: 0,
            offset_x: 0.0,
            offset_y: 0.0,
            offset_z: 0.0,
            velocity_coordinate_id: 0,
            velocity_x: 0.0,
            velocity_y: 0.0,
            velocity_z: 0.0,
            ang_vel_coordinate_id: 0,
            ang_vel_x: 0.0,
            ang_vel_y: 0.0,
            ang_vel_z: 0.0,
        }
    }
}

/// An enum member: a number (not validated) or a member name / numeric string.
fn enum_or(obj: &Map<String, Value>, name: &str, names: &[&str]) -> Result<i32, String> {
    match json::prop(obj, name) {
        None => Ok(0),
        Some(Value::String(s)) => {
            let t = s.trim();
            if let Some(i) = names.iter().position(|n| n.eq_ignore_ascii_case(t)) {
                return Ok(i as i32);
            }
            t.parse::<i32>()
                .map_err(|_| format!("{name}: unknown enum value {s:?}"))
        }
        Some(Value::Null) => Err(format!("{name}: null for a non-nullable enum")),
        Some(_) => json::i32_or(obj, name, 0),
    }
}

/// `WorldSpawnPosition` (`DefaultValueHandling.IgnoreAndPopulate`: missing members get their
/// `[DefaultValue]`, `WorldId` = 1).
fn spawn_position(v: &Value) -> Result<Option<WorldSpawnPosition>, String> {
    let o = match v {
        Value::Null => return Ok(None),
        Value::Object(o) => o,
        other => return Err(format!("Position: expected an object, got {other}")),
    };
    Ok(Some(WorldSpawnPosition {
        world_id: json::u32_or(o, "WorldId", 1)?,
        zone_id: json::u32_or(o, "ZoneId", 0)?,
        x: json::f32_or(o, "X", 0.0)?,
        y: json::f32_or(o, "Y", 0.0)?,
        z: json::f32_or(o, "Z", 0.0)?,
        yaw: json::f32_or(o, "Yaw", 0.0)?,
        pitch: json::f32_or(o, "Pitch", 0.0)?,
        roll: json::f32_or(o, "Roll", 0.0)?,
    }))
}

/// One `GimmickSpawner` object.
pub fn parse_gimmick_spawner(o: &Map<String, Value>) -> Result<GimmickSpawner, String> {
    let d = GimmickSpawner::default();
    Ok(GimmickSpawner {
        id: json::u32_or(o, "Id", d.id)?,
        spawner_id: json::u32_or(o, "SpawnerId", d.spawner_id)?,
        unit_id: json::u32_or(o, "UnitId", d.unit_id)?,
        follow_path: json::str_or(o, "FollowPath", "")?,
        follow_npc: json::u32_or(o, "FollowNpc", d.follow_npc)?,
        position: match json::prop(o, "Position") {
            Some(v) => spawn_position(v)?,
            None => None,
        },
        respawn_time: json::i32_or(o, "RespawnTime", d.respawn_time)?,
        despawn_time: json::i32_or(o, "DespawnTime", d.despawn_time)?,
        special_link: json::str_or(o, "SpecialLink", "")?,
        gimmick_id: json::u32_or(o, "GimmickId", 0)?,
        entity_guid: json::i64_or(o, "EntityGuid", 0)?,
        wait_time: json::f32_or(o, "WaitTime", 0.0)?,
        top_z: json::f32_or(o, "TopZ", 0.0)?,
        middle_z: json::f32_or(o, "MiddleZ", 0.0)?,
        bottom_z: json::f32_or(o, "BottomZ", 0.0)?,
        rotation_x: json::f32_or(o, "RotationX", 0.0)?,
        rotation_y: json::f32_or(o, "RotationY", 0.0)?,
        rotation_z: json::f32_or(o, "RotationZ", 0.0)?,
        rotation_w: json::f32_or(o, "RotationW", 0.0)?,
        scale: json::f32_or(o, "Scale", 0.0)?,
        count: json::u32_or(o, "Count", 0)?,
        offset_from_source: json::bool_or(o, "OffsetFromSource", false)?,
        offset_coordinate_id: enum_or(o, "OffsetCoordinateId", &OFFSET_COORDINATE_NAMES)?,
        offset_x: json::f32_or(o, "OffsetX", 0.0)?,
        offset_y: json::f32_or(o, "OffsetY", 0.0)?,
        offset_z: json::f32_or(o, "OffsetZ", 0.0)?,
        velocity_coordinate_id: enum_or(o, "VelocityCoordinateId", &VELOCITY_COORDINATE_NAMES)?,
        velocity_x: json::f32_or(o, "VelocityX", 0.0)?,
        velocity_y: json::f32_or(o, "VelocityY", 0.0)?,
        velocity_z: json::f32_or(o, "VelocityZ", 0.0)?,
        ang_vel_coordinate_id: enum_or(o, "AngVelCoordinateId", &VELOCITY_COORDINATE_NAMES)?,
        ang_vel_x: json::f32_or(o, "AngVelX", 0.0)?,
        ang_vel_y: json::f32_or(o, "AngVelY", 0.0)?,
        ang_vel_z: json::f32_or(o, "AngVelZ", 0.0)?,
    })
}

/// `JsonHelper.TryDeserializeObject<List<GimmickSpawner>>` of a `gimmick_spawns*.json` document
/// (parse it with [`json::parse`]). `Err` = the C# deserialization fails (the SpawnManager
/// rethrows), including a `null` document or a `null` entry (a `NullReferenceException` in the
/// SpawnManager loop).
pub fn parse_gimmick_spawners(doc: &Value) -> Result<Vec<GimmickSpawner>, String> {
    let entries = json::array(doc)?;
    entries
        .iter()
        .enumerate()
        .map(|(i, v)| match v {
            Value::Object(o) => {
                parse_gimmick_spawner(o).map_err(|e| format!("entry {}: {e}", i + 1))
            }
            Value::Null => Err(format!("entry {}: null spawner", i + 1)),
            other => Err(format!("entry {}: expected an object, got {other}", i + 1)),
        })
        .collect()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_elevator_entry() {
        let doc = json::parse(
            r#"[ // comment
    {   // 007_008
        "Position": { "X": 8144.58679, "Y": 9195.4581, "Z": 261.76044 },
        "TopZ": 294.17, "MiddleZ": 0, "BottomZ": 261.73,
        "RotationZ": 0.93969274, "RotationW": -0.34201983,
        "WaitTime": 6.0, "Scale": 1.0,
        "EntityGuid": 0x4a0da1f4d8420a38,
        "OffsetCoordinateId": "Unk2", "VelocityCoordinateId": 1
    },
    { "UnitId": 37 },
]"#,
        )
        .unwrap();
        let s = parse_gimmick_spawners(&doc).unwrap();
        assert_eq!(s.len(), 2);
        let e = &s[0];
        let p = e.position.unwrap();
        assert_eq!((p.world_id, p.zone_id), (1, 0));
        assert_eq!((p.x, p.y, p.z), (8144.587_f32, 9195.458_f32, 261.76044));
        assert_eq!(e.entity_guid, 0x4a0d_a1f4_d842_0a38);
        assert_eq!((e.top_z, e.bottom_z, e.wait_time), (294.17, 261.73, 6.0));
        assert_eq!((e.rotation_z, e.rotation_w), (0.93969274, -0.34201983));
        assert_eq!((e.respawn_time, e.despawn_time, e.unit_id), (15, 20, 0));
        assert_eq!((e.offset_coordinate_id, e.velocity_coordinate_id), (2, 1));
        assert_eq!(s[1].unit_id, 37);
        assert!(s[1].position.is_none());
        assert_eq!(s[1].scale, 0.0);
        assert!(parse_gimmick_spawners(&json::parse("[null]").unwrap()).is_err());
        assert!(parse_gimmick_spawners(&json::parse("{}").unwrap()).is_err());
    }
}
