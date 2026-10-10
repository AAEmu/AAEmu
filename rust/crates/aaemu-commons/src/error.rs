//! Port of `AAEmu.Commons.Exceptions`.
//!
//! * `GameException` → [`GameError`].
//! * `MarshalException` (parameterless "Marshal exception") → already covered by
//!   [`crate::MarshalError`], which carries the actual cause.

use std::error::Error as StdError;
use std::fmt;

/// `GameException`: a message plus an optional inner error.
#[derive(Debug)]
pub struct GameError {
    message: String,
    source: Option<Box<dyn StdError + Send + Sync + 'static>>,
}

impl GameError {
    /// `new GameException(message)`.
    pub fn new(message: impl Into<String>) -> Self {
        Self {
            message: message.into(),
            source: None,
        }
    }

    /// `new GameException(message, innerException)`.
    pub fn with_source(
        message: impl Into<String>,
        source: impl StdError + Send + Sync + 'static,
    ) -> Self {
        Self {
            message: message.into(),
            source: Some(Box::new(source)),
        }
    }

    pub fn message(&self) -> &str {
        &self.message
    }
}

impl fmt::Display for GameError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.message)?;
        if let Some(s) = &self.source {
            write!(f, ": {s}")?;
        }
        Ok(())
    }
}

impl StdError for GameError {
    fn source(&self) -> Option<&(dyn StdError + 'static)> {
        self.source
            .as_deref()
            .map(|e| e as &(dyn StdError + 'static))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn display_and_source() {
        let e = GameError::new("boom");
        assert_eq!(e.to_string(), "boom");
        assert!(e.source().is_none());
        let e = GameError::with_source("outer", GameError::new("inner"));
        assert_eq!(e.to_string(), "outer: inner");
        assert_eq!(e.message(), "outer");
        assert!(e.source().is_some());
    }
}
