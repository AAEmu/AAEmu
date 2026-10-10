//! Port of `AAEmu.Commons.Utils.StringExtensions`.

/// Error for invalid string arguments (C# `ArgumentException`).
#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
#[error("{0} is null or empty")]
pub struct ArgumentError(pub &'static str);

/// Upper-cases a single char the way .NET's `char.ToUpper` does: one char in, one char out
/// (characters whose upper case is several chars, like `ß`, are left unchanged).
fn char_to_upper(c: char) -> char {
    let mut up = c.to_uppercase();
    match (up.next(), up.next()) {
        (Some(u), None) => u,
        _ => c,
    }
}

pub trait StringExtensions {
    /// `FirstCharToUpper`: makes the first character upper case. Errors on an empty string.
    fn first_char_to_upper(&self) -> Result<String, ArgumentError>;

    /// `NormalizeName`: trims, upper-cases the first character and lower-cases the rest.
    /// A whitespace-only input is returned unchanged.
    fn normalize_name(&self) -> String;
}

impl StringExtensions for str {
    fn first_char_to_upper(&self) -> Result<String, ArgumentError> {
        let mut chars = self.chars();
        let first = chars.next().ok_or(ArgumentError("input"))?;
        let mut s = String::with_capacity(self.len());
        s.push(char_to_upper(first));
        s.push_str(chars.as_str());
        Ok(s)
    }

    fn normalize_name(&self) -> String {
        let trimmed = self.trim();
        let mut chars = trimmed.chars();
        let Some(first) = chars.next() else {
            return self.to_string();
        };
        let mut s = String::with_capacity(trimmed.len());
        s.push(char_to_upper(first));
        // C# lower-cases per UTF-16 unit into a buffer of the same length; per char here.
        for c in chars {
            let mut low = c.to_lowercase();
            match (low.next(), low.next()) {
                (Some(l), None) => s.push(l),
                _ => s.push(c),
            }
        }
        s
    }
}

/// Tests ported from `AAEmu.UnitTests/Commons/Utils/StringExtensionsTest.cs`.
#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn first_char_to_upper_should_work_as_expected() {
        for (input, expected) in [
            ("test", "Test"),
            ("Test", "Test"),
            ("tEST", "TEST"),
            ("TEST", "TEST"),
            ("test test", "Test test"),
            ("t", "T"),
            ("T", "T"),
            ("1", "1"),
        ] {
            assert_eq!(input.first_char_to_upper().unwrap(), expected);
        }
    }

    /// The C# test also passes `null`; Rust `&str` can't be null, so only `""` applies.
    #[test]
    fn first_char_to_upper_should_throw_when_invalid() {
        assert!("".first_char_to_upper().is_err());
    }

    #[test]
    fn normalize_name_should_work_as_expected() {
        for (input, expected) in [
            ("test", "Test"),
            ("Test", "Test"),
            ("tEST", "Test"),
            ("TEST", "Test"),
            ("TEST ", "Test"),
            (" tEsT ", "Test"),
            ("test test", "Test test"),
            ("t", "T"),
            ("T", "T"),
            ("1", "1"),
            (" \t ", " \t "),
        ] {
            assert_eq!(input.normalize_name(), expected, "{input:?}");
        }
    }

    #[test]
    fn non_ascii() {
        assert_eq!("ßa".first_char_to_upper().unwrap(), "ßa");
        assert_eq!("éLAN".normalize_name(), "Élan");
    }
}
