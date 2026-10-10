//! `Scripts/Commands/CofferActions.cs`.

use super::prelude::*;

/// `CofferActions`.
pub struct CofferActions;

impl CofferActions {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for CofferActions {
    fn command_names(&self) -> Vec<String> {
        names(&["coffer", "chest"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: CofferActions")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
