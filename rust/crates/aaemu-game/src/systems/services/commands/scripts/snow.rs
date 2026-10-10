//! `Scripts/Commands/Snow.cs`.

use std::sync::atomic::Ordering;

use super::prelude::*;
use crate::packets::world::SCOnOffSnowPacket;

/// `Snow`.
pub struct Snow;

impl Snow {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Snow {
    fn command_names(&self) -> Vec<String> {
        names(&["snow"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // If no argument is provided send usage information
        let Some(arg) = args.first() else {
            CommandManager::send_default_help_text(self, out);
            return Ok(());
        };
        // determine if we received true,false or something else
        if let Some(is_snowing) = parse_bool(arg) {
            let server = ctx.server()?;
            // Set Snowing state to user input, This will
            // Enable Snow on all players who log into the server
            server.world.is_snowing.store(is_snowing, Ordering::Relaxed);
            // Turn snow on or off for all online characters
            server
                .world
                .broadcast_to_server(&SCOnOffSnowPacket { on: is_snowing });
        } else {
            // user input was invalid notify them
            CommandManager::send_error_text(self, out, "Error parsing boolean");
        }
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        "<true||false>".into()
    }

    fn get_command_help_text(&self) -> String {
        "Enables or disables snow effect across the server".into()
    }
}

#[cfg(test)]
mod tests {
    use std::sync::atomic::Ordering;

    use super::super::harness;
    use crate::opcodes::sc;

    #[tokio::test]
    async fn snow_toggles_and_broadcasts() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let mut other = harness::pc(&server, 2, "Other", 900.0, 900.0);
        let (_, out) = harness::run(&server, &gm.p, "snow TRUE").await;
        assert!(out.is_empty());
        assert!(server.world.is_snowing.load(Ordering::Relaxed));
        for pc in [&mut gm, &mut other] {
            assert_eq!(pc.packets(), vec![(sc::SCOnOffSnowPacket, vec![1])]);
        }
        let (_, out) = harness::run(&server, &gm.p, "snow maybe").await;
        assert_eq!(out, [harness::error("snow", "Error parsing boolean")]);
        let (_, out) = harness::run(&server, &gm.p, "snow").await;
        assert_eq!(
            out,
            ["Help for |cFFFFFFFF/snow|r |cFFEEEEAA<true||false>|r\n|cFF888888Enables or disables snow effect across the server|r"]
        );
    }
}
