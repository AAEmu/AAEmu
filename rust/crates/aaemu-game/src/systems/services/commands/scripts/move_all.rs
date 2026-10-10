//! `Scripts/Commands/MoveAll.cs`.

use super::prelude::*;

/// `MoveAll`.
pub struct MoveAll;

impl MoveAll {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for MoveAll {
    fn command_names(&self) -> Vec<String> {
        names(&["moveall", "move_all"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: MoveAll")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
