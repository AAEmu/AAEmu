//! `Scripts/Commands/AddLabor.cs`.

use super::prelude::*;

/// `AddLabor`.
pub struct AddLabor;

impl AddLabor {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddLabor {
    fn command_names(&self) -> Vec<String> {
        names(&["labor", "addlabor", "add_labor"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddLabor")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
