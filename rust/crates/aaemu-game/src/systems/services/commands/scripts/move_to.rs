//! `Scripts/Commands/MoveTo.cs`.

use super::prelude::*;

/// `MoveTo`.
pub struct MoveTo;

impl MoveTo {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for MoveTo {
    fn command_names(&self) -> Vec<String> {
        names(&["moveto"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: MoveTo")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
