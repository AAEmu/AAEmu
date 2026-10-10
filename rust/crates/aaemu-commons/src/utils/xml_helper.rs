//! Port of `AAEmu.Commons.Utils.XML.XmlHelper` (on top of `roxmltree`).
//!
//! `AAEmu.Commons.Utils.XML.Serialization` (generic `XmlSerializer` wrappers) has no generic
//! equivalent here: callers parse with `roxmltree` directly (see
//! [`crate::utils::creature::Creature`] for the one Commons user).

use std::collections::HashMap;

/// `XmlHelper.StringToVector3`: parses `"x,y,z"` (invariant culture); `[0, 0, 0]` when the
/// string isn't exactly three valid floats.
pub fn string_to_vector3(position: &str) -> [f32; 3] {
    let xyz: Vec<&str> = position.split(',').collect();
    if xyz.len() == 3 {
        if let (Some(x), Some(y), Some(z)) =
            (parse_f32(xyz[0]), parse_f32(xyz[1]), parse_f32(xyz[2]))
        {
            return [x, y, z];
        }
    }
    [0.0; 3]
}

/// `float.TryParse(s, NumberStyles.Float, InvariantCulture)`: surrounding whitespace allowed;
/// accepts .NET's `Infinity` / `-Infinity` / `NaN` symbols.
pub fn parse_f32(s: &str) -> Option<f32> {
    let s = s.trim();
    match s {
        "Infinity" | "+Infinity" | "∞" => Some(f32::INFINITY),
        "-Infinity" | "-∞" => Some(f32::NEG_INFINITY),
        "NaN" => Some(f32::NAN),
        _ if s.chars().any(char::is_alphabetic) && !s.contains(['e', 'E']) => None,
        _ => s.parse().ok(),
    }
}

/// `XmlHelper.ReadNodeAttributes`: the node's attributes by name. Namespace declarations
/// (`xmlns:*`) are not attributes in `roxmltree` (they are in `System.Xml`), and names are
/// local names (no prefix).
pub fn read_node_attributes(node: roxmltree::Node<'_, '_>) -> HashMap<String, String> {
    node.attributes()
        .map(|a| (a.name().to_string(), a.value().to_string()))
        .collect()
}

/// Conversion used by [`read_attribute`] (C# `Convert.ChangeType(string, T)`).
pub trait FromXmlAttribute: Sized {
    fn from_xml_attribute(s: &str) -> Option<Self>;
}

macro_rules! from_xml_number {
    ($($t:ty),*) => {$(
        impl FromXmlAttribute for $t {
            fn from_xml_attribute(s: &str) -> Option<Self> {
                let s = s.trim();
                s.strip_prefix('+').unwrap_or(s).parse().ok()
            }
        }
    )*};
}
from_xml_number!(i8, i16, i32, i64, u8, u16, u32, u64, isize, usize);

impl FromXmlAttribute for f32 {
    fn from_xml_attribute(s: &str) -> Option<Self> {
        parse_f32(s)
    }
}

impl FromXmlAttribute for f64 {
    fn from_xml_attribute(s: &str) -> Option<Self> {
        let t = s.trim();
        match t {
            "Infinity" => Some(f64::INFINITY),
            "-Infinity" => Some(f64::NEG_INFINITY),
            "NaN" => Some(f64::NAN),
            _ => parse_f32(t).and_then(|_| t.parse().ok()),
        }
    }
}

impl FromXmlAttribute for bool {
    /// `bool.Parse`: `true`/`false`, case-insensitive, surrounding whitespace allowed.
    fn from_xml_attribute(s: &str) -> Option<Self> {
        let s = s.trim();
        if s.eq_ignore_ascii_case("true") {
            Some(true)
        } else if s.eq_ignore_ascii_case("false") {
            Some(false)
        } else {
            None
        }
    }
}

impl FromXmlAttribute for String {
    fn from_xml_attribute(s: &str) -> Option<Self> {
        Some(s.to_string())
    }
}

/// `XmlHelper.ReadAttribute<T>`: the converted attribute, or `default_value` when missing or
/// not convertible.
pub fn read_attribute<T: FromXmlAttribute>(
    attribs: &HashMap<String, String>,
    field: &str,
    default_value: T,
) -> T {
    attribs
        .get(field)
        .and_then(|v| T::from_xml_attribute(v))
        .unwrap_or(default_value)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn vector3() {
        assert_eq!(string_to_vector3("1.5, -2,3e2"), [1.5, -2.0, 300.0]);
        assert_eq!(string_to_vector3("1,2"), [0.0; 3]);
        assert_eq!(string_to_vector3("1,2,x"), [0.0; 3]);
        assert_eq!(string_to_vector3("1,2,inf"), [0.0; 3]);
    }

    #[test]
    fn attributes() {
        let doc = roxmltree::Document::parse(
            r#"<a xmlns:x="urn:x" id="12" f=" 1.25 " b="True" s="hi" neg="-3" bad="z"/>"#,
        )
        .unwrap();
        let attrs = read_node_attributes(doc.root_element());
        assert_eq!(attrs.len(), 6);
        assert_eq!(read_attribute(&attrs, "id", 0u32), 12);
        assert_eq!(read_attribute(&attrs, "f", 0.0f32), 1.25);
        assert_eq!(read_attribute(&attrs, "f", 0.0f64), 1.25);
        assert!(read_attribute(&attrs, "b", false));
        assert_eq!(read_attribute(&attrs, "s", String::new()), "hi");
        assert_eq!(read_attribute(&attrs, "neg", 0u8), 0);
        assert_eq!(read_attribute(&attrs, "neg", 0i8), -3);
        assert_eq!(read_attribute(&attrs, "bad", 7i32), 7);
        assert_eq!(read_attribute(&attrs, "missing", 9i32), 9);
    }
}
