//! `Scripts/Commands/Move.cs`.

use super::prelude::*;

/// `Move`.
pub struct Move;

impl Move {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for Move {
    fn command_names(&self) -> Vec<String> {
        names(&["move"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: Move")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
