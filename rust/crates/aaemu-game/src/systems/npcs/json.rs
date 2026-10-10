//! `JsonHelper.TryDeserializeObject` with Newtonsoft.Json semantics for the server data files
//! under `Data/Worlds/**` (spawn files): `//` and `/* */` comments, trailing commas,
//! hexadecimal integer literals (`0x4a0da1f4d8420a38` in `gimmick_spawns.json`), property names
//! matched case-insensitively, unknown members ignored, missing members keep their defaults.

use serde_json::{Map, Value};

/// Parses `text` into a JSON value (the lenient extensions are rewritten first).
pub fn parse(text: &str) -> serde_json::Result<Value> {
    let relaxed = crate::config::relax_json(text.trim_start_matches('\u{feff}'));
    serde_json::from_str(&hex_literals(&relaxed))
}

/// Rewrites `0x..` integer literals outside strings into decimal (Newtonsoft reads them as
/// integers).
fn hex_literals(src: &str) -> String {
    let b = src.as_bytes();
    let mut out = String::with_capacity(src.len());
    let mut i = 0;
    let mut in_str = false;
    let mut last = 0;
    while i < b.len() {
        let c = b[i];
        if in_str {
            if c == b'\\' {
                i += 2;
                continue;
            }
            if c == b'"' {
                in_str = false;
            }
            i += 1;
            continue;
        }
        if c == b'"' {
            in_str = true;
            i += 1;
            continue;
        }
        let neg = c == b'-' && b.get(i + 1) == Some(&b'0');
        let start = if neg { i + 1 } else { i };
        if b.get(start) == Some(&b'0')
            && matches!(b.get(start + 1), Some(b'x' | b'X'))
            && (i == 0 || !b[i - 1].is_ascii_alphanumeric())
        {
            let mut j = start + 2;
            while j < b.len() && b[j].is_ascii_hexdigit() {
                j += 1;
            }
            if j > start + 2 {
                let v = u64::from_str_radix(&src[start + 2..j], 16).unwrap_or(0);
                out.push_str(&src[last..i]);
                if neg {
                    out.push_str(&format!("-{}", v));
                } else {
                    // Newtonsoft reads values above long.MaxValue as BigInteger; the C# targets
                    // are `long` fields, where the bit pattern is what matters.
                    out.push_str(&(v as i64).to_string());
                }
                last = j;
                i = j;
                continue;
            }
        }
        i += 1;
    }
    out.push_str(&src[last..]);
    out
}

/// Case-insensitive property lookup (Newtonsoft's default member matching: exact match first).
pub fn prop<'a>(obj: &'a Map<String, Value>, name: &str) -> Option<&'a Value> {
    obj.get(name).or_else(|| {
        obj.iter()
            .find(|(k, _)| k.eq_ignore_ascii_case(name))
            .map(|(_, v)| v)
    })
}

/// A numeric member as `f64` (numbers, numeric strings and booleans as Newtonsoft converts
/// them). `Err` = not convertible (the C# deserialization fails).
pub fn as_f64(v: &Value) -> Result<f64, String> {
    match v {
        Value::Number(n) => n.as_f64().ok_or_else(|| format!("bad number {n}")),
        Value::String(s) => s.trim().parse().map_err(|_| format!("bad number {s:?}")),
        Value::Bool(b) => Ok(if *b { 1.0 } else { 0.0 }),
        Value::Null => Ok(0.0),
        o => Err(format!("expected a number, got {o}")),
    }
}

/// A member read as `float` (missing / null = `default`).
pub fn f32_or(obj: &Map<String, Value>, name: &str, default: f32) -> Result<f32, String> {
    match prop(obj, name) {
        None | Some(Value::Null) => Ok(default),
        Some(v) => Ok(as_f64(v)? as f32),
    }
}

/// A member read as an integer (missing / null = `default`). Integral JSON numbers are used
/// exactly; fractional ones truncate like `Convert.ToInt64` would reject — Newtonsoft throws for
/// a fractional value into an integer member, which is reported as an error here too.
pub fn i64_or(obj: &Map<String, Value>, name: &str, default: i64) -> Result<i64, String> {
    match prop(obj, name) {
        None | Some(Value::Null) => Ok(default),
        Some(Value::Number(n)) => {
            if let Some(i) = n.as_i64() {
                Ok(i)
            } else if let Some(u) = n.as_u64() {
                Ok(u as i64)
            } else {
                let f = n.as_f64().unwrap_or(0.0);
                if f.fract() == 0.0 {
                    Ok(f as i64)
                } else {
                    Err(format!("{name}: {f} is not an integer"))
                }
            }
        }
        Some(Value::String(s)) => s
            .trim()
            .parse()
            .map_err(|_| format!("{name}: bad integer {s:?}")),
        Some(Value::Bool(b)) => Ok(i64::from(*b)),
        Some(o) => Err(format!("{name}: expected an integer, got {o}")),
    }
}

/// `uint` member.
pub fn u32_or(obj: &Map<String, Value>, name: &str, default: u32) -> Result<u32, String> {
    Ok(i64_or(obj, name, i64::from(default))? as u32)
}

/// `int` member.
pub fn i32_or(obj: &Map<String, Value>, name: &str, default: i32) -> Result<i32, String> {
    Ok(i64_or(obj, name, i64::from(default))? as i32)
}

/// `string` member (null / missing = `default`).
pub fn str_or(obj: &Map<String, Value>, name: &str, default: &str) -> Result<String, String> {
    match prop(obj, name) {
        None | Some(Value::Null) => Ok(default.to_string()),
        Some(Value::String(s)) => Ok(s.clone()),
        Some(Value::Number(n)) => Ok(n.to_string()),
        Some(Value::Bool(b)) => Ok(if *b { "True" } else { "False" }.to_string()),
        Some(o) => Err(format!("{name}: expected a string, got {o}")),
    }
}

/// `bool` member.
pub fn bool_or(obj: &Map<String, Value>, name: &str, default: bool) -> Result<bool, String> {
    match prop(obj, name) {
        None | Some(Value::Null) => Ok(default),
        Some(Value::Bool(b)) => Ok(*b),
        Some(Value::Number(n)) => Ok(n.as_f64().unwrap_or(0.0) != 0.0),
        Some(Value::String(s)) => match s.trim().to_ascii_lowercase().as_str() {
            "true" => Ok(true),
            "false" => Ok(false),
            _ => Err(format!("{name}: bad bool {s:?}")),
        },
        Some(o) => Err(format!("{name}: expected a bool, got {o}")),
    }
}

/// `List<uint>` member (null / missing = empty).
pub fn u32_list(obj: &Map<String, Value>, name: &str) -> Result<Vec<u32>, String> {
    match prop(obj, name) {
        None | Some(Value::Null) => Ok(Vec::new()),
        Some(Value::Array(a)) => a
            .iter()
            .map(|v| as_f64(v).map(|f| f as i64 as u32))
            .collect(),
        Some(o) => Err(format!("{name}: expected an array, got {o}")),
    }
}

/// The top-level array of a spawn file (`List<T>`); `Err` when the document is not an array.
pub fn array(v: &Value) -> Result<&Vec<Value>, String> {
    match v {
        Value::Array(a) => Ok(a),
        o => Err(format!("expected an array, got {}", type_name(o))),
    }
}

fn type_name(v: &Value) -> &'static str {
    match v {
        Value::Null => "null",
        Value::Bool(_) => "bool",
        Value::Number(_) => "number",
        Value::String(_) => "string",
        Value::Array(_) => "array",
        Value::Object(_) => "object",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn lenient_extensions() {
        let v = parse(
            "[ // comment\n { \"unitid\": 5, \"G\": 0x4a0da1f4d8420a38, \"S\": \"0x12\", /* c */ }, ]",
        )
        .unwrap();
        let a = array(&v).unwrap();
        let o = a[0].as_object().unwrap();
        assert_eq!(u32_or(o, "UnitId", 0).unwrap(), 5);
        assert_eq!(i64_or(o, "G", 0).unwrap(), 0x4a0d_a1f4_d842_0a38);
        assert_eq!(str_or(o, "s", "").unwrap(), "0x12");
        assert_eq!(f32_or(o, "Missing", 1.5).unwrap(), 1.5);
        assert!(i64_or(o, "UnitId", 0).is_ok());
    }

    #[test]
    fn fractional_integer_is_an_error() {
        let v = parse("{\"A\": 1.5, \"B\": 2.0}").unwrap();
        let o = v.as_object().unwrap();
        assert!(u32_or(o, "A", 0).is_err());
        assert_eq!(u32_or(o, "B", 0).unwrap(), 2);
    }
}
