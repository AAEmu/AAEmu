//! `Scripts/Commands/Kick.cs` (+ `SCKickedPacket`).

use aaemu_commons::{Marshal, PacketStream};

use super::prelude::*;
use crate::net::packet::LEVEL_PLAIN;
use crate::opcodes::sc;
use crate::packets::packet_def;

/// `SCKickedPacket(reason, msg)`.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SCKickedPacket {
    /// `KickedReason` (0 duplicate account, 1 by GM, 2 maintenance, 3 invalid doodad
    /// interaction).
    pub reason: u8,
    pub msg: String,
}
packet_def!(
    SCKickedPacket,
    level = LEVEL_PLAIN,
    opcode = sc::SCKickedPacket
);
impl Marshal for SCKickedPacket {
    fn write(&self, s: &mut PacketStream) {
        s.write_u8(self.reason).write_str(&self.msg);
    }
}

/// `Kick`.
pub struct Kick;

impl Kick {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Kick {
    fn command_names(&self) -> Vec<String> {
        names(&["kick_player", "kick"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        if args.len() < 3 {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        }
        let server = ctx.server()?;
        let target = match try_parse::<u32>(&args[0]) {
            Some(id) => server.world.character_by_id(id),
            None => server.world.character_by_name(&args[0]),
        };
        let Some(target) = target else {
            CommandManager::send_normal_text(self, out, "Target not found");
            return Ok(());
        };
        // `(KickedReason)byte.Parse(args[1])`: throws on anything but a byte.
        let reason = match try_parse::<u8>(&args[1]) {
            Some(r) => r,
            None if try_parse::<i128>(&args[1]).is_some() => {
                anyhow::bail!("Value was either too large or too small for an unsigned byte.")
            }
            None => anyhow::bail!(
                "The input string '{}' was not in a correct format.",
                args[1]
            ),
        };
        let mut msg = String::new();
        for a in &args[2..] {
            msg += a;
            msg += " ";
        }
        target.send(SCKickedPacket { reason, msg });
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "(character name || id) (reason) (msg)".into()
    }

    fn get_command_help_text(&self) -> String {
        "Kicks target".into()
    }
}

#[cfg(test)]
mod tests {
    use aaemu_commons::PacketStream;

    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn kick_sends_the_kicked_packet() {
        let server = harness::server();
        let gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut bob = harness::pc(&server, 2, "Bob", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "kick bob 1 go away").await;
        assert!(out.is_empty());
        let mut body = PacketStream::new();
        body.write_u8(1).write_str("go away ");
        assert_eq!(bob.frames(), vec![(sc::SCKickedPacket, body.into_bytes())]);
        // By character id.
        harness::run(&server, &gm.p, "kick_player 2 0 x").await;
        assert_eq!(bob.frames().len(), 1);
        let (_, out) = harness::run(&server, &gm.p, "kick nobody 1 x").await;
        assert_eq!(out, ["[kick_player] Target not found"]);
        let (_, out) = harness::run(&server, &gm.p, "kick bob").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/kick_player|r |cFFEEEEAA(character name || id) (reason) (msg)|r\n|cFF888888Kicks target|r"]
        );
    }
}
