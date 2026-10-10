//! JSON as the C# WebApi produces and reads it:
//!
//! * responses: `Newtonsoft.Json.JsonConvert.SerializeObject` with the default settings
//!   (property names as declared, enums as numbers, ISO dates with `RoundtripKind`, doubles in
//!   the shortest round-trip form with a forced `.0`), built with [`JValue`];
//! * requests: `System.Text.Json.JsonSerializer.Deserialize` with the default options
//!   (case-sensitive property names, numbers only for numeric members, unknown members
//!   ignored) through the typed getters of [`Stj`].

use chrono::NaiveDateTime;
use serde_json::Value;

/// `DateTime.Kind`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DateKind {
    /// Values read from MySQL (`DateTimeKind.Unspecified`): no suffix.
    Unspecified,
    /// `DateTime.UtcNow`: `Z`.
    Utc,
}

/// A Newtonsoft-serialisable value. Objects keep their member order.
#[derive(Debug, Clone, PartialEq)]
pub enum JValue {
    Null,
    Bool(bool),
    Int(i64),
    UInt(u64),
    /// `double`.
    Double(f64),
    /// `float`.
    Single(f32),
    Str(String),
    Date(NaiveDateTime, DateKind),
    Array(Vec<JValue>),
    Object(Vec<(String, JValue)>),
    /// `JsonWriter.WriteRawValue(text)`.
    Raw(String),
}

impl JValue {
    /// An object from `(name, value)` pairs.
    pub fn object<I, K>(members: I) -> Self
    where
        I: IntoIterator<Item = (K, JValue)>,
        K: Into<String>,
    {
        JValue::Object(members.into_iter().map(|(k, v)| (k.into(), v)).collect())
    }

    pub fn str(s: impl Into<String>) -> Self {
        JValue::Str(s.into())
    }

    /// A nullable string (`null` for `None`).
    pub fn opt_str(s: Option<impl Into<String>>) -> Self {
        s.map_or(JValue::Null, |s| JValue::Str(s.into()))
    }

    /// `JsonConvert.SerializeObject(value)`.
    pub fn to_json(&self) -> String {
        let mut out = String::new();
        self.write(&mut out);
        out
    }

    /// `JsonConvert.SerializeObject(value, Formatting.Indented)` (two spaces,
    /// `Environment.NewLine`).
    pub fn to_json_indented(&self) -> String {
        let mut out = String::new();
        self.write_indented(&mut out, 0);
        out
    }

    fn write_indented(&self, out: &mut String, level: usize) {
        let nl = if cfg!(windows) { "\r\n" } else { "\n" };
        let pad = |out: &mut String, n: usize| out.push_str(&"  ".repeat(n));
        match self {
            JValue::Array(items) if !items.is_empty() => {
                out.push('[');
                for (i, v) in items.iter().enumerate() {
                    if i > 0 {
                        out.push(',');
                    }
                    out.push_str(nl);
                    pad(out, level + 1);
                    v.write_indented(out, level + 1);
                }
                out.push_str(nl);
                pad(out, level);
                out.push(']');
            }
            JValue::Object(members) if !members.is_empty() => {
                out.push('{');
                for (i, (k, v)) in members.iter().enumerate() {
                    if i > 0 {
                        out.push(',');
                    }
                    out.push_str(nl);
                    pad(out, level + 1);
                    write_string(out, k);
                    out.push_str(": ");
                    v.write_indented(out, level + 1);
                }
                out.push_str(nl);
                pad(out, level);
                out.push('}');
            }
            other => other.write(out),
        }
    }

    fn write(&self, out: &mut String) {
        match self {
            JValue::Raw(s) => out.push_str(s),
            JValue::Null => out.push_str("null"),
            JValue::Bool(b) => out.push_str(if *b { "true" } else { "false" }),
            JValue::Int(i) => out.push_str(&i.to_string()),
            JValue::UInt(u) => out.push_str(&u.to_string()),
            JValue::Double(d) => out.push_str(&format_double(*d)),
            JValue::Single(f) => out.push_str(&format_single(*f)),
            JValue::Str(s) => write_string(out, s),
            JValue::Date(d, kind) => {
                out.push('"');
                out.push_str(&format_date(*d, *kind));
                out.push('"');
            }
            JValue::Array(items) => {
                out.push('[');
                for (i, v) in items.iter().enumerate() {
                    if i > 0 {
                        out.push(',');
                    }
                    v.write(out);
                }
                out.push(']');
            }
            JValue::Object(members) => {
                out.push('{');
                for (i, (k, v)) in members.iter().enumerate() {
                    if i > 0 {
                        out.push(',');
                    }
                    write_string(out, k);
                    out.push(':');
                    v.write(out);
                }
                out.push('}');
            }
        }
    }
}

impl From<&str> for JValue {
    fn from(s: &str) -> Self {
        JValue::Str(s.to_string())
    }
}

impl From<String> for JValue {
    fn from(s: String) -> Self {
        JValue::Str(s)
    }
}

impl From<bool> for JValue {
    fn from(b: bool) -> Self {
        JValue::Bool(b)
    }
}

macro_rules! jvalue_from_int {
    ($($t:ty => $v:ident),*) => {$(
        impl From<$t> for JValue {
            fn from(i: $t) -> Self {
                JValue::$v(i.into())
            }
        }
    )*};
}
jvalue_from_int!(i8 => Int, i16 => Int, i32 => Int, i64 => Int, u8 => UInt, u16 => UInt, u32 => UInt, u64 => UInt);

/// Newtonsoft's `JavaScriptUtils.WriteEscapedJavaScriptString` with the default escape
/// handling: `"`, `\`, control characters, U+0085, U+2028 and U+2029.
fn write_string(out: &mut String, s: &str) {
    out.push('"');
    for c in s.chars() {
        match c {
            '"' => out.push_str("\\\""),
            '\\' => out.push_str("\\\\"),
            '\u{8}' => out.push_str("\\b"),
            '\t' => out.push_str("\\t"),
            '\n' => out.push_str("\\n"),
            '\u{c}' => out.push_str("\\f"),
            '\r' => out.push_str("\\r"),
            c if (c as u32) < 0x20 || matches!(c, '\u{85}' | '\u{2028}' | '\u{2029}') => {
                out.push_str(&format!("\\u{:04x}", c as u32));
            }
            c => out.push(c),
        }
    }
    out.push('"');
}

/// `DateTime` with `IsoDateFormat` + `RoundtripKind`: `yyyy-MM-ddTHH:mm:ss.FFFFFFFK`.
pub fn format_date(d: NaiveDateTime, kind: DateKind) -> String {
    let mut s = d.format("%Y-%m-%dT%H:%M:%S").to_string();
    let ticks = d.and_utc().timestamp_subsec_nanos() / 100;
    if ticks > 0 {
        let frac = format!("{ticks:07}");
        s.push('.');
        s.push_str(frac.trim_end_matches('0'));
    }
    if kind == DateKind::Utc {
        s.push('Z');
    }
    s
}

/// .NET Core `double.ToString("R")`: shortest round-trip digits, scientific notation for
/// exponents >= 15 or < -5 (`1E+15`, `1E-05`).
fn dotnet_r(mantissa_digits: &str, exp: i32, negative: bool) -> String {
    let mut s = String::new();
    if negative {
        s.push('-');
    }
    let digits = mantissa_digits;
    if (-4..15).contains(&exp) {
        if exp >= 0 {
            let int_len = (exp + 1) as usize;
            if digits.len() <= int_len {
                s.push_str(digits);
                s.push_str(&"0".repeat(int_len - digits.len()));
            } else {
                s.push_str(&digits[..int_len]);
                s.push('.');
                s.push_str(&digits[int_len..]);
            }
        } else {
            s.push_str("0.");
            s.push_str(&"0".repeat((-exp - 1) as usize));
            s.push_str(digits);
        }
    } else {
        s.push_str(&digits[..1]);
        if digits.len() > 1 {
            s.push('.');
            s.push_str(&digits[1..]);
        }
        s.push('E');
        s.push(if exp < 0 { '-' } else { '+' });
        s.push_str(&format!("{:02}", exp.abs()));
    }
    s
}

/// Splits Rust's shortest `{:e}` output into (digits, exponent).
fn shortest_parts(e_repr: &str) -> (String, i32) {
    let (mant, exp) = e_repr.split_once('e').unwrap_or((e_repr, "0"));
    let mant = mant.trim_start_matches('-');
    (mant.replace('.', ""), exp.parse().unwrap_or(0))
}

fn ensure_decimal_place(s: String) -> String {
    if s.contains('.') || s.contains('E') || s.contains('e') || s.contains("Infinity") || s == "NaN"
    {
        s
    } else {
        s + ".0"
    }
}

/// `double.ToString()` (.NET Core 3.0+: shortest round-trip, `E+XX` outside [1e-5, 1e15)).
pub fn dotnet_double_string(d: f64) -> String {
    if d.is_nan() {
        return "NaN".into();
    }
    if d.is_infinite() {
        return if d > 0.0 { "Infinity" } else { "-Infinity" }.into();
    }
    if d == 0.0 {
        return if d.is_sign_negative() { "-0" } else { "0" }.into();
    }
    let (digits, exp) = shortest_parts(&format!("{d:e}"));
    dotnet_r(&digits, exp, d < 0.0)
}

/// `float.ToString()`.
pub fn dotnet_single_string(f: f32) -> String {
    if f.is_nan() || f.is_infinite() || f == 0.0 {
        return dotnet_double_string(f64::from(f));
    }
    let (digits, exp) = shortest_parts(&format!("{f:e}"));
    dotnet_r(&digits, exp, f < 0.0)
}

/// `JsonConvert.ToString(double)`.
pub fn format_double(d: f64) -> String {
    if d.is_nan() {
        return "\"NaN\"".into();
    }
    if d.is_infinite() {
        return if d > 0.0 {
            "\"Infinity\"".into()
        } else {
            "\"-Infinity\"".into()
        };
    }
    if d == 0.0 {
        return if d.is_sign_negative() { "-0.0" } else { "0.0" }.into();
    }
    let (digits, exp) = shortest_parts(&format!("{d:e}"));
    ensure_decimal_place(dotnet_r(&digits, exp, d < 0.0))
}

/// `JsonConvert.ToString(float)`.
pub fn format_single(f: f32) -> String {
    if f.is_nan() || f.is_infinite() {
        return format_double(f64::from(f));
    }
    if f == 0.0 {
        return if f.is_sign_negative() { "-0.0" } else { "0.0" }.into();
    }
    let (digits, exp) = shortest_parts(&format!("{f:e}"));
    ensure_decimal_place(dotnet_r(&digits, exp, f < 0.0))
}

/// A `System.Text.Json` failure (the C# exception message ends in the WebApi error page).
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[error("{0}")]
pub struct StjError(pub String);

/// `System.Text.Json` reading with the default `JsonSerializerOptions`.
pub struct Stj;

impl Stj {
    /// `JsonSerializer.Deserialize<JsonElement>(body)` (any JSON value).
    pub fn parse(body: &str) -> Result<Value, StjError> {
        if body.trim().is_empty() {
            return Err(StjError(
                "The input does not contain any JSON tokens. Expected the input to start with a \
                 valid JSON token, while the input is empty. Path: $ | LineNumber: 0 | \
                 BytePositionInLine: 0."
                    .into(),
            ));
        }
        serde_json::from_str(body.trim_start_matches('\u{feff}')).map_err(|e| {
            StjError(format!(
                "'{}' is an invalid JSON literal. {e}",
                first_char(body)
            ))
        })
    }

    /// `JsonElement.GetProperty(name)` (case-sensitive; missing -> `KeyNotFoundException`).
    pub fn property<'a>(v: &'a Value, name: &str) -> Result<&'a Value, StjError> {
        match v {
            Value::Object(m) => m
                .get(name)
                .ok_or_else(|| StjError("The given key was not present in the dictionary.".into())),
            _ => Err(StjError(format!(
                "The requested operation requires an element of type 'Object', but the target \
                 element has type '{}'.",
                kind_name(v)
            ))),
        }
    }

    /// `JsonElement.TryGetProperty(name, out _)`.
    pub fn try_property<'a>(v: &'a Value, name: &str) -> Option<&'a Value> {
        v.as_object().and_then(|m| m.get(name))
    }

    /// `JsonElement.GetString()`: a string or `null`.
    pub fn get_string(v: &Value) -> Result<Option<String>, StjError> {
        match v {
            Value::String(s) => Ok(Some(s.clone())),
            Value::Null => Ok(None),
            other => Err(StjError(format!(
                "The requested operation requires an element of type 'String', but the target \
                 element has type '{}'.",
                kind_name(other)
            ))),
        }
    }

    /// `JsonElement.GetInt32()` / an `int` member.
    pub fn get_i32(v: &Value) -> Result<i32, StjError> {
        Self::integer(v, "Int32").and_then(|i| i32::try_from(i).map_err(|_| number_error("Int32")))
    }

    /// `JsonElement.GetUInt32()` / a `uint` member.
    pub fn get_u32(v: &Value) -> Result<u32, StjError> {
        Self::integer(v, "UInt32")
            .and_then(|i| u32::try_from(i).map_err(|_| number_error("UInt32")))
    }

    /// `JsonElement.GetByte()` / a `byte` member.
    pub fn get_u8(v: &Value) -> Result<u8, StjError> {
        Self::integer(v, "Byte").and_then(|i| u8::try_from(i).map_err(|_| number_error("Byte")))
    }

    /// A `long` member.
    pub fn get_i64(v: &Value) -> Result<i64, StjError> {
        Self::integer(v, "Int64").and_then(|i| i64::try_from(i).map_err(|_| number_error("Int64")))
    }

    /// A `bool` member.
    pub fn get_bool(v: &Value) -> Result<bool, StjError> {
        v.as_bool().ok_or_else(|| {
            StjError(format!(
                "The JSON value could not be converted to System.Boolean. Got '{}'.",
                kind_name(v)
            ))
        })
    }

    fn integer(v: &Value, ty: &str) -> Result<i128, StjError> {
        match v {
            Value::Number(n) => {
                if let Some(i) = n.as_i64() {
                    Ok(i128::from(i))
                } else if let Some(u) = n.as_u64() {
                    Ok(i128::from(u))
                } else {
                    Err(number_error(ty))
                }
            }
            other => Err(StjError(format!(
                "The JSON value could not be converted to System.{ty}. Got '{}'.",
                kind_name(other)
            ))),
        }
    }
}

fn number_error(ty: &str) -> StjError {
    StjError(format!(
        "The JSON value could not be converted to System.{ty}."
    ))
}

fn first_char(s: &str) -> char {
    s.trim_start().chars().next().unwrap_or(' ')
}

/// `JsonValueKind` names.
pub fn kind_name(v: &Value) -> &'static str {
    match v {
        Value::Null => "Null",
        Value::Bool(true) => "True",
        Value::Bool(false) => "False",
        Value::Number(_) => "Number",
        Value::String(_) => "String",
        Value::Array(_) => "Array",
        Value::Object(_) => "Object",
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn dt(s: &str) -> NaiveDateTime {
        NaiveDateTime::parse_from_str(s, "%Y-%m-%d %H:%M:%S%.f").unwrap()
    }

    #[test]
    fn newtonsoft_scalars() {
        assert_eq!(format_double(1.0), "1.0");
        assert_eq!(format_double(0.5), "0.5");
        assert_eq!(format_double(-2.25), "-2.25");
        assert_eq!(format_double(1e15), "1E+15");
        assert_eq!(format_double(123456789012345.0), "123456789012345.0");
        assert_eq!(format_double(0.0001), "0.0001");
        assert_eq!(format_double(0.00001), "1E-05");
        assert_eq!(format_double(0.1 + 0.2), "0.30000000000000004");
        assert_eq!(format_single(0.05), "0.05");
        assert_eq!(format_single(25.0), "25.0");
        assert_eq!(
            JValue::str("a\"b\\c\n\u{1}\u{2028}é").to_json(),
            "\"a\\\"b\\\\c\\n\\u0001\\u2028é\""
        );
        assert_eq!(
            JValue::Date(dt("2024-05-01 12:34:56"), DateKind::Unspecified).to_json(),
            "\"2024-05-01T12:34:56\""
        );
        assert_eq!(
            JValue::Date(dt("2024-05-01 12:34:56.120"), DateKind::Utc).to_json(),
            "\"2024-05-01T12:34:56.12Z\""
        );
    }

    #[test]
    fn newtonsoft_objects_keep_member_order() {
        let v = JValue::object([
            ("Id", JValue::from(1u32)),
            ("Name", JValue::from("x")),
            ("Messages", JValue::Array(vec![])),
            ("Null", JValue::Null),
        ]);
        assert_eq!(
            v.to_json(),
            r#"{"Id":1,"Name":"x","Messages":[],"Null":null}"#
        );
    }

    #[test]
    fn system_text_json_reading() {
        let v = Stj::parse(r#"{"a": 1, "b": "x", "c": 1.5, "d": -1, "e": null}"#).unwrap();
        assert_eq!(Stj::get_u32(Stj::property(&v, "a").unwrap()).unwrap(), 1);
        assert!(Stj::property(&v, "A").is_err());
        assert!(Stj::get_u32(Stj::property(&v, "c").unwrap()).is_err());
        assert!(Stj::get_u32(Stj::property(&v, "d").unwrap()).is_err());
        assert_eq!(Stj::get_i32(Stj::property(&v, "d").unwrap()).unwrap(), -1);
        assert!(Stj::get_i32(Stj::property(&v, "b").unwrap()).is_err());
        assert_eq!(
            Stj::get_string(Stj::property(&v, "e").unwrap()).unwrap(),
            None
        );
        assert!(Stj::parse("").is_err());
        assert!(Stj::parse("{").is_err());
    }
}
