//! Port of `AAEmu.Commons.Utils.JsonHelper` (Newtonsoft.Json → serde_json), plus a serializer
//! that reproduces Newtonsoft.Json's output format (used where the C# wrote JSON files).

use std::io;

use serde::de::DeserializeOwned;
use serde::Serialize;
use serde_json::ser::{CharEscape, Formatter};

use crate::dotnet_fmt;

#[derive(Debug, thiserror::Error)]
pub enum JsonHelperError {
    /// C#: `ArgumentException("NullOrWhiteSpace")`.
    #[error("json is null or whitespace")]
    NullOrWhiteSpace,
    #[error(transparent)]
    Parse(#[from] serde_json::Error),
    /// The document deserialized to `null`.
    #[error("json deserialized to null")]
    NullResult,
}

/// `JsonHelper.DeserializeObject<T>`. Accepts the Newtonsoft extensions used by AAEmu's data
/// files (comments, trailing commas; see [`strip_newtonsoft_extensions`]). The C#
/// `JsonConverter[]` parameter has no equivalent; use serde attributes instead.
pub fn deserialize_object<T: DeserializeOwned>(json: &str) -> serde_json::Result<T> {
    serde_json::from_str(&strip_newtonsoft_extensions(json))
}

/// `JsonHelper.TryDeserializeObject<T>`: fails on blank input, parse errors and a `null`
/// result.
pub fn try_deserialize_object<T: DeserializeOwned>(json: &str) -> Result<T, JsonHelperError> {
    if json.trim().is_empty() {
        return Err(JsonHelperError::NullOrWhiteSpace);
    }
    let value: serde_json::Value = serde_json::from_str(&strip_newtonsoft_extensions(json))?;
    if value.is_null() {
        return Err(JsonHelperError::NullResult);
    }
    Ok(serde_json::from_value(value)?)
}

/// Rewrites Newtonsoft-lenient JSON into strict JSON for serde_json: removes `//` and
/// `/* */` comments (outside strings) and trailing commas before `]` / `}`. AAEmu's data files
/// (e.g. `Data/Worlds/*/npc_spawns.json`) contain comments. Other Newtonsoft leniencies
/// (single-quoted strings, unquoted names, `NaN` literals) are not handled.
pub fn strip_newtonsoft_extensions(json: &str) -> String {
    let mut out = String::with_capacity(json.len());
    let mut chars = json.chars().peekable();
    let mut in_string = false;
    let mut escaped = false;
    // byte index in `out` of the last significant (non-whitespace) char outside strings
    let mut last_sig: Option<usize> = None;
    while let Some(c) = chars.next() {
        if in_string {
            out.push(c);
            if escaped {
                escaped = false;
            } else if c == '\\' {
                escaped = true;
            } else if c == '"' {
                in_string = false;
                last_sig = Some(out.len() - 1);
            }
            continue;
        }
        match c {
            '/' if chars.peek() == Some(&'/') => {
                for n in chars.by_ref() {
                    if n == '\n' {
                        out.push('\n');
                        break;
                    }
                }
            }
            '/' if chars.peek() == Some(&'*') => {
                chars.next();
                let mut prev = '\0';
                for n in chars.by_ref() {
                    if prev == '*' && n == '/' {
                        break;
                    }
                    prev = n;
                }
                out.push(' ');
            }
            ']' | '}' => {
                if let Some(i) = last_sig {
                    if out.as_bytes()[i] == b',' {
                        out.replace_range(i..=i, " ");
                    }
                }
                out.push(c);
                last_sig = Some(out.len() - 1);
            }
            '"' => {
                in_string = true;
                out.push(c);
            }
            c if c.is_whitespace() => out.push(c),
            c => {
                out.push(c);
                last_sig = Some(out.len() - c.len_utf8());
            }
        }
    }
    out
}

/// `serialize_with` helpers writing non-finite floats like Newtonsoft
/// (`FloatFormatHandling.String`: `"NaN"`, `"Infinity"`, `"-Infinity"`); serde_json would
/// write `null`. Finite values go through the formatter (see [`NewtonsoftFormatter`]).
pub mod newtonsoft_float {
    use serde::Serializer;

    fn non_finite(v: f64) -> &'static str {
        if v.is_nan() {
            "NaN"
        } else if v > 0.0 {
            "Infinity"
        } else {
            "-Infinity"
        }
    }

    pub fn f32<S: Serializer>(v: &f32, s: S) -> Result<S::Ok, S::Error> {
        if v.is_finite() {
            s.serialize_f32(*v)
        } else {
            s.serialize_str(non_finite(f64::from(*v)))
        }
    }

    pub fn f64<S: Serializer>(v: &f64, s: S) -> Result<S::Ok, S::Error> {
        if v.is_finite() {
            s.serialize_f64(*v)
        } else {
            s.serialize_str(non_finite(*v))
        }
    }
}

/// Newline used by Newtonsoft's indented output (`Environment.NewLine`).
pub const PLATFORM_NEWLINE: &str = if cfg!(windows) { "\r\n" } else { "\n" };

/// `JsonConvert.SerializeObject(value, indented ? Formatting.Indented : Formatting.None)`,
/// using the platform newline.
pub fn serialize_newtonsoft<T: Serialize + ?Sized>(
    value: &T,
    indented: bool,
) -> serde_json::Result<String> {
    serialize_newtonsoft_with_newline(value, indented, PLATFORM_NEWLINE)
}

/// [`serialize_newtonsoft`] with an explicit newline sequence.
pub fn serialize_newtonsoft_with_newline<T: Serialize + ?Sized>(
    value: &T,
    indented: bool,
    newline: &str,
) -> serde_json::Result<String> {
    let mut out = Vec::new();
    let fmt = NewtonsoftFormatter::new(indented, newline);
    let mut ser = serde_json::Serializer::with_formatter(&mut out, fmt);
    value.serialize(&mut ser)?;
    Ok(String::from_utf8(out).expect("serde_json writes UTF-8"))
}

/// A `serde_json` formatter producing Newtonsoft.Json-style output:
/// * floats as .NET round-trip strings with a forced `.0` (`4096.0`, `1E+17`),
/// * indentation of 2 spaces, `"key": value`, `[]` / `{}` for empty containers,
/// * U+0085, U+2028 and U+2029 escaped like Newtonsoft does.
///
/// Limitation: serde_json writes non-finite floats as `null` without consulting the formatter
/// (Newtonsoft writes `"NaN"` / `"Infinity"`); annotate such fields with
/// `#[serde(serialize_with = "newtonsoft_float::f32")]`.
#[derive(Debug, Clone)]
pub struct NewtonsoftFormatter {
    indented: bool,
    newline: String,
    level: usize,
    has_value: bool,
}

impl NewtonsoftFormatter {
    pub fn new(indented: bool, newline: &str) -> Self {
        Self {
            indented,
            newline: newline.to_string(),
            level: 0,
            has_value: false,
        }
    }

    fn indent<W: ?Sized + io::Write>(&self, w: &mut W) -> io::Result<()> {
        if self.indented {
            w.write_all(self.newline.as_bytes())?;
            for _ in 0..self.level {
                w.write_all(b"  ")?;
            }
        }
        Ok(())
    }
}

impl Formatter for NewtonsoftFormatter {
    fn write_f32<W: ?Sized + io::Write>(&mut self, w: &mut W, value: f32) -> io::Result<()> {
        w.write_all(dotnet_fmt::json_f32(value).as_bytes())
    }

    fn write_f64<W: ?Sized + io::Write>(&mut self, w: &mut W, value: f64) -> io::Result<()> {
        w.write_all(dotnet_fmt::json_f64(value).as_bytes())
    }

    fn write_string_fragment<W: ?Sized + io::Write>(
        &mut self,
        w: &mut W,
        fragment: &str,
    ) -> io::Result<()> {
        let mut start = 0;
        for (i, c) in fragment.char_indices() {
            if matches!(c, '\u{85}' | '\u{2028}' | '\u{2029}') {
                w.write_all(&fragment.as_bytes()[start..i])?;
                write!(w, "\\u{:04x}", c as u32)?;
                start = i + c.len_utf8();
            }
        }
        w.write_all(&fragment.as_bytes()[start..])
    }

    fn write_char_escape<W: ?Sized + io::Write>(
        &mut self,
        w: &mut W,
        escape: CharEscape,
    ) -> io::Result<()> {
        let s: &[u8] = match escape {
            CharEscape::Quote => b"\\\"",
            CharEscape::ReverseSolidus => b"\\\\",
            CharEscape::Solidus => b"/",
            CharEscape::Backspace => b"\\b",
            CharEscape::FormFeed => b"\\f",
            CharEscape::LineFeed => b"\\n",
            CharEscape::CarriageReturn => b"\\r",
            CharEscape::Tab => b"\\t",
            CharEscape::AsciiControl(b) => return write!(w, "\\u{:04x}", b),
        };
        w.write_all(s)
    }

    fn begin_array<W: ?Sized + io::Write>(&mut self, w: &mut W) -> io::Result<()> {
        self.level += 1;
        self.has_value = false;
        w.write_all(b"[")
    }

    fn end_array<W: ?Sized + io::Write>(&mut self, w: &mut W) -> io::Result<()> {
        self.level -= 1;
        if self.has_value {
            self.indent(w)?;
        }
        w.write_all(b"]")
    }

    fn begin_array_value<W: ?Sized + io::Write>(
        &mut self,
        w: &mut W,
        first: bool,
    ) -> io::Result<()> {
        if !first {
            w.write_all(b",")?;
        }
        self.indent(w)
    }

    fn end_array_value<W: ?Sized + io::Write>(&mut self, _w: &mut W) -> io::Result<()> {
        self.has_value = true;
        Ok(())
    }

    fn begin_object<W: ?Sized + io::Write>(&mut self, w: &mut W) -> io::Result<()> {
        self.level += 1;
        self.has_value = false;
        w.write_all(b"{")
    }

    fn end_object<W: ?Sized + io::Write>(&mut self, w: &mut W) -> io::Result<()> {
        self.level -= 1;
        if self.has_value {
            self.indent(w)?;
        }
        w.write_all(b"}")
    }

    fn begin_object_key<W: ?Sized + io::Write>(
        &mut self,
        w: &mut W,
        first: bool,
    ) -> io::Result<()> {
        if !first {
            w.write_all(b",")?;
        }
        self.indent(w)
    }

    fn begin_object_value<W: ?Sized + io::Write>(&mut self, w: &mut W) -> io::Result<()> {
        w.write_all(if self.indented { b": " } else { b":" })
    }

    fn end_object_value<W: ?Sized + io::Write>(&mut self, _w: &mut W) -> io::Result<()> {
        self.has_value = true;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use serde::Deserialize;

    use super::*;

    #[derive(Debug, Serialize, Deserialize, PartialEq)]
    struct S {
        a: i32,
        b: Vec<f32>,
        c: Option<String>,
        d: Inner,
        e: Vec<i32>,
    }

    #[derive(Debug, Serialize, Deserialize, PartialEq)]
    struct Inner {}

    #[test]
    fn lenient_json() {
        let src = "[ // first
 {\"a\": \"x // not a comment\", /* c */ \"b\": [1, 2,],},
]";
        let v: serde_json::Value = deserialize_object(src).unwrap();
        assert_eq!(
            v,
            serde_json::json!([{"a": "x // not a comment", "b": [1, 2]}])
        );
        assert_eq!(strip_newtonsoft_extensions("\"a,\\\",]\""), "\"a,\\\",]\"");
        let t: Vec<i32> = try_deserialize_object("[1, /* x */ 2,]").unwrap();
        assert_eq!(t, [1, 2]);
    }

    #[test]
    fn non_finite_floats() {
        #[derive(Serialize)]
        struct F {
            #[serde(serialize_with = "newtonsoft_float::f32")]
            a: f32,
            #[serde(serialize_with = "newtonsoft_float::f64")]
            b: f64,
            #[serde(serialize_with = "newtonsoft_float::f32")]
            c: f32,
        }
        let s = serialize_newtonsoft(
            &F {
                a: f32::NAN,
                b: f64::NEG_INFINITY,
                c: 2.0,
            },
            false,
        )
        .unwrap();
        assert_eq!(s, r#"{"a":"NaN","b":"-Infinity","c":2.0}"#);
    }

    #[test]
    fn try_deserialize() {
        assert!(matches!(
            try_deserialize_object::<S>("  "),
            Err(JsonHelperError::NullOrWhiteSpace)
        ));
        assert!(matches!(
            try_deserialize_object::<Option<S>>("null"),
            Err(JsonHelperError::NullResult)
        ));
        assert!(matches!(
            try_deserialize_object::<S>("{"),
            Err(JsonHelperError::Parse(_))
        ));
        let v: Vec<i32> = try_deserialize_object("[1,2]").unwrap();
        assert_eq!(v, [1, 2]);
        assert_eq!(deserialize_object::<i32>("5").unwrap(), 5);
    }

    #[test]
    fn newtonsoft_layout() {
        let s = S {
            a: 1,
            b: vec![4096.0, 0.5],
            c: Some("x\"\u{2028}\u{1}".into()),
            d: Inner {},
            e: vec![],
        };
        assert_eq!(
            serialize_newtonsoft_with_newline(&s, true, "\r\n").unwrap(),
            "{\r\n  \"a\": 1,\r\n  \"b\": [\r\n    4096.0,\r\n    0.5\r\n  ],\r\n  \"c\": \"x\\\"\\u2028\\u0001\",\r\n  \"d\": {},\r\n  \"e\": []\r\n}"
        );
        assert_eq!(
            serialize_newtonsoft(&s, false).unwrap(),
            "{\"a\":1,\"b\":[4096.0,0.5],\"c\":\"x\\\"\\u2028\\u0001\",\"d\":{},\"e\":[]}"
        );
        let empty: Vec<S> = vec![];
        assert_eq!(serialize_newtonsoft(&empty, true).unwrap(), "[]");
    }
}
