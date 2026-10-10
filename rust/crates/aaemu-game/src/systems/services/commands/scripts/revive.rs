//! `Scripts/Commands/Revive.cs`.

use super::prelude::*;

/// `Revive`.
pub struct Revive;

impl Revive {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Revive {
    fn command_names(&self) -> Vec<String> {
        names(&["revive"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Revive")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
