//! Helpers shared by the GM commands: the C# parsing (`int.TryParse`, `float.TryParse`,
//! `bool.TryParse`, ...) and formatting (`float.ToString()`, `"F1"`, `"#,0.#"`) rules, the
//! `WorldManager` lookups the scripts use (`GetTargetOrSelf`, `GetAround<T>(obj, radius)`) and
//! `Transform.ToString()` / `ToFullString()`.

use crate::data::world_templates::REGION_SIZE;
use crate::models::transform::PositionAndRotation;
use crate::server::GameServer;
use crate::systems::services::webapi::json::{dotnet_double_string, dotnet_single_string};
use crate::utils::numeric::rad_to_deg_f32;
use crate::world::{PlayerRef, WorldInstance, WorldObject};

// ---------------------------------------------------------------------------------------------
// Parsing (invariant culture: the server runs with `CultureInfo.InvariantCulture`)
// ---------------------------------------------------------------------------------------------

/// `NumberStyles.Integer`: surrounding white space and one leading sign.
fn parse_integer(text: &str) -> Option<i128> {
    let t = text.trim_matches(|c: char| c.is_ascii_whitespace());
    let (neg, digits) = match t.as_bytes().first() {
        Some(b'-') => (true, &t[1..]),
        Some(b'+') => (false, &t[1..]),
        _ => (false, t),
    };
    if digits.is_empty() || !digits.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    // Leading zeros are allowed; cap the length so huge inputs fail like an overflow.
    let trimmed = digits.trim_start_matches('0');
    if trimmed.len() > 38 {
        return None;
    }
    let v: i128 = if trimmed.is_empty() {
        0
    } else {
        trimmed.parse().ok()?
    };
    Some(if neg { -v } else { v })
}

/// `T.TryParse(text, out v)` for the integer types (`int`, `uint`, `short`, `ushort`, `byte`,
/// `sbyte`, `long`, `ulong`).
pub fn try_parse<T: TryFrom<i128>>(text: &str) -> Option<T> {
    parse_integer(text).and_then(|v| T::try_from(v).ok())
}

/// `NumberStyles.Float | NumberStyles.AllowThousands` (the default of `float.TryParse` /
/// `double.TryParse`), invariant culture: white space, a leading sign, `,` group separators
/// before the decimal point, one `.`, an exponent; `Infinity` / `-Infinity` / `NaN`.
fn float_text(text: &str) -> Option<String> {
    let t = text.trim_matches(|c: char| c.is_ascii_whitespace());
    let (neg, body) = match t.as_bytes().first() {
        Some(b'-') => (true, &t[1..]),
        Some(b'+') => (false, &t[1..]),
        _ => (false, t),
    };
    let sign = if neg { "-" } else { "" };
    for (sym, v) in [("Infinity", "inf"), ("∞", "inf"), ("NaN", "NaN")] {
        if body.eq_ignore_ascii_case(sym) {
            return Some(format!("{sign}{v}"));
        }
    }
    let (mantissa, exponent) = match body.find(['e', 'E']) {
        Some(i) => (&body[..i], Some(&body[i + 1..])),
        None => (body, None),
    };
    let (int_part, frac_part) = match mantissa.split_once('.') {
        Some((i, f)) => (i, f),
        None => (mantissa, ""),
    };
    if int_part.starts_with(',') {
        return None;
    }
    let int_digits: String = int_part.chars().filter(|&c| c != ',').collect();
    if !int_digits.bytes().all(|b| b.is_ascii_digit())
        || !frac_part.bytes().all(|b| b.is_ascii_digit())
    {
        return None;
    }
    if int_digits.is_empty() && frac_part.is_empty() {
        return None;
    }
    let mut s = format!(
        "{sign}{}.{}",
        if int_digits.is_empty() {
            "0"
        } else {
            &int_digits
        },
        if frac_part.is_empty() { "0" } else { frac_part }
    );
    if let Some(e) = exponent {
        let (eneg, edigits) = match e.as_bytes().first() {
            Some(b'-') => (true, &e[1..]),
            Some(b'+') => (false, &e[1..]),
            _ => (false, e),
        };
        if edigits.is_empty() || !edigits.bytes().all(|b| b.is_ascii_digit()) {
            return None;
        }
        s.push('e');
        if eneg {
            s.push('-');
        }
        s.push_str(edigits);
    }
    Some(s)
}

/// `float.TryParse(text, out v)`.
pub fn parse_f32(text: &str) -> Option<f32> {
    float_text(text).and_then(|s| s.parse().ok())
}

/// `double.TryParse(text, out v)`.
pub fn parse_f64(text: &str) -> Option<f64> {
    float_text(text).and_then(|s| s.parse().ok())
}

/// `bool.TryParse(text, out v)`: "True" / "False" (any case), surrounding white space and
/// trailing NULs ignored.
pub fn parse_bool(text: &str) -> Option<bool> {
    let t = text
        .trim_matches(|c: char| c.is_whitespace())
        .trim_end_matches('\0')
        .trim_matches(|c: char| c.is_whitespace());
    if t.eq_ignore_ascii_case("true") {
        Some(true)
    } else if t.eq_ignore_ascii_case("false") {
        Some(false)
    } else {
        None
    }
}

/// `bool.ToString()`.
pub fn bool_str(v: bool) -> &'static str {
    if v {
        "True"
    } else {
        "False"
    }
}

// ---------------------------------------------------------------------------------------------
// Formatting
// ---------------------------------------------------------------------------------------------

/// `float.ToString()` (shortest round-trip).
pub fn f32s(v: f32) -> String {
    dotnet_single_string(v)
}

/// `double.ToString()` (shortest round-trip).
pub fn f64s(v: f64) -> String {
    dotnet_double_string(v)
}

/// `value.ToString("F{decimals}")` / `{value:F1}` / `{value:0.0}` (half away from zero on
/// the decimal expansion, like .NET).
pub fn fixed(v: f64, decimals: usize) -> String {
    if v.is_nan() {
        return "NaN".into();
    }
    if v.is_infinite() {
        return if v > 0.0 { "Infinity" } else { "-Infinity" }.into();
    }
    let s = format!("{:.*}", decimals + 20, v.abs());
    let rounded = round_half_away(&s, decimals);
    if v.is_sign_negative() && rounded.bytes().any(|b| (b'1'..=b'9').contains(&b)) {
        format!("-{rounded}")
    } else if v.is_sign_negative() && v != 0.0 {
        // .NET Core 3.0+ keeps the sign of a negative value that rounds to zero.
        format!("-{rounded}")
    } else {
        rounded
    }
}

/// Rounds a non-negative decimal string (`digits.digits`) to `decimals` places, half away
/// from zero.
fn round_half_away(s: &str, decimals: usize) -> String {
    let (int_part, frac_part) = s.split_once('.').unwrap_or((s, ""));
    let mut digits: Vec<u8> = int_part.bytes().map(|b| b - b'0').collect();
    let frac: Vec<u8> = frac_part.bytes().map(|b| b - b'0').collect();
    digits.extend(frac.iter().take(decimals));
    let round_up = frac.get(decimals).is_some_and(|d| *d >= 5);
    if round_up {
        let mut i = digits.len();
        loop {
            if i == 0 {
                digits.insert(0, 1);
                break;
            }
            i -= 1;
            if digits[i] == 9 {
                digits[i] = 0;
            } else {
                digits[i] += 1;
                break;
            }
        }
    }
    let int_len = digits.len() - decimals;
    let mut out: String = digits[..int_len]
        .iter()
        .map(|d| (d + b'0') as char)
        .collect();
    if decimals > 0 {
        out.push('.');
        out.extend(digits[int_len..].iter().map(|d| (d + b'0') as char));
    }
    out
}

/// The custom format `"#,0.#"` (group separators, at most one decimal).
pub fn group_1(v: f64) -> String {
    let s = fixed(v, 1);
    let (sign, body) = match s.strip_prefix('-') {
        Some(b) => ("-", b),
        None => ("", s.as_str()),
    };
    let (int_part, frac) = body.split_once('.').unwrap_or((body, "0"));
    let mut grouped = String::new();
    for (i, c) in int_part.chars().enumerate() {
        if i > 0 && (int_part.len() - i) % 3 == 0 {
            grouped.push(',');
        }
        grouped.push(c);
    }
    let frac = frac.trim_end_matches('0');
    let body = if frac.is_empty() {
        grouped
    } else {
        format!("{grouped}.{frac}")
    };
    if sign == "-" && body == "0" {
        // `#,0.#` drops the sign of a value that rounds to zero.
        return body;
    }
    format!("{sign}{body}")
}

/// `PositionAndRotation.ToString()`:
/// `X:{0:#,0.#} Y:{1:#,0.#} Z:{2:#,0.#}  r:{3:#,0.#}° p:{4:#,0.#}° y:{5:#,0.#}°`.
pub fn pos_rot_string(p: &PositionAndRotation) -> String {
    let g = |v: f32| group_1(f64::from(v));
    format!(
        "X:{} Y:{} Z:{}  r:{}° p:{}° y:{}°",
        g(p.x),
        g(p.y),
        g(p.z),
        g(rad_to_deg_f32(p.rot_x)),
        g(rad_to_deg_f32(p.rot_y)),
        g(rad_to_deg_f32(p.rot_z))
    )
}

/// `System.Numerics.Vector3.ToString()`: `<x, y, z>` (each `float.ToString()`, invariant
/// culture list separator).
pub fn vector3_string(v: [f32; 3]) -> String {
    format!("<{}, {}, {}>", f32s(v[0]), f32s(v[1]), f32s(v[2]))
}

/// `Transform.ToFullString(isFirstInList, chatFormatted)` (`Transform.ToString()` is
/// `ToFullString()`). Sticky parents are not modelled in Rust (none exist outside slaves).
pub fn transform_full_string(
    server: &GameServer,
    obj: &WorldObject,
    is_first_in_list: bool,
    chat_formatted: bool,
) -> String {
    let (white, green, restore, lf) = if chat_formatted {
        ("|cFFFFFFFF", "|cFF00FF00", "|r", "\n")
    } else {
        ("", "", "", "")
    };
    let world = server.world.world_of(obj);
    let t = obj.transform();
    let mut res = format!(
        "[i{}] ",
        world
            .as_ref()
            .map_or_else(|| "?".to_string(), |w| w.id.to_string())
    );
    let parent = if t.parent_obj_id != 0 {
        world.as_ref().and_then(|w| w.object(t.parent_obj_id))
    } else {
        None
    };
    if is_first_in_list && parent.is_some() {
        let wp = world
            .as_ref()
            .map_or_else(|| t.local(), |w| w.world_pos_rot(obj));
        res += &format!("[{white}{}{restore}] {lf}=> ", pos_rot_string(&wp));
    }
    res += &pos_rot_string(&t.local());
    if let Some(parent) = parent {
        res += "\n on ( ";
        if parent.is_unit() || parent.as_doodad().is_some() {
            let name = parent.with_base_unit(|b| b.name().to_string());
            if !name.is_empty() {
                res += &format!("{green}{name}{restore} ");
            }
            res += &format!("#{white}{}{restore} ", parent.obj_id());
        }
        res += &transform_full_string(server, &parent, false, chat_formatted);
        res += &format!(" ){lf}");
    }
    res
}

// ---------------------------------------------------------------------------------------------
// World lookups
// ---------------------------------------------------------------------------------------------

/// `character.CurrentTarget` (the object behind the target obj id, in any instance).
pub fn current_target(server: &GameServer, player: &PlayerRef) -> Option<WorldObject> {
    let target = player.data().unit.current_target;
    if target == 0 {
        return None;
    }
    server.world.object(target)
}

/// `WorldManager.Instance.GetTargetOrSelf(character, targetName, out firstNonNameArgument)`.
pub fn get_target_or_self(
    server: &GameServer,
    character: &PlayerRef,
    target_name: Option<&str>,
) -> (PlayerRef, usize) {
    if let Some(name) = target_name.filter(|n| !n.trim().is_empty()) {
        if let Some(p) = server.world.character_by_name(name) {
            return (p, 1);
        }
    }
    if let Some(t) = current_target(server, character).and_then(|t| t.as_player().cloned()) {
        return (t, 0);
    }
    (character.clone(), 0)
}

/// `character.ParentWorld`.
pub fn parent_world(
    server: &GameServer,
    player: &PlayerRef,
) -> Option<std::sync::Arc<WorldInstance>> {
    server
        .world
        .world_of(&WorldObject::from(player))
        .or_else(|| server.world.world(player.data().unit.transform.instance_id))
}

/// `RadiusFitsCurrentRegion` (float remainders, as C#).
fn radius_fits_current_region(x: f32, y: f32, radius: f32) -> bool {
    let region = REGION_SIZE as f32;
    let x_mod = x % region;
    if x_mod - radius < 0.0 || x_mod + radius > region {
        return false;
    }
    let y_mod = y % region;
    !(y_mod - radius < 0.0 || y_mod + radius > region)
}

/// `WorldManager.GetAround<GameObject>(obj, radius)`: every object (any kind) whose world
/// position is inside the 2D radius, `obj` excluded, in region list order.
pub fn around_radius(world: &WorldInstance, obj: &WorldObject, radius: f32) -> Vec<WorldObject> {
    let mut result = Vec::new();
    if radius <= 0.0 {
        return result;
    }
    let Some((inst, rx, ry)) = obj.region() else {
        return result;
    };
    if inst != world.id {
        return result;
    }
    let origin = world.world_pos_rot(obj);
    let sqrad = radius * radius;
    let regions = if radius_fits_current_region(origin.x, origin.y, radius) {
        vec![(rx, ry)]
    } else {
        world.neighbors((rx, ry))
    };
    let id = obj.obj_id();
    for r in regions {
        for o in world.objects_in_region(r) {
            if o.obj_id() == id {
                continue;
            }
            let p = world.world_pos_rot(&o);
            let dx = (p.x - origin.x) * (p.x - origin.x);
            if dx > sqrad {
                continue;
            }
            let dy = (p.y - origin.y) * (p.y - origin.y);
            if dx + dy < sqrad {
                result.push(o);
            }
        }
    }
    result
}

/// `character.Id != targetPlayer.Id`.
pub fn is_other(a: &PlayerRef, b: &PlayerRef) -> bool {
    a.character_id != b.character_id
}

/// `character.Name`.
pub fn name_of(p: &PlayerRef) -> String {
    p.data().unit.name.clone()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn integer_parsing() {
        assert_eq!(try_parse::<i32>(" -12 "), Some(-12));
        assert_eq!(try_parse::<i32>("+7"), Some(7));
        assert_eq!(try_parse::<u32>("-1"), None);
        assert_eq!(try_parse::<u8>("256"), None);
        assert_eq!(try_parse::<i16>("1.5"), None);
        assert_eq!(try_parse::<i32>(""), None);
        assert_eq!(
            try_parse::<u64>("000000000000000000000000000000000000000000001"),
            Some(1)
        );
    }

    #[test]
    fn float_parsing() {
        assert_eq!(parse_f32("1.5"), Some(1.5));
        assert_eq!(parse_f32("-1,000.25"), Some(-1000.25));
        assert_eq!(parse_f32("1e3"), Some(1000.0));
        assert_eq!(parse_f32(".5"), Some(0.5));
        assert_eq!(parse_f32("5."), Some(5.0));
        assert_eq!(parse_f32("2-"), None);
        assert_eq!(parse_f32("abc"), None);
        assert_eq!(parse_f64("Infinity"), Some(f64::INFINITY));
    }

    #[test]
    fn bool_parsing() {
        assert_eq!(parse_bool("TRUE"), Some(true));
        assert_eq!(parse_bool(" false "), Some(false));
        assert_eq!(parse_bool("1"), None);
    }

    #[test]
    fn formatting() {
        assert_eq!(fixed(1.25, 1), "1.3");
        assert_eq!(fixed(-1.25, 1), "-1.3");
        assert_eq!(fixed(9.96, 1), "10.0");
        assert_eq!(fixed(12.0, 0), "12");
        assert_eq!(fixed(0.5, 0), "1");
        assert_eq!(group_1(12345.67), "12,345.7");
        assert_eq!(group_1(100.0), "100");
        assert_eq!(group_1(-0.01), "0");
        assert_eq!(group_1(-1234.0), "-1,234");
        let p = PositionAndRotation::new(1000.0, 2.25, -3.0, 0.0, 0.0, std::f32::consts::PI);
        assert_eq!(pos_rot_string(&p), "X:1,000 Y:2.3 Z:-3  r:0° p:0° y:180°");
        assert_eq!(vector3_string([1.0, 2.5, -3.0]), "<1, 2.5, -3>");
    }
}

// ---- objects helpers ----
// Shared by the object commands (Around, Despawn, Spawn, Nwrite, ...) and the Doodad / Npc /
// Slave / Gimmick sub commands.

use std::collections::HashMap;
use std::path::{Path, PathBuf};
use std::sync::Arc;

use crate::data::world_templates::WorldSpawnPosition;
use crate::models::transform::Transform;
use crate::systems::npcs::doodad::spawner::DoodadSpawner;
use crate::systems::npcs::npc::spawner::NpcSpawner;
use crate::systems::services::webapi::json::JValue;
use crate::utils::json_models::JsonPosition;

/// The message of a .NET `NullReferenceException`.
pub const NRE: &str = "Object reference not set to an instance of an object.";

/// A .NET `NullReferenceException` escaping a command.
pub fn nre() -> anyhow::Error {
    anyhow::anyhow!(NRE)
}

/// `value.ToString("0.#")` (at most one decimal; the sign of a value that rounds to zero is
/// dropped like [`group_1`]).
pub fn fmt_0_1(v: f32) -> String {
    group_1(f64::from(v)).replace(',', "")
}

/// `obj.GetType().FullName` of a world object (used in .NET cast exception messages).
pub fn dotnet_type_name(obj: &WorldObject) -> &'static str {
    match obj {
        WorldObject::Player(_) => "AAEmu.Game.Models.Game.Char.Character",
        WorldObject::Npc(n) => {
            if n.data().portal.is_some() {
                "AAEmu.Game.Models.Game.Units.Portal"
            } else {
                "AAEmu.Game.Models.Game.NPChar.Npc"
            }
        }
        WorldObject::Doodad(d) => {
            if d.data().coffer.is_some() {
                "AAEmu.Game.Models.Game.DoodadObj.DoodadCoffer"
            } else {
                "AAEmu.Game.Models.Game.DoodadObj.Doodad"
            }
        }
        WorldObject::Slave(_) => "AAEmu.Game.Models.Game.Units.Slave",
        WorldObject::Mate(_) => "AAEmu.Game.Models.Game.Units.Mate",
        WorldObject::House(_) => "AAEmu.Game.Models.Game.Housing.House",
        WorldObject::Transfer(_) => "AAEmu.Game.Models.Game.Units.Transfer",
        WorldObject::Shipyard(_) => "AAEmu.Game.Models.Game.Shipyard.Shipyard",
        WorldObject::Gimmick(_) => "AAEmu.Game.Models.Game.Gimmicks.Gimmick",
    }
}

/// The .NET `InvalidCastException` of `(T)obj`.
pub fn invalid_cast(obj: &WorldObject, target: &str) -> anyhow::Error {
    anyhow::anyhow!(
        "Unable to cast object of type '{}' to type '{target}'.",
        dotnet_type_name(obj)
    )
}

/// `obj.ParentWorld` (falls back to the transform's instance).
pub fn world_of_obj(
    server: &GameServer,
    obj: &WorldObject,
) -> Option<std::sync::Arc<WorldInstance>> {
    crate::systems::skills::geometry::world_of(server, obj)
}

/// `obj.Transform.World`.
pub fn world_pos(server: &GameServer, obj: &WorldObject) -> PositionAndRotation {
    match world_of_obj(server, obj) {
        Some(w) => w.world_pos_rot(obj),
        None => obj.transform().local(),
    }
}

/// `obj.Transform.CloneDetached()`: the world position as a parentless transform.
pub fn clone_detached(server: &GameServer, obj: &WorldObject) -> Transform {
    let t = obj.transform();
    let w = world_pos(server, obj);
    Transform {
        instance_id: t.instance_id,
        world_id: t.world_id,
        zone_id: t.zone_id,
        x: w.x,
        y: w.y,
        z: w.z,
        rot_x: w.rot_x,
        rot_y: w.rot_y,
        rot_z: w.rot_z,
        parent_obj_id: 0,
    }
}

/// `Transform.CloneAsSpawnPosition()` of a parentless transform.
pub fn spawn_position(t: &Transform) -> WorldSpawnPosition {
    WorldSpawnPosition {
        world_id: t.world_id,
        zone_id: t.zone_id,
        x: t.x,
        y: t.y,
        z: t.z,
        yaw: t.rot_z,
        pitch: t.rot_y,
        roll: t.rot_x,
    }
}

/// `GameObject.Hide(); GameObject.Show();` (the zone changes of `Show` are dispatched).
pub fn hide_show(server: &GameServer, world: &WorldInstance, obj: &WorldObject) {
    world.hide(obj);
    let changes = world.show(obj, &server.view_ctx());
    crate::world::session::handle_zone_changes(server, changes);
}

/// `WorldManager.GetAround<T>(character, radius)` with a kind filter.
pub fn around_of<T>(
    server: &GameServer,
    player: &PlayerRef,
    radius: f32,
    f: impl Fn(&WorldObject) -> Option<T>,
) -> Vec<T> {
    let obj = WorldObject::from(player);
    match parent_world(server, player) {
        Some(w) => around_radius(&w, &obj, radius)
            .iter()
            .filter_map(f)
            .collect(),
        None => Vec::new(),
    }
}

/// `NpcManager.GetSpawnName(id)` / `DoodadManager.GetSpawnName(id)`: the titles of
/// `Data/Creatures.xml` (both C# managers load `Creature.GetAllCreatures()`).
pub fn spawn_names(server: &GameServer) -> HashMap<u32, String> {
    let path = server.config.app_path.join("Data").join("Creatures.xml");
    aaemu_commons::utils::creature::Creature::load_file(path)
        .into_iter()
        .map(|(id, c)| (id, c.title))
        .collect()
}

/// `Path.Combine(FileManager.AppPath, "Data", "Worlds", worldName)`.
pub fn world_data_dir(server: &GameServer, world_name: &str) -> PathBuf {
    server
        .config
        .app_path
        .join("Data")
        .join("Worlds")
        .join(world_name)
}

/// The spawn file model kinds (`JsonDoodadSpawns`, `JsonNpcSpawns`, `JsonSlaveSpawns`).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum SpawnFileKind {
    Doodad,
    Npc,
    Slave,
}

/// One `JsonDoodadSpawns` / `JsonNpcSpawns` / `JsonSlaveSpawns` entry with the C# defaults of
/// a deserialized object (`null` reference members are `None`).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct JsonSpawnEntry {
    pub id: u32,
    pub unit_id: u32,
    pub title: Option<String>,
    /// `RelatedIds` / `NpcSpawnerIds`.
    pub ids: Option<Vec<u32>>,
    pub follow_path: Option<String>,
    pub position: Option<JsonPosition>,
    pub scale: f32,
    pub func_group_id: u32,
}

fn json_position_of(v: &serde_json::Value) -> Result<JsonPosition, String> {
    use crate::systems::npcs::json::f32_or;
    let o = v
        .as_object()
        .ok_or_else(|| format!("Position: expected an object, got {v}"))?;
    Ok(JsonPosition {
        x: f32_or(o, "X", 0.0)?,
        y: f32_or(o, "Y", 0.0)?,
        z: f32_or(o, "Z", 0.0)?,
        roll: f32_or(o, "Roll", 0.0)?,
        pitch: f32_or(o, "Pitch", 0.0)?,
        yaw: f32_or(o, "Yaw", 0.0)?,
    })
}

/// `JsonHelper.DeserializeObject<List<T>>(contents)` (Newtonsoft defaults: case-insensitive
/// members, unknown members ignored). `Ok(None)` = the document is `null`; `Err` = the C#
/// deserialization exception.
pub fn parse_spawn_entries(
    text: &str,
    kind: SpawnFileKind,
) -> Result<Option<Vec<JsonSpawnEntry>>, String> {
    use crate::systems::npcs::json::{self as j, prop};
    use serde_json::Value;
    let v = j::parse(text).map_err(|e| e.to_string())?;
    if v.is_null() {
        return Ok(None);
    }
    let list = j::array(&v)?;
    let mut out = Vec::with_capacity(list.len());
    for e in list {
        let Value::Object(o) = e else {
            return Err(format!("expected an object, got {e}"));
        };
        let opt_str = |name: &str| -> Result<Option<String>, String> {
            match prop(o, name) {
                None | Some(Value::Null) => Ok(None),
                Some(_) => j::str_or(o, name, "").map(Some),
            }
        };
        let ids_name = match kind {
            SpawnFileKind::Doodad => "RelatedIds",
            _ => "NpcSpawnerIds",
        };
        let mut entry = JsonSpawnEntry {
            id: j::u32_or(o, "Id", 0)?,
            unit_id: j::u32_or(o, "UnitId", 0)?,
            position: match prop(o, "Position") {
                None | Some(Value::Null) => None,
                Some(p) => Some(json_position_of(p)?),
            },
            ..JsonSpawnEntry::default()
        };
        if kind != SpawnFileKind::Slave {
            entry.title = opt_str("Title")?;
            entry.ids = match prop(o, ids_name) {
                None | Some(Value::Null) => None,
                Some(_) => Some(j::u32_list(o, ids_name)?),
            };
            entry.scale = j::f32_or(o, "Scale", 0.0)?;
        }
        match kind {
            SpawnFileKind::Doodad => entry.func_group_id = j::u32_or(o, "FuncGroupId", 0)?,
            SpawnFileKind::Npc => entry.follow_path = opt_str("FollowPath")?,
            SpawnFileKind::Slave => {}
        }
        out.push(entry);
    }
    Ok(Some(out))
}

impl JsonSpawnEntry {
    /// `JsonConvert.SerializeObject(entry)` without converters (every property in declaration
    /// order; `JsonPosition` with all six members).
    pub fn to_default_jvalue(&self, kind: SpawnFileKind) -> JValue {
        let ids = || {
            self.ids.as_ref().map_or(JValue::Null, |l| {
                JValue::Array(l.iter().map(|v| JValue::from(*v)).collect())
            })
        };
        let pos = self.position.map_or(JValue::Null, |p| {
            JValue::object([
                ("X", JValue::Single(p.x)),
                ("Y", JValue::Single(p.y)),
                ("Z", JValue::Single(p.z)),
                ("Roll", JValue::Single(p.roll)),
                ("Pitch", JValue::Single(p.pitch)),
                ("Yaw", JValue::Single(p.yaw)),
            ])
        });
        let mut m: Vec<(&str, JValue)> = vec![
            ("Id", JValue::from(self.id)),
            ("UnitId", JValue::from(self.unit_id)),
        ];
        match kind {
            SpawnFileKind::Doodad => {
                m.push(("Title", JValue::opt_str(self.title.clone())));
                m.push(("RelatedIds", ids()));
                m.push(("Position", pos));
                m.push(("Scale", JValue::Single(self.scale)));
                m.push(("FuncGroupId", JValue::from(self.func_group_id)));
            }
            SpawnFileKind::Npc => {
                m.push(("Title", JValue::opt_str(self.title.clone())));
                m.push(("NpcSpawnerIds", ids()));
                m.push(("FollowPath", JValue::opt_str(self.follow_path.clone())));
                m.push(("Position", pos));
                m.push(("Scale", JValue::Single(self.scale)));
            }
            SpawnFileKind::Slave => m.push(("Position", pos)),
        }
        JValue::object(m)
    }

    /// `JsonConvert.SerializeObject(entry, new JsonModelsConverter())`.
    pub fn to_converter_jvalue(&self, kind: SpawnFileKind) -> JValue {
        use crate::utils::json_models::{JsonDoodadSpawns, JsonNpcSpawns};
        match kind {
            SpawnFileKind::Doodad => JsonDoodadSpawns {
                id: self.id,
                unit_id: self.unit_id,
                title: self.title.clone(),
                related_ids: self.ids.clone().unwrap_or_default(),
                position: self.position,
                scale: self.scale,
                func_group_id: self.func_group_id,
            }
            .to_jvalue(),
            SpawnFileKind::Npc => JsonNpcSpawns {
                id: self.id,
                unit_id: self.unit_id,
                title: self.title.clone(),
                npc_spawner_ids: self.ids.clone().unwrap_or_default(),
                follow_path: self.follow_path.clone(),
                position: self.position,
                scale: self.scale,
            }
            .to_jvalue(),
            // No converter for JsonSlaveSpawns: the default members, JsonPosition converted.
            SpawnFileKind::Slave => JValue::object([
                ("Id", JValue::from(self.id)),
                ("UnitId", JValue::from(self.unit_id)),
                (
                    "Position",
                    self.position.map_or(JValue::Null, |p| p.to_jvalue()),
                ),
            ]),
        }
    }
}

/// `JsonConvert.SerializeObject(list, Formatting.Indented[, new JsonModelsConverter()])`.
pub fn spawn_entries_json(
    entries: &[JsonSpawnEntry],
    kind: SpawnFileKind,
    converter: bool,
) -> String {
    JValue::Array(
        entries
            .iter()
            .map(|e| {
                if converter {
                    e.to_converter_jvalue(kind)
                } else {
                    e.to_default_jvalue(kind)
                }
            })
            .collect(),
    )
    .to_json_indented()
}

/// `FileManager.GetFileContents(path)` (missing file = empty).
pub fn file_contents(path: &Path) -> String {
    std::fs::read_to_string(path).unwrap_or_default()
}

/// `string.IsNullOrWhiteSpace(contents)` (a lone BOM counts as white space here).
pub fn is_blank(contents: &str) -> bool {
    contents
        .trim_matches(|c: char| c.is_whitespace() || c == '\u{feff}')
        .is_empty()
}

/// `Directory.GetFiles(dir, "<prefix>*.json")` (file-name order). `Err` = the .NET
/// `DirectoryNotFoundException`.
pub fn json_files_with_prefix(dir: &Path, prefix: &str) -> anyhow::Result<Vec<PathBuf>> {
    let rd = std::fs::read_dir(dir)
        .map_err(|_| anyhow::anyhow!("Could not find a part of the path '{}'.", dir.display()))?;
    let mut files: Vec<(String, PathBuf)> = rd
        .filter_map(Result::ok)
        .filter(|e| e.file_type().is_ok_and(|t| t.is_file()))
        .filter_map(|e| {
            let name = e.file_name().to_string_lossy().to_ascii_lowercase();
            (name.starts_with(prefix) && name.ends_with(".json")).then(|| (name, e.path()))
        })
        .collect();
    files.sort();
    Ok(files.into_iter().map(|(_, p)| p).collect())
}

/// `new NpcSpawner { ParentWorld, Id = 0, UnitId, Position }` +
/// `ParentWorld.SpawnManager.AddNpcSpawner(npcSpawner)`: registers the spawner(s) and returns
/// the command's own `npcSpawner` object as `AddNpcSpawner` leaves it: with the generated
/// spawner template when the NPC has no spawner in the game data (that object is never stored),
/// else without a template (the memberwise clones stored in the spawn manager spawn the NPC on
/// the active-region tick).
pub fn add_runtime_npc_spawner(
    server: &GameServer,
    world: &Arc<WorldInstance>,
    unit_id: u32,
    position: WorldSpawnPosition,
) -> anyhow::Result<Arc<NpcSpawner>> {
    use crate::systems::npcs::doodad::spawner::{NpcSpawnerDef, SpawnerBase};
    use crate::systems::npcs::npc::spawner::{NpcSpawnerInit, NpcSpawnerShared};
    let manager = server.systems.npcs.spawns.get_or_create(world);
    let data = &server.game_data.systems.npcs.npc_spawners;
    let generated = data
        .get_spawner_ids(unit_id)
        .is_none_or(|ids| ids.is_empty());
    let fake_id = server.systems.npcs.spawns.peek_fake_spawner_id();
    manager
        .add_npc_spawner(
            server,
            NpcSpawnerDef {
                base: SpawnerBase {
                    unit_id,
                    position,
                    ..SpawnerBase::default()
                },
                count: 1,
                npc_spawner_ids: Vec::new(),
            },
        )
        .map_err(anyhow::Error::msg)?;
    let (id, ids, template) = if generated {
        (
            fake_id,
            vec![fake_id],
            data.get_npc_spawner_template(fake_id),
        )
    } else {
        // The memberwise clones append their ids to the shared `NpcSpawnerIds` list.
        (0, data.get_spawner_ids(unit_id).unwrap_or_default(), None)
    };
    Ok(Arc::new(NpcSpawner::new(
        NpcSpawnerInit {
            id,
            spawner_id: 0,
            unit_id,
            follow_path: String::new(),
            follow_npc: 0,
            respawn_time: 15,
            despawn_time: 20,
            special_link: String::new(),
            count: 1,
            npc_spawner_ids: ids,
            parent_world: Some(world.id),
            template,
            spawnable_npcs: Vec::new(),
        },
        NpcSpawnerShared::new(position),
    )))
}

/// The spawner of a doodad (`doodad.Spawner`).
pub fn doodad_spawner_of(server: &GameServer, obj_id: u32) -> Option<Arc<DoodadSpawner>> {
    server.systems.npcs.spawns.doodad_spawner_of(obj_id)
}

/// The spawner of an NPC (`npc.Spawner`).
pub fn npc_spawner_of(server: &GameServer, obj_id: u32) -> Option<Arc<NpcSpawner>> {
    server.systems.npcs.spawns.npc_spawner_of(obj_id)
}

/// `new DoodadSpawner { ParentWorld, Id = 0, UnitId, Position }`.
pub fn new_doodad_spawner(
    world_id: u32,
    unit_id: u32,
    position: WorldSpawnPosition,
) -> Arc<DoodadSpawner> {
    use crate::systems::npcs::doodad::spawner::{DoodadSpawnerDef, SpawnerBase};
    Arc::new(DoodadSpawner::new(
        DoodadSpawnerDef {
            base: SpawnerBase {
                unit_id,
                position,
                ..SpawnerBase::default()
            },
            scale: 1.0,
            count: 1,
            related_ids: None,
            respawn_doodad_template_id: 0,
        },
        Some(world_id),
    ))
}

/// `GameObject.DebugName()` (`BaseUnit`: `(objId) - Name`; the type name without a name).
pub fn debug_name(obj: &WorldObject) -> String {
    let name = obj.with_base_unit(|b| b.name().to_string());
    if name.trim().is_empty() {
        format!("({}) - {}", obj.obj_id(), dotnet_type_name(obj))
    } else {
        format!("({}) - {name}", obj.obj_id())
    }
}

/// `MathUtil.CalculateAngleFrom(Transform obj1, Transform obj2)` (world positions, degrees).
pub fn angle_from(a: &Transform, b: &Transform) -> f64 {
    crate::utils::math::calculate_angle_from(a.x, a.y, b.x, b.y)
}

/// `PositionAndRotation.AddDistanceToFront(distance)`.
pub fn add_distance_to_front(t: &mut Transform, distance: f32) {
    crate::systems::vehicles::slaves::add_distance_to_front(t, distance);
}

/// `PositionAndRotation.AddDistanceToRight(distance)`.
pub fn add_distance_to_right(t: &mut Transform, distance: f32) {
    crate::systems::vehicles::slaves::add_distance_to_right(t, distance);
}

/// `(world, (Npc)character.CurrentTarget)` / `ParentWorld.GetNpc(objId)` selection shared by
/// the Npc sub commands: `Err(message)` = the red reply.
pub fn character_world(
    server: &GameServer,
    player: &PlayerRef,
) -> anyhow::Result<Arc<WorldInstance>> {
    parent_world(server, player).ok_or_else(nre)
}

/// Game data and spawn helpers for the object command tests.
#[cfg(test)]
pub mod objects_test_support {
    use std::path::PathBuf;
    use std::sync::atomic::{AtomicU32, Ordering};
    use std::sync::Arc;

    use crate::data::GameData;
    use crate::server::GameServer;
    use crate::systems::npcs::doodad::templates::DoodadTemplate;
    use crate::systems::npcs::gimmick::GimmickTemplate;
    use crate::systems::npcs::npc::data::NpcTemplate;
    use crate::systems::npcs::npc::spawner_data::{
        NpcSpawnerData, NpcSpawnerNpc, NpcSpawnerTemplate,
    };
    use crate::systems::skills::data::effects::SpawnGimmickEffect;
    use crate::systems::skills::data::{EffectKind, EffectTemplate};
    use crate::systems::vehicles::data::SlaveTemplate;
    use crate::world::{DoodadRef, NpcRef, WorldObject};

    /// An NPC template with a game-data spawner (template 10).
    pub const NPC: u32 = 7000;
    /// A doodad template (5 and 6 exist too).
    pub const DOODAD: u32 = 8000;
    pub const SLAVE: u32 = 68;
    pub const GIMMICK: u32 = 30;

    fn spawner_template(id: u32) -> NpcSpawnerTemplate {
        NpcSpawnerTemplate {
            id,
            name: format!("t{id}"),
            max_population: 1,
            test_radius_npc: 2.0,
            test_radius_pc: 2.0,
            ..NpcSpawnerTemplate::default()
        }
    }

    fn member(id: u32, spawner: u32, npc: u32) -> NpcSpawnerNpc {
        NpcSpawnerNpc {
            id,
            npc_spawner_template_id: spawner,
            member_id: npc,
            member_type: "Npc".into(),
            weight: 1.0,
            unit_id: 0,
        }
    }

    /// NPC / doodad / slave / gimmick templates, NPC spawner 10 for [`NPC`] (and spawner
    /// template 1, the source of generated spawners), the gimmick's spawn effect.
    pub fn data() -> GameData {
        let mut d = GameData::empty();
        d.systems.npcs.npc.templates.insert(
            NPC,
            NpcTemplate {
                id: NPC,
                name: "Guard".into(),
                level: 1,
                model_id: 1,
                scale: 1.0,
                ..NpcTemplate::default()
            },
        );
        for id in [DOODAD, 5, 6] {
            d.systems.npcs.doodad.templates.insert(
                id,
                DoodadTemplate {
                    id,
                    ..DoodadTemplate::default()
                },
            );
        }
        d.systems.npcs.npc_spawners = NpcSpawnerData::from_parts(
            vec![spawner_template(1), spawner_template(10)],
            vec![member(1, 1, 1), member(100, 10, NPC)],
        )
        .expect("spawner data");
        d.systems.vehicles.slave_templates.insert(
            SLAVE,
            SlaveTemplate {
                id: SLAVE,
                name: "Cart".into(),
                model_id: 300,
                level: 1,
                ..SlaveTemplate::default()
            },
        );
        d.systems.npcs.gimmick.templates.insert(
            GIMMICK,
            GimmickTemplate {
                id: GIMMICK,
                ..GimmickTemplate::default()
            },
        );
        d.systems
            .skills
            .effects
            .entry(EffectKind::SpawnGimmickEffect)
            .or_default()
            .insert(
                1,
                EffectTemplate::SpawnGimmick(SpawnGimmickEffect {
                    id: 1,
                    gimmick_id: GIMMICK,
                    offset_from_source: false,
                    offset_coordinate_id: 1,
                    offset: [0.0; 3],
                    scale: 1.0,
                    velocity_coordinate_id: 0,
                    velocity: [0.0; 3],
                    ang_vel_coordinate_id: 0,
                    ang_vel: [0.0; 3],
                }),
            );
        d
    }

    /// A command test server with [`data`]; the object ids of the test players (`0x100 + id`)
    /// are taken from the object id manager first so spawned objects never collide with them.
    pub fn server() -> Arc<GameServer> {
        let server = super::super::harness::server_with(data());
        while server.ids.object.get_next_id().expect("object id") < 0x400 {}
        server
    }

    /// An NPC of `template_id` (NpcManager.Create) spawned at (x, y, 100) in instance 0.
    pub fn spawn_npc(server: &GameServer, template_id: u32, x: f32, y: f32) -> NpcRef {
        let npc = crate::systems::npcs::npc::manager::create(server, 0, template_id)
            .expect("npc template");
        {
            let mut n = npc.data();
            n.unit.transform.instance_id = 0;
            n.unit.transform.set_xyz(x, y, 100.0);
        }
        let world = server.world.world(0).expect("world 0");
        let changes = world.spawn(&npc, &server.view_ctx());
        crate::world::session::handle_zone_changes(server, changes);
        npc
    }

    /// A doodad of `template_id` (DoodadManager.Create, no phase start) spawned at (x, y, 100)
    /// in instance 0, without a spawner.
    pub fn spawn_doodad(server: &GameServer, template_id: u32, x: f32, y: f32) -> DoodadRef {
        let world = server.world.world(0).expect("world 0");
        let doodad = crate::systems::npcs::doodad::manager::create(
            server,
            &world,
            0,
            template_id,
            None,
            true,
        )
        .expect("doodad template");
        doodad.data().transform.set_xyz(x, y, 100.0);
        crate::systems::npcs::doodad::manager::spawn(server, &world, &doodad);
        let _ = WorldObject::from(&doodad);
        doodad
    }

    /// A doodad spawned by a runtime `DoodadSpawner` (`Id = 0`) at (x, y, 100).
    pub fn spawn_doodad_with_spawner(
        server: &GameServer,
        template_id: u32,
        x: f32,
        y: f32,
        world_id: u32,
    ) -> DoodadRef {
        let position = crate::data::world_templates::WorldSpawnPosition {
            world_id,
            x,
            y,
            z: 100.0,
            ..Default::default()
        };
        let spawner = super::new_doodad_spawner(world_id, template_id, position);
        Arc::clone(&spawner)
            .spawn(server, 0)
            .expect("doodad template")
    }

    /// A fresh temporary directory set as `FileManager.AppPath`.
    pub fn temp_app_path(server: &GameServer, name: &str) -> PathBuf {
        static N: AtomicU32 = AtomicU32::new(0);
        let dir = std::env::temp_dir().join(format!(
            "aaemu_cmd_objects_{}_{name}_{}",
            std::process::id(),
            N.fetch_add(1, Ordering::SeqCst)
        ));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("temp dir");
        let d = dir.clone();
        server.config.update(|c| c.app_path = d);
        dir
    }
}
