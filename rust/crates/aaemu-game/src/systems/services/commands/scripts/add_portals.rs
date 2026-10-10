//! `Scripts/Commands/AddPortals.cs`.

use super::prelude::*;

/// `AddPortals`.
pub struct AddPortals;

impl AddPortals {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for AddPortals {
    fn command_names(&self) -> Vec<String> {
        names(&["register_portal", "add_portal", "addportal"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: AddPortals")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
