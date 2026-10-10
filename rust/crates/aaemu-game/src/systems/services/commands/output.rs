//! `Utils/Scripts/IMessageOutput`, `CharacterMessageOutput` and the `ICharacter` members the
//! commands use (`SendMessage`, `SendErrorMessage`), plus `System.Drawing.Color`.

use std::sync::{Arc, Mutex};

use crate::packets::world::SCErrorMsgPacket;
use crate::systems::social::chat::chat_type;
use crate::world::PlayerRef;

/// `System.Drawing.Color` (ARGB).
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Color {
    pub a: u8,
    pub r: u8,
    pub g: u8,
    pub b: u8,
}

impl Color {
    pub const fn argb(a: u8, r: u8, g: u8, b: u8) -> Self {
        Self { a, r, g, b }
    }

    pub const RED: Color = Color::argb(255, 255, 0, 0);
    pub const LAWN_GREEN: Color = Color::argb(255, 124, 252, 0);
    pub const CORAL: Color = Color::argb(255, 255, 127, 80);
    pub const YELLOW: Color = Color::argb(255, 255, 255, 0);
    pub const ORANGE: Color = Color::argb(255, 255, 165, 0);
    pub const GREEN: Color = Color::argb(255, 0, 128, 0);
    pub const WHITE: Color = Color::argb(255, 255, 255, 255);
    pub const GRAY: Color = Color::argb(255, 128, 128, 128);
    pub const CYAN: Color = Color::argb(255, 0, 255, 255);
    pub const ORANGE_RED: Color = Color::argb(255, 255, 69, 0);

    /// `|cAARRGGBB{message}|r`.
    pub fn wrap(self, message: &str) -> String {
        format!(
            "|c{:02X}{:02X}{:02X}{:02X}{message}|r",
            self.a, self.r, self.g, self.b
        )
    }
}

/// The `ICharacter` members the command infrastructure uses.
pub trait ICharacter: Send + Sync {
    /// `Name`.
    fn name(&self) -> String;
    /// `SendMessage(ChatType type, string message, Color? color = null)`.
    fn send_message_typed(&self, chat_type: i16, message: &str, color: Option<Color>);
    /// `SendMessage(string message)`.
    fn send_message(&self, message: &str) {
        self.send_message_typed(chat_type::SYSTEM, message, None);
    }
    /// `SendErrorMessage(errorMsgType, type, isNotify)`.
    fn send_error_message(&self, error: i16, kind: u32, is_notify: bool);
    /// The world player behind this character (`None` for test doubles).
    fn player(&self) -> Option<PlayerRef> {
        None
    }
}

/// A world player as an `ICharacter`.
#[derive(Debug, Clone)]
pub struct PlayerCharacter(pub PlayerRef);

impl PlayerCharacter {
    pub fn arc(p: &PlayerRef) -> Arc<dyn ICharacter> {
        Arc::new(Self(p.clone()))
    }
}

impl ICharacter for PlayerCharacter {
    fn name(&self) -> String {
        self.0.data().unit.name.clone()
    }

    fn send_message_typed(&self, chat_type: i16, message: &str, color: Option<Color>) {
        let message = match color {
            Some(c) => c.wrap(message),
            None => message.to_string(),
        };
        // `SendPacket(new SCChatMessagePacket(type, message))`.
        crate::world::session::send_message(&self.0, chat_type, &message);
    }

    fn send_error_message(&self, error: i16, kind: u32, is_notify: bool) {
        self.0.send(SCErrorMsgPacket::new(error, kind, is_notify));
    }

    fn player(&self) -> Option<PlayerRef> {
        Some(self.0.clone())
    }
}

/// `IMessageOutput`.
pub trait MessageOutput: Send {
    /// `Messages`.
    fn messages(&self) -> &[String];
    /// `ErrorMessages`.
    fn error_messages(&self) -> &[String];
    /// `SendMessage(ChatType chatType, string message, Color? color = null)`.
    fn send_message_typed(&mut self, chat_type: i16, message: &str, color: Option<Color>);
    /// `SendMessage(string message)`.
    fn send_message(&mut self, message: &str) {
        self.send_message_typed(chat_type::SYSTEM, message, None);
    }
    /// `SendMessage(ICharacter target, string message)`.
    fn send_message_to(&mut self, target: &dyn ICharacter, message: &str);
}

/// `CharacterMessageOutput(character)`: records every message and forwards it to the
/// character.
pub struct CharacterMessageOutput {
    character: Arc<dyn ICharacter>,
    messages: Vec<String>,
    error_messages: Vec<String>,
}

impl CharacterMessageOutput {
    pub fn new(character: Arc<dyn ICharacter>) -> Self {
        Self {
            character,
            messages: Vec::new(),
            error_messages: Vec::new(),
        }
    }

    /// `SendErrorMessage(messageType, type, isNotify)` (`ErrorMessageType.ToString()` is the
    /// recorded text: pass the enum member name).
    pub fn send_error_message(&mut self, error: i16, name: &str, kind: u32, is_notify: bool) {
        self.error_messages.push(name.to_string());
        self.character.send_error_message(error, kind, is_notify);
    }
}

impl MessageOutput for CharacterMessageOutput {
    fn messages(&self) -> &[String] {
        &self.messages
    }

    fn error_messages(&self) -> &[String] {
        &self.error_messages
    }

    fn send_message_typed(&mut self, chat_type: i16, message: &str, color: Option<Color>) {
        let message = match color {
            Some(c) => c.wrap(message),
            None => message.to_string(),
        };
        self.messages.push(message.clone());
        self.character.send_message_typed(chat_type, &message, None);
    }

    fn send_message_to(&mut self, target: &dyn ICharacter, message: &str) {
        self.messages
            .push(format!("Target: {} - {message}", target.name()));
        target.send_message(message);
    }
}

/// A test double for `ICharacter` that records what it receives.
#[derive(Debug, Default)]
pub struct RecordingCharacter {
    pub name: String,
    /// (chat type, message, color).
    pub received: Mutex<Vec<(i16, String, Option<Color>)>>,
    pub errors: Mutex<Vec<(i16, u32, bool)>>,
}

impl RecordingCharacter {
    pub fn named(name: &str) -> Arc<Self> {
        Arc::new(Self {
            name: name.into(),
            ..Self::default()
        })
    }

    pub fn messages(&self) -> Vec<String> {
        self.received
            .lock()
            .expect("poisoned")
            .iter()
            .map(|(_, m, _)| m.clone())
            .collect()
    }
}

impl ICharacter for RecordingCharacter {
    fn name(&self) -> String {
        self.name.clone()
    }

    fn send_message_typed(&self, chat_type: i16, message: &str, color: Option<Color>) {
        self.received
            .lock()
            .expect("poisoned")
            .push((chat_type, message.to_string(), color));
    }

    fn send_error_message(&self, error: i16, kind: u32, is_notify: bool) {
        self.errors
            .lock()
            .expect("poisoned")
            .push((error, kind, is_notify));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn character_message_output_records_and_forwards() {
        let c = RecordingCharacter::named("Bob");
        let mut out = CharacterMessageOutput::new(c.clone());
        out.send_message("hi");
        out.send_message_typed(chat_type::SYSTEM, "err", Some(Color::RED));
        let target = RecordingCharacter::named("Al");
        out.send_message_to(target.as_ref(), "x");
        out.send_error_message(5, "BagFull", 0, true);
        assert_eq!(out.messages(), ["hi", "|cFFFF0000err|r", "Target: Al - x"]);
        assert_eq!(out.error_messages(), ["BagFull"]);
        assert_eq!(c.messages(), ["hi", "|cFFFF0000err|r"]);
        assert_eq!(target.messages(), ["x"]);
        assert_eq!(*c.errors.lock().unwrap(), vec![(5, 0, true)]);
    }
}
