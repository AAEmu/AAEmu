//! `Tools/UpdatesForTransform/WorldPositions.cs`: the old and new spawn JSON models, with
//! Newtonsoft.Json-compatible (de)serialization rules, and the rotation conversion.

use aaemu_commons::utils::json_helper::newtonsoft_float;
use serde::Serialize;
use serde_json::{Map, Value};

#[derive(Debug, thiserror::Error, PartialEq)]
pub enum SpawnJsonError {
    #[error("Unexpected token when deserializing: expected {expected} for '{path}'")]
    Type {
        path: String,
        expected: &'static str,
    },
    #[error("Required property '{0}' expects a non-null value.")]
    Null(String),
    #[error("Value was either too large or too small for '{0}'.")]
    Overflow(String),
}

/// `WorldSpawnPositionOld`.
#[derive(Debug, Clone, Copy, Default, PartialEq)]
pub struct WorldSpawnPositionOld {
    pub x: f32,
    pub y: f32,
    pub z: f32,
    pub rotation_x: i8,
    pub rotation_y: i8,
    pub rotation_z: i8,
}

/// `NpcSpawnerOld` (C# defaults: `Count = 1`, `Scale = 1`, `FuncGroupId = 0`).
#[derive(Debug, Clone, PartialEq)]
pub struct NpcSpawnerOld {
    pub id: i64,
    pub unit_id: i64,
    pub count: i64,
    /// `None` when the JSON had `"Position": null` (the C# then crashed with a
    /// NullReferenceException).
    pub position: Option<WorldSpawnPositionOld>,
    pub scale: f32,
    pub func_group_id: i64,
}

impl Default for NpcSpawnerOld {
    fn default() -> Self {
        Self {
            id: 0,
            unit_id: 0,
            count: 1,
            position: Some(WorldSpawnPositionOld::default()),
            scale: 1.0,
            func_group_id: 0,
        }
    }
}

fn is_zero_f32(v: &f32) -> bool {
    *v == 0.0
}
fn is_one_f32(v: &f32) -> bool {
    *v == 1.0
}
fn is_one_i64(v: &i64) -> bool {
    *v == 1
}
fn is_zero_i64(v: &i64) -> bool {
    *v == 0
}

/// `WorldSpawnPositionNew`: every field is omitted when it equals its default (0).
#[derive(Debug, Clone, Copy, Default, PartialEq, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct WorldSpawnPositionNew {
    #[serde(
        skip_serializing_if = "is_zero_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub x: f32,
    #[serde(
        skip_serializing_if = "is_zero_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub y: f32,
    #[serde(
        skip_serializing_if = "is_zero_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub z: f32,
    #[serde(
        skip_serializing_if = "is_zero_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub roll: f32,
    #[serde(
        skip_serializing_if = "is_zero_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub pitch: f32,
    #[serde(
        skip_serializing_if = "is_zero_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub yaw: f32,
}

/// `NpcSpawnerNew`: `Count` (1), `Scale` (1) and `FuncGroupId` (0) are omitted at their
/// default values; `Id`, `UnitId` and `Position` are always written.
#[derive(Debug, Clone, PartialEq, Serialize)]
#[serde(rename_all = "PascalCase")]
pub struct NpcSpawnerNew {
    pub id: i64,
    pub unit_id: i64,
    #[serde(skip_serializing_if = "is_one_i64")]
    pub count: i64,
    pub position: WorldSpawnPositionNew,
    #[serde(
        skip_serializing_if = "is_one_f32",
        serialize_with = "newtonsoft_float::f32"
    )]
    pub scale: f32,
    #[serde(skip_serializing_if = "is_zero_i64")]
    pub func_group_id: i64,
}

/// Looks a property up like Newtonsoft: exact name first, then case-insensitive.
fn prop<'a>(obj: &'a Map<String, Value>, name: &str) -> Option<&'a Value> {
    obj.get(name).or_else(|| {
        obj.iter()
            .find(|(k, _)| k.eq_ignore_ascii_case(name))
            .map(|(_, v)| v)
    })
}

fn as_i64(v: &Value, path: &str) -> Result<i64, SpawnJsonError> {
    let ty = || SpawnJsonError::Type {
        path: path.to_string(),
        expected: "Int64",
    };
    match v {
        Value::Null => Err(SpawnJsonError::Null(path.to_string())),
        Value::Number(n) => match (n.as_i64(), n.as_f64()) {
            (Some(i), _) => Ok(i),
            (None, _) if n.is_u64() => Err(SpawnJsonError::Overflow(path.to_string())),
            // Newtonsoft: Convert.ChangeType(double, long) (banker's rounding, range checked)
            (None, Some(f)) => {
                let r = f.round_ties_even();
                if (-9.223_372_036_854_776e18..9.223_372_036_854_776e18).contains(&r) {
                    Ok(r as i64)
                } else {
                    Err(SpawnJsonError::Overflow(path.to_string()))
                }
            }
            (None, None) => Err(ty()),
        },
        Value::String(s) => s.trim().parse().map_err(|_| ty()),
        _ => Err(ty()),
    }
}

fn as_f32(v: &Value, path: &str) -> Result<f32, SpawnJsonError> {
    let ty = || SpawnJsonError::Type {
        path: path.to_string(),
        expected: "Single",
    };
    match v {
        Value::Null => Err(SpawnJsonError::Null(path.to_string())),
        Value::Number(n) => n.as_f64().map(|f| f as f32).ok_or_else(ty),
        Value::String(s) => s.trim().parse().map_err(|_| ty()),
        _ => Err(ty()),
    }
}

fn as_i8(v: &Value, path: &str) -> Result<i8, SpawnJsonError> {
    let i = as_i64(v, path)?;
    i8::try_from(i).map_err(|_| SpawnJsonError::Overflow(path.to_string()))
}

impl WorldSpawnPositionOld {
    fn from_json(obj: &Map<String, Value>, path: &str) -> Result<Self, SpawnJsonError> {
        let mut p = Self::default();
        let field = |n: &str| format!("{path}.{n}");
        if let Some(v) = prop(obj, "X") {
            p.x = as_f32(v, &field("X"))?;
        }
        if let Some(v) = prop(obj, "Y") {
            p.y = as_f32(v, &field("Y"))?;
        }
        if let Some(v) = prop(obj, "Z") {
            p.z = as_f32(v, &field("Z"))?;
        }
        if let Some(v) = prop(obj, "RotationX") {
            p.rotation_x = as_i8(v, &field("RotationX"))?;
        }
        if let Some(v) = prop(obj, "RotationY") {
            p.rotation_y = as_i8(v, &field("RotationY"))?;
        }
        if let Some(v) = prop(obj, "RotationZ") {
            p.rotation_z = as_i8(v, &field("RotationZ"))?;
        }
        Ok(p)
    }
}

impl NpcSpawnerOld {
    fn from_json(v: &Value, path: &str) -> Result<Self, SpawnJsonError> {
        let Value::Object(obj) = v else {
            return Err(SpawnJsonError::Type {
                path: path.to_string(),
                expected: "NpcSpawnerOld",
            });
        };
        let mut s = Self::default();
        let field = |n: &str| format!("{path}.{n}");
        if let Some(v) = prop(obj, "Id") {
            s.id = as_i64(v, &field("Id"))?;
        }
        if let Some(v) = prop(obj, "UnitId") {
            s.unit_id = as_i64(v, &field("UnitId"))?;
        }
        if let Some(v) = prop(obj, "Count") {
            s.count = as_i64(v, &field("Count"))?;
        }
        if let Some(v) = prop(obj, "Position") {
            s.position = match v {
                Value::Null => None,
                Value::Object(p) => Some(WorldSpawnPositionOld::from_json(p, &field("Position"))?),
                _ => {
                    return Err(SpawnJsonError::Type {
                        path: field("Position"),
                        expected: "WorldSpawnPositionOld",
                    })
                }
            };
        }
        if let Some(v) = prop(obj, "Scale") {
            s.scale = as_f32(v, &field("Scale"))?;
        }
        if let Some(v) = prop(obj, "FuncGroupId") {
            s.func_group_id = as_i64(v, &field("FuncGroupId"))?;
        }
        Ok(s)
    }

    /// `JsonConvert.DeserializeObject<List<NpcSpawnerOld>>`: `None` for a JSON `null`.
    pub fn list_from_json(v: &Value) -> Result<Option<Vec<Self>>, SpawnJsonError> {
        match v {
            Value::Null => Ok(None),
            Value::Array(items) => items
                .iter()
                .enumerate()
                .map(|(i, it)| Self::from_json(it, &format!("[{i}]")))
                .collect::<Result<Vec<_>, _>>()
                .map(Some),
            _ => Err(SpawnJsonError::Type {
                path: String::new(),
                expected: "List<NpcSpawnerOld>",
            }),
        }
    }
}

// ---- rotation helpers (Program.cs) ----------------------------------------------------------

/// `ConvertSbyteDirectionToDegree`.
pub fn convert_sbyte_direction_to_degree(direction: i8) -> f32 {
    let angle = f32::from(direction) * (360f32 / 128.0);
    angle % 360f32
}

/// `ConvertDegreeToSByteDirection` (unused by the conversion, ported for completeness).
#[allow(dead_code)]
pub fn convert_degree_to_sbyte_direction(degree: f32) -> i8 {
    let degree = degree % 360f32;
    let mut res = (degree / (360f32 / 128.0)) as i8;
    if res > 85 {
        res = ((degree - 360.0) / (360f32 / 128.0)) as i8;
    }
    res
}

/// `FromQuaternion(x, y, z, w)`: Euler angles (roll, pitch, yaw) in radians.
pub fn from_quaternion(x: f32, y: f32, z: f32, w: f32) -> [f32; 3] {
    // roll (x-axis rotation)
    let sin_r_cos_p = 2.0 * (w * x + y * z);
    let cos_r_cos_p = 1.0 - 2.0 * (x * x + y * y);
    let roll = sin_r_cos_p.atan2(cos_r_cos_p);
    // pitch (y-axis rotation)
    let sin_p = 2.0 * (w * y - z * x);
    let pitch = if sin_p.abs() >= 1.0 {
        (std::f32::consts::PI / 2.0).copysign(sin_p)
    } else {
        sin_p.asin()
    };
    // yaw (z-axis rotation)
    let sin_y_cos_p = 2.0 * (w * z + x * y);
    let cos_y_cos_p = 1.0 - 2.0 * (y * y + z * z);
    let yaw = sin_y_cos_p.atan2(cos_y_cos_p);
    [roll, pitch, yaw]
}

/// `MathF.Round(x, 4, MidpointRounding.ToEven)`.
pub fn round4(x: f32) -> f32 {
    if x.abs() < 1e8 {
        (x * 10000f32).round_ties_even() / 10000f32
    } else {
        x
    }
}

/// Converts one old spawner (the loop body of `ConvertNpcSpawnsFile`). `is_quat_rotation` is
/// set for doodad files, whose rotations were stored as quaternion components.
pub fn convert_spawner(
    os: &NpcSpawnerOld,
    pos: &WorldSpawnPositionOld,
    is_quat_rotation: bool,
) -> NpcSpawnerNew {
    let mut np = WorldSpawnPositionNew {
        x: pos.x,
        y: pos.y,
        z: pos.z,
        ..Default::default()
    };
    if !is_quat_rotation {
        np.roll = round4(convert_sbyte_direction_to_degree(pos.rotation_x)) % 360f32;
        np.pitch = round4(convert_sbyte_direction_to_degree(pos.rotation_y)) % 360f32;
        np.yaw = round4(convert_sbyte_direction_to_degree(pos.rotation_z)) % 360f32;
    } else {
        // Special consideration for doodads, as those where stored wrong originally
        let y = (convert_sbyte_direction_to_degree(pos.rotation_x) % 360f32) / 360f32;
        let x = (convert_sbyte_direction_to_degree(pos.rotation_y) % 360f32) / 360f32;
        let z = (convert_sbyte_direction_to_degree(pos.rotation_z) % 360f32) / 360f32;
        let ww = 1f32 - ((x * x) + (y * y) + (z * z));
        let w = ww.sqrt();
        let v = from_quaternion(x, y, z, w);
        let pi = std::f32::consts::PI;
        np.roll = round4(v[0] / pi * 180f32) % 360f32;
        np.pitch = round4(v[1] / pi * 180f32) % 360f32;
        np.yaw = round4(v[2] / pi * 180f32) % 360f32;
    }
    NpcSpawnerNew {
        id: os.id,
        unit_id: os.unit_id,
        count: os.count,
        position: np,
        // Fix the zero-scale in the old files
        scale: if os.scale > 0.0 { os.scale } else { 1.0 },
        func_group_id: os.func_group_id,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn deserializes_like_newtonsoft() {
        let v: Value = serde_json::from_str(
            r#"[{"id": 5, "UnitId": "7", "Position": {"x": 1.5, "Y": 2, "RotationZ": -64}, "Extra": true},
                {"Id": 1, "UnitId": 2, "Count": 3, "Scale": 0, "FuncGroupId": 9, "Position": null}]"#,
        )
        .unwrap();
        let list = NpcSpawnerOld::list_from_json(&v).unwrap().unwrap();
        assert_eq!(list[0].id, 5);
        assert_eq!(list[0].unit_id, 7);
        assert_eq!(list[0].count, 1);
        assert_eq!(list[0].scale, 1.0);
        let p = list[0].position.unwrap();
        assert_eq!((p.x, p.y, p.rotation_z), (1.5, 2.0, -64));
        assert_eq!(list[1].position, None);
        assert_eq!(list[1].count, 3);
        assert!(NpcSpawnerOld::list_from_json(&Value::Null)
            .unwrap()
            .is_none());
        let bad: Value = serde_json::from_str(r#"[{"Count": null}]"#).unwrap();
        assert!(matches!(
            NpcSpawnerOld::list_from_json(&bad),
            Err(SpawnJsonError::Null(_))
        ));
        let bad: Value = serde_json::from_str(r#"[{"Position": {"RotationX": 200}}]"#).unwrap();
        assert!(matches!(
            NpcSpawnerOld::list_from_json(&bad),
            Err(SpawnJsonError::Overflow(_))
        ));
    }

    #[test]
    fn rotations() {
        assert_eq!(convert_sbyte_direction_to_degree(64), 180.0);
        assert_eq!(convert_sbyte_direction_to_degree(-128), -360.0 % 360.0);
        assert_eq!(convert_degree_to_sbyte_direction(180.0), 64);
        assert_eq!(convert_degree_to_sbyte_direction(270.0), -32);
        let os = NpcSpawnerOld {
            scale: 0.0,
            ..Default::default()
        };
        let pos = WorldSpawnPositionOld {
            rotation_z: 32,
            ..Default::default()
        };
        let n = convert_spawner(&os, &pos, false);
        assert_eq!(n.position.yaw, 90.0);
        assert_eq!(n.scale, 1.0);
        let q = convert_spawner(&os, &pos, true);
        assert!(q.position.yaw > 0.0 && q.position.roll == 0.0);
    }
}
