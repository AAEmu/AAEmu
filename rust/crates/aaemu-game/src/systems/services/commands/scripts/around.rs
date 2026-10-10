//! `Scripts/Commands/Around.cs`.

use super::prelude::*;

/// `Around`.
pub struct Around;

impl Around {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Around {
    fn command_names(&self) -> Vec<String> {
        names(&["around", "near"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Around")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
