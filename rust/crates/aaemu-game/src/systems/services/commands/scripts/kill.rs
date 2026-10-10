//! `Scripts/Commands/Kill.cs`.

use super::prelude::*;

/// `Kill`.
pub struct Kill;

impl Kill {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Kill {
    fn command_names(&self) -> Vec<String> {
        names(&["kill"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Kill")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
