//! `Scripts/Commands/PingPosition.cs`.

use super::prelude::*;

/// `PingPosition`.
pub struct PingPosition;

impl PingPosition {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for PingPosition {
    fn command_names(&self) -> Vec<String> {
        names(&["pingpos", "ping_pos", "pingposition"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: PingPosition")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
