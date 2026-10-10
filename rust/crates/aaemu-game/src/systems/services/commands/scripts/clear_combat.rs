//! `Scripts/Commands/ClearCombat.cs`.

use super::prelude::*;

/// `ClearCombat`.
pub struct ClearCombat;

impl ClearCombat {
    pub fn new(_env: &ScriptEnv) -> Self {
        Self
    }
}

impl Command for ClearCombat {
    fn command_names(&self) -> Vec<String> {
        names(&["clearcombat", "clear_combat", "cc"])
    }

    fn execute(
        &self,
        _ctx: &CommandContext<'_>,
        _args: &[String],
        _out: &mut dyn MessageOutput,
    ) -> anyhow::Result<()> {
        anyhow::bail!("PORT-TODO: ClearCombat")
    }

    fn get_command_line_help(&self) -> String {
        String::new() // PORT-TODO
    }

    fn get_command_help_text(&self) -> String {
        String::new() // PORT-TODO
    }
}
