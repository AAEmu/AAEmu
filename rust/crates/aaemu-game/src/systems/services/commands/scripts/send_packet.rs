//! `Scripts/Commands/SendPacket.cs` (+ `SCRawPacket`).

use aaemu_commons::PacketStream;

use super::prelude::*;
use crate::net::packet::{encode, ServerPacket, LEVEL_PLAIN};

/// `SCRawPacket(opcode, payload)`: a level-1 packet with any opcode and body.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCRawPacket {
    pub opcode: u16,
    pub payload: Vec<u8>,
}

impl ServerPacket for SCRawPacket {
    fn opcode(&self) -> u16 {
        self.opcode
    }

    fn level(&self) -> u8 {
        LEVEL_PLAIN
    }

    fn name(&self) -> &'static str {
        "SCRawPacket"
    }

    fn write_body(&self, stream: &mut PacketStream) {
        stream.write_raw(&self.payload);
    }
}

/// `GamePacket.Encode().ToString()`: the length-prefixed frame as `BitConverter.ToString`
/// (`"0A-00-DD-01-..."`).
pub fn encoded_string(packet: &dyn ServerPacket) -> String {
    let frame = encode(packet);
    let mut bytes = (frame.len() as u16).to_le_bytes().to_vec();
    bytes.extend(frame);
    bytes
        .iter()
        .map(|b| format!("{b:02X}"))
        .collect::<Vec<_>>()
        .join("-")
}

/// `SendPacket`.
pub struct SendPacket;

impl SendPacket {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for SendPacket {
    fn command_names(&self) -> Vec<String> {
        names(&["packet"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() != 1 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let path = std::env::current_dir()?.join(&args[0]);
        let hex = if path.is_file() {
            CommandManager::send_normal_text(self, out, &format!("File: {}", args[0]));
            let bytes = std::fs::read(&path)?;
            String::from_utf8_lossy(bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(&bytes))
                .into_owned()
        } else {
            args[0].clone()
        };
        let hex: Vec<char> = hex.chars().collect();
        if hex.len() & 1 != 0 {
            CommandManager::send_error_text(
                self,
                out,
                "Hex-data must be even in length and greater than 2 bytes",
            );
            return Ok(());
        }
        if !hex.iter().all(char::is_ascii_hexdigit) {
            ctx.character
                .send_message(&format!("[Packet] {COMMAND_PREFIX}packet <hex/file path>"));
            return Ok(());
        }
        let nibble = |c: char| {
            let c = c as u32;
            ((c & 0xf) + ((c & 0x40) >> 6) * 9) as u8
        };
        let raw: Vec<u8> = hex
            .chunks(2)
            .map(|p| (nibble(p[0]) << 4) | nibble(p[1]))
            .collect();
        if raw.len() < 2 {
            anyhow::bail!("Index was outside the bounds of the array.");
        }
        let type_id = u16::from_le_bytes([raw[0], raw[1]]);
        let p = SCRawPacket {
            opcode: type_id,
            payload: raw[2..].to_vec(),
        };
        CommandManager::send_normal_text(self, out, &encoded_string(&p));
        ctx.player()?.send(p);
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<hex values/file path>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Send packet (hex) to player's character from either a file path or received message".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn packet_sends_raw_bytes() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "packet 9f00aBcd").await;
        assert_eq!(out, ["[packet] 08-00-DD-01-00-00-9F-00-AB-CD"]);
        assert_eq!(gm.packets(), vec![(0x9f, vec![0xab, 0xcd])]);
        let (_, out) = harness::run(&server, &gm.p, "packet 9f0").await;
        assert_eq!(
            out,
            [harness::error(
                "packet",
                "Hex-data must be even in length and greater than 2 bytes"
            )]
        );
        gm.chat();
        harness::run(&server, &gm.p, "packet zz").await;
        assert_eq!(gm.chat(), ["[Packet] /packet <hex/file path>"]);
        // One byte: the C# reads raw[1] and throws.
        harness::run(&server, &gm.p, "packet 0a").await;
        assert_eq!(gm.chat(), ["Index was outside the bounds of the array."]);
    }
}
