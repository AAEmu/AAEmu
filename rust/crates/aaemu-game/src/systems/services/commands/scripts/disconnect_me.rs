//! `Scripts/Commands/DisconnectMe.cs`.

use super::prelude::*;

/// `DisconnectMe`.
pub struct DisconnectMe;

impl DisconnectMe {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for DisconnectMe {
    fn command_names(&self) -> Vec<String> {
        names(&["disconnectme"])
    }

    fn execute(
        &self,
        ctx: &CommandContext<'_>,
        _args: &[String],
        out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        // user input was invalid notify them
        CommandManager::send_error_text(self, out, "Disconnecting ...");
        // `character.Connection.ActiveChar = null; character.Connection = null; con.Shutdown()`:
        // the connection closes without saving / removing the character (C#).
        ctx.player()?.conn.detach_and_close();
        Ok(())
    }

    fn get_command_line_help(&self) -> String {
        String::new()
    }

    fn get_command_help_text(&self) -> String {
        "Forcefully disconnect your character from your connection".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;
    use crate::net::connection::ConnCommand;

    #[tokio::test]
    async fn disconnect_me_detaches_and_closes() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (_, out) = harness::run(&server, &gm.p, "disconnectme").await;
        assert_eq!(out, [harness::error("disconnectme", "Disconnecting ...")]);
        let cmds = gm.client.commands();
        assert!(matches!(cmds.as_slice(), [ConnCommand::DetachAndClose]));
    }
}
