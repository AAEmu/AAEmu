//! `Scripts/Commands/Run.cs`.

use super::prelude::*;

/// `Run`.
pub struct Run;

impl Run {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Run {
    fn command_names(&self) -> Vec<String> {
        names(&["run"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Run")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
