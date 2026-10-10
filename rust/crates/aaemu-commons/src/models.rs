//! Port of `AAEmu.Commons.Models`.

use serde::{Deserialize, Serialize};

use crate::packet_stream::{Marshal, PacketStream, Result, Unmarshal};

/// `LoginCharacterInfo`: character summary exchanged between login and game servers.
///
/// Note: like the C# `Read`/`Write`, `gs_id` is **not** part of the wire format.
#[derive(Debug, Clone, Default, PartialEq, Eq)]
pub struct LoginCharacterInfo {
    pub id: u32,
    pub account_id: u32,
    pub gs_id: u8,
    pub name: String,
    pub race: u8,
    pub gender: u8,
}

impl Marshal for LoginCharacterInfo {
    fn write(&self, stream: &mut PacketStream) {
        stream
            .write_u32(self.id)
            .write_u32(self.account_id)
            .write_str(&self.name)
            .write_u8(self.race)
            .write_u8(self.gender);
    }
}

impl Unmarshal for LoginCharacterInfo {
    fn read(stream: &mut PacketStream) -> Result<Self> {
        Ok(Self {
            id: stream.read_u32()?,
            account_id: stream.read_u32()?,
            gs_id: 0,
            name: stream.read_string()?,
            race: stream.read_u8()?,
            gender: stream.read_u8()?,
        })
    }
}

/// `MySqlConnectionSettings` (configuration section; JSON keys as in the C# config files).
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(default, rename_all = "PascalCase")]
pub struct MySqlConnectionSettings {
    pub host: String,
    pub port: u16,
    pub user: String,
    pub password: String,
    pub database: String,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn login_character_info_roundtrip() {
        let c = LoginCharacterInfo {
            id: 7,
            account_id: 9,
            gs_id: 3,
            name: "Bob".into(),
            race: 1,
            gender: 2,
        };
        let mut s = PacketStream::new();
        s.write(&c);
        assert_eq!(s.len(), 4 + 4 + 2 + 3 + 1 + 1);
        let back: LoginCharacterInfo = s.read().unwrap();
        assert_eq!(back, LoginCharacterInfo { gs_id: 0, ..c });
    }

    #[test]
    fn settings_json() {
        let s: MySqlConnectionSettings = serde_json::from_str(
            r#"{"Host":"db","Port":3307,"User":"u","Password":"p","Database":"d"}"#,
        )
        .unwrap();
        assert_eq!(s.port, 3307);
        assert_eq!(s.host, "db");
    }
}
