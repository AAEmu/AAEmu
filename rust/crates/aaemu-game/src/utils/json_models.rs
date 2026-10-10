//! `Models/Json/{JsonPosition, JsonNpcSpawns, JsonDoodadSpawns, JsonQuestSphere}` and their
//! `Utils/Converters` (`JsonModelsConverter`, `JsonPositionConverter`, ...): the minimal JSON
//! form the GM save commands write (`JsonConvert.SerializeObject(x, Formatting.Indented, new
//! JsonModelsConverter())`).

use serde_json::Value;

use crate::systems::services::webapi::json::JValue;

/// `JsonPosition`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct JsonPosition {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub roll: f32,
    pub pitch: f32,
    pub yaw: f32,
}

/// `value.ToString("0.####")`: at most four decimals, no trailing zeros.
pub fn format_0_4(v: f32) -> String {
    let s = format!("{:.4}", f64::from(v));
    let s = s.trim_end_matches('0').trim_end_matches('.');
    if s.is_empty() || s == "-" {
        "0".into()
    } else {
        s.to_string()
    }
}

impl JsonPosition {
    /// `JsonPositionConverter.ReadJson`: `X`, `Y`, `Z`, `Roll`, `Yaw`, `Pitch` (missing = 0).
    pub fn from_json(v: &Value) -> Self {
        let f = |k: &str| v.get(k).and_then(Value::as_f64).unwrap_or(0.0) as f32;
        Self {
            x: f("X"),
            y: f("Y"),
            z: f("Z"),
            roll: f("Roll"),
            yaw: f("Yaw"),
            pitch: f("Pitch"),
        }
    }

    /// `JsonPositionConverter.WriteJson`: rotations only when not 0.
    pub fn to_jvalue(&self) -> JValue {
        let mut m = vec![
            ("X".to_string(), JValue::Single(self.x)),
            ("Y".to_string(), JValue::Single(self.y)),
            ("Z".to_string(), JValue::Single(self.z)),
        ];
        if self.roll != 0.0 {
            m.push(("Roll".into(), JValue::Raw(format_0_4(self.roll))));
        }
        if self.yaw != 0.0 {
            m.push(("Yaw".into(), JValue::Raw(format_0_4(self.yaw))));
        }
        if self.pitch != 0.0 {
            m.push(("Pitch".into(), JValue::Raw(format_0_4(self.pitch))));
        }
        JValue::Object(m)
    }

    /// `AsVector3()`.
    pub fn as_vector3(&self) -> [f32; 3] {
        [self.x, self.y, self.z]
    }
}

/// `JsonNpcSpawns` (`NpcSpawnerIds` is not written by the converter).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct JsonNpcSpawns {
    pub id: u32,
    pub unit_id: u32,
    pub title: Option<String>,
    pub npc_spawner_ids: Vec<u32>,
    pub follow_path: Option<String>,
    pub position: Option<JsonPosition>,
    pub scale: f32,
}

impl JsonNpcSpawns {
    /// `JsonNpcSpawnsConverter.WriteJson`.
    pub fn to_jvalue(&self) -> JValue {
        JValue::object([
            ("Id", JValue::from(self.id)),
            ("UnitId", JValue::from(self.unit_id)),
            ("Title", JValue::opt_str(self.title.clone())),
            ("FollowPath", JValue::opt_str(self.follow_path.clone())),
            (
                "Position",
                self.position.map_or(JValue::Null, |p| p.to_jvalue()),
            ),
            ("Scale", JValue::Single(self.scale)),
        ])
    }
}

/// `JsonDoodadSpawns` (`RelatedIds` is not written).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct JsonDoodadSpawns {
    pub id: u32,
    pub unit_id: u32,
    pub title: Option<String>,
    pub related_ids: Vec<u32>,
    pub position: Option<JsonPosition>,
    pub scale: f32,
    pub func_group_id: u32,
}

impl JsonDoodadSpawns {
    /// `JsonDoodadSpawnsConverter.WriteJson`.
    pub fn to_jvalue(&self) -> JValue {
        JValue::object([
            ("Id", JValue::from(self.id)),
            ("UnitId", JValue::from(self.unit_id)),
            ("Title", JValue::opt_str(self.title.clone())),
            (
                "Position",
                self.position.map_or(JValue::Null, |p| p.to_jvalue()),
            ),
            ("FuncGroupId", JValue::from(self.func_group_id)),
            ("Scale", JValue::Single(self.scale)),
        ])
    }
}

/// `JsonQuestSphere`.
#[derive(Debug, Clone, Default, PartialEq)]
pub struct JsonQuestSphere {
    pub id: u32,
    pub quest_id: u32,
    pub sphere_id: u32,
    pub radius: f32,
    pub position: Option<JsonPosition>,
}

impl JsonQuestSphere {
    /// `JsonQuestSphereConverter.WriteJson`.
    pub fn to_jvalue(&self) -> JValue {
        JValue::object([
            ("Id", JValue::from(self.id)),
            ("QuestId", JValue::from(self.quest_id)),
            ("SphereId", JValue::from(self.sphere_id)),
            ("Radius", JValue::Single(self.radius)),
            (
                "Position",
                self.position.map_or(JValue::Null, |p| p.to_jvalue()),
            ),
        ])
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn position_round_trip_and_format() {
        let p = JsonPosition {
            x: 100.0,
            y: 200.5,
            z: 3.0,
            yaw: 1.234567,
            ..JsonPosition::default()
        };
        assert_eq!(
            p.to_jvalue().to_json(),
            r#"{"X":100.0,"Y":200.5,"Z":3.0,"Yaw":1.2346}"#
        );
        let back =
            JsonPosition::from_json(&serde_json::from_str(&p.to_jvalue().to_json()).unwrap());
        assert_eq!(back.x, 100.0);
        assert_eq!(back.yaw, 1.2346);
        assert_eq!(format_0_4(2.5), "2.5");
    }

    #[test]
    fn spawns_indented() {
        let s = JsonNpcSpawns {
            id: 1,
            unit_id: 2,
            title: Some("t".into()),
            position: Some(JsonPosition::default()),
            scale: 1.0,
            ..JsonNpcSpawns::default()
        };
        let text = JValue::Array(vec![s.to_jvalue()]).to_json_indented();
        let nl = if cfg!(windows) { "\r\n" } else { "\n" };
        let expected = [
            "[",
            "  {",
            "    \"Id\": 1,",
            "    \"UnitId\": 2,",
            "    \"Title\": \"t\",",
            "    \"FollowPath\": null,",
            "    \"Position\": {",
            "      \"X\": 0.0,",
            "      \"Y\": 0.0,",
            "      \"Z\": 0.0",
            "    },",
            "    \"Scale\": 1.0",
            "  }",
            "]",
        ]
        .join(nl);
        assert_eq!(text, expected);
    }
}
