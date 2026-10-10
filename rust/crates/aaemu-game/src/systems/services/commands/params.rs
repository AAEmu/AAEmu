//! `Utils/Scripts/SubCommands/{SubCommandParameterBase, StringSubCommandParameter,
//! NumericSubCommandParameter<T>, ParameterResult(<T>), ParameterValue}`.

use crate::systems::services::webapi::json::{dotnet_double_string, dotnet_single_string};

/// `ParameterValue` (the boxed value of a parameter; `None` = `null`).
#[derive(Debug, Clone, PartialEq)]
pub enum ParameterValue {
    Null,
    Single(f32),
    Double(f64),
    Int64(i64),
    UInt64(u64),
    Int32(i32),
    UInt32(u32),
    Byte(u8),
    String(String),
}

impl ParameterValue {
    /// The .NET type name (`GetType().Name`).
    pub fn type_name(&self) -> &'static str {
        match self {
            ParameterValue::Null => "Object",
            ParameterValue::Single(_) => "Single",
            ParameterValue::Double(_) => "Double",
            ParameterValue::Int64(_) => "Int64",
            ParameterValue::UInt64(_) => "UInt64",
            ParameterValue::Int32(_) => "Int32",
            ParameterValue::UInt32(_) => "UInt32",
            ParameterValue::Byte(_) => "Byte",
            ParameterValue::String(_) => "String",
        }
    }

    fn cast_error(&self, target: &str) -> anyhow::Error {
        anyhow::anyhow!("Cannot cast {} to {target}", self.type_name())
    }

    /// `As<float>()` / implicit `float`.
    pub fn as_f32(&self) -> anyhow::Result<f32> {
        match self {
            ParameterValue::Single(v) => Ok(*v),
            other => Err(other.cast_error("Single")),
        }
    }

    pub fn as_f64(&self) -> anyhow::Result<f64> {
        match self {
            ParameterValue::Double(v) => Ok(*v),
            other => Err(other.cast_error("Double")),
        }
    }

    pub fn as_i64(&self) -> anyhow::Result<i64> {
        match self {
            ParameterValue::Int64(v) => Ok(*v),
            other => Err(other.cast_error("Int64")),
        }
    }

    pub fn as_u64(&self) -> anyhow::Result<u64> {
        match self {
            ParameterValue::UInt64(v) => Ok(*v),
            other => Err(other.cast_error("UInt64")),
        }
    }

    pub fn as_i32(&self) -> anyhow::Result<i32> {
        match self {
            ParameterValue::Int32(v) => Ok(*v),
            other => Err(other.cast_error("Int32")),
        }
    }

    pub fn as_u32(&self) -> anyhow::Result<u32> {
        match self {
            ParameterValue::UInt32(v) => Ok(*v),
            other => Err(other.cast_error("UInt32")),
        }
    }

    pub fn as_u8(&self) -> anyhow::Result<u8> {
        match self {
            ParameterValue::Byte(v) => Ok(*v),
            other => Err(other.cast_error("Byte")),
        }
    }

    pub fn as_str(&self) -> anyhow::Result<&str> {
        match self {
            ParameterValue::String(v) => Ok(v),
            // `value is string` is false for null: `As<string>` throws (NullReference on
            // `value.GetType()` in C#).
            other => Err(other.cast_error("String")),
        }
    }
}

impl std::fmt::Display for ParameterValue {
    /// `ToString()`.
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            ParameterValue::Null => Ok(()),
            ParameterValue::Single(v) => f.write_str(&dotnet_single_string(*v)),
            ParameterValue::Double(v) => f.write_str(&dotnet_double_string(*v)),
            ParameterValue::Int64(v) => write!(f, "{v}"),
            ParameterValue::UInt64(v) => write!(f, "{v}"),
            ParameterValue::Int32(v) => write!(f, "{v}"),
            ParameterValue::UInt32(v) => write!(f, "{v}"),
            ParameterValue::Byte(v) => write!(f, "{v}"),
            ParameterValue::String(v) => f.write_str(v),
        }
    }
}

impl From<&str> for ParameterValue {
    fn from(s: &str) -> Self {
        ParameterValue::String(s.to_string())
    }
}

/// `ParameterResult`.
#[derive(Debug, Clone, PartialEq)]
pub struct ParameterResult {
    pub name: String,
    pub value: ParameterValue,
    pub invalid_message: Option<String>,
}

impl ParameterResult {
    pub fn new(name: &str, value: ParameterValue, invalid_message: Option<String>) -> Self {
        Self {
            name: name.to_string(),
            value,
            invalid_message,
        }
    }

    /// `IsValid` (`string.IsNullOrEmpty(InvalidMessage)`).
    pub fn is_valid(&self) -> bool {
        self.invalid_message.as_deref().is_none_or(str::is_empty)
    }
}

/// The `IDictionary<string, ParameterValue>` an `Execute` receives (insertion order).
#[derive(Debug, Clone, Default, PartialEq)]
pub struct Parameters(pub Vec<(String, ParameterValue)>);

impl Parameters {
    /// `TryGetValue(name)`.
    pub fn get(&self, name: &str) -> Option<&ParameterValue> {
        self.0.iter().find(|(k, _)| k == name).map(|(_, v)| v)
    }

    /// `parameters[name]` (`KeyNotFoundException` when missing).
    pub fn require(&self, name: &str) -> anyhow::Result<&ParameterValue> {
        self.get(name).ok_or_else(|| {
            anyhow::anyhow!("The given key '{name}' was not present in the dictionary.")
        })
    }

    pub fn contains_key(&self, name: &str) -> bool {
        self.get(name).is_some()
    }

    pub fn len(&self) -> usize {
        self.0.len()
    }

    pub fn is_empty(&self) -> bool {
        self.0.is_empty()
    }

    pub fn iter(&self) -> impl Iterator<Item = &(String, ParameterValue)> {
        self.0.iter()
    }
}

/// `SubCommandParameterBase` members.
#[derive(Debug, Clone, PartialEq)]
pub struct ParameterBase {
    pub name: String,
    /// `DisplayName` (`displayName ?? name`).
    pub display_name: String,
    pub is_required: bool,
    pub prefix: Option<String>,
    /// `DefaultValue` (used for optional parameters that were not given).
    pub default_value: Option<ParameterValue>,
}

impl ParameterBase {
    pub fn new(name: &str, display_name: Option<&str>, required: bool) -> Self {
        Self {
            name: name.to_string(),
            display_name: display_name.unwrap_or(name).to_string(),
            is_required: required,
            prefix: None,
            default_value: None,
        }
    }

    /// `GetValueWithoutPrefix(argumentValue)`.
    pub fn value_without_prefix<'a>(&self, argument: &'a str) -> &'a str {
        match &self.prefix {
            None => argument,
            Some(p) => argument.get(p.len() + 1..).unwrap_or(""),
        }
    }

    /// `MatchPrefix(argument)`.
    pub fn match_prefix(&self, argument: &str) -> bool {
        self.prefix
            .as_ref()
            .is_some_and(|p| argument.starts_with(&format!("{p}=")))
    }
}

/// `SubCommandParameterBase`.
pub trait SubCommandParameter: Send + Sync {
    fn base(&self) -> &ParameterBase;
    fn base_mut(&mut self) -> &mut ParameterBase;
    /// `CallExample`.
    fn call_example(&self) -> String;
    /// `Load(argument)`.
    fn load(&self, argument: &str) -> ParameterResult;
}

/// `StringSubCommandParameter`.
#[derive(Debug, Clone)]
pub struct StringSubCommandParameter {
    base: ParameterBase,
    /// `_values` (lower-cased valid values; empty = anything).
    values: Vec<String>,
}

impl StringSubCommandParameter {
    /// `(name, displayName, isRequired, params validValues)`.
    pub fn new(
        name: &str,
        display_name: Option<&str>,
        required: bool,
        valid_values: &[&str],
    ) -> Self {
        Self {
            base: ParameterBase::new(name, display_name, required),
            values: valid_values.iter().map(|s| s.to_lowercase()).collect(),
        }
    }

    /// `(name, displayName, isRequired, prefix)`.
    pub fn with_prefix(
        name: &str,
        display_name: Option<&str>,
        required: bool,
        prefix: Option<&str>,
    ) -> Self {
        let mut p = Self::new(name, display_name, required, &[]);
        p.base.prefix = prefix.map(str::to_string);
        p
    }

    pub fn with_default(mut self, default: ParameterValue) -> Self {
        self.base.default_value = Some(default);
        self
    }
}

impl SubCommandParameter for StringSubCommandParameter {
    fn base(&self) -> &ParameterBase {
        &self.base
    }

    fn base_mut(&mut self) -> &mut ParameterBase {
        &mut self.base
    }

    fn call_example(&self) -> String {
        if self.values.is_empty() {
            self.base.display_name.clone()
        } else {
            self.values.join("||")
        }
    }

    fn load(&self, argument: &str) -> ParameterResult {
        if self.values.is_empty() {
            return ParameterResult::new(
                &self.base.name,
                ParameterValue::from(self.base.value_without_prefix(argument)),
                None,
            );
        }
        if !self.values.contains(&argument.to_lowercase()) {
            return ParameterResult::new(
                &self.base.name,
                ParameterValue::Null,
                Some(format!(
                    "Parameter [{}] only accepts: {}",
                    self.base.display_name,
                    self.values.join("||")
                )),
            );
        }
        ParameterResult::new(&self.base.name, ParameterValue::from(argument), None)
    }
}

/// The numeric types `NumericSubCommandParameter<T>` supports.
pub trait NumericKind: Copy + PartialOrd + Send + Sync + 'static {
    const TYPE_NAME: &'static str;
    const MIN: Self;
    const MAX: Self;
    /// `T.TryParse(text)` with the C# styles (integers: `NumberStyles.Integer`; floats:
    /// `NumberStyles.Number`, invariant culture).
    fn try_parse(text: &str) -> Option<Self>;
    fn to_value(self) -> ParameterValue;
    /// `ToString()`.
    fn to_dotnet_string(self) -> String;
    /// The value of a failed `TryParse` (`default`).
    fn zero() -> Self;
}

/// `NumberStyles.Integer`: surrounding white space and a leading sign.
fn integer_text(text: &str) -> Option<(bool, &str)> {
    let t = text.trim_matches(|c: char| c.is_ascii_whitespace());
    let (neg, digits) = match t.as_bytes().first() {
        Some(b'-') => (true, &t[1..]),
        Some(b'+') => (false, &t[1..]),
        _ => (false, t),
    };
    (!digits.is_empty() && digits.bytes().all(|b| b.is_ascii_digit())).then_some((neg, digits))
}

fn parse_signed(text: &str) -> Option<i128> {
    let (neg, digits) = integer_text(text)?;
    let v: i128 = digits.parse().ok()?;
    Some(if neg { -v } else { v })
}

/// `NumberStyles.Number` with the invariant culture: surrounding white space, a leading or
/// trailing sign, `,` group separators (only before the decimal point) and one `.`.
/// Returns the cleaned text for Rust's float parser.
fn parse_number_style(text: &str) -> Option<String> {
    let t = text.trim_matches(|c: char| c.is_ascii_whitespace());
    for (sym, v) in [("Infinity", "inf"), ("-Infinity", "-inf"), ("NaN", "NaN")] {
        if t.eq_ignore_ascii_case(sym) {
            return Some(v.to_string());
        }
    }
    let (mut neg, mut body) = (false, t);
    if let Some(rest) = body.strip_prefix('-') {
        neg = true;
        body = rest;
    } else if let Some(rest) = body.strip_prefix('+') {
        body = rest;
    } else if let Some(rest) = body.strip_suffix('-') {
        neg = true;
        body = rest;
    } else if let Some(rest) = body.strip_suffix('+') {
        body = rest;
    }
    let (int_part, frac_part) = match body.split_once('.') {
        Some((i, f)) => (i, Some(f)),
        None => (body, None),
    };
    if int_part.starts_with(',') {
        return None;
    }
    let int_digits: String = int_part.chars().filter(|&c| c != ',').collect();
    if !int_digits.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    let frac = frac_part.unwrap_or("");
    if !frac.bytes().all(|b| b.is_ascii_digit()) {
        return None;
    }
    if int_digits.is_empty() && frac.is_empty() {
        return None;
    }
    Some(format!(
        "{}{}.{}",
        if neg { "-" } else { "" },
        if int_digits.is_empty() {
            "0"
        } else {
            &int_digits
        },
        if frac.is_empty() { "0" } else { frac }
    ))
}

macro_rules! numeric_int {
    ($t:ty, $name:literal, $variant:ident) => {
        impl NumericKind for $t {
            const TYPE_NAME: &'static str = $name;
            const MIN: Self = <$t>::MIN;
            const MAX: Self = <$t>::MAX;
            fn try_parse(text: &str) -> Option<Self> {
                parse_signed(text).and_then(|v| <$t>::try_from(v).ok())
            }
            fn to_value(self) -> ParameterValue {
                ParameterValue::$variant(self)
            }
            fn to_dotnet_string(self) -> String {
                self.to_string()
            }
            fn zero() -> Self {
                0
            }
        }
    };
}

numeric_int!(i32, "Int32", Int32);
numeric_int!(i64, "Int64", Int64);
numeric_int!(u32, "UInt32", UInt32);
numeric_int!(u64, "UInt64", UInt64);
numeric_int!(u8, "Byte", Byte);

impl NumericKind for f32 {
    const TYPE_NAME: &'static str = "Single";
    const MIN: Self = f32::MIN;
    const MAX: Self = f32::MAX;
    fn try_parse(text: &str) -> Option<Self> {
        // .NET parses straight to float (correctly rounded), as here.
        parse_number_style(text).and_then(|s| s.parse().ok())
    }
    fn to_value(self) -> ParameterValue {
        ParameterValue::Single(self)
    }
    fn to_dotnet_string(self) -> String {
        dotnet_single_string(self)
    }
    fn zero() -> Self {
        0.0
    }
}

impl NumericKind for f64 {
    const TYPE_NAME: &'static str = "Double";
    const MIN: Self = f64::MIN;
    const MAX: Self = f64::MAX;
    fn try_parse(text: &str) -> Option<Self> {
        parse_number_style(text).and_then(|s| s.parse().ok())
    }
    fn to_value(self) -> ParameterValue {
        ParameterValue::Double(self)
    }
    fn to_dotnet_string(self) -> String {
        dotnet_double_string(self)
    }
    fn zero() -> Self {
        0.0
    }
}

/// `NumericSubCommandParameter<T>`.
#[derive(Debug, Clone)]
pub struct NumericSubCommandParameter<T: NumericKind> {
    base: ParameterBase,
    min: T,
    max: T,
}

impl<T: NumericKind + std::fmt::Debug> NumericSubCommandParameter<T> {
    /// `(name, displayName, required)`: the full range of `T`.
    pub fn new(name: &str, display_name: Option<&str>, required: bool) -> Self {
        Self {
            base: ParameterBase::new(name, display_name, required),
            min: T::MIN,
            max: T::MAX,
        }
    }

    /// `(name, displayName, required, prefix)`.
    pub fn with_prefix(
        name: &str,
        display_name: Option<&str>,
        required: bool,
        prefix: Option<&str>,
    ) -> Self {
        let mut p = Self::new(name, display_name, required);
        p.base.prefix = prefix.map(str::to_string);
        p
    }

    /// `(name, displayName, required, [prefix,] minValue, maxValue)`: fails like the C#
    /// `ArgumentOutOfRangeException` when `min > max`.
    pub fn with_range(
        name: &str,
        display_name: Option<&str>,
        required: bool,
        prefix: Option<&str>,
        min: T,
        max: T,
    ) -> anyhow::Result<Self> {
        let base = ParameterBase::new(name, display_name, required);
        if min > max {
            return Err(anyhow::anyhow!(
                "Parameter [{}] minimum value {} must be less than or equal to maximum value (Parameter 'minValue')",
                base.display_name,
                min.to_dotnet_string()
            ));
        }
        let mut p = Self { base, min, max };
        p.base.prefix = prefix.map(str::to_string);
        Ok(p)
    }

    pub fn with_default(mut self, default: ParameterValue) -> Self {
        self.base.default_value = Some(default);
        self
    }
}

impl<T: NumericKind + std::fmt::Debug> SubCommandParameter for NumericSubCommandParameter<T> {
    fn base(&self) -> &ParameterBase {
        &self.base
    }

    fn base_mut(&mut self) -> &mut ParameterBase {
        &mut self.base
    }

    fn call_example(&self) -> String {
        self.base.display_name.clone()
    }

    fn load(&self, argument: &str) -> ParameterResult {
        let text = self.base.value_without_prefix(argument);
        let parsed = T::try_parse(text);
        let result = parsed.unwrap_or_else(T::zero);
        let invalid = match parsed {
            None => Some(format!(
                "Invalid numeric value for parameter: {}",
                self.base.display_name
            )),
            Some(v) if !(v >= self.min && v <= self.max) => Some(format!(
                "The number {} should be between {} and {} for parameter: {}",
                v.to_dotnet_string(),
                self.min.to_dotnet_string(),
                self.max.to_dotnet_string(),
                self.base.display_name
            )),
            Some(_) => None,
        };
        ParameterResult::new(&self.base.name, result.to_value(), invalid)
    }
}

#[cfg(test)]
mod tests {
    //! Port of `NumericSubCommandParameterTests.cs`.
    use super::*;

    fn load<T: NumericKind + std::fmt::Debug>(arg: &str) -> ParameterResult {
        NumericSubCommandParameter::<T>::new("numericParameter", Some("numeric Parameter"), true)
            .load(arg)
    }

    #[test]
    fn load_when_type_is_supported_and_value_is_valid() {
        assert_eq!(load::<i32>("123").value, ParameterValue::Int32(123));
        assert_eq!(load::<i32>("-123").value, ParameterValue::Int32(-123));
        assert_eq!(load::<u32>("123").value, ParameterValue::UInt32(123));
        assert_eq!(load::<f32>("123").value, ParameterValue::Single(123.0));
        assert_eq!(load::<f32>("123.23").value, ParameterValue::Single(123.23));
        assert_eq!(
            load::<f32>("-123.23").value,
            ParameterValue::Single(-123.23)
        );
        assert_eq!(load::<i64>("123").value, ParameterValue::Int64(123));
        assert_eq!(load::<i64>("-123").value, ParameterValue::Int64(-123));
        assert_eq!(load::<u8>("123").value, ParameterValue::Byte(123));
        assert!(load::<u8>("123").is_valid());
        // NumberStyles.Number extras.
        assert_eq!(
            load::<f32>(" 1,000.5 ").value,
            ParameterValue::Single(1000.5)
        );
        assert_eq!(load::<f64>("2-").value, ParameterValue::Double(-2.0));
    }

    #[test]
    fn load_when_value_is_invalid() {
        for r in [
            load::<i32>("123.23"),
            load::<i32>("invalid"),
            load::<u32>("123.23"),
            load::<u32>("-123.23"),
            load::<u32>("invalid"),
            load::<f32>("invalid"),
            load::<f32>("1e5"),
            load::<i64>("123.23"),
            load::<i64>("invalid"),
            load::<u8>("123.23"),
            load::<u8>("invalid"),
        ] {
            assert!(!r.is_valid());
            assert!(r
                .invalid_message
                .unwrap()
                .contains("Invalid numeric value for parameter: numeric Parameter"));
        }
    }

    fn ranged<T: NumericKind + std::fmt::Debug>(arg: &str, min: T, max: T) -> ParameterResult {
        NumericSubCommandParameter::<T>::with_range(
            "validRangeParameter",
            Some("valid Range Parameter"),
            true,
            None,
            min,
            max,
        )
        .unwrap()
        .load(arg)
    }

    #[test]
    fn load_when_value_range_is_valid() {
        assert_eq!(
            ranged::<i32>("123", 1, 200).value,
            ParameterValue::Int32(123)
        );
        assert_eq!(
            ranged::<u32>("123", 1, 200).value,
            ParameterValue::UInt32(123)
        );
        for (min, max) in [
            (23.0, 690.0),
            (599.823, 599.823),
            (599.821, 599.823),
            (599.823, 599.824),
        ] {
            let r = ranged::<f32>("599.823", min, max);
            assert!(r.is_valid(), "{min} {max}");
            assert_eq!(r.value, ParameterValue::Single(599.823));
        }
        assert_eq!(
            ranged::<i64>("123", 10, 123).value,
            ParameterValue::Int64(123)
        );
        assert_eq!(ranged::<u8>("123", 1, 143).value, ParameterValue::Byte(123));
    }

    #[test]
    fn load_when_config_range_is_invalid() {
        let e = NumericSubCommandParameter::<i32>::with_range(
            "x",
            Some("invalid Config Range Parameter"),
            true,
            None,
            1240,
            125,
        )
        .unwrap_err();
        assert!(e
            .to_string()
            .contains("Parameter [invalid Config Range Parameter] minimum value 1240 must be less than or equal to maximum value"));
        assert!(NumericSubCommandParameter::<f32>::with_range(
            "x", None, true, None, 2300.0, 690.0
        )
        .is_err());
        assert!(
            NumericSubCommandParameter::<u8>::with_range("x", None, true, None, 200, 143).is_err()
        );
    }

    #[test]
    fn load_when_value_range_is_invalid() {
        let check = |r: ParameterResult, min: &str, max: &str| {
            assert!(!r.is_valid());
            let m = r.invalid_message.unwrap();
            assert!(
                m.contains(&format!(
                    "should be between {min} and {max} for parameter: valid Range Parameter"
                )),
                "{m}"
            );
        };
        check(ranged::<i32>("123", 120, 122), "120", "122");
        check(ranged::<i32>("123", 220, 1122), "220", "1122");
        check(ranged::<u32>("123", 1, 100), "1", "100");
        check(ranged::<f32>("599.823", 23.0, 500.0), "23", "500");
        check(
            ranged::<f32>("599.823", -599.821, 598.823),
            "-599.821",
            "598.823",
        );
        check(ranged::<i64>("123", 130, 220), "130", "220");
        check(ranged::<u8>("123", 124, 220), "124", "220");
        // Full float range message.
        let r = NumericSubCommandParameter::<f32>::new("p", None, true).load("1");
        assert!(r.is_valid());
        assert_eq!(f32::MAX.to_dotnet_string(), "3.4028235E+38");
    }

    #[test]
    fn load_when_prefix_numeric_parameter_is_valid() {
        let p = |prefix: &str| {
            NumericSubCommandParameter::<i32>::with_prefix(
                "numericParameter",
                None,
                true,
                Some(prefix),
            )
        };
        assert_eq!(
            p("prefix").load("prefix=123").value,
            ParameterValue::Int32(123)
        );
        assert_eq!(p("a").load("a=-123").value, ParameterValue::Int32(-123));
        let f = NumericSubCommandParameter::<f32>::with_prefix("n", None, true, Some("xr"));
        assert_eq!(f.load("xr=-123.23").value, ParameterValue::Single(-123.23));
        let b = NumericSubCommandParameter::<u8>::with_prefix("n", None, true, Some("sdaf"));
        assert_eq!(b.load("sdaf=123").value, ParameterValue::Byte(123));
        assert_eq!(b.base().display_name, "n");
    }

    #[test]
    fn string_parameter() {
        let p =
            StringSubCommandParameter::new("test", Some("test display name"), true, &["A", "b"]);
        assert_eq!(p.call_example(), "a||b");
        assert_eq!(p.load("A").value, ParameterValue::from("A"));
        assert_eq!(
            p.load("c").invalid_message.as_deref(),
            Some("Parameter [test display name] only accepts: a||b")
        );
        let p = StringSubCommandParameter::with_prefix("n", None, false, Some("x"));
        assert_eq!(p.load("x=hello").value, ParameterValue::from("hello"));
        assert_eq!(p.call_example(), "n");
    }

    #[test]
    fn parameter_value_casts() {
        assert_eq!(ParameterValue::Single(1.5).as_f32().unwrap(), 1.5);
        assert_eq!(
            ParameterValue::Int32(1).as_f32().unwrap_err().to_string(),
            "Cannot cast Int32 to Single"
        );
        assert_eq!(ParameterValue::Single(50.93).to_string(), "50.93");
    }
}
