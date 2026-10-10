//! `Scripts/Commands/Help.cs`.

use futures::future::BoxFuture;

use super::prelude::*;
use crate::systems::services::character::effective_access_level;

/// `Help`.
pub struct Help;

impl Help {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Help {
    fn command_names(&self) -> Vec<String> {
        names(&["help", "?"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("Help runs through execute_async")
    }

    fn execute_async<'a>(
        &'a self,
        ctx: &'a CommandContext<'a>,
        args: &'a [String],
        _out: &'a mut dyn MessageOutput,
    ) -> BoxFuture<'a, anyhow::Result<()>> {
        Box::pin(async move {
            let server = ctx.server()?;
            let player = ctx.player()?;
            let commands = &server.systems.services.commands;
            let character = &ctx.character;
            if let Some(first) = args.first() {
                let this_command = first.to_lowercase();
                let access_level = player.data().access_level;
                let not_available =
                    format!("Help for: |cFFFFFFFF{COMMAND_PREFIX}{this_command}|r not available!");
                if commands
                    .access_levels
                    .get_level(&commands.get_command_name_base(&this_command))
                    > access_level
                {
                    // deliberately the same error as command not found
                    character.send_message(&not_available);
                } else {
                    let Some(cmd) = commands.get_command_interface_by_name(&this_command) else {
                        // deliberately the same error as insufficient rights
                        character.send_message(&not_available);
                        return Ok(());
                    };
                    character.send_message(&format!(
                        "Help for: |cFFFFFFFF{COMMAND_PREFIX}{this_command} {}|r\n|cFF999999{}|r",
                        cmd.get_command_line_help(),
                        cmd.get_command_help_text()
                    ));
                }
                return Ok(());
            }

            character.send_message(
                "|cFF80FFFFList of available GM Commands|r\n-------------------------\n",
            );
            let mut list = commands.get_command_keys();
            // `List<string>.Sort()` (culture compare): the keys are lower-case letters and `_`,
            // which sorts before letters both culturally and ordinally.
            list.sort();
            let character_access_level = effective_access_level(server, &player).await;
            for command in list {
                if command == "help" {
                    continue;
                }
                if commands.access_levels.get_level(&command) > character_access_level {
                    continue;
                }
                let Some(cmd) = commands.get_command_interface_by_name(&command) else {
                    continue; // should never happen
                };
                let help_line_text = cmd.get_command_line_help();
                if !help_line_text.is_empty() {
                    character.send_message(&format!(
                        "{COMMAND_PREFIX}{command} |cFF999999{help_line_text}|r"
                    ));
                } else {
                    character.send_message(&format!("{COMMAND_PREFIX}{command}"));
                }
            }
            Ok(())
        })
    }

    fn get_command_line_help(&self) -> String {
        "[topic]".into()
    }

    fn get_command_help_text(&self) -> String {
        "Displays help about a command <topic>. If no <topic> is provided, a list of all GM commands will be displayed".into()
    }
}

#[cfg(test)]
mod tests {
    use super::super::harness;

    #[tokio::test]
    async fn help_topic_and_unknown() {
        let server = harness::server();
        let mut gm = harness::pc(&server, 1, "Gm", 100.0, 100.0);
        let (handled, _) = harness::run(&server, &gm.p, "help echo").await;
        assert!(handled);
        assert_eq!(
            gm.chat(),
            ["Help for: |cFFFFFFFF/echo <text>|r\n|cFF999999Repeats the provided arguments in chat as raw text|r"]
        );
        harness::run(&server, &gm.p, "? nothing").await;
        assert_eq!(gm.chat(), ["Help for: |cFFFFFFFF/nothing|r not available!"]);
        // `help` for a command above the character's own (raw) access level.
        gm.p.data().access_level = 0;
        harness::run(&server, &gm.p, "help echo").await;
        assert_eq!(gm.chat(), ["Help for: |cFFFFFFFF/echo|r not available!"]);
    }
}
