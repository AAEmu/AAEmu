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
