//! `Scripts/Commands/Kit.cs`.

use super::prelude::*;

/// `AddKit`.
pub struct AddKit;

impl AddKit {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddKit {
    fn command_names(&self) -> Vec<String> {
        names(&["kit", "addkit", "add_kit"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddKit")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
